using System.Net;
using System.Text;
using FluentAssertions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using SalesforceCore.AspNetCore.Extensions;
using SalesforceCore.ModelGenerator;
using SalesforceCore.Models.Configuration;
using SalesforceCore.Models.Data;
using SalesforceCore.Models.Errors;
using SalesforceCore.Models.Security;
using SalesforceCore.Security;
using SalesforceCore.Services.Caching;
using SalesforceCore.Services.Core;
using SalesforceCore.Services.Data;
using SalesforceCore.Services.Metadata;
using SalesforceCore.Services.Query;
using SalesforceCore.Utilities;
using Xunit;
using static SalesforceCore.Tests.SalesforceClientEndToEndTests;

namespace SalesforceCore.Tests;

/// <summary>
/// Offline regression tests for the v1.2.0 security hardening.
/// </summary>
public class SecurityHardeningTests
{
    // ---------------- Instance / login URL allow-list ----------------

    [Theory]
    [InlineData("https://login.salesforce.com")]
    [InlineData("https://test.salesforce.com")]
    [InlineData("https://acme.my.salesforce.com")]
    [InlineData("https://acme--uat.sandbox.my.salesforce.com/")]
    [InlineData("https://na1.salesforce.com")]
    [InlineData("https://acme.lightning.force.com")]
    [InlineData("https://acme.my.salesforce.mil")]
    [InlineData("https://acme.my.sfcrmproducts.cn")]
    [InlineData("HTTPS://ACME.MY.SALESFORCE.COM")]
    public void UrlValidator_AllowsSalesforceHosts(string url)
        => SalesforceUrlValidator.IsAllowed(url).Should().BeTrue();

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not a url")]
    [InlineData("/services/data")]
    [InlineData("http://acme.my.salesforce.com")]
    [InlineData("https://evil.com")]
    [InlineData("https://salesforce.com.evil.com")]
    [InlineData("https://evilsalesforce.com")]
    [InlineData("https://my.salesforce.com@evil.com")]
    [InlineData("https://user:pw@acme.my.salesforce.com")]
    [InlineData("javascript:alert(1)")]
    [InlineData("file:///etc/passwd")]
    [InlineData("https://.salesforce.com")]
    public void UrlValidator_RejectsNonSalesforceOrInsecureUrls(string? url)
        => SalesforceUrlValidator.IsAllowed(url).Should().BeFalse();

    [Fact]
    public void UrlValidator_AdditionalHosts_SupportExactAndSuffix()
    {
        SalesforceUrlValidator.IsAllowed("https://sf-proxy.corp.example", new[] { "sf-proxy.corp.example" }).Should().BeTrue();
        SalesforceUrlValidator.IsAllowed("https://a.corp.example", new[] { ".corp.example" }).Should().BeTrue();
        SalesforceUrlValidator.IsAllowed("https://corp.example", new[] { ".corp.example" }).Should().BeFalse();
        SalesforceUrlValidator.IsAllowed("http://sf-proxy.corp.example", new[] { "sf-proxy.corp.example" }).Should().BeFalse();
    }

    [Fact]
    public void UrlValidator_EnsureAllowed_ThrowsAuthException()
    {
        var act = () => SalesforceUrlValidator.EnsureAllowed("https://evil.com", null);
        act.Should().Throw<SalesforceAuthException>().WithMessage("*evil.com*");
    }

    private static SalesforceClient ClientFor(FakeSalesforce http, FakeTokenProvider tokens, Action<SalesforceOptions>? cfg = null)
    {
        var o = new SalesforceOptions();
        cfg?.Invoke(o);
        return new SalesforceClient(new HttpClient(http), tokens, Options.Create(o), NullLogger<SalesforceClient>.Instance);
    }

    [Fact]
    public async Task Client_UntrustedInstanceUrl_NeverSendsToken()
    {
        var http = new FakeSalesforce();
        var tokens = new FakeTokenProvider { Instance = "https://attacker.example" };
        var c = ClientFor(http, tokens);

        var act = () => c.GetAsync("/sobjects/Account");
        await act.Should().ThrowAsync<SalesforceAuthException>();
        http.Requests.Should().BeEmpty("the bearer token must not leave the process");
    }

    [Fact]
    public async Task Client_HttpInstanceUrl_IsRejected()
    {
        var http = new FakeSalesforce();
        var c = ClientFor(http, new FakeTokenProvider { Instance = "http://acme.my.salesforce.com" });
        await c.Invoking(x => x.GetAsync("/limits")).Should().ThrowAsync<SalesforceAuthException>();
        http.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task Client_ValidationOptOut_AllowsCustomHost()
    {
        var http = new FakeSalesforce();
        var c = ClientFor(http, new FakeTokenProvider { Instance = "https://localhost:5001" }, o => o.ValidateInstanceUrl = false);
        await c.GetAsync("/limits");
        http.Requests.Single().Uri.Host.Should().Be("localhost");
    }

    [Fact]
    public async Task Client_AdditionalAllowedHosts_AreHonoured()
    {
        var http = new FakeSalesforce();
        var c = ClientFor(http, new FakeTokenProvider { Instance = "https://sf.proxy.example" }, o => o.AdditionalAllowedHosts = new[] { "sf.proxy.example" });
        await c.GetAsync("/limits");
        http.Requests.Should().HaveCount(1);
    }

    [Fact]
    public async Task Client_Persistent401_RefreshesOnlyOnce()
    {
        var http = new FakeSalesforce { Default = _ => new HttpResponseMessage(HttpStatusCode.Unauthorized) { Content = new StringContent("[{\"errorCode\":\"INVALID_SESSION_ID\",\"message\":\"x\"}]") } };
        var tokens = new FakeTokenProvider();
        var c = ClientFor(http, tokens);
        await c.Invoking(x => x.GetAsync("/limits")).Should().ThrowAsync<Exception>();
        tokens.RefreshCalls.Should().Be(1);
        http.Requests.Should().HaveCount(2);
    }

    // ---------------- Client-credentials provider ----------------

    private sealed class StubFactory : IHttpClientFactory
    {
        private readonly HttpMessageHandler _h;
        public StubFactory(HttpMessageHandler h) => _h = h;
        public HttpClient CreateClient(string name) => new(_h, disposeHandler: false);
    }

    private static (ClientCredentialsTokenProvider provider, FakeSalesforce http, ICacheProvider cache) CcProvider(Action<SalesforceOptions>? cfg = null)
    {
        var sf = new SalesforceOptions { Domain = "https://acme.my.salesforce.com", ClientId = "cid", ClientSecret = "super-secret" };
        cfg?.Invoke(sf);
        var http = new FakeSalesforce();
        var cache = new MemoryCacheProvider(new MemoryCache(new MemoryCacheOptions()), Options.Create(sf), NullLogger<MemoryCacheProvider>.Instance);
        var p = new ClientCredentialsTokenProvider(Options.Create(new ClientCredentialsOptions()), Options.Create(sf),
            new StubFactory(http), cache, new SynchronizationService(), NullLogger<ClientCredentialsTokenProvider>.Instance);
        return (p, http, cache);
    }

    [Fact]
    public async Task ClientCredentials_UntrustedInstanceUrlInTokenResponse_IsRejected()
    {
        var (p, http, _) = CcProvider();
        http.Enqueue(HttpStatusCode.OK, "{\"access_token\":\"AT\",\"instance_url\":\"https://evil.example\",\"expires_in\":3600}");
        await p.Invoking(x => x.GetAccessTokenAsync()).Should().ThrowAsync<SalesforceAuthException>();
        (await p.IsAuthenticatedAsync()).Should().BeFalse("a rejected token must not be cached");
    }

    [Fact]
    public async Task ClientCredentials_UntrustedDomain_DoesNotSendClientSecret()
    {
        var (p, http, _) = CcProvider(o => o.Domain = "https://phish.example");
        await p.Invoking(x => x.GetAccessTokenAsync()).Should().ThrowAsync<SalesforceAuthException>();
        http.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task ClientCredentials_ErrorBody_IsRedactedInException()
    {
        var (p, http, _) = CcProvider();
        http.Enqueue(HttpStatusCode.BadRequest, "{\"error\":\"invalid_grant\",\"access_token\":\"LEAKED123\"}");
        var ex = await p.Invoking(x => x.GetAccessTokenAsync()).Should().ThrowAsync<SalesforceAuthException>();
        ex.Which.Message.Should().NotContain("LEAKED123").And.Contain("invalid_grant");
    }

    [Fact]
    public async Task ClientCredentials_RefreshAfter401_ReauthenticatesEvenIfCachedTokenNotExpired()
    {
        var (p, http, _) = CcProvider();
        http.Enqueue(HttpStatusCode.OK, "{\"access_token\":\"AT1\",\"instance_url\":\"https://acme.my.salesforce.com\",\"expires_in\":7200}");
        http.Enqueue(HttpStatusCode.OK, "{\"access_token\":\"AT2\",\"instance_url\":\"https://acme.my.salesforce.com\",\"expires_in\":7200}");

        (await p.GetAccessTokenAsync()).Should().Be("AT1");
        // Simulates SalesforceClient reacting to a 401 for AT1 (revoked server-side, still "valid" locally)
        (await p.RefreshTokenAsync()).Should().Be("AT2");
        http.Requests.Should().HaveCount(2);
    }

    [Fact]
    public async Task ClientCredentials_ConcurrentRefresh_AuthenticatesOnce()
    {
        var (p, http, _) = CcProvider();
        http.Default = _ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("{\"access_token\":\"AT\",\"instance_url\":\"https://acme.my.salesforce.com\",\"expires_in\":7200}", Encoding.UTF8, "application/json")
        };
        var results = await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => p.GetAccessTokenAsync()));
        results.Should().AllBe("AT");
        http.Requests.Should().HaveCount(1);
    }

    // ---------------- Redaction ----------------

    [Theory]
    [InlineData("{\"access_token\":\"abc.def\"}", "abc.def")]
    [InlineData("refresh_token=5Aep861abcdef&grant_type=refresh_token", "5Aep861abcdef")]
    [InlineData("Authorization: Bearer 00Dxx0000001gPL!AQ0AQH0dMHZfz972Szmpkb58urFRkgeBGsxL", "AQ0AQH0dMHZfz972")]
    [InlineData("client_secret=ABCDEF123", "ABCDEF123")]
    [InlineData("session 00D5g000004XXXX!AQ4AQFakeSessionIdValue1234567890", "AQ4AQFakeSessionIdValue")]
    public void Redactor_MasksSecrets(string input, string secret)
    {
        var redacted = SensitiveDataRedactor.Redact(input);
        redacted.Should().NotContain(secret).And.Contain(SensitiveDataRedactor.Redacted);
    }

    [Fact]
    public void Redactor_TruncatesLongBodies()
        => SensitiveDataRedactor.ForDiagnostics(new string('x', 5000), 100).Length.Should().BeLessThan(130);

    [Fact]
    public void Redactor_Fingerprint_IsStableAndNotReversible()
    {
        var f = SensitiveDataRedactor.Fingerprint("secret-token");
        f.Should().Be(SensitiveDataRedactor.Fingerprint("secret-token")).And.StartWith("sha256:").And.NotContain("secret");
    }

    [Fact]
    public void CachedToken_ToString_DoesNotLeakTokens()
    {
        var t = new CachedToken { AccessToken = "ACCESS-SECRET", RefreshToken = "REFRESH-SECRET", InstanceUrl = "https://acme.my.salesforce.com" };
        t.ToString().Should().NotContain("ACCESS-SECRET").And.NotContain("REFRESH-SECRET").And.Contain("acme.my.salesforce.com");
    }

    // ---------------- SOQL / SOSL injection ----------------

    [Fact]
    public void SanitizeSoql_BackslashQuoteInjection_IsNeutralised()
    {
        // Pre-1.2.0 output was \'' which SOQL parses as escaped-quote + string terminator.
        var attack = "\\' OR Id != null OR Name = \\'";
        var escaped = SecurityUtils.SanitizeSoql(attack);
        escaped.Should().Be("\\\\\\' OR Id != null OR Name = \\\\\\'");
        AssertLiteralStaysClosed($"'{escaped}'");
    }

    [Theory]
    [InlineData("O'Brien", "O\\'Brien")]
    [InlineData("a\\b", "a\\\\b")]
    [InlineData("say \"hi\"", "say \\\"hi\\\"")]
    [InlineData("l1\nl2\r\tx", "l1\\nl2\\r\\tx")]
    [InlineData("", "")]
    public void EscapeSoqlStringLiteral_UsesBackslashEscapes(string input, string expected)
        => SecurityUtils.EscapeSoqlStringLiteral(input).Should().Be(expected);

    [Theory]
    [InlineData("x' OR Name LIKE '%")]
    [InlineData("\\\\' OR '1'='1")]
    [InlineData("\\")]
    [InlineData("'\\'\\\\''")]
    [InlineData("abc\\")]
    public void FormatSoqlValue_AnyInput_ProducesSingleClosedLiteral(string input)
        => AssertLiteralStaysClosed(SecurityUtils.FormatSoqlValue(input));

    [Fact]
    public void SoqlBuilder_WhereEquals_BackslashInjection_StaysInsideLiteral()
    {
        var soql = SoqlBuilder.From("Account").Select("Id").Where("Name", "\\' OR Id != null OR Name = \\'").Build();
        var literal = soql[(soql.IndexOf('\'') )..];
        AssertLiteralStaysClosed(literal.Split(" LIMIT")[0].Split(" ORDER")[0]);
    }

    [Fact]
    public void SanitizeSoqlLike_EscapesWildcardsAndBackslashesOnce()
    {
        SecurityUtils.SanitizeSoqlLike("50%_\\'").Should().Be("50\\%\\_\\\\\\'");
    }

    [Fact]
    public void SoslBuilder_EscapesReservedCharactersWithoutDoubleEscaping()
    {
        var sosl = new SoslBuilder().Find("a&b\\c} RETURNING User(Id)").Returning("Account").Build();
        sosl.Should().StartWith("FIND {a\\&b\\\\c\\} RETURNING User\\(Id\\)}");
    }

    [Fact]
    public void EscapeSoslSearchTerm_EscapesAllReserved()
        => SecurityUtils.EscapeSoslSearchTerm("a?b*{c}\\").Should().Be("a\\?b\\*\\{c\\}\\\\");

    /// <summary>Minimal SOQL string-literal scanner: input must be exactly one quoted literal.</summary>
    private static void AssertLiteralStaysClosed(string literal)
    {
        literal.Should().StartWith("'");
        var i = 1;
        while (i < literal.Length)
        {
            var c = literal[i];
            if (c == '\\') { i += 2; continue; }
            if (c == '\'') break;
            i++;
        }
        i.Should().Be(literal.Length - 1, $"the literal must close only at its final quote: {literal}");
    }

    // ---------------- IDs / paths / redirects ----------------

    [Theory]
    [InlineData("001000000000001AAA", true)]
    [InlineData("001000000000001", true)]
    [InlineData("00100000000001\n", false)]
    [InlineData("../../query?q=x", false)]
    [InlineData("001000000000001%2F", false)]
    public void IsValidSalesforceId_RejectsPathAndNewlineTricks(string id, bool ok)
        => SecurityUtils.IsValidSalesforceId(id).Should().Be(ok);

    [Theory]
    [InlineData("/\\evil.com", false)]
    [InlineData("\\\\evil.com", false)]
    [InlineData("~//evil.com", false)]
    [InlineData("/ok\r\nSet-Cookie:x", false)]
    [InlineData("//evil.com", false)]
    [InlineData("/home/index", true)]
    public void IsLocalUrl_BlocksBackslashOpenRedirects(string url, bool ok)
        => SecurityUtils.IsLocalUrl(url).Should().Be(ok);

    private static DataService DataServiceWith(Mock<ISalesforceClient> client, SalesforceOptions? o = null)
        => new(client.Object, new Mock<ISchemaService>().Object, new Mock<IBulkService>().Object, new Mock<ICacheProvider>().Object,
            Options.Create(o ?? new SalesforceOptions()), NullLogger<DataService>.Instance);

    [Theory]
    [InlineData("../../query?q=SELECT+Id+FROM+User")]
    [InlineData("001000000000001/../../x")]
    [InlineData("")]
    public async Task DataService_RejectsPathTraversalIds(string id)
    {
        var client = new Mock<ISalesforceClient>(MockBehavior.Strict);
        var svc = DataServiceWith(client);
        await svc.Invoking(s => s.DeleteRecordAsync("Account", id)).Should().ThrowAsync<ArgumentException>();
        await svc.Invoking(s => s.GetRecordAsync("Account", id)).Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task DataService_IdValidation_CanBeDisabled()
    {
        var client = new Mock<ISalesforceClient>();
        var svc = DataServiceWith(client, new SalesforceOptions { ValidateSoqlInputs = false });
        await svc.DeleteRecordAsync("Account", "custom-id");
        client.Verify(c => c.DeleteAsync("/sobjects/Account/custom-id", It.IsAny<CancellationToken>()), Times.Once);
    }

    // ---------------- Options / ASP.NET Core ----------------

    [Fact]
    public void OptionsValidator_RejectsUntrustedDomain()
    {
        var v = new SalesforceOptionsValidator();
        v.Validate(null, new SalesforceOptions { Domain = "https://evil.example" }).Failed.Should().BeTrue();
        v.Validate(null, new SalesforceOptions { Domain = "http://login.salesforce.com" }).Failed.Should().BeTrue();
        v.Validate(null, new SalesforceOptions()).Succeeded.Should().BeTrue();
        v.Validate(null, new SalesforceOptions { Domain = "https://localhost", ValidateInstanceUrl = false }).Succeeded.Should().BeTrue();
    }

    private static IConfiguration Config(params (string k, string v)[] kv)
        => new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?> { ["Salesforce:ClientId"] = "cid" }
                .Concat(kv.Select(x => new KeyValuePair<string, string?>(x.k, x.v)))).Build();

    [Fact]
    public void AddSalesforceAuthentication_UntrustedDomain_FailsFast()
    {
        var services = new ServiceCollection();
        var act = () => services.AddSalesforceAuthentication(Config(("Salesforce:Domain", "https://evil.example")));
        act.Should().Throw<SalesforceAuthException>();
    }

    [Fact]
    public void AddSalesforceAuthentication_CustomDomainAllowedViaAdditionalHosts()
    {
        var services = new ServiceCollection();
        var act = () => services.AddSalesforceAuthentication(Config(
            ("Salesforce:Domain", "https://login.corp.example"),
            ("Salesforce:AdditionalAllowedHosts:0", "login.corp.example")));
        act.Should().NotThrow();
    }

    // ---------------- ModelGenerator ----------------

    [Fact]
    public void Generator_CSharpString_CannotBreakOutOfLiteral()
    {
        var evil = "x\");} public class Pwn { static Pwn(){System.Diagnostics.Process.Start(\"calc\");} //\n";
        var escaped = GeneratorEscaping.CSharpString(evil);
        escaped.Should().NotContain("\n");
        // Every quote is preceded by a backslash
        for (var i = 0; i < escaped.Length; i++)
            if (escaped[i] == '"') escaped[i - 1].Should().Be('\\');
    }

    [Fact]
    public void Generator_XmlComment_CannotInjectNewLines()
    {
        var label = "Nice label\n}\npublic class Evil {\u2028";
        var escaped = GeneratorEscaping.Xml(label);
        escaped.Should().NotContainAny("\n", "\r", "\u2028");
        GeneratorEscaping.Comment("a\r\nb").Should().Be("a  b");
    }

    [Theory]
    [InlineData("../evil.cs")]
    [InlineData("..")]
    [InlineData("sub/evil.cs")]
    [InlineData("")]
    public void Generator_SafeOutputPath_RejectsTraversal(string fileName)
    {
        var act = () => GeneratorEscaping.SafeOutputPath(Path.GetTempPath(), fileName);
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Generator_SafeOutputPath_AllowsPlainFile()
    {
        var dir = Path.Combine(Path.GetTempPath(), "sfc-gen");
        GeneratorEscaping.SafeOutputPath(dir, "Account.cs").Should().Be(Path.Combine(Path.GetFullPath(dir), "Account.cs"));
    }
}
