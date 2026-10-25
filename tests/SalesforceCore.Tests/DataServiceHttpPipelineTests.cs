using System.Net;
using System.Text.Json.Nodes;
using System.Web;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using SalesforceCore.Models.Configuration;
using SalesforceCore.Models.Errors;
using SalesforceCore.Services.Caching;
using SalesforceCore.Services.Core;
using SalesforceCore.Services.Data;
using SalesforceCore.Services.Metadata;
using SalesforceCore.Models.Metadata;
using Xunit;
using static SalesforceCore.Tests.SalesforceClientEndToEndTests;

namespace SalesforceCore.Tests;

/// <summary>
/// Integration-style tests: DataService -> real SalesforceClient -> fake Salesforce REST endpoint.
/// </summary>
public class DataServiceHttpPipelineTests
{
    private readonly FakeSalesforce _http = new();
    private readonly Mock<IBulkService> _bulk = new();

    private DataService Service()
    {
        var opts = Options.Create(new SalesforceOptions());
        var client = new SalesforceClient(new HttpClient(_http), new FakeTokenProvider(), opts, NullLogger<SalesforceClient>.Instance);
        var schema = new Mock<ISchemaService>();
        var fields = new List<SObjectField> { new() { Name = "Name" }, new() { Name = "Phone" } };
        schema.Setup(x => x.GetCreateableFieldsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(fields);
        schema.Setup(x => x.GetUpdateableFieldsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(fields);
        schema.Setup(x => x.GetFieldsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(fields);
        return new DataService(client, schema.Object, _bulk.Object, new Mock<ICacheProvider>().Object, opts, NullLogger<DataService>.Instance);
    }

    [Fact]
    public async Task Query_EncodesSoqlInQueryString()
    {
        _http.Enqueue(HttpStatusCode.OK, """{"totalSize":0,"done":true,"records":[]}""");
        await Service().QueryAsync("SELECT Id FROM Account WHERE Name = 'A&B #1'");
        var q = HttpUtility.ParseQueryString(_http.Requests.Single().Uri.Query)["q"];
        q.Should().Be("SELECT Id FROM Account WHERE Name = 'A&B #1'");
    }

    [Fact]
    public async Task QueryAll_FollowsAllPages()
    {
        _http.Enqueue(HttpStatusCode.OK, """{"totalSize":3,"done":false,"nextRecordsUrl":"/services/data/v60.0/query/01g-1","records":[{"Id":"1"}]}""")
             .Enqueue(HttpStatusCode.OK, """{"totalSize":3,"done":false,"nextRecordsUrl":"/services/data/v60.0/query/01g-2","records":[{"Id":"2"}]}""")
             .Enqueue(HttpStatusCode.OK, """{"totalSize":3,"done":true,"records":[{"Id":"3"}]}""");
        var all = await Service().QueryAllAsync("SELECT Id FROM Account");
        all.Select(r => r["Id"]!.GetValue<string>()).Should().Equal("1", "2", "3");
        _http.Requests.Should().HaveCount(3);
    }

    [Fact]
    public async Task QueryNext_HitsNextRecordsUrl()
    {
        _http.Enqueue(HttpStatusCode.OK, """{"totalSize":1,"done":true,"records":[{"Id":"9"}]}""");
        var r = await Service().QueryNextAsync("/services/data/v60.0/query/01g-9");
        r.Records.Should().HaveCount(1);
        _http.Requests.Single().Uri.AbsolutePath.Should().Be("/services/data/v60.0/query/01g-9");
    }

    [Fact]
    public async Task Query_MalformedSoql_SurfacesSalesforceError()
    {
        _http.Enqueue(HttpStatusCode.BadRequest, """[{"message":"unexpected token: FORM","errorCode":"MALFORMED_QUERY"}]""");
        var ex = await Service().Invoking(s => s.QueryAsync("SELECT Id FORM Account")).Should().ThrowAsync<SalesforceException>();
        ex.Which.ErrorCode.Should().Be("MALFORMED_QUERY");
    }

    [Fact]
    public async Task Create_PostsToSObjectEndpoint_ReturnsId()
    {
        _http.Enqueue(HttpStatusCode.Created, """{"id":"001NEW","success":true,"errors":[]}""");
        var id = await Service().CreateRecordAsync("Account", new Dictionary<string, object?> { ["Name"] = "Acme" });
        id.Should().Be("001NEW");
        var req = _http.Requests.Single();
        req.Method.Should().Be(HttpMethod.Post);
        req.Uri.AbsolutePath.TrimEnd('/').Should().EndWith("/sobjects/Account");
        JsonNode.Parse(req.Body!)!["Name"]!.GetValue<string>().Should().Be("Acme");
    }

    [Fact]
    public async Task Create_ValidationFailure_Throws()
    {
        _http.Enqueue(HttpStatusCode.BadRequest, """[{"message":"Name required","errorCode":"REQUIRED_FIELD_MISSING","fields":["Name"]}]""");
        await Service().Invoking(s => s.CreateRecordAsync("Account", new Dictionary<string, object?>())).Should().ThrowAsync<SalesforceException>();
    }

    [Fact]
    public async Task Update_SendsPatch_204Succeeds()
    {
        _http.Enqueue(HttpStatusCode.NoContent);
        await Service().UpdateRecordAsync("Account", "001000000000001AAA", new Dictionary<string, object?> { ["Name"] = "New" });
        var req = _http.Requests.Single();
        req.Method.Should().Be(HttpMethod.Patch);
        req.Uri.AbsolutePath.Should().EndWith("/sobjects/Account/001000000000001AAA");
    }

    [Fact]
    public async Task Delete_SendsDelete_204Succeeds()
    {
        _http.Enqueue(HttpStatusCode.NoContent);
        await Service().DeleteRecordAsync("Account", "001000000000001AAA");
        _http.Requests.Single().Method.Should().Be(HttpMethod.Delete);
    }

    [Fact]
    public async Task Delete_NotFound_Throws()
    {
        _http.Enqueue(HttpStatusCode.NotFound, """[{"message":"entity is deleted","errorCode":"ENTITY_IS_DELETED"}]""");
        (await Service().Invoking(s => s.DeleteRecordAsync("Account", "001000000000001AAA")).Should().ThrowAsync<SalesforceException>())
            .Which.HttpStatusCode.Should().Be(404);
    }

    [Fact]
    public async Task Get_ExpiredSession_RefreshesTransparently()
    {
        _http.Enqueue(HttpStatusCode.Unauthorized).Enqueue(HttpStatusCode.OK, """{"Id":"001","Name":"A"}""");
        var rec = await Service().GetRecordAsync("Account", "001000000000001AAA");
        rec["Name"]!.GetValue<string>().Should().Be("A");
        _http.Requests.Select(r => r.Auth).Should().Equal("tok-1", "tok-2");
    }

    [Fact]
    public async Task BatchCreate_Small_UsesCompositeNotBulk()
    {
        _http.Default = _ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""[{"id":"001A","success":true,"errors":[]},{"id":null,"success":false,"errors":[{"message":"dup","statusCode":"DUPLICATE_VALUE","fields":[]}]}]""")
        };
        var res = await Service().BatchCreateAsync("Account", new[]
        {
            new Dictionary<string, object?> { ["Name"] = "A" },
            new Dictionary<string, object?> { ["Name"] = "B" },
        });
        res.TotalCount.Should().Be(2);
        res.UsedBulkApi.Should().BeFalse();
        res.SuccessfulIds.Should().Equal("001A");
        res.SuccessCount.Should().Be(1);
        res.FailureCount.Should().Be(1);
        res.AllSucceeded.Should().BeFalse();
        _http.Requests.Should().OnlyContain(r => r.Uri.AbsolutePath.Contains("/composite/sobjects"));
        _bulk.Invocations.Should().BeEmpty();
    }

    [Fact]
    public async Task BatchCreate_Empty_MakesNoCalls()
    {
        var res = await Service().BatchCreateAsync("Account", Array.Empty<IDictionary<string, object?>>());
        res.TotalCount.Should().Be(0);
        _http.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task BatchDelete_Small_UsesCompositeDelete()
    {
        _http.Default = _ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""[{"id":"001A","success":true,"errors":[]},{"id":"001B","success":true,"errors":[]}]""")
        };
        var res = await Service().BatchDeleteAsync("Account", new[] { "001A", "001B" });
        res.AllSucceeded.Should().BeTrue();
        var req = _http.Requests.Should().ContainSingle().Which;
        req.Method.Should().Be(HttpMethod.Delete);
        HttpUtility.UrlDecode(req.Uri.Query).Should().Contain("001A,001B");
    }

    [Fact]
    public async Task Create_SendsPayloadUnchanged_SalesforceDecidesWhatIsWriteable()
    {
        _http.Enqueue(HttpStatusCode.Created, """{"id":"001NEW","success":true,"errors":[]}""");
        await Service().CreateRecordAsync("Account", new Dictionary<string, object?> { ["Name"] = "Acme", ["SystemModstamp"] = "x" });
        _http.Requests.Should().ContainSingle("no describe call is made on write in 2.0");
        var body = JsonNode.Parse(_http.Requests.Single().Body!)!.AsObject();
        body.ContainsKey("Name").Should().BeTrue();
        body.ContainsKey("SystemModstamp").Should().BeTrue();
    }

    [Fact]
    public async Task Create_SalesforceFlsRejection_SurfacesAsTypedException()
    {
        _http.Enqueue(HttpStatusCode.BadRequest, """[{"message":"Unable to create/update fields: SystemModstamp.","errorCode":"INVALID_FIELD_FOR_INSERT_UPDATE","fields":["SystemModstamp"]}]""");
        var act = () => Service().CreateRecordAsync("Account", new Dictionary<string, object?> { ["SystemModstamp"] = "x" });
        var ex = await act.Should().ThrowAsync<SalesforceCore.Models.Errors.SalesforceFieldSecurityException>();
        ex.Which.ErrorCode.Should().Be("INVALID_FIELD_FOR_INSERT_UPDATE");
        ex.Which.FieldName.Should().Be("SystemModstamp");
        ex.Which.HttpStatusCode.Should().Be(400);
    }
}
