using System.Net;
using System.Security.Claims;
using System.Text.Json.Nodes;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Razor.TagHelpers;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using SalesforceCore.AspNetCore.Authentication;
using SalesforceCore.AspNetCore.TagHelpers;
using SalesforceCore.Attributes;
using SalesforceCore.Mapping;
using SalesforceCore.Models.Data;
using SalesforceCore.Models.Errors;
using SalesforceCore.Models.Metadata;
using SalesforceCore.Query;
using SalesforceCore.Schema;
using SalesforceCore.Services.Authorization;
using SalesforceCore.Services.Caching;
using SalesforceCore.Services.Data;
using SalesforceCore.Services.Files;
using SalesforceCore.Services.Metadata;
using SalesforceCore.Services.Query;
using SalesforceCore.Tests.TestKit;
using Xunit;

namespace SalesforceCore.Tests.V21;

[SalesforceObject("Account")]
public class V21Account
{
    public string? Id { get; set; }
    public string? Name { get; set; }
    public int? NumberOfEmployees { get; set; }
}

[SalesforceObject("Contact")]
public class V21Contact
{
    public string? Id { get; set; }
    [SalesforceField("LastName")] public string? LastName { get; set; }
    public string? Nickname { get; set; }
    [SalesforceExternalId] [SalesforceField("Ext__c")] public string? Ext { get; set; }
}

/// <summary>In-memory executor proving the LINQ provider works without IDataService (Query no longer depends on Data).</summary>
internal sealed class FakeExecutor : ISoqlQueryExecutor
{
    public List<string> Soql { get; } = new();
    public List<JsonObject> Rows { get; } = new();
    public int Delay { get; set; }

    public async Task<QueryResult> QueryAsync(string soql, CancellationToken cancellationToken = default)
    {
        Soql.Add(soql);
        if (Delay > 0) await Task.Delay(Delay, cancellationToken);
        if (soql.Contains("COUNT()", StringComparison.OrdinalIgnoreCase))
            return new QueryResult { TotalSize = Rows.Count, Done = true };
        return new QueryResult { Records = Rows.Select(r => (JsonObject)r.DeepClone()).ToList(), TotalSize = Rows.Count, Done = true };
    }

    public async Task<List<JsonObject>> QueryAllAsync(string soql, CancellationToken cancellationToken = default)
        => (await QueryAsync(soql, cancellationToken)).Records;

    public async IAsyncEnumerable<JsonObject> QueryAllAsyncEnumerable(string soql, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        foreach (var r in (await QueryAsync(soql, cancellationToken)).Records) yield return r;
    }
}

/// <summary>Single-threaded SynchronizationContext like WinForms/WPF/legacy ASP.NET: blocking on it deadlocks naive sync-over-async.</summary>
internal sealed class SingleThreadContext : SynchronizationContext
{
    private readonly System.Collections.Concurrent.BlockingCollection<(SendOrPostCallback, object?)> _q = new();
    public override void Post(SendOrPostCallback d, object? state) => _q.Add((d, state));

    public static T Run<T>(Func<T> func)
    {
        var ctx = new SingleThreadContext();
        var prev = Current;
        SetSynchronizationContext(ctx);
        try
        {
            T result = default!;
            Exception? error = null;
            ctx.Post(_ => { try { result = func(); } catch (Exception e) { error = e; } finally { ctx._q.CompleteAdding(); } }, null);
            foreach (var (d, s) in ctx._q.GetConsumingEnumerable()) d(s);
            if (error != null) throw error;
            return result;
        }
        finally { SetSynchronizationContext(prev); }
    }
}

public class LinqProviderV21Tests
{
    private static JsonObject Acc(string id, string name, int emp) => SalesforceJson.Record("Account", id, ("Name", name), ("NumberOfEmployees", emp));

    [Fact]
    public async Task Queryable_RunsOnAnyExecutor()
    {
        var ex = new FakeExecutor();
        ex.Rows.Add(Acc("001000000000001AAA", "Acme", 10));
        var list = await new SalesforceQueryable<V21Account>(ex).Where(a => a.Name == "Acme").OrderBy(a => a.Name).Take(5).ToListAsync();
        list.Should().ContainSingle().Which.Name.Should().Be("Acme");
        ex.Soql.Single().Should().Contain("FROM Account").And.Contain("WHERE").And.Contain("ORDER BY Name").And.Contain("LIMIT 5");
    }

    [Fact]
    public void Queryable_ExtensionMethod_WorksOnExecutor()
    {
        ISoqlQueryExecutor ex = new FakeExecutor();
        ex.Query<V21Account>().Should().NotBeNull();
    }

    [Fact]
    public async Task SyncToList_UnderSingleThreadedContext_DoesNotDeadlock()
    {
        var ex = new FakeExecutor { Delay = 20 };
        ex.Rows.Add(Acc("001000000000001AAA", "Acme", 10));
        var task = Task.Run(() => SingleThreadContext.Run(() => new SalesforceQueryable<V21Account>(ex).Where(a => a.NumberOfEmployees > 1).ToList()));
        // A deadlock surfaces as TimeoutException here.
        (await task.WaitAsync(TimeSpan.FromSeconds(10))).Should().ContainSingle();
    }

    [Fact]
    public async Task SyncFirst_UnderContext_ReturnsValue()
    {
        var ex = new FakeExecutor { Delay = 5 };
        ex.Rows.Add(Acc("001000000000001AAA", "Acme", 10));
        var task = Task.Run(() => SingleThreadContext.Run(() => new SalesforceQueryable<V21Account>(ex).First()));
        (await task.WaitAsync(TimeSpan.FromSeconds(10))).Name.Should().Be("Acme");
    }

    [Fact]
    public void SyncExecute_ExceptionsAreUnwrapped()
    {
        var ex = new Mock<ISoqlQueryExecutor>();
        ex.Setup(e => e.QueryAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ThrowsAsync(new SalesforceException("boom"));
        ex.Setup(e => e.QueryAllAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ThrowsAsync(new SalesforceException("boom"));
        var act = () => new SalesforceQueryable<V21Account>(ex.Object).ToList();
        act.Should().Throw<SalesforceException>().WithMessage("boom");
    }

    [Fact]
    public async Task ToListAsync_HonoursCancellation()
    {
        var ex = new FakeExecutor { Delay = 5000 };
        using var cts = new CancellationTokenSource(50);
        var act = () => new SalesforceQueryable<V21Account>(ex).ToListAsync(cts.Token);
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task CountAsync_UsesCountQuery()
    {
        var ex = new FakeExecutor();
        ex.Rows.Add(Acc("001000000000001AAA", "A", 1));
        ex.Rows.Add(Acc("001000000000002AAA", "B", 2));
        (await new SalesforceQueryable<V21Account>(ex).CountAsync()).Should().Be(2);
    }

    [Fact]
    public void Constructor_NullExecutor_Throws()
    {
        var act = () => new SalesforceQueryable<V21Account>((ISoqlQueryExecutor)null!);
        act.Should().Throw<ArgumentNullException>();
    }
}

public class PaginationV21Tests
{
    [Fact]
    public async Task QueryAll_SelfReferencingCursor_ThrowsInsteadOfLoopingForever()
    {
        await using var host = SalesforceTestHost.Create();
        host.Server.On(HttpMethod.Get, "/services/data/v62.0/query/?", _ => FakeSalesforceServer.Json(HttpStatusCode.OK,
            SalesforceJson.QueryPage(new[] { SalesforceJson.Record("Account", "001000000000001AAA") }, "/services/data/v62.0/query/01gLOOP-1", 9)));
        host.Server.On(HttpMethod.Get, "/services/data/v62.0/query/01gLOOP-1", _ => FakeSalesforceServer.Json(HttpStatusCode.OK,
            SalesforceJson.QueryPage(new[] { SalesforceJson.Record("Account", "001000000000002AAA") }, "/services/data/v62.0/query/01gLOOP-1", 9)));

        var act = () => host.Get<IDataService>().QueryAllAsync("SELECT Id FROM Account");
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*already fetched*");
        host.Server.Requests.Should().HaveCount(2);
    }

    [Fact]
    public async Task QueryAll_PageWithNullRecords_IsTreatedAsEmpty()
    {
        await using var host = SalesforceTestHost.Create();
        host.Server.OnJson(HttpMethod.Get, "/services/data/v62.0/query/?", new JsonObject { ["totalSize"] = 0, ["done"] = true, ["records"] = null });
        var all = await host.Get<IDataService>().QueryAllAsync("SELECT Id FROM Account");
        all.Should().BeEmpty();
    }

    [Fact]
    public async Task QueryAll_NotDoneButNoCursor_Stops()
    {
        await using var host = SalesforceTestHost.Create();
        host.Server.OnJson(HttpMethod.Get, "/services/data/v62.0/query/?", new JsonObject
        {
            ["totalSize"] = 5, ["done"] = false, ["records"] = new JsonArray(SalesforceJson.Record("Account", "001000000000001AAA"))
        });
        (await host.Get<IDataService>().QueryAllAsync("SELECT Id FROM Account")).Should().ContainSingle();
    }

    [Fact]
    public async Task QueryAllEnumerable_CancelledBetweenPages_Stops()
    {
        await using var host = SalesforceTestHost.Create();
        host.Server.On(HttpMethod.Get, "/services/data/v62.0/query/?", _ => FakeSalesforceServer.Json(HttpStatusCode.OK,
            SalesforceJson.QueryPage(new[] { SalesforceJson.Record("Account", "001000000000001AAA") }, "/services/data/v62.0/query/01gC-1", 2)));
        using var cts = new CancellationTokenSource();
        var act = async () =>
        {
            await foreach (var _ in host.Get<IDataService>().QueryAllAsyncEnumerable("SELECT Id FROM Account", cts.Token))
                cts.Cancel();
        };
        await act.Should().ThrowAsync<OperationCanceledException>();
        host.Server.Requests.Should().HaveCount(1);
    }
}

public class FileServiceV21Tests
{
    private const string Parent = "001000000000001AAA";
    private const string Cv = "068000000000001AAA";
    private const string Doc = "069000000000001AAA";

    [Fact]
    public async Task Upload_PostsContentVersion_WithCleanFileName()
    {
        await using var host = SalesforceTestHost.Create();
        host.Server.OnJson(HttpMethod.Post, "/services/data/v62.0/sobjects/ContentVersion/?", SalesforceJson.Created(Cv), HttpStatusCode.Created);
        var id = await host.Get<IFileService>().UploadAsync(Parent, "C:\\Users\\me\\report.pdf", new byte[] { 1, 2, 3 });
        id.Should().Be(Cv);
        var body = host.Server.Requests.Single().Json;
        body["PathOnClient"]!.GetValue<string>().Should().Be("report.pdf");
        body["Title"]!.GetValue<string>().Should().Be("report");
        body["VersionData"]!.GetValue<string>().Should().Be(Convert.ToBase64String(new byte[] { 1, 2, 3 }));
        body["FirstPublishLocationId"]!.GetValue<string>().Should().Be(Parent);
    }

    [Fact]
    public async Task Upload_DotFile_UsesFullNameAsTitle()
    {
        await using var host = SalesforceTestHost.Create();
        host.Server.OnJson(HttpMethod.Post, "/services/data/v62.0/sobjects/ContentVersion/?", SalesforceJson.Created(Cv), HttpStatusCode.Created);
        await host.Get<IFileService>().UploadAsync(Parent, ".env", new byte[] { 1 });
        host.Server.Requests.Single().Json["Title"]!.GetValue<string>().Should().Be(".env");
    }

    [Fact]
    public async Task Upload_SuccessFalseWithoutErrors_Throws()
    {
        await using var host = SalesforceTestHost.Create();
        host.Server.OnJson(HttpMethod.Post, "/services/data/v62.0/sobjects/ContentVersion/?",
            new JsonObject { ["id"] = "", ["success"] = false, ["errors"] = new JsonArray() }, HttpStatusCode.Created);
        var act = () => host.Get<IFileService>().UploadAsync(Parent, "a.txt", new byte[] { 1 });
        await act.Should().ThrowAsync<SalesforceException>();
    }

    [Theory]
    [InlineData("../../sobjects/User")]
    [InlineData("001")]
    [InlineData("001000000000001AAA/x")]
    public async Task IdsInPaths_AreValidated(string badId)
    {
        await using var host = SalesforceTestHost.Create();
        var files = host.Get<IFileService>();
        await FluentActions.Awaiting(() => files.DownloadAsync(badId)).Should().ThrowAsync<ArgumentException>();
        await FluentActions.Awaiting(() => files.DeleteAsync(badId)).Should().ThrowAsync<ArgumentException>();
        await FluentActions.Awaiting(() => files.LinkFileAsync(badId, Parent)).Should().ThrowAsync<ArgumentException>();
        await FluentActions.Awaiting(() => files.LinkFileAsync(Doc, badId)).Should().ThrowAsync<ArgumentException>();
        await FluentActions.Awaiting(() => files.UploadAsync(badId, "a.txt", new byte[] { 1 })).Should().ThrowAsync<ArgumentException>();
        await FluentActions.Awaiting(() => files.GetFilesAsync(badId)).Should().ThrowAsync<ArgumentException>();
        host.Server.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task Upload_NonSeekableStreamOverLimit_ThrowsWithoutUnboundedBuffering()
    {
        await using var host = SalesforceTestHost.Create();
        var stream = new NonSeekable(26L * 1024 * 1024);
        var act = () => host.Get<IFileService>().UploadAsync(Parent, "big.bin", stream);
        await act.Should().ThrowAsync<ArgumentException>().WithMessage("*exceeds*");
        stream.BytesRead.Should().BeLessThan(26L * 1024 * 1024);
        host.Server.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task Upload_SeekableStreamOverLimit_FailsFast()
    {
        await using var host = SalesforceTestHost.Create();
        var ms = new Mock<Stream>();
        ms.SetupGet(s => s.CanSeek).Returns(true);
        ms.SetupGet(s => s.Length).Returns(30L * 1024 * 1024);
        ms.SetupGet(s => s.Position).Returns(0);
        var act = () => host.Get<IFileService>().UploadAsync(Parent, "big.bin", ms.Object);
        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task Upload_Stream_Works()
    {
        await using var host = SalesforceTestHost.Create();
        host.Server.OnJson(HttpMethod.Post, "/services/data/v62.0/sobjects/ContentVersion/?", SalesforceJson.Created(Cv), HttpStatusCode.Created);
        (await host.Get<IFileService>().UploadAsync(Parent, "a.txt", new MemoryStream(new byte[] { 9, 9 }))).Should().Be(Cv);
    }

    [Fact]
    public async Task GetFileMetadata_ParsesStringVersionNumberAndSize()
    {
        await using var host = SalesforceTestHost.Create();
        host.Server.OnJson(HttpMethod.Get, "/services/data/v62.0/query/?", SalesforceJson.QueryPage(new[]
        {
            SalesforceJson.Record("ContentVersion", Cv, ("ContentDocumentId", Doc), ("Title", "T"), ("FileExtension", "pdf"),
                ("ContentSize", 1234), ("FileType", "PDF"), ("CreatedDate", "2026-01-02T03:04:05.000+0000"), ("VersionNumber", "3"))
        }));
        var meta = await host.Get<IFileService>().GetFileMetadataAsync(Cv);
        meta!.VersionNumber.Should().Be(3);
        meta.ContentSize.Should().Be(1234);
        meta.ContentDocumentId.Should().Be(Doc);
        meta.CreatedDate.Year.Should().Be(2026);
    }

    [Fact]
    public async Task GetFileMetadata_NoRows_ReturnsNull()
    {
        await using var host = SalesforceTestHost.Create();
        host.Server.OnJson(HttpMethod.Get, "/services/data/v62.0/query/?", SalesforceJson.QueryPage(Array.Empty<JsonObject>()));
        (await host.Get<IFileService>().GetFileMetadataAsync(Cv)).Should().BeNull();
    }

    [Fact]
    public async Task DownloadToStream_WritesBytes()
    {
        await using var host = SalesforceTestHost.Create();
        host.Server.On(HttpMethod.Get, $"/services/data/v62.0/sobjects/ContentVersion/{Cv}/VersionData",
            _ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(new byte[] { 7, 8 }) });
        var dest = new MemoryStream();
        await host.Get<IFileService>().DownloadToStreamAsync(Cv, dest);
        dest.ToArray().Should().Equal(7, 8);
    }

    [Fact]
    public async Task Delete_And_Link_HitExpectedEndpoints()
    {
        await using var host = SalesforceTestHost.Create();
        host.Server.On(HttpMethod.Delete, $"/services/data/v62.0/sobjects/ContentDocument/{Doc}", _ => FakeSalesforceServer.Empty());
        host.Server.OnJson(HttpMethod.Post, "/services/data/v62.0/sobjects/ContentDocumentLink/?", SalesforceJson.Created("06A000000000001AAA"), HttpStatusCode.Created);
        var files = host.Get<IFileService>();
        await files.DeleteAsync(Doc);
        (await files.LinkFileAsync(Doc, Parent, "I", "InternalUsers")).Should().Be("06A000000000001AAA");
        var link = host.Server.RequestsTo("/services/data/v62.0/sobjects/ContentDocumentLink/?").Single().Json;
        link["ShareType"]!.GetValue<string>().Should().Be("I");
        link["Visibility"]!.GetValue<string>().Should().Be("InternalUsers");
    }

    [Theory]
    [InlineData("5", 5L)]
    [InlineData(" x", null)]
    public void ReadLong_HandlesStrings(string input, long? expected)
        => FileService.ReadLong(JsonValue.Create(input)).Should().Be(expected);

    [Fact]
    public void ReadLong_HandlesNumbersAndNull()
    {
        FileService.ReadLong(JsonValue.Create(12.0)).Should().Be(12);
        FileService.ReadLong(JsonValue.Create(7L)).Should().Be(7);
        FileService.ReadLong(null).Should().BeNull();
        FileService.ReadLong(new JsonObject()).Should().BeNull();
    }

    private sealed class NonSeekable(long length) : Stream
    {
        public long BytesRead { get; private set; }
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count)
        {
            var n = (int)Math.Min(count, length - BytesRead);
            BytesRead += n;
            return n;
        }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}

public class LookupServiceV21Tests
{
    private static SalesforceTestHost Host(Action<Microsoft.Extensions.DependencyInjection.IServiceCollection>? services = null)
    {
        var host = SalesforceTestHost.Create(services: services);
        host.Server.OnJson(HttpMethod.Get, "/services/data/v62.0/sobjects/Account/describe/?",
            new DescribeBuilder("Account").NameField().Field("Industry").Field("Phone").Build());
        return host;
    }

    private static JsonObject Page(params string[] names) => SalesforceJson.QueryPage(
        names.Select((n, i) => SalesforceJson.Record("Account", $"001{i:D15}", ("Name", n), ("Industry", "Tech"))).ToArray());

    [Fact]
    public async Task Search_EscapesUserWildcards()
    {
        await using var host = Host();
        host.Server.OnJson(HttpMethod.Get, "/services/data/v62.0/query/?", Page("100% Acme"));
        await host.Get<ILookupService>().SearchAsync(new LookupSearchOptions { TargetObject = "Account", Query = "100%_x", SearchFields = new() { "Name" } });
        var soql = host.Server.RequestsTo("/services/data/v62.0/query/?").Single().Query("q")!;
        soql.Should().Contain("LIKE '%100\\%\\_x%'");
    }

    [Fact]
    public async Task Search_ScoresAndSortsResults()
    {
        await using var host = Host();
        host.Server.OnJson(HttpMethod.Get, "/services/data/v62.0/query/?", Page("Big Acme", "Acme", "Acme Corp"));
        var r = await host.Get<ILookupService>().SearchAsync(new LookupSearchOptions { TargetObject = "Account", Query = "acme" });
        r.Items.Select(i => i.DisplayName).Should().Equal("Acme", "Acme Corp", "Big Acme");
        r.Items[0].ObjectType.Should().Be("Account");
    }

    [Fact]
    public async Task Search_HasMore_UsesClampedLimit()
    {
        await using var host = Host();
        host.Server.OnJson(HttpMethod.Get, "/services/data/v62.0/query/?", Page("A"));
        var r = await host.Get<ILookupService>().SearchAsync(new LookupSearchOptions { TargetObject = "Account", Query = "a", Limit = 0 });
        r.HasMore.Should().BeTrue("limit 0 is clamped to 1 and one row came back");
        host.Server.Requests.Last().Query("q").Should().EndWith("LIMIT 1");

        var r2 = await host.Get<ILookupService>().SearchAsync(new LookupSearchOptions { TargetObject = "Account", Query = "b", Limit = 500 });
        r2.HasMore.Should().BeFalse();
        host.Server.Requests.Last().Query("q").Should().EndWith("LIMIT 200");
    }

    [Fact]
    public async Task Search_CachesSecondCall_ButNotWhenFieldsDiffer()
    {
        await using var host = Host();
        host.Server.OnJson(HttpMethod.Get, "/services/data/v62.0/query/?", Page("Acme"));
        var svc = host.Get<ILookupService>();
        await svc.SearchAsync(new LookupSearchOptions { TargetObject = "Account", Query = "acme" });
        await svc.SearchAsync(new LookupSearchOptions { TargetObject = "Account", Query = "ACME" });
        host.Server.RequestsTo("/services/data/v62.0/query/?").Should().HaveCount(1);

        await svc.SearchAsync(new LookupSearchOptions { TargetObject = "Account", Query = "acme", ContextFields = new() { "Phone" } });
        host.Server.RequestsTo("/services/data/v62.0/query/?").Should().HaveCount(2);
    }

    [Fact]
    public void CacheKey_EqualFilters_ShareKey_AndUserInputIsHashed()
    {
        var a = new LookupSearchOptions { TargetObject = "Account", Query = "x", Filter = SoqlCondition.Equals("Industry", "Tech") };
        var b = new LookupSearchOptions { TargetObject = "Account", Query = "x", Filter = SoqlCondition.Equals("Industry", "Tech") };
        var c = new LookupSearchOptions { TargetObject = "Account", Query = "x", Filter = SoqlCondition.Equals("Industry", "Bank") };
        LookupService.GenerateSearchCacheKey(a, 10).Should().Be(LookupService.GenerateSearchCacheKey(b, 10));
        LookupService.GenerateSearchCacheKey(a, 10).Should().NotBe(LookupService.GenerateSearchCacheKey(c, 10));
        var big = new LookupSearchOptions { TargetObject = "Account", Query = new string('q', 5000) };
        LookupService.GenerateSearchCacheKey(big, 10).Length.Should().BeLessThan(80);
    }

    [Fact]
    public async Task Search_CacheIsPartitionedByUser()
    {
        var user = new MutableUser();
        await using var host = Host(s => s.AddSingleton<IUserContextProvider>(user));
        host.Server.OnJson(HttpMethod.Get, "/services/data/v62.0/query/?", Page("Acme"));
        var svc = host.Get<ILookupService>();
        user.Id = "005A";
        await svc.SearchAsync(new LookupSearchOptions { TargetObject = "Account", Query = "acme" });
        user.Id = "005B";
        await svc.SearchAsync(new LookupSearchOptions { TargetObject = "Account", Query = "acme" });
        host.Server.RequestsTo("/services/data/v62.0/query/?").Should().HaveCount(2, "user B must not be served user A's sharing-filtered rows");
    }

    [Theory]
    [InlineData("Account; DELETE")]
    [InlineData("")]
    public async Task Search_InvalidObject_Throws(string obj)
    {
        await using var host = Host();
        var act = () => host.Get<ILookupService>().SearchAsync(new LookupSearchOptions { TargetObject = obj, Query = "x" });
        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task Search_NullOptions_Throws()
    {
        await using var host = Host();
        await FluentActions.Awaiting(() => host.Get<ILookupService>().SearchAsync(null!)).Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task Search_ServerError_ReturnsEmptyResult()
    {
        await using var host = Host();
        host.Server.Error(HttpMethod.Get, "/services/data/v62.0/query/?", HttpStatusCode.BadRequest, "INVALID_FIELD", "nope");
        var r = await host.Get<ILookupService>().SearchAsync(new LookupSearchOptions { TargetObject = "Account", Query = "x" });
        r.Items.Should().BeEmpty();
    }

    [Fact]
    public async Task Search_Cancelled_Propagates()
    {
        await using var host = Host();
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var act = () => host.Get<ILookupService>().SearchAsync(new LookupSearchOptions { TargetObject = "Account", Query = "x" }, cts.Token);
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task Polymorphic_DeduplicatesTargets()
    {
        await using var host = Host();
        host.Server.OnJson(HttpMethod.Get, "/services/data/v62.0/query/?", Page("Acme"));
        var r = await host.Get<ILookupService>().SearchPolymorphicAsync(new[] { "Account", "account", " " }, "acme", 3);
        r.Items.Should().ContainSingle();
    }

    [Fact]
    public async Task RecentItems_ValidatesAndMaps()
    {
        await using var host = Host();
        host.Server.OnJson(HttpMethod.Get, "/services/data/v62.0/query/?", Page("Recent"));
        var svc = host.Get<ILookupService>();
        var items = await svc.GetRecentItemsAsync("Account", 3);
        items.Should().ContainSingle().Which.IsRecent.Should().BeTrue();
        host.Server.Requests.Last().Query("q").Should().Contain("ORDER BY LastViewedDate DESC NULLS LAST").And.EndWith("LIMIT 3");
        await FluentActions.Awaiting(() => svc.GetRecentItemsAsync("Account", 0)).Should().ThrowAsync<ArgumentOutOfRangeException>();
        await FluentActions.Awaiting(() => svc.GetRecentItemsAsync("Bad Name", 3)).Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task SearchWithConfig_AppliesDependentParentFilter()
    {
        await using var host = Host();
        host.Server.OnJson(HttpMethod.Get, "/services/data/v62.0/query/?", Page("Child"));
        var config = new SalesforceCore.Models.Configuration.RelationshipConfig { DependsOn = "ParentId" };
        await host.Get<ILookupService>().SearchWithConfigAsync("Account", "c", config, new Dictionary<string, string> { ["ParentId"] = "001000000000009AAA" });
        host.Server.Requests.Last().Query("q").Should().Contain("ParentId = '001000000000009AAA'");
    }

    [Theory]
    [InlineData("Acme", "acme", 100)]
    [InlineData("Acme Corp", "acme", 80)]
    [InlineData("Big Acme", "acme", 60)]
    [InlineData("MegaAcme", "acme", 40)]
    [InlineData("Other", "acme", 0)]
    [InlineData("", "acme", 0)]
    public async Task CalculateScore_Tiers(string name, string q, double expected)
    {
        await using var host = Host();
        var w = new SalesforceCore.Models.Configuration.LookupWeights();
        var score = host.Get<ILookupService>().CalculateScore(name, q);
        var map = new Dictionary<double, double> { [100] = w.ExactMatch, [80] = w.StartsWith, [60] = w.WordStartsWith, [40] = w.Contains, [0] = 0 };
        score.Should().Be(map[expected]);
    }

    private sealed class MutableUser : IUserContextProvider
    {
        public string? Id { get; set; }
        public ClaimsPrincipal? GetUser() => Id == null ? null
            : new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.NameIdentifier, Id) }, "test"));
    }
}

public class SchemaCacheV21Tests
{
    [Fact]
    public async Task Describe_IsCachedCaseInsensitively_AndInvalidationClearsBoth()
    {
        await using var host = SalesforceTestHost.Create();
        host.Server.OnJson(HttpMethod.Get, "/services/data/v62.0/sobjects/(?i:account)/describe/?", new DescribeBuilder("Account").NameField().Build());
        var schema = host.Get<ISchemaService>();
        await schema.GetDescribeAsync("Account");
        await schema.GetDescribeAsync("account");
        host.Server.Requests.Should().HaveCount(1);

        await schema.InvalidateCacheAsync("ACCOUNT", CancellationToken.None);
        await schema.GetDescribeAsync("Account");
        host.Server.Requests.Should().HaveCount(2);
    }
}

public class MapperModeV21Tests
{
    [Fact]
    public void UseFieldAttributeMode_IsScopedToTheAsyncFlow()
    {
        SalesforceMapper.IsSalesforceFieldAttributeRequired.Should().BeFalse();
        SalesforceMapper.GetQueryableFields<V21Contact>().Should().Contain("Nickname");
        using (SalesforceMapper.UseFieldAttributeMode(true))
        {
            SalesforceMapper.IsSalesforceFieldAttributeRequired.Should().BeTrue();
            SalesforceMapper.GetQueryableFields<V21Contact>().Should().NotContain("Nickname").And.Contain("LastName").And.Contain("Id");
        }
        SalesforceMapper.IsSalesforceFieldAttributeRequired.Should().BeFalse();
        SalesforceMapper.GetQueryableFields<V21Contact>().Should().Contain("Nickname");
    }

    [Fact]
    public async Task UseFieldAttributeMode_DoesNotLeakToParallelFlows()
    {
        var gate = new TaskCompletionSource();
        var strictTask = Task.Run(async () =>
        {
            using var _ = SalesforceMapper.UseFieldAttributeMode(true);
            await gate.Task;
            return SalesforceMapper.GetQueryableFields<V21Contact>().ToList();
        });
        var loose = await Task.Run(() => SalesforceMapper.GetQueryableFields<V21Contact>().ToList());
        gate.SetResult();
        (await strictTask).Should().NotContain("Nickname");
        loose.Should().Contain("Nickname");
    }

    [Fact]
    public void Scope_DoubleDispose_IsSafe()
    {
        var s = SalesforceMapper.UseFieldAttributeMode(true);
        s.Dispose();
        s.Dispose();
        SalesforceMapper.IsSalesforceFieldAttributeRequired.Should().BeFalse();
    }
}

public class TypedDataV21Tests
{
    [Fact]
    public async Task Upsert_UsesAtomicExternalIdPatch()
    {
        await using var host = SalesforceTestHost.Create();
        host.Server.OnJson(HttpMethod.Patch, "/services/data/v62.0/sobjects/Contact/Ext__c/E-1", SalesforceJson.Created("003000000000001AAA"), HttpStatusCode.Created);
        var id = await host.Get<ITypedDataService>().UpsertAsync(new V21Contact { LastName = "Doe", Ext = "E-1" });
        id.Should().Be("003000000000001AAA");
        host.Server.Requests.Should().ContainSingle().Which.Method.Should().Be(HttpMethod.Patch);
        host.Server.Requests.Single().Json.AsObject().ContainsKey("Ext__c").Should().BeFalse();
    }

    [Fact]
    public async Task Upsert_WithoutExternalIdValue_Throws()
    {
        await using var host = SalesforceTestHost.Create();
        var act = () => host.Get<ITypedDataService>().UpsertAsync(new V21Contact { LastName = "Doe" });
        await act.Should().ThrowAsync<InvalidOperationException>();
        host.Server.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task GetById_NotFound_ReturnsNull()
    {
        await using var host = SalesforceTestHost.Create();
        var r = await host.Get<ITypedDataService>().GetByIdAsync<V21Account>("001000000000001AAA");
        r.Should().BeNull();
    }

    [Fact]
    public async Task Update_WithoutId_Throws_AndCreate_SendsMappedFields()
    {
        await using var host = SalesforceTestHost.Create();
        host.Server.OnJson(HttpMethod.Post, "/services/data/v62.0/sobjects/Account/?", SalesforceJson.Created("001000000000001AAA"), HttpStatusCode.Created);
        var typed = host.Get<ITypedDataService>();
        await FluentActions.Awaiting(() => typed.UpdateAsync(new V21Account { Name = "x" })).Should().ThrowAsync<InvalidOperationException>();
        (await typed.CreateAsync(new V21Account { Name = "Acme", NumberOfEmployees = 3 })).Should().Be("001000000000001AAA");
        host.Server.Requests.Single().Json["Name"]!.GetValue<string>().Should().Be("Acme");
    }

    [Fact]
    public async Task AnyAndCount_Work()
    {
        await using var host = SalesforceTestHost.Create();
        host.Server.On(HttpMethod.Get, "/services/data/v62.0/query/?", r => FakeSalesforceServer.Json(HttpStatusCode.OK,
            r.Query("q")!.Contains("COUNT()")
                ? new JsonObject { ["totalSize"] = 4, ["done"] = true, ["records"] = new JsonArray() }
                : SalesforceJson.QueryPage(new[] { SalesforceJson.Record("Account", "001000000000001AAA", ("Name", "A")) })));
        var typed = host.Get<ITypedDataService>();
        (await typed.CountAsync<V21Account>(a => a.Name != null)).Should().Be(4);
        (await typed.AnyAsync<V21Account>()).Should().BeTrue();
        (await typed.GetAsync<V21Account>(a => a.Name == "A"))!.Name.Should().Be("A");
        (await typed.GetAllAsync<V21Account>()).Should().ContainSingle();
    }
}

public class TicketStoreV21Tests
{
    private static DistributedCacheTicketStore Store(IDistributedCache cache, TimeSpan? timeout = null)
        => new(cache, NullLogger<DistributedCacheTicketStore>.Instance, timeout,
            Microsoft.AspNetCore.DataProtection.DataProtectionProvider.Create("sfc-tests"));

    private static AuthenticationTicket Ticket(DateTimeOffset? expires = null)
    {
        var props = new AuthenticationProperties { ExpiresUtc = expires };
        return new AuthenticationTicket(new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim("sub", "u1") }, "c")), props, "Cookies");
    }

    [Fact]
    public async Task RoundTrip_WithCancellationOverloads()
    {
        var cache = new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions()));
        var store = Store(cache);
        var key = await store.StoreAsync(Ticket(), CancellationToken.None);
        (await store.RetrieveAsync(key, CancellationToken.None))!.Principal.FindFirst("sub")!.Value.Should().Be("u1");
        await store.RemoveAsync(key, CancellationToken.None);
        (await store.RetrieveAsync(key)).Should().BeNull();
    }

    [Fact]
    public void EntryOptions_CappedByTicketExpiry()
    {
        var store = Store(Mock.Of<IDistributedCache>(), TimeSpan.FromHours(1));
        var expires = DateTimeOffset.UtcNow.AddMinutes(5);
        var o = store.EntryOptionsFor(Ticket(expires));
        o.AbsoluteExpiration!.Value.Should().BeCloseTo(expires, TimeSpan.FromSeconds(1));
        o.SlidingExpiration.Should().Be(TimeSpan.FromHours(1));
        store.EntryOptionsFor(Ticket()).AbsoluteExpiration.Should().BeNull();
    }

    [Fact]
    public async Task CorruptBytes_ReturnNull_InsteadOfThrowing()
    {
        var cache = new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions()));
        var store = new DistributedCacheTicketStore(cache, NullLogger<DistributedCacheTicketStore>.Instance);
        await cache.SetAsync("SalesforceAuth:k", new byte[] { 1, 2, 3, 4 });
        (await store.RetrieveAsync("k")).Should().BeNull();
        (await store.RetrieveAsync("")).Should().BeNull();
        await store.RemoveAsync("");
    }

    [Fact]
    public async Task Cancellation_FlowsToCache()
    {
        var cache = new Mock<IDistributedCache>();
        using var cts = new CancellationTokenSource();
        await Store(cache.Object).RemoveAsync("k", cts.Token);
        cache.Verify(c => c.RemoveAsync("SalesforceAuth:k", cts.Token), Times.Once);
    }
}

public class PicklistTagHelperV21Tests
{
    private static (SalesforcePicklistTagHelper, Mock<ISchemaService>) Create()
    {
        var schema = new Mock<ISchemaService>();
        schema.Setup(s => s.GetFieldMapAsync("Account", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, SObjectField>(StringComparer.OrdinalIgnoreCase) { ["Industry__c"] = new SObjectField { Name = "Industry__c" } });
        schema.Setup(s => s.GetPicklistValuesAsync("Account", "Industry__c", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PicklistValuesResult
            {
                Values = new()
                {
                    new PicklistEntry { Value = "Tech", Label = "Tech <b>", Active = true },
                    new PicklistEntry { Value = "Bank", Label = "Bank", Active = true },
                    new PicklistEntry { Value = "Old", Label = "Old", Active = false }
                }
            });
        return (new SalesforcePicklistTagHelper(schema.Object, Mock.Of<IRecordTypeManager>()) { ObjectName = "Account", FieldName = "Industry__c" }, schema);
    }

    private static TagHelperOutput Output() => new("select", new TagHelperAttributeList(), (_, _) => Task.FromResult<TagHelperContent>(new DefaultTagHelperContent()));
    private static TagHelperContext Ctx() => new(new TagHelperAttributeList(), new Dictionary<object, object>(), "t");

    private static ModelExpression For(object? model)
    {
        var provider = new EmptyModelMetadataProvider();
        return new ModelExpression("Industry__c", provider.GetModelExplorerForType(model?.GetType() ?? typeof(string), model));
    }

    [Fact]
    public async Task MultiSelect_BoundToCollection_PreselectsValues()
    {
        var (th, _) = Create();
        th.Multiple = true;
        th.For = For(new List<string> { "Tech", "Bank" });
        var output = Output();
        await th.ProcessAsync(Ctx(), output);
        var html = output.Content.GetContent();
        html.Should().Contain("<option value=\"Tech\" selected>").And.Contain("<option value=\"Bank\" selected>");
        output.Attributes["multiple"].Should().NotBeNull();
    }

    [Fact]
    public async Task SingleSelect_EncodesLabels_SkipsInactive_AddsBlank()
    {
        var (th, _) = Create();
        th.For = For("Bank");
        var output = Output();
        await th.ProcessAsync(Ctx(), output);
        var html = output.Content.GetContent();
        html.Should().Contain("Tech &lt;b&gt;").And.Contain("<option value=\"Bank\" selected>").And.NotContain("Old");
        html.Should().StartWith("<option value=\"\">");
    }

    [Fact]
    public async Task SchemaFailure_RendersEmptySelect()
    {
        var (th, schema) = Create();
        schema.Setup(s => s.GetFieldMapAsync("Account", It.IsAny<CancellationToken>())).ThrowsAsync(new SalesforceException("down"));
        th.IncludeBlank = false;
        var output = Output();
        await th.ProcessAsync(Ctx(), output);
        output.Content.GetContent().Should().BeEmpty();
        output.TagName.Should().Be("select");
    }
}

public class ModelGeneratorV21Tests
{
    [Theory]
    [InlineData("../sobjects")]
    [InlineData("Account/describe?x")]
    [InlineData("")]
    public async Task DescribeObject_RejectsInvalidNames_BeforeAnyRequest(string name)
    {
        using var gen = new SalesforceCore.ModelGenerator.ModelGenerator("https://acme.my.salesforce.com", "tok", "v62.0");
        var act = () => gen.DescribeObjectAsync(name, fieldsOnly: true);
        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task Generate_IsCancellable()
    {
        using var gen = new SalesforceCore.ModelGenerator.ModelGenerator("https://acme.my.salesforce.com", "tok", "v62.0");
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var act = () => gen.GenerateAsync(new[] { "*" }, new SalesforceCore.ModelGenerator.GeneratorOptions { OutputDirectory = Path.GetTempPath() }, cts.Token);
        await act.Should().ThrowAsync<OperationCanceledException>();
    }
}

public class EncryptedSqlCacheV21Tests
{
    private static readonly string Key = Convert.ToBase64String(Enumerable.Range(1, 32).Select(i => (byte)i).ToArray());

    private static SalesforceCore.Services.Caching.SqlServer.EncryptedSqlServerCache Create(IServiceScopeFactory? scopes = null, string? key = null, bool allowDerived = false)
        => new(scopes ?? Mock.Of<IServiceScopeFactory>(),
            Options.Create(new SalesforceCore.Models.Configuration.SalesforceOptions { SqlCacheEncryptionKey = key ?? Key, AllowInsecureSqlCacheKeyDerivation = allowDerived }),
            NullLogger<SalesforceCore.Services.Caching.SqlServer.EncryptedSqlServerCache>.Instance);

    [Fact]
    public void Encrypt_RoundTrips_AndIsBoundToKey()
    {
        var cache = Create();
        var plain = System.Text.Encoding.UTF8.GetBytes("secret token");
        var enc = cache.Encrypt(plain, "k1");
        enc.Should().NotEqual(plain);
        cache.Decrypt(enc, "k1").Should().Equal(plain);
        FluentActions.Invoking(() => cache.Decrypt(enc, "k2")).Should().Throw<System.Security.Cryptography.CryptographicException>();
        cache.Encrypt(plain, "k1").Should().NotEqual(enc, "a fresh nonce is used per write");
    }

    [Fact]
    public void Decrypt_TamperedOrShort_Throws()
    {
        var cache = Create();
        var enc = cache.Encrypt(new byte[] { 1, 2, 3 }, "k");
        enc[^1] ^= 0xFF;
        FluentActions.Invoking(() => cache.Decrypt(enc, "k")).Should().Throw<System.Security.Cryptography.CryptographicException>();
        FluentActions.Invoking(() => cache.Decrypt(new byte[5], "k")).Should().Throw<System.Security.Cryptography.CryptographicException>();
    }

    [Fact]
    public void Constructor_ValidatesKey()
    {
        FluentActions.Invoking(() => Create(key: Convert.ToBase64String(new byte[16]))).Should().Throw<InvalidOperationException>().WithMessage("*32 bytes*");
        FluentActions.Invoking(() => Create(key: "")).Should().Throw<InvalidOperationException>().WithMessage("*required*");
        FluentActions.Invoking(() => Create(key: "", allowDerived: true)).Should().NotThrow();
    }

    [Fact]
    public void CalculateExpiration_SubSecondSliding_DoesNotExpireImmediately()
    {
        var now = DateTimeOffset.UtcNow;
        var (exp, sliding, abs) = SalesforceCore.Services.Caching.SqlServer.EncryptedSqlServerCache.CalculateExpiration(
            new DistributedCacheEntryOptions { SlidingExpiration = TimeSpan.FromMilliseconds(300) }, now);
        sliding.Should().Be(1);
        exp.Should().BeAfter(now);
        abs.Should().BeNull();
    }

    [Fact]
    public void CalculateExpiration_SlidingCappedByAbsolute_AndDefaultOneHour()
    {
        var now = DateTimeOffset.UtcNow;
        var (exp, _, _) = SalesforceCore.Services.Caching.SqlServer.EncryptedSqlServerCache.CalculateExpiration(
            new DistributedCacheEntryOptions { SlidingExpiration = TimeSpan.FromHours(2), AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(10) }, now);
        exp.Should().Be(now.AddMinutes(10));
        var (def, _, defAbs) = SalesforceCore.Services.Caching.SqlServer.EncryptedSqlServerCache.CalculateExpiration(new DistributedCacheEntryOptions(), now);
        def.Should().Be(now.AddHours(1));
        defAbs.Should().Be(def);
    }

    [Fact]
    public async Task Get_StoreFailure_ReturnsNull_ButCancellationPropagates()
    {
        var scopes = new Mock<IServiceScopeFactory>();
        scopes.Setup(s => s.CreateScope()).Throws(new InvalidOperationException("db down"));
        var cache = Create(scopes.Object);
        (await cache.GetAsync("k")).Should().BeNull();
        cache.Get("k").Should().BeNull();

        using var cts = new CancellationTokenSource();
        cts.Cancel();
        scopes.Setup(s => s.CreateScope()).Throws(new OperationCanceledException(cts.Token));
        await FluentActions.Awaiting(() => cache.GetAsync("k", cts.Token)).Should().ThrowAsync<OperationCanceledException>();
        await FluentActions.Awaiting(() => cache.RemoveAsync("k", cts.Token)).Should().ThrowAsync<OperationCanceledException>();
        await FluentActions.Awaiting(() => cache.RefreshAsync("k", cts.Token)).Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task Set_StoreFailure_Throws_AndNullArgsRejected()
    {
        var scopes = new Mock<IServiceScopeFactory>();
        scopes.Setup(s => s.CreateScope()).Throws(new InvalidOperationException("db down"));
        var cache = Create(scopes.Object);
        await FluentActions.Awaiting(() => cache.SetAsync("k", new byte[] { 1 }, new DistributedCacheEntryOptions())).Should().ThrowAsync<InvalidOperationException>();
        FluentActions.Invoking(() => cache.Set("k", new byte[] { 1 }, new DistributedCacheEntryOptions())).Should().Throw<InvalidOperationException>();
        await FluentActions.Awaiting(() => cache.GetAsync(null!)).Should().ThrowAsync<ArgumentNullException>();
        await FluentActions.Awaiting(() => cache.SetAsync("k", null!, new DistributedCacheEntryOptions())).Should().ThrowAsync<ArgumentNullException>();
        cache.Invoking(c => c.Remove("k")).Should().NotThrow();
        cache.Invoking(c => c.Refresh("k")).Should().NotThrow();
    }
}

public class ModelGeneratorCodegenV21Tests
{
    private static SObjectDescribe Describe() => new()
    {
        Name = "Invoice__c", Label = "Invoice", Queryable = true, Createable = true, Updateable = true, Deletable = true,
        Fields = new()
        {
            new SObjectField { Name = "Id", Type = "id", Label = "Id" },
            new SObjectField { Name = "Amount__c", Type = "currency", Label = "Amount", Createable = true, Updateable = true, Nillable = true, InlineHelpText = "Total <incl tax>" },
            new SObjectField { Name = "Account__c", Type = "reference", Label = "Account", ReferenceTo = new() { "Account" }, RelationshipName = "Account__r" },
            new SObjectField { Name = "What__c", Type = "reference", Label = "What", ReferenceTo = new() { "Account", "Opportunity" } },
            new SObjectField { Name = "Status__c", Type = "picklist", Label = "Status", PicklistValues = new() { new PicklistEntry { Value = "Open\"", Label = "Open", Active = true } } },
            new SObjectField { Name = "Tags__c", Type = "multipicklist", Label = "Tags", PicklistValues = new() { new PicklistEntry { Value = "a", Label = "a", Active = true } } },
            new SObjectField { Name = "class", Type = "boolean", Label = "Keyword" },
            new SObjectField { Name = "When__c", Type = "datetime", Label = "When" },
            new SObjectField { Name = "Day__c", Type = "date", Label = "Day" },
            new SObjectField { Name = "Count__c", Type = "int", Label = "Count" },
            new SObjectField { Name = "Pct__c", Type = "percent", Label = "Pct" }
        }
    };

    [Fact]
    public void GenerateClass_EmitsAttributesTypesAndEscapes()
    {
        using var gen = new SalesforceCore.ModelGenerator.ModelGenerator(Mock.Of<SalesforceCore.Services.Core.ISalesforceClient>());
        var code = gen.GenerateClass(Describe(), new SalesforceCore.ModelGenerator.GeneratorOptions { Namespace = "My.Models" });
        code.Should().Contain("namespace My.Models;");
        code.Should().Contain("[SalesforceObject(\"Invoice__c\"");
        code.Should().Contain("Total &lt;incl tax&gt;");
        code.Should().Contain("Open\\\"");
        code.Should().Contain("Polymorphic = true");
        code.Should().Contain("MultiSelect = true");
        code.Should().Contain("@class");
        code.Should().Contain("decimal?").And.Contain("DateTimeOffset").And.Contain("DateOnly").And.Contain("bool");
        code.Should().Contain("[JsonPropertyName(\"class\")]").And.NotContain("[JsonProperty(");
        code.Should().NotContain("RelationshipName = \"\"");
        code.IndexOf(" Id ", StringComparison.Ordinal).Should().BeLessThan(code.IndexOf("Amount", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("X; public class Evil {}")]
    [InlineData("1Bad")]
    [InlineData("")]
    public void GenerateClass_RejectsInjectedNamespace(string ns)
    {
        using var gen = new SalesforceCore.ModelGenerator.ModelGenerator(Mock.Of<SalesforceCore.Services.Core.ISalesforceClient>());
        FluentActions.Invoking(() => gen.GenerateClass(Describe(), new SalesforceCore.ModelGenerator.GeneratorOptions { Namespace = ns }))
            .Should().Throw<ArgumentException>();
    }

    [Fact]
    public async Task GenerateAsync_WritesFiles_ForWildcardQueryableObjects()
    {
        var client = new Mock<SalesforceCore.Services.Core.ISalesforceClient>();
        client.Setup(c => c.GetAsync<GlobalDescribeResult>("/sobjects/", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GlobalDescribeResult { SObjects = new()
            {
                new SObjectInfo { Name = "Invoice__c", Queryable = true, Custom = true },
                new SObjectInfo { Name = "Hidden", Queryable = false }
            } });
        client.Setup(c => c.GetAsync<SObjectDescribe>("/sobjects/Invoice__c/describe", It.IsAny<CancellationToken>())).ReturnsAsync(Describe());
        var dir = Path.Combine(Path.GetTempPath(), "sfgen-" + Guid.NewGuid().ToString("N"));
        try
        {
            using var gen = new SalesforceCore.ModelGenerator.ModelGenerator(client.Object);
            await gen.GenerateAsync(new[] { "*" }, new SalesforceCore.ModelGenerator.GeneratorOptions { OutputDirectory = dir, IncludeCustomObjects = true });
            Directory.GetFiles(dir).Select(Path.GetFileName).Should().BeEquivalentTo(new[] { "Invoice__c.cs" });
            (await File.ReadAllTextAsync(Path.Combine(dir, "Invoice__c.cs"))).Should().Contain("public class Invoice__c");
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
    }

    [Fact]
    public async Task ListObjects_FiltersByWildcardAndCustom()
    {
        var client = new Mock<SalesforceCore.Services.Core.ISalesforceClient>();
        client.Setup(c => c.GetAsync<GlobalDescribeResult>("/sobjects/", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GlobalDescribeResult { SObjects = new()
            {
                new SObjectInfo { Name = "Account" }, new SObjectInfo { Name = "AccountTeam__c", Custom = true }, new SObjectInfo { Name = "Contact" }
            } });
        using var gen = new SalesforceCore.ModelGenerator.ModelGenerator(client.Object);
        (await gen.ListObjectsAsync("Acc*", false)).Select(o => o.Name).Should().Equal("Account", "AccountTeam__c");
        (await gen.ListObjectsAsync(null, true)).Select(o => o.Name).Should().Equal("AccountTeam__c");
    }
}

public class FormTagHelpersV21Tests
{
    private static TagHelperOutput Output(string tag) => new(tag, new TagHelperAttributeList(), (_, _) => Task.FromResult<TagHelperContent>(new DefaultTagHelperContent()));
    private static TagHelperContext Ctx() => new(new TagHelperAttributeList(), new Dictionary<object, object>(), "t");

    private static Mock<ISchemaService> Schema(params SObjectField[] fields)
    {
        var m = new Mock<ISchemaService>();
        m.Setup(s => s.GetFieldMapAsync("Account", It.IsAny<CancellationToken>()))
            .ReturnsAsync(fields.ToDictionary(f => f.Name, StringComparer.OrdinalIgnoreCase));
        return m;
    }

    public class Model { public decimal Amount { get; set; } = 1.5m; public DateTime When { get; set; } = new(2026, 3, 4, 5, 6, 0); public string? Name { get; set; } = "\"><script>"; }

    private static ModelExpression For(string name, object? value, Type type)
        => new(name, new EmptyModelMetadataProvider().GetModelExplorerForType(type, value));

    [Fact]
    public async Task FieldGroup_EncodesInjectedNames_AndUsesInvariantNumbers()
    {
        var prev = System.Globalization.CultureInfo.CurrentCulture;
        System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo("de-DE");
        try
        {
            var th = new SalesforceFieldGroupTagHelper(Schema(new SObjectField { Name = "Amount", Type = "currency", Label = "Amount", Scale = 2 }).Object)
            {
                ObjectName = "Account", FieldName = "Amount", For = For("Amount", 1.5m, typeof(decimal)), InputClass = "x\" onfocus=\"alert(1)"
            };
            var output = Output("sf-field-group");
            await th.ProcessAsync(Ctx(), output);
            var html = output.Content.GetContent();
            html.Should().Contain("value=\"1.5\"").And.Contain("step=\"0.01\"").And.Contain("input-group-text\">$");
            html.Should().NotContain("\" onfocus=\"");
            output.Attributes["class"].Value.ToString().Should().Be("mb-3");
        }
        finally { System.Globalization.CultureInfo.CurrentCulture = prev; }
    }

    [Theory]
    [InlineData("date", "type=\"date\"")]
    [InlineData("datetime", "type=\"datetime-local\"")]
    [InlineData("textarea", "<textarea")]
    [InlineData("email", "type=\"email\"")]
    [InlineData("phone", "type=\"tel\"")]
    [InlineData("url", "type=\"url\"")]
    [InlineData("int", "step=\"1\"")]
    [InlineData("boolean", "type=\"checkbox\"")]
    [InlineData("encryptedstring", "type=\"password\"")]
    [InlineData("string", "type=\"text\"")]
    [InlineData("picklist", "<sf-picklist")]
    [InlineData("reference", "<sf-lookup")]
    public async Task FieldGroup_RendersInputPerType(string type, string expected)
    {
        foreach (var fw in new[] { CssFramework.Bootstrap5, CssFramework.SLDS, CssFramework.None })
        {
            var th = new SalesforceFieldGroupTagHelper(Schema(new SObjectField { Name = "F", Type = type, Label = "F <x>", Nillable = false, Createable = true, Length = 10, InlineHelpText = "help" }).Object)
            { ObjectName = "Account", FieldName = "F", Framework = fw, RecordTypeId = "012\"x" };
            var output = Output("sf-field-group");
            await th.ProcessAsync(Ctx(), output);
            var html = output.Content.GetContent();
            html.Should().Contain(expected).And.Contain("F &lt;x&gt;").And.Contain("help");
            html.Should().NotContain("012\"x");
        }
    }

    [Fact]
    public async Task FieldGroup_DateUnderThaiCulture_UsesGregorianIso()
    {
        var prev = System.Globalization.CultureInfo.CurrentCulture;
        System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo("th-TH");
        try
        {
            var th = new SalesforceFieldGroupTagHelper(Schema(new SObjectField { Name = "D", Type = "date", Label = "D" }).Object)
            { ObjectName = "Account", FieldName = "D", For = For("D", new DateTime(2026, 1, 2), typeof(DateTime)) };
            var output = Output("sf-field-group");
            await th.ProcessAsync(Ctx(), output);
            output.Content.GetContent().Should().Contain("value=\"2026-01-02\"");
        }
        finally { System.Globalization.CultureInfo.CurrentCulture = prev; }
    }

    [Fact]
    public async Task FieldGroup_SchemaFailure_StillRendersPlainInput()
    {
        var m = new Mock<ISchemaService>();
        m.Setup(s => s.GetFieldMapAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ThrowsAsync(new SalesforceException("down"));
        var th = new SalesforceFieldGroupTagHelper(m.Object) { ObjectName = "Account", FieldName = "Name", Label = "Nm" };
        var output = Output("sf-field-group");
        await th.ProcessAsync(Ctx(), output);
        output.Content.GetContent().Should().Contain("type=\"text\"").And.Contain("Nm");
    }

    [Fact]
    public async Task ModelForm_RendersFields_EncodesValues_AndFiltersByMode()
    {
        var schema = Schema(
            new SObjectField { Name = "Name", Type = "string", Label = "Name", Createable = true, Updateable = true },
            new SObjectField { Name = "Amount", Type = "currency", Label = "Amount", Createable = true, Updateable = true },
            new SObjectField { Name = "When", Type = "datetime", Label = "When", Createable = true, Updateable = true },
            new SObjectField { Name = "CreatedDate", Type = "datetime", Label = "Created", Createable = false },
            new SObjectField { Name = "ReadOnly__c", Type = "string", Label = "RO", Createable = false, Updateable = false });
        var th = new SalesforceModelFormTagHelper(schema.Object)
        {
            ObjectName = "Account", Model = For("m", new Model(), typeof(Model)), Action = "Create", Controller = "Account",
            CancelUrl = "javascript:alert(1)", Columns = 2
        };
        var output = Output("sf-model-form");
        await th.ProcessAsync(Ctx(), output);
        var html = output.Content.GetContent();
        html.Should().Contain("name=\"m.Name\"").And.Contain("value=\"&quot;>&lt;script>\"").And.NotContain("\"><script>");
        html.Should().Contain("value=\"1.5\"").And.Contain("value=\"2026-03-04T05:06\"");
        html.Should().NotContain("CreatedDate").And.NotContain("ReadOnly__c");
        html.Should().Contain("href=\"javascript:history.back()\"").And.NotContain("alert(1)");
        html.Should().Contain("__RequestVerificationToken");
        output.Attributes["action"].Value.ToString().Should().Be("/Account/Create");
    }

    [Theory]
    [InlineData("/accounts", true)]
    [InlineData("https://x.example/a", true)]
    [InlineData("//evil.example", false)]
    [InlineData("javascript:alert(1)", false)]
    [InlineData("data:text/html,x", false)]
    [InlineData(null, false)]
    public void ModelForm_IsSafeUrl(string? url, bool ok) => SalesforceModelFormTagHelper.IsSafeUrl(url).Should().Be(ok);

    [Fact]
    public async Task ModelForm_IncludeFields_OrdersAndOverridesFilter()
    {
        var schema = Schema(
            new SObjectField { Name = "B", Type = "string", Label = "B", Createable = true },
            new SObjectField { Name = "A", Type = "string", Label = "A", Createable = true },
            new SObjectField { Name = "C", Type = "boolean", Label = "C", Createable = true });
        var th = new SalesforceModelFormTagHelper(schema.Object) { ObjectName = "Account", IncludeFields = "B,A", Framework = CssFramework.SLDS, ShowCancel = false, IncludeAntiforgery = false };
        var output = Output("sf-model-form");
        await th.ProcessAsync(Ctx(), output);
        var html = output.Content.GetContent();
        html.IndexOf("name=\"B\"", StringComparison.Ordinal).Should().BeLessThan(html.IndexOf("name=\"A\"", StringComparison.Ordinal));
        html.Should().NotContain("name=\"C\"").And.NotContain("__RequestVerificationToken");
    }

    [Fact]
    public async Task Lookup_UsesConfigurableSearchUrl()
    {
        var schema = new Mock<ISchemaService>();
        schema.Setup(s => s.GetFieldMapAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, SObjectField>(StringComparer.OrdinalIgnoreCase)
            {
                ["AccountId"] = new SObjectField { Name = "AccountId", Type = "reference", ReferenceTo = new() { "Account" }, RelationshipName = "Account" }
            });
        var ctor = typeof(SalesforceLookupTagHelper).GetConstructors().OrderByDescending(c => c.GetParameters().Length).First();
        var args = ctor.GetParameters().Select(p => p.ParameterType == typeof(ISchemaService) ? schema.Object
            : p.ParameterType.IsInterface ? typeof(Mock).GetMethod("Of", Type.EmptyTypes)!.MakeGenericMethod(p.ParameterType).Invoke(null, null)
            : null).ToArray();
        var th = (SalesforceLookupTagHelper)ctor.Invoke(args);
        th.SearchUrl = "/app/lookup?x=\"y";
        typeof(SalesforceLookupTagHelper).GetProperty("ObjectName")?.SetValue(th, "Contact");
        typeof(SalesforceLookupTagHelper).GetProperty("FieldName")?.SetValue(th, "AccountId");
        var output = Output("sf-lookup");
        await th.ProcessAsync(Ctx(), output);
        output.Content.GetContent().Should().Contain("data-sf-search-url=\"/app/lookup?x=&quot;y\"");
    }
}
