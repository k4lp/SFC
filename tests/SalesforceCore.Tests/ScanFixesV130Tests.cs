using System.IdentityModel.Tokens.Jwt;
using System.Reflection;
using System.Security.Claims;
using System.Text;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using SalesforceCore.AspNetCore.Controllers;
using SalesforceCore.AspNetCore.Extensions;
using SalesforceCore.AspNetCore.TokenProviders;
using SalesforceCore.Models.Configuration;
using SalesforceCore.Models.Data;
using SalesforceCore.Models.Errors;
using SalesforceCore.Models.Metadata;
using SalesforceCore.Security;
using SalesforceCore.Services.Authorization;
using SalesforceCore.Services.Caching;
using SalesforceCore.Services.Caching.SqlServer;
using SalesforceCore.Services.Configuration;
using SalesforceCore.Services.Core;
using SalesforceCore.Services.Data;
using SalesforceCore.Services.Metadata;
using SalesforceCore.Services.Query;
using Xunit;

namespace SalesforceCore.Tests;

/// <summary>
/// Offline regression tests for the v1.3.0 security scan fixes (SFC-xx IDs refer to docs/security/SFC_Security_Scan_Report.md).
/// </summary>
public class ScanFixesV130Tests
{
    private const string AccountId = "001000000000001AAA";

    // ---------------- SFC-01 File preview XSS ----------------

    private static FileController NewFileController(byte[] content)
    {
        var data = new Mock<IDataService>();
        data.Setup(d => d.GetFileContentAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(content);
        return new FileController(data.Object, NullLogger<FileController>.Instance)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };
    }

    [Theory]
    [InlineData("xml")]
    [InlineData("json")]
    [InlineData("txt")]
    [InlineData("csv")]
    [InlineData("docx")]
    [InlineData("html")]
    public async Task Preview_NonPassiveType_IsForcedToDownload(string type)
    {
        var c = NewFileController(Encoding.UTF8.GetBytes("<html xmlns='http://www.w3.org/1999/xhtml'><script>alert(1)</script></html>"));
        var result = await c.Preview("068000000000001AAA", type);
        var file = result.Should().BeOfType<FileContentResult>().Subject;
        file.ContentType.Should().Be("application/octet-stream");
        file.FileDownloadName.Should().NotBeNullOrEmpty();
        c.Response.Headers["Content-Disposition"].ToString().Should().NotBe("inline");
        c.Response.Headers["Content-Security-Policy"].ToString().Should().Contain("sandbox");
        c.Response.Headers["X-Content-Type-Options"].ToString().Should().Be("nosniff");
    }

    [Theory]
    [InlineData("pdf", "application/pdf")]
    [InlineData("png", "image/png")]
    [InlineData(null, "application/pdf")]
    public async Task Preview_PassiveType_StillInline(string? type, string expected)
    {
        var c = NewFileController(new byte[] { 1, 2, 3 });
        var file = (await c.Preview("068000000000001AAA", type)).Should().BeOfType<FileContentResult>().Subject;
        file.ContentType.Should().Be(expected);
        c.Response.Headers["Content-Disposition"].ToString().Should().Be("inline");
        c.Response.Headers["Content-Security-Policy"].ToString().Should().Contain("sandbox");
    }

    [Theory]
    [InlineData("xml")]
    [InlineData("pdf")]
    [InlineData("json")]
    public async Task GetImage_NonImage_IsForcedToDownload(string type)
    {
        var c = NewFileController(new byte[] { 1 });
        var file = (await c.GetImage("068000000000001AAA", type)).Should().BeOfType<FileContentResult>().Subject;
        file.ContentType.Should().Be("application/octet-stream");
    }

    [Fact]
    public async Task GetImage_Image_IsServedAsImage()
    {
        var c = NewFileController(new byte[] { 1 });
        var file = (await c.GetImage("068000000000001AAA", "jpg")).Should().BeOfType<FileContentResult>().Subject;
        file.ContentType.Should().Be("image/jpeg");
    }

    [Fact]
    public async Task Download_SetsNoSniffAndSandbox()
    {
        var c = NewFileController(new byte[] { 1 });
        var file = (await c.Download("068000000000001AAA", "a.xml")).Should().BeOfType<FileContentResult>().Subject;
        file.FileDownloadName.Should().Be("a.xml");
        c.Response.Headers["X-Content-Type-Options"].ToString().Should().Be("nosniff");
    }

    // ---------------- SFC-02 / SFC-10 / SFC-11 / SFC-13 / SFC-14 SalesforceController ----------------

    private sealed class Ctx
    {
        public Mock<ISchemaService> Schema { get; } = new();
        public Mock<IDataService> Data { get; } = new();
        public Mock<IConfigurationService> Config { get; } = new();

        public SalesforceController Build()
        {
            var c = new SalesforceController(Schema.Object, Data.Object, Config.Object,
                Options.Create(new SalesforceOptions()),
                Options.Create(new SalesforceMvcOptions()),
                NullLogger<SalesforceController>.Instance);
            var http = new DefaultHttpContext();
            c.ControllerContext = new ControllerContext { HttpContext = http };
            c.TempData = new Microsoft.AspNetCore.Mvc.ViewFeatures.TempDataDictionary(http, Mock.Of<Microsoft.AspNetCore.Mvc.ViewFeatures.ITempDataProvider>());
            return c;
        }
    }


    [Fact]
    public void SafeErrorMessage_HidesNonSalesforceExceptionDetails()
    {
        SalesforceController.SafeErrorMessage(new InvalidOperationException("Server=db;Password=hunter2"))
            .Should().NotContain("hunter2");
        SalesforceController.SafeErrorMessage(new SalesforceException("Required field missing: Name"))
            .Should().Be("Required field missing: Name");
    }

    [Fact]
    public async Task Upload_RejectsRecordIdOfAnotherObject()
    {
        var ctx = new Ctx();
        ctx.Schema.Setup(s => s.GetDescribeAsync("Account", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SObjectDescribe { Name = "Account", KeyPrefix = "001" });
        var file = new FormFile(new MemoryStream(new byte[] { 1 }), 0, 1, "file", "a.pdf");

        var result = await ctx.Build().Upload("Account", "003000000000001AAA", file);

        result.Should().BeOfType<NotFoundObjectResult>();
        ctx.Data.Verify(d => d.UploadFileAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<long>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData(int.MaxValue)]
    [InlineData(0)]
    [InlineData(-5)]
    [InlineData(1000)]
    public async Task Index_RejectsOutOfRangePages(int page)
    {
        var ctx = new Ctx();
        ctx.Schema.Setup(s => s.GetDescribeAsync("Account", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SObjectDescribe { Name = "Account" });
        ctx.Schema.Setup(s => s.SanitizeFieldListAsync("Account", It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string> { "Name" });

        var result = await ctx.Build().Index("Account", page: page, pageSize: 25);

        result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public void SoqlCondition_Contains_EscapesWildcardsAndQuotes()
    {
        var soql = new SoqlBuilder("Account").Select("Id").WhereCondition(SoqlCondition.Contains("Name", "50%_off' OR Name != '")).Build();
        soql.Should().Contain(@"Name LIKE '%50\%\_off\' OR Name != \'%'");
    }

    [Fact]
    public void SoqlCondition_StartsWith_EscapesWildcards()
    {
        var soql = new SoqlBuilder("Account").Select("Id").WhereCondition(SoqlCondition.StartsWith("Name", "a_b")).Build();
        soql.Should().Contain(@"Name LIKE 'a\_b%'");
    }

    [Fact]
    public void SoqlCondition_Contains_EscapesBackslashOnce()
    {
        var soql = new SoqlBuilder("Account").Select("Id").WhereCondition(SoqlCondition.Contains("Name", @"a\")).Build();
        soql.Should().Contain(@"Name LIKE '%a\\%'");
    }

    // ---------------- SFC-12 Lookup limit ----------------

    [Theory]
    [InlineData(100000, 200)]
    [InlineData(-1, 1)]
    [InlineData(15, 15)]
    public async Task LookupSearch_ClampsLimit(int requested, int expected)
    {
        var lookup = new Mock<ILookupService>();
        LookupSearchOptions? captured = null;
        lookup.Setup(l => l.SearchAsync(It.IsAny<LookupSearchOptions>(), It.IsAny<CancellationToken>()))
            .Callback<LookupSearchOptions, CancellationToken>((o, _) => captured = o)
            .ReturnsAsync(new LookupSearchResult());
        var c = new LookupController(lookup.Object, Mock.Of<IConfigurationService>(), NullLogger<LookupController>.Instance);

        await c.Search("Account", "a", limit: requested);

        captured!.Limit.Should().Be(expected);
    }

    // ---------------- SFC-09 CSRF on cache refresh ----------------

    [Fact]
    public void DynamicUiRefresh_RequiresAntiforgery()
    {
        typeof(DynamicUiController).GetMethod(nameof(DynamicUiController.RefreshCache))!
            .GetCustomAttribute<ValidateAntiForgeryTokenAttribute>().Should().NotBeNull();
    }

    // ---------------- SFC-03 / SFC-04 DistributedCacheTokenProvider ----------------

    private static (DistributedCacheTokenProvider Provider, MemoryDistributedCache Cache, DefaultHttpContext Http) NewDistributed(
        IDataProtectionProvider? dp, DistributedCacheTokenProviderOptions? opts = null, string? userId = "005A", string? sessionId = null)
    {
        var cache = new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions()));
        var http = new DefaultHttpContext();
        if (userId != null)
        {
            http.User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.NameIdentifier, userId) }, "test"));
        }
        if (sessionId != null)
        {
            var session = new Mock<ISession>();
            session.SetupGet(s => s.Id).Returns(sessionId);
            http.Features.Set<ISessionFeature>(new SessionFeature { Session = session.Object });
        }
        var accessor = new HttpContextAccessor { HttpContext = http };
        var p = new DistributedCacheTokenProvider(cache, accessor, Mock.Of<IHttpClientFactory>(),
            Options.Create(new SalesforceOptions()), NullLogger<DistributedCacheTokenProvider>.Instance,
            new SynchronizationService(), null, opts, dp);
        return (p, cache, http);
    }

    private sealed class SessionFeature : ISessionFeature
    {
        public ISession Session { get; set; } = null!;
    }

    [Fact]
    public async Task DistributedTokens_AreEncryptedAtRest()
    {
        var (p, cache, _) = NewDistributed(new EphemeralDataProtectionProvider());
        await p.SetTokensAsync("00D!SECRET_ACCESS", "https://acme.my.salesforce.com", "REFRESH_SECRET");

        var raw = await cache.GetStringAsync(p.GetCacheKeyForTesting());
        raw.Should().StartWith("dp1:").And.NotContain("SECRET");
        (await p.GetAccessTokenAsync()).Should().Be("00D!SECRET_ACCESS");
        (await p.GetRefreshTokenAsync()).Should().Be("REFRESH_SECRET");
    }

    [Fact]
    public async Task DistributedTokens_RefuseToStorePlaintextByDefault()
    {
        var (p, _, _) = NewDistributed(null);
        var act = () => p.SetTokensAsync("a", "https://acme.my.salesforce.com");
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*unencrypted*");
    }

    [Fact]
    public async Task DistributedTokens_PlaintextOptOut_Works()
    {
        var (p, _, _) = NewDistributed(null, new DistributedCacheTokenProviderOptions { AllowUnprotectedTokenStorage = true });
        await p.SetTokensAsync("tok", "https://acme.my.salesforce.com");
        (await p.GetAccessTokenAsync()).Should().Be("tok");
    }

    [Fact]
    public async Task DistributedTokens_LegacyPlaintextEntriesAreIgnoredWhenProtected()
    {
        var (p, cache, _) = NewDistributed(new EphemeralDataProtectionProvider());
        await cache.SetStringAsync(p.GetCacheKeyForTesting(), "{\"AccessToken\":\"injected\",\"InstanceUrl\":\"https://acme.my.salesforce.com\"}");
        (await p.GetAccessTokenAsync()).Should().BeNull();
    }

    [Fact]
    public void DistributedTokens_KeyBindsSessionToUser()
    {
        // HttpContextAccessor is AsyncLocal-backed, so read each key before creating the next provider.
        var (victim, _, _) = NewDistributed(null, userId: "005VICTIM", sessionId: "planted-session");
        var victimKey = victim.GetCacheKeyForTesting();
        var (attacker, _, _) = NewDistributed(null, userId: "005ATTACKER", sessionId: "planted-session");
        var attackerKey = attacker.GetCacheKeyForTesting();
        victimKey.Should().NotBe(attackerKey);
        victimKey.Should().Contain("planted-session").And.NotContain("005VICTIM");
    }

    [Fact]
    public void DistributedTokens_AnonymousSessionKeyRequiresOptIn()
    {
        var (p, _, _) = NewDistributed(null, userId: null, sessionId: "s1");
        var act = () => p.GetCacheKeyForTesting();
        act.Should().Throw<InvalidOperationException>();

        var (p2, _, _) = NewDistributed(null, new DistributedCacheTokenProviderOptions { AllowAnonymousSessionCacheKey = true }, userId: null, sessionId: "s1");
        p2.GetCacheKeyForTesting().Should().EndWith("s1");
    }

    // ---------------- SFC-06 refresh lock striping ----------------

    [Fact]
    public void RefreshLock_IsPerSessionStripe()
    {
        AspNetCoreTokenProvider.GetRefreshLock("abc").Should().BeSameAs(AspNetCoreTokenProvider.GetRefreshLock("abc"));
        var distinct = Enumerable.Range(0, 200).Select(i => AspNetCoreTokenProvider.GetRefreshLock("session-" + i)).Distinct().Count();
        distinct.Should().BeGreaterThan(1);
    }

    // ---------------- SFC-07 per-user schema cache ----------------

    private sealed class FixedUser : IUserContextProvider
    {
        private readonly ClaimsPrincipal? _p;
        public FixedUser(string? id) => _p = id == null ? null : new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.NameIdentifier, id) }, "t"));
        public ClaimsPrincipal? GetUser() => _p;
    }

    private static List<string> CaptureDescribeKeys(SalesforceOptions opts, IUserContextProvider? user)
    {
        var keys = new List<string>();
        var cache = new Mock<ICacheProvider>();
        cache.Setup(c => c.GetOrCreateAsync(It.IsAny<string>(), It.IsAny<Func<CancellationToken, Task<SObjectDescribe?>>>(), It.IsAny<TimeSpan?>(), It.IsAny<CancellationToken>()))
            .Callback<string, Func<CancellationToken, Task<SObjectDescribe?>>, TimeSpan?, CancellationToken>((k, _, _, _) => keys.Add(k))
            .ReturnsAsync((SObjectDescribe?)null);
        var svc = new SchemaService(Mock.Of<ISalesforceClient>(), cache.Object, Options.Create(opts), NullLogger<SchemaService>.Instance, user);
        svc.GetDescribeAsync("Account").GetAwaiter().GetResult();
        return keys;
    }

    [Fact]
    public void SchemaCache_IsPartitionedPerUser()
    {
        var a = CaptureDescribeKeys(new SalesforceOptions(), new FixedUser("005A")).Single();
        var b = CaptureDescribeKeys(new SalesforceOptions(), new FixedUser("005B")).Single();
        a.Should().NotBe(b);
        a.Should().StartWith("Schema_account_u");
    }

    [Fact]
    public void SchemaCache_SharedWhenNoUserOrOptedOut()
    {
        CaptureDescribeKeys(new SalesforceOptions(), new FixedUser(null)).Single().Should().Be("Schema_account");
        CaptureDescribeKeys(new SalesforceOptions { PartitionSchemaCacheByUser = false }, new FixedUser("005A")).Single().Should().Be("Schema_account");
        CaptureDescribeKeys(new SalesforceOptions(), null).Single().Should().Be("Schema_account");
    }

    // ---------------- SFC-08 allow-list ----------------

    [Theory]
    [InlineData("https://attacker.my.salesforce-sites.com")]
    [InlineData("https://attacker.salesforce-sites.com/services/apexrest/x")]
    public void UrlValidator_RejectsTenantSitesDomain(string url)
        => SalesforceUrlValidator.IsAllowed(url).Should().BeFalse();

    [Fact]
    public void UrlValidator_SitesDomain_CanBeOptedIn()
        => SalesforceUrlValidator.IsAllowed("https://x.my.salesforce-sites.com", new[] { ".salesforce-sites.com" }).Should().BeTrue();

    [Theory]
    [InlineData("https://acme.my.salesforce.com.")]
    [InlineData("https://acme.my.salesforce.com:8443")]
    [InlineData("https://xn--acme.my.salesforce.com")]
    public void UrlValidator_EdgeCasesStillSalesforce(string url)
        => SalesforceUrlValidator.IsAllowed(url).Should().BeTrue();

    [Theory]
    [InlineData("https://acme.my.salesforce.com\\@evil.com")]
    [InlineData("https://evil.com#.salesforce.com")]
    [InlineData("https://evil.com?.salesforce.com")]
    [InlineData("https://evil.com/.salesforce.com")]
    [InlineData("https://salesforce.com")]
    [InlineData("https://.salesforce.com")]
    [InlineData("https://evil.com%2F.salesforce.com")]
    public void UrlValidator_RejectsParserTricks(string url)
        => SalesforceUrlValidator.IsAllowed(url).Should().BeFalse();

    // ---------------- SFC-15 JWT assertion ----------------

    [Fact]
    public void JwtAssertion_HasSingleIssAndAud()
    {
        using var rsa = System.Security.Cryptography.RSA.Create(2048);
        var pem = rsa.ExportPkcs8PrivateKeyPem();
        var provider = new JwtTokenProvider(
            Options.Create(new JwtTokenProviderOptions { PrivateKey = pem, Username = "u@x.com" }),
            Options.Create(new SalesforceOptions { ClientId = "cid", Domain = "https://login.salesforce.com" }),
            Mock.Of<IHttpClientFactory>(), Mock.Of<ICacheProvider>(), new SynchronizationService(),
            NullLogger<JwtTokenProvider>.Instance);
        var method = typeof(JwtTokenProvider).GetMethod("CreateJwtAssertion", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var jwt = new JwtSecurityTokenHandler().ReadJwtToken((string)method.Invoke(provider, null)!);

        jwt.Claims.Count(c => c.Type == "iss").Should().Be(1);
        jwt.Claims.Count(c => c.Type == "aud").Should().Be(1);
        jwt.Audiences.Should().Equal("https://login.salesforce.com");
        jwt.Subject.Should().Be("u@x.com");
        jwt.Header.Alg.Should().Be("RS256");
    }

    // ---------------- SFC-16 AES-GCM associated data ----------------

    [Fact]
    public void EncryptedSqlCache_CiphertextIsBoundToKey()
    {
        var opts = new SalesforceOptions { SqlCacheEncryptionKey = Convert.ToBase64String(new byte[32]) };
        var cache = new EncryptedSqlServerCache(Mock.Of<IServiceScopeFactory>(), Options.Create(opts), NullLogger<EncryptedSqlServerCache>.Instance);
        var enc = typeof(EncryptedSqlServerCache).GetMethod("Encrypt", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var dec = typeof(EncryptedSqlServerCache).GetMethod("Decrypt", BindingFlags.NonPublic | BindingFlags.Instance)!;

        var blob = (byte[])enc.Invoke(cache, new object[] { Encoding.UTF8.GetBytes("victim-token"), "sf:user-a" })!;
        Encoding.UTF8.GetString((byte[])dec.Invoke(cache, new object[] { blob, "sf:user-a" })!).Should().Be("victim-token");

        var swap = () => dec.Invoke(cache, new object[] { blob, "sf:user-b" });
        swap.Should().Throw<TargetInvocationException>().WithInnerException<System.Security.Cryptography.CryptographicException>();
    }

    // ---------------- SFC-14 DataService paging ----------------

    [Fact]
    public async Task QueryPaged_HugePage_DoesNotOverflow()
    {
        var schema = new Mock<ISchemaService>();
        schema.Setup(s => s.SanitizeFieldListAsync(It.IsAny<string>(), It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string> { "Name" });
        var ds = new DataService(Mock.Of<ISalesforceClient>(), schema.Object, Mock.Of<IBulkService>(), Mock.Of<ICacheProvider>(), Options.Create(new SalesforceOptions()), NullLogger<DataService>.Instance);
        var act = () => ds.QueryPagedAsync("Account", new[] { "Name" }, null, null, false, int.MaxValue, 200);
        await act.Should().ThrowAsync<ArgumentOutOfRangeException>();
    }
    // ---------------- SFC-05 logout revokes the refresh token ----------------

    private sealed class CaptureHandler : HttpMessageHandler
    {
        public List<string> Bodies { get; } = new();
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Bodies.Add(request.Content == null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken));
            return new HttpResponseMessage(System.Net.HttpStatusCode.OK);
        }
    }

    [Theory]
    [InlineData("RT_SECRET", "token=RT_SECRET")]
    [InlineData(null, "token=AT_SECRET")]
    public async Task Revoke_PrefersRefreshToken(string? refreshToken, string expectedBody)
    {
        var props = new Microsoft.AspNetCore.Authentication.AuthenticationProperties();
        var tokens = new List<Microsoft.AspNetCore.Authentication.AuthenticationToken> { new() { Name = "access_token", Value = "AT_SECRET" } };
        if (refreshToken != null) tokens.Add(new() { Name = "refresh_token", Value = refreshToken });
        props.StoreTokens(tokens);
        props.Items["instance_url"] = "https://acme.my.salesforce.com";
        var principal = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.NameIdentifier, "005A") }, "Cookies"));
        var auth = new Mock<Microsoft.AspNetCore.Authentication.IAuthenticationService>();
        auth.Setup(a => a.AuthenticateAsync(It.IsAny<HttpContext>(), It.IsAny<string?>()))
            .ReturnsAsync(Microsoft.AspNetCore.Authentication.AuthenticateResult.Success(new Microsoft.AspNetCore.Authentication.AuthenticationTicket(principal, props, "Cookies")));
        var http = new DefaultHttpContext { User = principal, RequestServices = new ServiceCollection().AddSingleton(auth.Object).BuildServiceProvider() };

        var handler = new CaptureHandler();
        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(() => new HttpClient(handler, disposeHandler: false));
        var provider = new AspNetCoreTokenProvider(new HttpContextAccessor { HttpContext = http }, factory.Object,
            Options.Create(new SalesforceOptions()), NullLogger<AspNetCoreTokenProvider>.Instance);

        await provider.RevokeTokenAsync();

        handler.Bodies.Should().ContainSingle().Which.Should().Be(expectedBody);
    }

    // ---------------- SFC-17 lookup tag helper attribute encoding ----------------

    [Fact]
    public void LookupTagHelper_EncodesAttributeValues()
    {
        var helper = new SalesforceCore.AspNetCore.TagHelpers.SalesforceLookupTagHelper(Mock.Of<ISchemaService>(), Mock.Of<ILookupService>(),
            Mock.Of<Microsoft.AspNetCore.Mvc.ViewFeatures.IHtmlGenerator>())
        { SearchFields = "Name\"><script>x</script>" };
        var build = typeof(SalesforceCore.AspNetCore.TagHelpers.SalesforceLookupTagHelper).GetMethod("BuildLookupHtml", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var html = (string)build.Invoke(helper, new object?[] { "F\"onx=\"1", "id\"><b", null, "d", new List<string> { "Account\"<" }, false })!;
        html.Should().NotContain("<script>").And.NotContain("\"onx=").And.NotContain("id\"><b").And.NotContain("Account\"<");
    }
    // ---------------- SFC-25 ReDoS in URL validation ----------------

    [Fact]
    public void UrlRegex_IsNotCatastrophic()
    {
        var evil = "http://a.bc/" + new string('a', 5000) + "!";
        var sw = System.Diagnostics.Stopwatch.StartNew();
        SalesforceCore.Validation.FieldValidator.IsValidUrlFormat(evil).Should().BeFalse();
        sw.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(2));
    }

    [Theory]
    [InlineData("https://www.example.com/path/to page.html")]
    [InlineData("example.com")]
    [InlineData("http://sub.example.co.uk/a/b/")]
    public void UrlRegex_StillAcceptsValidUrls(string url)
        => SalesforceCore.Validation.FieldValidator.IsValidUrlFormat(url).Should().BeTrue();
}
