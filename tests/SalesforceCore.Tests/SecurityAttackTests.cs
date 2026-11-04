using System.IO.Compression;
using System.Net;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using SalesforceCore.Http;
using SalesforceCore.Models.Configuration;
using SalesforceCore.Models.Errors;
using SalesforceCore.Security;
using SalesforceCore.Services.Caching.SqlServer;
using SalesforceCore.Services.Core;
using SalesforceCore.Services.Query;
using SalesforceCore.Services.Reports;
using SalesforceCore.Services.Tooling;
using SalesforceCore.Utilities;
using Xunit;

namespace SalesforceCore.Tests;

/// <summary>2.7.0 attack corpus: injection, traversal, header injection, bombs, hostile identifiers.</summary>
public class SecurityAttackTests
{
    public static readonly TheoryData<string> SoqlPayloads = new()
    {
        "' OR Id != null OR Name = '",
        "\\' OR Id != null OR Name = \\'",
        "\\\\' OR 1=1 --",
        "x'); DELETE FROM Account; --",
        "O’Brien' OR Name LIKE '%",
        "a\0' OR Name != '",
        "a\r\n' OR Name != '",
        "%' OR Name LIKE '%",
        "\\",
        "'",
        "''",
        "\\\\\\'",
        "\u2019 OR 1=1",
        "\" OR Name != \"",
        "a\\' ) OR (Name != '",
    };

    /// <summary>Walks SOQL, honouring backslash escapes, and returns the number of string literals and whether the text ends outside a literal.</summary>
    private static (int literals, bool closed, string tail) Scan(string soql)
    {
        var literals = 0; var inside = false; var lastClose = -1;
        for (var i = 0; i < soql.Length; i++)
        {
            var c = soql[i];
            if (inside)
            {
                if (c == '\\') { i++; continue; }
                if (c == '\'') { inside = false; lastClose = i; }
            }
            else if (c == '\'') { inside = true; literals++; }
        }
        return (literals, !inside, lastClose >= 0 ? soql[(lastClose + 1)..] : soql);
    }

    [Theory, MemberData(nameof(SoqlPayloads))]
    public void Where_InjectionPayload_StaysInsideOneLiteral(string payload)
    {
        var soql = SoqlBuilder.From("Account").Select("Id").Where("Name", payload).Build();
        var (literals, closed, tail) = Scan(soql);
        literals.Should().Be(1, soql);
        closed.Should().BeTrue(soql);
        tail.Trim().Should().BeEmpty(soql);
    }

    [Theory, MemberData(nameof(SoqlPayloads))]
    public void WhereLike_InjectionPayload_StaysInsideOneLiteral(string payload)
    {
        var soql = SoqlBuilder.From("Account").Select("Id").WhereLike("Name", payload).Build();
        var (literals, closed, tail) = Scan(soql);
        literals.Should().Be(1, soql);
        closed.Should().BeTrue();
        tail.Trim().Should().BeEmpty(soql);
    }

    [Theory, MemberData(nameof(SoqlPayloads))]
    public void WhereIn_InjectionPayload_StaysInsideLiterals(string payload)
    {
        var soql = SoqlBuilder.From("Account").Select("Id").WhereIn("Name", new object?[] { payload, "b" }).Build();
        var (literals, closed, tail) = Scan(soql);
        literals.Should().Be(2, soql);
        closed.Should().BeTrue();
        tail.Trim().Should().Be(")", soql);
    }

    [Theory]
    [InlineData("Name FROM User --")]
    [InlineData("Id, (SELECT Id FROM Contacts)")]
    [InlineData("Name\n")]
    [InlineData("Name'")]
    [InlineData("Name) OR (Id")]
    [InlineData("Account..Name")]
    public void HostileFieldNames_AreRejected(string field)
    {
        var select = () => SoqlBuilder.From("Account").Select(field).Build();
        var order = () => SoqlBuilder.From("Account").Select("Id").OrderBy(field).Build();
        var where = () => SoqlBuilder.From("Account").Select("Id").Where(field, "x").Build();
        select.Should().Throw<Exception>();
        order.Should().Throw<Exception>();
        where.Should().Throw<Exception>();
    }

    [Fact]
    public void EmptyFieldName_InOrderByOrWhere_IsRejected()
    {
        ((Action)(() => SoqlBuilder.From("Account").Select("Id").OrderBy("").Build())).Should().Throw<Exception>();
        ((Action)(() => SoqlBuilder.From("Account").Select("Id").Where("", "x").Build())).Should().Throw<Exception>();
    }

    [Theory]
    [InlineData("Account\n")]
    [InlineData("Account\r\n")]
    [InlineData("Account WHERE")]
    [InlineData("../Account")]
    [InlineData("Account/")]
    public void HostileObjectNames_AreRejected(string name)
    {
        SecurityUtils.IsValidObjectName(name).Should().BeFalse();
        var act = () => SoqlBuilder.From(name).Select("Id").Build();
        act.Should().Throw<Exception>();
    }

    [Fact]
    public void TrailingNewline_NoLongerPassesApiNameValidation()
    {
        SecurityUtils.IsValidFieldName("Name\n").Should().BeFalse();
        SecurityUtils.IsValidObjectName("Account\n").Should().BeFalse();
        SecurityUtils.IsValidSalesforceId("001000000000001\n").Should().BeFalse();
    }

    [Theory]
    [InlineData("/sobjects/Account/../../apexrest/Admin")]
    [InlineData("/sobjects/Account/%2e%2e/%2e%2e/apexrest")]
    [InlineData("/sobjects/Account/%252e%252e/x")]
    [InlineData("/sobjects/Account/.")]
    [InlineData("/sobjects/Account\\..\\x")]
    [InlineData("/sobjects/Account%5c..%5cx")]
    [InlineData("/sobjects/Account/001\r\nX-Evil: 1")]
    [InlineData("/sobjects/Account/001%0d%0aX-Evil")]
    [InlineData("//evil.com/services/data")]
    [InlineData("https://evil.com/services/data")]
    [InlineData("/sobjects/Account/\0")]
    public void PathGuard_RejectsTraversalAndInjection(string path)
    {
        SalesforcePathGuard.TryValidate(path, out _).Should().BeFalse(path);
    }

    [Theory]
    [InlineData("/sobjects/Account/001000000000001")]
    [InlineData("/query?q=SELECT+Id+FROM+Account+WHERE+Name+%3D+%27a..b%27")]
    [InlineData("/services/data/v62.0/query/01gxx-2000")]
    [InlineData("/sobjects/Account/Ext__c/a%2Fb")]
    [InlineData("/services/data/v62.0/sobjects/Contact/@{newAccount.id}")]
    public void PathGuard_AllowsLegitimatePaths(string path)
    {
        SalesforcePathGuard.TryValidate(path, out var reason).Should().BeTrue(reason);
    }

    [Fact]
    public void PathGuardSegment_EncodesSlashesAndRejectsDotSegments()
    {
        SalesforcePathGuard.Segment("a/../b").Should().Be("a%2F..%2Fb");
        ((Action)(() => SalesforcePathGuard.Segment(".."))).Should().Throw<ArgumentException>();
        ((Action)(() => SalesforcePathGuard.Segment("."))).Should().Throw<ArgumentException>();
        ((Action)(() => SalesforcePathGuard.Segment("a\nb"))).Should().Throw<ArgumentException>();
    }

    [Fact]
    public void SalesforceClientBuildPath_RejectsTraversal()
    {
        var client = new SalesforceClient(new HttpClient(), Mock.Of<ITokenProvider>(),
            Options.Create(new SalesforceOptions()), Mock.Of<ILogger<SalesforceClient>>());
        ((Action)(() => client.BuildPath("/sobjects/Account/../../apexrest/x"))).Should().Throw<ArgumentException>();
        client.BuildPath("/sobjects/Account/001000000000001").Should().EndWith("/sobjects/Account/001000000000001");
    }

    [Theory]
    [InlineData("X-Test", "a\r\nX-Evil: 1")]
    [InlineData("X-Test", "a\nb")]
    [InlineData("X-Test", "a\0b")]
    [InlineData("X-Test\r\nX-Evil", "a")]
    [InlineData("X Test", "a")]
    [InlineData("X:Test", "a")]
    public void HeaderInjection_IsRejected(string name, string value)
    {
        ((Action)(() => SalesforceClient.EnsureSafeHeader(name, value))).Should().Throw<ArgumentException>();
    }

    [Fact]
    public async Task ReportService_RejectsNonIdReportIds()
    {
        var client = new Mock<ISalesforceClient>(MockBehavior.Strict);
        var service = new ReportService(client.Object, Mock.Of<ILogger<ReportService>>());
        foreach (var id in new[] { "00O/../../apexrest/x", "00Oxxx", "00O000000000001?x=1", "00O000000000001/x" })
        {
            await service.Invoking(s => s.RunReportAsync(id)).Should().ThrowAsync<ArgumentException>(id);
            await service.Invoking(s => s.DescribeReportAsync(id)).Should().ThrowAsync<ArgumentException>(id);
        }
        await service.Invoking(s => s.GetReportInstanceAsync("00O000000000001", "../x")).Should().ThrowAsync<ArgumentException>();
    }

    [Theory]
    [InlineData("../../etc/passwd", false)]
    [InlineData("..\\..\\windows\\win.ini", false)]
    [InlineData("a\r\nContent-Type: text/html", false)]
    [InlineData("report\".html", false)]
    [InlineData("CON.txt", false)]
    [InlineData("..", false)]
    [InlineData(".htaccess", false)]
    public void SanitizeFileName_NeutralisesHostileNames(string name, bool _)
    {
        var safe = SecurityUtils.SanitizeFileName(name);
        safe.Should().NotContainAny("/", "\\", "\r", "\n", "\"", ":");
        safe.Should().NotStartWith(".");
        safe.Should().NotBeEquivalentTo("CON.txt");
        safe.Should().NotBeEmpty();
    }

    [Theory]
    [InlineData("Cache]; DROP TABLE Users; --", "[Cache]]; DROP TABLE Users; --]")]
    [InlineData("Normal", "[Normal]")]
    public void SqlIdentifier_EscapesClosingBracket(string name, string expected)
    {
        SqlIdentifier.Quote(name).Should().Be(expected);
        SqlIdentifier.Qualify("dbo]", name).Should().StartWith("[dbo]]].");
    }

    [Fact]
    public async Task SizeLimitedReadStream_StopsGzipBomb()
    {
        using var ms = new MemoryStream();
        using (var gz = new GZipStream(ms, CompressionLevel.SmallestSize, true)) gz.Write(new byte[20 * 1024 * 1024]);
        ms.Position = 0;
        await using var limited = new SizeLimitedReadStream(new GZipStream(ms, CompressionMode.Decompress), 1024 * 1024);
        var act = async () => await limited.CopyToAsync(Stream.Null);
        await act.Should().ThrowAsync<InvalidDataException>();
    }

    [Fact]
    public async Task TransportHandler_RefusesTokenOverHttp()
    {
        var handler = new SalesforceTransportSecurityHandler(Monitor(new SalesforceOptions())) { InnerHandler = new Stub(_ => new HttpResponseMessage(HttpStatusCode.OK)) };
        using var invoker = new HttpMessageInvoker(handler);
        var req = new HttpRequestMessage(HttpMethod.Get, "http://acme.my.salesforce.com/services/data");
        req.Headers.Authorization = new("Bearer", "tok");
        await invoker.Invoking(i => i.SendAsync(req, default)).Should().ThrowAsync<SalesforceAuthException>();
    }

    [Fact]
    public async Task TransportHandler_DiscardsCrossHostRedirect()
    {
        var handler = new SalesforceTransportSecurityHandler(Monitor(new SalesforceOptions()))
        {
            InnerHandler = new Stub(r => new HttpResponseMessage(HttpStatusCode.OK) { RequestMessage = new HttpRequestMessage(HttpMethod.Get, "https://evil.example.com/x") })
        };
        using var invoker = new HttpMessageInvoker(handler);
        var req = new HttpRequestMessage(HttpMethod.Get, "https://acme.my.salesforce.com/services/data");
        req.Headers.Authorization = new("Bearer", "tok");
        await invoker.Invoking(i => i.SendAsync(req, default)).Should().ThrowAsync<SalesforceConnectionException>();
    }

    [Fact]
    public void OptionsValidator_RejectsHttpDomainAndCrLfClientName()
    {
        var v = new SalesforceOptionsValidator();
        v.Validate(null, new SalesforceOptions { Domain = "http://login.salesforce.com" }).Failed.Should().BeTrue();
        v.Validate(null, new SalesforceOptions { ClientName = "a\r\nb" }).Failed.Should().BeTrue();
        v.Validate(null, new SalesforceOptions()).Failed.Should().BeFalse();
    }

    [Fact]
    public async Task ToolingService_RejectsHostileTargets()
    {
        var client = new Mock<ISalesforceClient>(MockBehavior.Strict);
        var service = new ToolingService(client.Object, Options.Create(new SalesforceOptions()), Mock.Of<ILogger<ToolingService>>());
        await service.Invoking(s => s.GetDebugLogContentAsync("../../x")).Should().ThrowAsync<ArgumentException>();
        await service.Invoking(s => s.DeleteAsync("ApexClass/..", "01p000000000001")).Should().ThrowAsync<ArgumentException>();
        await service.Invoking(s => s.UpdateAsync("ApexClass", "../x", new { })).Should().ThrowAsync<ArgumentException>();
    }

    private static IOptionsMonitor<SalesforceOptions> Monitor(SalesforceOptions o)
        => Mock.Of<IOptionsMonitor<SalesforceOptions>>(m => m.CurrentValue == o);

    private sealed class Stub(Func<HttpRequestMessage, HttpResponseMessage> f) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => Task.FromResult(f(request));
    }
}

public class ErrorRedactionTests
{
    [Fact]
    public void JsonErrorBody_KeepsErrorCode_AndRedactsEchoedTokens()
    {
        var body = "[{\"message\":\"bad Bearer 00Dabc!xyzxyzxyzxyzxyzxyzxyzxyz\",\"errorCode\":\"INVALID_FIELD\"}]";
        var ex = Models.Errors.SalesforceErrorMapper.FromResponse(400, body, null, null, 2000, SalesforceCore.Security.SensitiveDataRedactor.Redact);
        ex.Message.Should().NotContain("xyzxyzxyz");
        ex.Errors.Should().Contain(e => e.ErrorCode == "INVALID_FIELD");
        SalesforceCore.Security.SensitiveDataRedactor.Redact("\"errorCode\":\"INVALID_FIELD\"").Should().Contain("INVALID_FIELD");
    }
}
