using System.Net;
using System.Text.Json.Nodes;
using FluentAssertions;
using SalesforceCore.Attributes;
using SalesforceCore.Models.Data;
using SalesforceCore.Query;
using SalesforceCore.Services.Data;
using SalesforceCore.Services.Query;
using SalesforceCore.Tests.TestKit;
using Xunit;

namespace SalesforceCore.Tests.V22;

[SalesforceObject("Account")]
public class V22Account
{
    public string? Id { get; set; }
    public string? Name { get; set; }
    public string? Industry { get; set; }
    public string? Status { get; set; }
    public decimal? AnnualRevenue { get; set; }
    public int? NumberOfEmployees { get; set; }
    public DateTime? CreatedDate { get; set; }
    public DateOnly? CloseDate { get; set; }
    public bool IsDeleted { get; set; }
    public string? OwnerId { get; set; }
    [SalesforceField("Regions__c")] public string? Regions { get; set; }
    [SalesforceField("Location__c")] public object? Location { get; set; }
}

[SalesforceObject("Contact")]
public class V22Contact
{
    public string? Id { get; set; }
    public string? AccountId { get; set; }
    public string? Email { get; set; }
}

/// <summary>Executor that records SOQL and the endpoint (query vs queryAll) it was sent to.</summary>
internal sealed class RecordingExecutor : ISoqlQueryExecutor, ISoqlDeletedRowsQueryExecutor
{
    public List<(string Endpoint, string Soql)> Calls { get; } = new();
    public List<JsonObject> Rows { get; } = new();
    public int? TotalSize { get; set; }
    public string Last => Calls[^1].Soql;

    private QueryResult Result(string soql)
    {
        if (soql.StartsWith("SELECT COUNT() ", StringComparison.Ordinal))
            return new QueryResult { TotalSize = TotalSize ?? Rows.Count, Done = true };
        return new QueryResult { Records = Rows.Select(r => (JsonObject)r.DeepClone()).ToList(), TotalSize = Rows.Count, Done = true };
    }

    public Task<QueryResult> QueryAsync(string soql, CancellationToken cancellationToken = default)
    { Calls.Add(("query", soql)); return Task.FromResult(Result(soql)); }

    public Task<List<JsonObject>> QueryAllAsync(string soql, CancellationToken cancellationToken = default)
    { Calls.Add(("query", soql)); return Task.FromResult(Result(soql).Records); }

    public async IAsyncEnumerable<JsonObject> QueryAllAsyncEnumerable(string soql, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        Calls.Add(("query", soql));
        await Task.Yield();
        foreach (var r in Result(soql).Records) yield return r;
    }

    public Task<QueryResult> QueryIncludingDeletedAsync(string soql, CancellationToken cancellationToken = default)
    { Calls.Add(("queryAll", soql)); return Task.FromResult(Result(soql)); }

    public async IAsyncEnumerable<JsonObject> QueryAllIncludingDeletedAsyncEnumerable(string soql, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        Calls.Add(("queryAll", soql));
        await Task.Yield();
        foreach (var r in Result(soql).Records) yield return r;
    }
}

/// <summary>Executor without queryAll support.</summary>
internal sealed class PlainExecutor : ISoqlQueryExecutor
{
    public Task<QueryResult> QueryAsync(string soql, CancellationToken cancellationToken = default) => Task.FromResult(new QueryResult { Done = true });
    public Task<List<JsonObject>> QueryAllAsync(string soql, CancellationToken cancellationToken = default) => Task.FromResult(new List<JsonObject>());
    public async IAsyncEnumerable<JsonObject> QueryAllAsyncEnumerable(string soql, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    { await Task.Yield(); yield break; }
}

public class LinqPagingSemanticsV22Tests
{
    private readonly RecordingExecutor _ex = new();
    private SalesforceQueryable<V22Account> Q => _ex.Query<V22Account>();

    [Fact] public void TakeThenSkip_ReducesLimit() => Q.Take(5).Skip(2).ToSoql().Should().EndWith("LIMIT 3 OFFSET 2");
    [Fact] public void SkipThenTake_KeepsBoth() => Q.Skip(2).Take(5).ToSoql().Should().EndWith("LIMIT 5 OFFSET 2");
    [Fact] public void TakeTake_UsesMinimum()
    {
        Q.Take(5).Take(10).ToSoql().Should().EndWith("LIMIT 5");
        Q.Take(10).Take(5).ToSoql().Should().EndWith("LIMIT 5");
    }
    [Fact] public void SkipSkip_Accumulates() => Q.Skip(2).Skip(3).ToSoql().Should().EndWith("OFFSET 5");
    [Fact] public void TakeSkipPastLimit_ClampsToZero() => Q.Take(2).Skip(5).ToSoql().Should().Contain("LIMIT 0");

    [Fact]
    public async Task TakeZero_ReturnsEmptyWithoutQuery()
    {
        (await Q.Take(0).ToListAsync()).Should().BeEmpty();
        (await Q.Take(0).ToListAllAsync()).Should().BeEmpty();
        (await Q.Take(0).CountAsync()).Should().Be(0);
        (await Q.Take(0).AnyAsync()).Should().BeFalse();
        var streamed = new List<V22Account>();
        await foreach (var a in Q.Take(0).ToAsyncEnumerable()) streamed.Add(a);
        streamed.Should().BeEmpty();
        _ex.Calls.Should().BeEmpty();
    }

    [Theory]
    [InlineData(10, 3, 5, 5)]
    [InlineData(10, 8, 5, 2)]
    [InlineData(10, 20, 5, 0)]
    [InlineData(10, 0, 0, 10)]
    public async Task Count_AppliesSkipAndTakeArithmetically(int total, int skip, int take, int expected)
    {
        _ex.TotalSize = total;
        IQueryable<V22Account> q = Q.Where(a => a.Industry == "Tech");
        if (skip > 0) q = q.Skip(skip);
        if (take > 0) q = q.Take(take);
        (await ((SalesforceQueryable<V22Account>)q).CountAsync()).Should().Be(expected);
        _ex.Last.Should().Be("SELECT COUNT() FROM Account WHERE (Industry = 'Tech')");
    }

    [Fact]
    public async Task Any_AfterSkip_MeansMoreThanSkipped()
    {
        _ex.TotalSize = 3;
        (await Q.Skip(2).AnyAsync()).Should().BeTrue();
        (await Q.Skip(3).AnyAsync()).Should().BeFalse();
    }

    [Fact]
    public async Task LongCount_SyncAndAsync()
    {
        _ex.TotalSize = 42;
        (await Q.LongCountAsync()).Should().Be(42L);
        (await Q.LongCountAsync(a => a.Name == "x")).Should().Be(42L);
        _ex.Last.Should().Contain("COUNT()").And.Contain("(Name = 'x')");
        Q.Where(a => a.Name != null).LongCount().Should().Be(42L);
    }
}

public class LinqWhereTranslationV22Tests
{
    private readonly RecordingExecutor _ex = new();
    private string Where(System.Linq.Expressions.Expression<Func<V22Account, bool>> p)
    {
        var soql = _ex.Query<V22Account>().Where(p).ToSoql();
        var i = soql.IndexOf(" WHERE ", StringComparison.Ordinal);
        return i < 0 ? "" : soql[(i + 7)..];
    }

    [Fact] public void ConstantOnLeft_IsFlipped()
    {
        Where(a => 5 < a.NumberOfEmployees).Should().Be("(NumberOfEmployees > 5)");
        Where(a => 5 >= a.NumberOfEmployees).Should().Be("(NumberOfEmployees <= 5)");
        Where(a => "Acme" == a.Name).Should().Be("(Name = 'Acme')");
        Where(a => null == a.Name).Should().Be("(Name = NULL)");
    }

    [Fact] public void ClientSideMethodCalls_AreEvaluated()
    {
        var anchor = new DateTime(2026, 10, 30, 12, 0, 0, DateTimeKind.Utc);
        Where(a => a.CreatedDate > anchor.AddDays(-7)).Should().Be("(CreatedDate > 2026-10-23T12:00:00Z)");
        Where(a => a.CloseDate >= new DateOnly(2026, 1, 1)).Should().Be("(CloseDate >= 2026-01-01)");
        var prefix = "ac";
        Where(a => a.Name == prefix.ToUpperInvariant()).Should().Be("(Name = 'AC')");
        int x = 2, y = 3;
        Where(a => a.NumberOfEmployees > x * y + 1).Should().Be("(NumberOfEmployees > 7)");
        bool flag = true;
        Where(a => a.Name == (flag ? "A" : "B")).Should().Be("(Name = 'A')");
    }

    [Fact] public void ArithmeticOnFields_ThrowsClearly()
    {
        var act = () => Where(a => a.NumberOfEmployees + 1 > 5);
        act.Should().Throw<NotSupportedException>().WithMessage("*Add*not supported*");
    }

    [Fact] public void UnsupportedMethod_PointsAtDocs()
    {
        var act = () => Where(a => a.Name!.ToUpper() == "X");
        act.Should().Throw<NotSupportedException>();
        var act2 = () => Where(a => a.Name!.Normalize() == "X");
        act2.Should().Throw<NotSupportedException>().WithMessage("*19-LINQ-to-SOQL*");
    }

    [Fact] public void IsNullOrEmpty_TranslatesToNull()
    {
        Where(a => string.IsNullOrEmpty(a.Name)).Should().Be("(Name = NULL)");
        Where(a => !string.IsNullOrWhiteSpace(a.Name)).Should().Be("NOT (Name = NULL)");
    }

    [Fact] public void NullableNullComparison()
    {
        Where(a => a.CreatedDate == null).Should().Be("(CreatedDate = NULL)");
        Where(a => a.NumberOfEmployees != null).Should().Be("(NumberOfEmployees != NULL)");
    }

    [Fact] public void ArrayAndListContains()
    {
        var names = new[] { "A", "B" };
        Where(a => names.Contains(a.Name)).Should().Be("Name IN ('A', 'B')");
        var list = new List<string?> { "A" };
        Where(a => !list.Contains(a.Name)).Should().Be("Name NOT IN ('A')");
        var empty = new List<string?>();
        Where(a => !empty.Contains(a.Name)).Should().Be("(Id != NULL)");
        Where(a => empty.Contains(a.Name)).Should().Be("(Id = NULL)");
        var set = new HashSet<string?> { "Z" };
        Where(a => set.Contains(a.Industry)).Should().Be("Industry IN ('Z')");
    }

    [Fact] public void DateLiterals()
    {
        Where(a => a.CreatedDate == SoqlFunctions.Literal(DateLiteral.TODAY)).Should().Be("(CreatedDate = TODAY)");
        Where(a => a.CreatedDate >= SoqlFunctions.Literal(DateLiteral.LAST_N_DAYS, 30)).Should().Be("(CreatedDate >= LAST_N_DAYS:30)");
        Where(a => a.CloseDate < SoqlFunctions.LiteralDate(DateLiteral.THIS_YEAR)).Should().Be("(CloseDate < THIS_YEAR)");
        Where(a => a.CloseDate == SoqlFunctions.LiteralDate(DateLiteral.N_DAYS_AGO, 3)).Should().Be("(CloseDate = N_DAYS_AGO:3)");
        Where(a => SoqlFunctions.Literal(DateLiteral.YESTERDAY) < a.CreatedDate).Should().Be("(CreatedDate > YESTERDAY)");
    }

    [Fact] public void DateLiterals_ValidateN()
    {
        ((Action)(() => Where(a => a.CreatedDate == SoqlFunctions.Literal(DateLiteral.LAST_N_DAYS)))).Should().Throw<ArgumentException>();
        ((Action)(() => Where(a => a.CreatedDate == SoqlFunctions.Literal(DateLiteral.TODAY, 3)))).Should().Throw<ArgumentException>();
        ((Action)(() => Where(a => a.CreatedDate == SoqlFunctions.Literal(DateLiteral.LAST_N_DAYS, -1)))).Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact] public void DateFunctions()
    {
        Where(a => SoqlFunctions.CalendarQuarter(a.CloseDate) == 2).Should().Be("(CALENDAR_QUARTER(CloseDate) = 2)");
        Where(a => SoqlFunctions.FiscalYear(a.CreatedDate) == 2026).Should().Be("(FISCAL_YEAR(CreatedDate) = 2026)");
        Where(a => SoqlFunctions.FiscalQuarter(a.CloseDate) == 1).Should().Be("(FISCAL_QUARTER(CloseDate) = 1)");
        Where(a => SoqlFunctions.FiscalMonth(a.CreatedDate) == 1).Should().Be("(FISCAL_MONTH(CreatedDate) = 1)");
        Where(a => SoqlFunctions.WeekInYear(a.CreatedDate) > 10).Should().Be("(WEEK_IN_YEAR(CreatedDate) > 10)");
        Where(a => SoqlFunctions.WeekInMonth(a.CloseDate) == 1).Should().Be("(WEEK_IN_MONTH(CloseDate) = 1)");
        Where(a => SoqlFunctions.DatePart(DateFunction.HOUR_IN_DAY, SoqlFunctions.ConvertTimezone(a.CreatedDate)) < 9)
            .Should().Be("(HOUR_IN_DAY(convertTimezone(CreatedDate)) < 9)");
        Where(a => SoqlFunctions.DayOnly(a.CreatedDate) == new DateOnly(2026, 10, 30)).Should().Be("(DAY_ONLY(CreatedDate) = 2026-10-30)");
        Where(a => a.CreatedDate!.Value.Year == 2026).Should().Be("(CALENDAR_YEAR(CreatedDate) = 2026)");
    }

    [Fact] public void MultiSelectPicklists()
    {
        Where(a => SoqlFunctions.Includes(a.Regions, "EMEA", "APAC;LATAM")).Should().Be("(Regions__c INCLUDES ('EMEA', 'APAC;LATAM'))");
        Where(a => SoqlFunctions.Excludes(a.Regions, "O'Brien")).Should().Be("(Regions__c EXCLUDES ('O\\'Brien'))");
        ((Action)(() => Where(a => SoqlFunctions.Includes(a.Regions)))).Should().Throw<ArgumentException>();
    }

    [Fact] public void ToLabelAndLike()
    {
        Where(a => SoqlFunctions.ToLabel(a.Status) == "Open").Should().Be("(toLabel(Status) = 'Open')");
        Where(a => SoqlFunctions.Like(a.Name, "Ac%_e")).Should().Be("(Name LIKE 'Ac%_e')");
        Where(a => SoqlFunctions.Like(a.Name, "it's%")).Should().Be("(Name LIKE 'it\\'s%')");
        Where(a => a.Name!.Contains("50%")).Should().Be("Name LIKE '%50\\%%'");
    }

    [Fact] public void Distance_InWhereAndOrderBy()
    {
        Where(a => SoqlFunctions.Distance(a.Location, 37.77, -122.42, DistanceUnit.Kilometers) < 20)
            .Should().Be("(DISTANCE(Location__c, GEOLOCATION(37.77, -122.42), 'km') < 20)");
        _ex.Query<V22Account>().OrderBy(a => SoqlFunctions.Distance(a.Location, 1.5, 2.5, DistanceUnit.Miles)).ToSoql()
            .Should().EndWith("ORDER BY DISTANCE(Location__c, GEOLOCATION(1.5, 2.5), 'mi') ASC");
        ((Action)(() => Where(a => SoqlFunctions.Distance(a.Location, 91, 0, DistanceUnit.Miles) < 1))).Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact] public void SemiAndAntiJoins()
    {
        var contacts = _ex.Query<V22Contact>().Where(c => c.Email != null).Select(c => c.AccountId);
        Where(a => SoqlFunctions.In(a.Id, contacts)).Should().Be("Id IN (SELECT AccountId FROM Contact WHERE (Email != NULL))");
        Where(a => SoqlFunctions.NotIn(a.Id, contacts)).Should().Be("Id NOT IN (SELECT AccountId FROM Contact WHERE (Email != NULL))");
        Where(a => contacts.Contains(a.Id)).Should().Be("Id IN (SELECT AccountId FROM Contact WHERE (Email != NULL))");
        Where(a => !contacts.Contains(a.Id)).Should().Be("Id NOT IN (SELECT AccountId FROM Contact WHERE (Email != NULL))");
        Where(a => !SoqlFunctions.NotIn(a.Id, contacts)).Should().Be("Id IN (SELECT AccountId FROM Contact WHERE (Email != NULL))");
        var all = _ex.Query<V22Contact>().Select(c => c.AccountId);
        Where(a => SoqlFunctions.In(a.Id, all) && a.Name != null).Should().Be("(Id IN (SELECT AccountId FROM Contact) AND (Name != NULL))");
    }

    [Fact] public void SemiJoin_RejectsInvalidSubqueries()
    {
        var limited = _ex.Query<V22Contact>().Take(5).Select(c => c.AccountId);
        ((Action)(() => Where(a => SoqlFunctions.In(a.Id, limited)))).Should().Throw<NotSupportedException>().WithMessage("*Semi-join*");
        var twoFields = _ex.Query<V22Contact>().Select(c => c.AccountId + c.Email);
        ((Action)(() => Where(a => SoqlFunctions.In(a.Id, twoFields)))).Should().Throw<NotSupportedException>();
        var local = new[] { "1" }.AsQueryable();
        Where(a => local.Contains(a.Id)).Should().Be("Id IN ('1')");
    }

    [Fact] public void SoqlFunctions_ThrowClientSide()
    {
        ((Action)(() => SoqlFunctions.Literal(DateLiteral.TODAY))).Should().Throw<InvalidOperationException>().WithMessage("*LINQ*");
        ((Action)(() => SoqlFunctions.Includes("a", "b"))).Should().Throw<InvalidOperationException>();
        ((Action)(() => SoqlFunctions.Distance(null, 0, 0))).Should().Throw<InvalidOperationException>();
    }
}

public class LinqClausesV22Tests
{
    private readonly RecordingExecutor _ex = new();
    private SalesforceQueryable<V22Account> Q => _ex.Query<V22Account>();

    [Fact] public void NullsFirstLast()
    {
        Q.OrderBy(a => a.Name).NullsFirst().ThenByDescending(a => a.Industry).NullsLast().ToSoql()
            .Should().EndWith("ORDER BY Name ASC NULLS FIRST, Industry DESC NULLS LAST");
        ((Action)(() => Q.NullsFirst().ToSoql())).Should().Throw<NotSupportedException>().WithMessage("*OrderBy*");
    }

    [Fact] public async Task SecurityModes_ApplyToAllQueryShapes()
    {
        Q.Where(a => a.Name == "A").OrderBy(a => a.Name).WithSecurityEnforced().ToSoql()
            .Should().Contain("WHERE (Name = 'A') WITH SECURITY_ENFORCED ORDER BY Name ASC");
        Q.WithSystemMode().ToSoql().Should().EndWith("FROM Account WITH SYSTEM_MODE");
        await Q.WithUserMode().CountAsync();
        _ex.Last.Should().Be("SELECT COUNT() FROM Account WITH USER_MODE");
        await Q.WithUserMode().AnyAsync();
        _ex.Last.Should().Be("SELECT COUNT() FROM Account WITH USER_MODE LIMIT 1");
        await Q.WithUserMode().SumAsync(a => a.AnnualRevenue);
        _ex.Last.Should().Be("SELECT SUM(AnnualRevenue) sum FROM Account WITH USER_MODE");
        Q.Where(a => a.Name != null).WithUserMode().Select(a => a.Name).ToSoql().Should().Contain("WITH USER_MODE");
    }

    [Fact] public void SoqlBuilder_WithClausePosition()
    {
        SoqlBuilder.From("Account").Select("Industry").WhereEquals("Name", "A").WithSecurityEnforced().GroupBy("Industry").Build()
            .Should().Be("SELECT Industry FROM Account WHERE Name = 'A' WITH SECURITY_ENFORCED GROUP BY Industry");
        SoqlBuilder.From("Account").WithUserMode().Limit(1).Build().Should().Be("SELECT Id FROM Account WITH USER_MODE LIMIT 1");
        SoqlBuilder.From("Account").WithSystemMode().Build().Should().EndWith("WITH SYSTEM_MODE");
    }

    [Fact] public async Task IncludeDeleted_RoutesToQueryAll()
    {
        _ex.Rows.Add(SalesforceJson.Record("Account", "001000000000001AAA", ("Name", "Gone"), ("IsDeleted", true)));
        var list = await Q.IncludeDeleted().Where(a => a.IsDeleted).ToListAsync();
        list.Single().IsDeleted.Should().BeTrue();
        _ex.Calls[^1].Should().Be(("queryAll", "SELECT Id, Name, Industry, Status, AnnualRevenue, NumberOfEmployees, CreatedDate, CloseDate, IsDeleted, OwnerId, Regions__c, Location__c FROM Account WHERE IsDeleted = TRUE"));
        await Q.IncludeDeleted().ToListAllAsync();
        _ex.Calls[^1].Endpoint.Should().Be("queryAll");
        await foreach (var _ in Q.IncludeDeleted().ToAsyncEnumerable()) { }
        _ex.Calls[^1].Endpoint.Should().Be("queryAll");
        await Q.IncludeDeleted().CountAsync();
        _ex.Calls[^1].Endpoint.Should().Be("queryAll");
        await Q.IncludeDeleted().GroupByFieldAsync(a => a.Industry);
        _ex.Calls[^1].Endpoint.Should().Be("queryAll");
        await Q.ToListAsync();
        _ex.Calls[^1].Endpoint.Should().Be("query");
        (await Q.IncludeDeleted().Select(a => a.Name).ToListAsync()).Should().ContainSingle("Gone");
        _ex.Calls[^1].Endpoint.Should().Be("queryAll");
    }

    [Fact] public async Task IncludeDeleted_WithoutCapability_Throws()
    {
        var act = () => new PlainExecutor().Query<V22Account>().IncludeDeleted().ToListAsync();
        await act.Should().ThrowAsync<NotSupportedException>().WithMessage("*ISoqlDeletedRowsQueryExecutor*");
    }

    [Fact] public async Task DataService_QueryIncludingDeleted_HitsQueryAllEndpoint()
    {
        await using var host = SalesforceTestHost.Create();
        host.Server.On(HttpMethod.Get, "/services/data/v62.0/queryAll/?", _ => FakeSalesforceServer.Json(HttpStatusCode.OK,
            SalesforceJson.QueryPage(new[] { SalesforceJson.Record("Account", "001000000000001AAA", ("Name", "A")) }, "/services/data/v62.0/queryAll/01gQA-1", 2)));
        host.Server.On(HttpMethod.Get, "/services/data/v62.0/queryAll/01gQA-1", _ => FakeSalesforceServer.Json(HttpStatusCode.OK,
            SalesforceJson.QueryPage(new[] { SalesforceJson.Record("Account", "001000000000002AAA", ("Name", "B")) }, null, 2)));

        var data = host.Get<IDataService>();
        var rows = await data.Query<V22Account>().IncludeDeleted().Where(a => a.IsDeleted).ToListAllAsync();
        rows.Select(r => r.Name).Should().Equal("A", "B");
        host.Server.Requests[0].Path.Should().Be("/services/data/v62.0/queryAll/");
        host.Server.Requests[0].Query("q").Should().Contain("WHERE IsDeleted = TRUE");
        ((ISoqlDeletedRowsQueryExecutor)data).Should().NotBeNull();
    }

    [Fact] public void ToSoql_OnProjection()
    {
        Q.Where(a => a.Name != null).Select(a => new { a.Name, a.Industry }).Take(3).ToSoql()
            .Should().Be("SELECT Id, Name, Industry FROM Account WHERE (Name != NULL) LIMIT 3");
        ((Action)(() => new[] { 1 }.AsQueryable().ToSoql())).Should().Throw<NotSupportedException>();
    }
}

public class LinqAggregatesV22Tests
{
    private readonly RecordingExecutor _ex = new();
    private SalesforceQueryable<V22Account> Q => _ex.Query<V22Account>();

    [Fact] public async Task ScalarAggregates_IgnoreOrderLimitAndTrickyLiterals()
    {
        _ex.Rows.Add(new JsonObject { ["sum"] = 5, ["avg"] = 2.5, ["min"] = 1, ["max"] = 9, ["expr0"] = 3 });
        var q = Q.Where(a => a.Name == "x WHERE y ORDER BY z LIMIT 1").OrderBy(a => a.Name).Take(5);
        (await q.SumAsync(a => a.AnnualRevenue)).Should().Be(5);
        _ex.Last.Should().Be("SELECT SUM(AnnualRevenue) sum FROM Account WHERE (Name = 'x WHERE y ORDER BY z LIMIT 1')");
        (await ((SalesforceQueryProvider)q.Provider).ExecuteAggregateAsync("SELECT COUNT(Id) FROM Account", default)).Count.Should().Be(3);

        (await q.AverageAsync(a => a.AnnualRevenue)).Should().Be(2.5m);
        _ex.Last.Should().StartWith("SELECT AVG(AnnualRevenue) avg FROM Account WHERE").And.NotContain("ORDER BY Name");
        (await q.MinAsync(a => a.NumberOfEmployees)).Should().Be(1);
        (await q.MaxAsync(a => a.NumberOfEmployees)).Should().Be(9);
        _ex.Last.Should().Be("SELECT MAX(NumberOfEmployees) max FROM Account WHERE (Name = 'x WHERE y ORDER BY z LIMIT 1')");
    }

    [Fact] public async Task AggregateValues_FromParsedJson_AreUnwrapped()
    {
        _ex.Rows.Add((JsonObject)JsonNode.Parse("{\"attributes\":{\"type\":\"AggregateResult\"},\"Industry\":\"Tech\",\"cnt\":7,\"total\":1234.5,\"flag\":true,\"none\":null}")!);
        var row = (await Q.GroupByFieldAsync(a => a.Industry)).Single();
        row.Get<string>("Industry").Should().Be("Tech");
        row.Get<int>("cnt").Should().Be(7);
        row.Get<decimal>("total").Should().Be(1234.5m);
        row["flag"].Should().Be(true);
        row["none"].Should().BeNull();
        row.Get<int?>("cnt").Should().Be(7);
        row.Get<long>("cnt").Should().Be(7L);
        row.Get<double?>("total").Should().Be(1234.5);
        row.Get<int>("Industry").Should().Be(0); // unconvertible -> default
        row.Keys.Should().NotContain("attributes");
        (await Q.SumAsync(a => a.AnnualRevenue)).Should().Be(0); // no "sum" key in this row
    }

    [Fact] public async Task DistinctAsync_KeepsEveryWhere()
    {
        _ex.Rows.Add(new JsonObject { ["Industry"] = "Tech" });
        var result = await Q.Where(a => a.Name != null).Where(a => a.NumberOfEmployees > 5).DistinctAsync(a => a.Industry);
        result.Should().Equal("Tech");
        _ex.Last.Should().Be("SELECT Industry FROM Account WHERE ((Name != NULL)) AND ((NumberOfEmployees > 5)) GROUP BY Industry");
    }

    [Fact] public async Task GroupByHelpers_UseParsedWhere()
    {
        await Q.Where(a => a.Name == "a ORDER BY b").OrderBy(a => a.Name).GroupByFieldAsync(a => a.Industry);
        _ex.Last.Should().Be("SELECT Industry, COUNT(Id) cnt FROM Account WHERE (Name = 'a ORDER BY b') GROUP BY Industry");
        await Q.Take(3).GroupByWithAggregatesAsync(a => a.Industry, default, new AggregateSpec("AnnualRevenue", AggregateFunction.Sum, "total"));
        _ex.Last.Should().Be("SELECT Industry, SUM(AnnualRevenue) total FROM Account GROUP BY Industry");
    }

    [Fact] public async Task AggregateAsync_FullShape()
    {
        _ex.Rows.Add(new JsonObject { ["Industry"] = "Tech", ["cnt"] = 3, ["total"] = 300 });
        var rows = await Q.Where(a => a.NumberOfEmployees > 0).AggregateAsync(g => g
            .GroupBy(a => a.Industry)
            .Count()
            .Sum(a => a.AnnualRevenue, "total")
            .Average(a => a.AnnualRevenue, "avgRev")
            .Min(a => a.CreatedDate, "first")
            .Max(a => a.CreatedDate, "last")
            .CountDistinct(a => a.OwnerId, "owners")
            .HavingCount(">", 1)
            .Having(AggregateFunction.Sum, a => a.AnnualRevenue, ">=", 100.5m)
            .OrderByAggregate(AggregateFunction.Sum, a => a.AnnualRevenue, descending: true)
            .OrderBy(a => a.Industry)
            .Limit(10));
        rows.Single().Get<int>("cnt").Should().Be(3);
        _ex.Last.Should().Be("SELECT Industry, COUNT(Id) cnt, SUM(AnnualRevenue) total, AVG(AnnualRevenue) avgRev, MIN(CreatedDate) first, MAX(CreatedDate) last, COUNT_DISTINCT(OwnerId) owners FROM Account WHERE (NumberOfEmployees > 0) GROUP BY Industry HAVING COUNT(Id) > 1 AND SUM(AnnualRevenue) >= 100.5 ORDER BY SUM(AnnualRevenue) DESC, Industry ASC LIMIT 10");
    }

    [Fact] public void AggregateSoql_RollupCubeAndDateKeys()
    {
        Q.ToAggregateSoql(g => g.GroupBy(a => a.Industry).GroupBy(a => a.Status).Rollup().Count())
            .Should().Be("SELECT Industry, Status, COUNT(Id) cnt FROM Account GROUP BY ROLLUP(Industry, Status)");
        Q.WithSecurityEnforced().ToAggregateSoql(g => g.GroupBy(a => a.Industry).Cube().Count("n").OrderByCount(true))
            .Should().Be("SELECT Industry, COUNT(Id) n FROM Account WITH SECURITY_ENFORCED GROUP BY CUBE(Industry) ORDER BY COUNT(Id) DESC");
        Q.ToAggregateSoql(g => g.GroupBy(a => SoqlFunctions.CalendarQuarter(a.CreatedDate), "qtr").Count(a => a.Name, "named"))
            .Should().Be("SELECT CALENDAR_QUARTER(CreatedDate) qtr, COUNT(Name) named FROM Account GROUP BY CALENDAR_QUARTER(CreatedDate)");
        Q.ToAggregateSoql(g => g.GroupBy(a => SoqlFunctions.CalendarYear(a.CloseDate), "yr").GroupBy(a => SoqlFunctions.CalendarMonth(a.CreatedDate)).Count())
            .Should().Be("SELECT CALENDAR_YEAR(CloseDate) yr, CALENDAR_MONTH(CreatedDate), COUNT(Id) cnt FROM Account GROUP BY CALENDAR_YEAR(CloseDate), CALENDAR_MONTH(CreatedDate)");
    }

    [Fact] public async Task AggregateAsync_Validation()
    {
        ((Action)(() => Q.ToAggregateSoql(g => g.HavingCount("; DELETE", 1)))).Should().Throw<ArgumentException>();
        ((Action)(() => Q.ToAggregateSoql(g => g.Limit(0)))).Should().Throw<ArgumentOutOfRangeException>();
        await ((Func<Task>)(() => Q.AggregateAsync(_ => { }))).Should().ThrowAsync<InvalidOperationException>();
        await ((Func<Task>)(() => Q.AggregateAsync(g => g.Count().Rollup()))).Should().ThrowAsync<InvalidOperationException>();
        ((Action)(() => Q.ToAggregateSoql(g => g.Count("bad alias;")))).Should().Throw<Exception>();
    }

    [Fact] public async Task CountDistinctAsync_Works()
    {
        _ex.Rows.Add(new JsonObject { ["cnt"] = 4 });
        (await Q.Where(a => a.Industry == "Tech").CountDistinctAsync(a => a.OwnerId)).Should().Be(4);
        _ex.Last.Should().Be("SELECT COUNT_DISTINCT(OwnerId) cnt FROM Account WHERE (Industry = 'Tech')");
    }
}

public class LinqAsyncTerminalsV22Tests
{
    private readonly RecordingExecutor _ex = new();
    private SalesforceQueryable<V22Account> Q => _ex.Query<V22Account>();
    private static JsonObject Acc(string id, string name) => SalesforceJson.Record("Account", id, ("Name", name));

    [Fact] public async Task FirstAndSingleVariants()
    {
        await ((Func<Task>)(() => Q.FirstAsync())).Should().ThrowAsync<InvalidOperationException>();
        (await Q.SingleOrDefaultAsync()).Should().BeNull();
        (await Q.FirstOrDefaultAsync(a => a.Name == "x")).Should().BeNull();
        _ex.Last.Should().EndWith("WHERE (Name = 'x') LIMIT 1");

        _ex.Rows.Add(Acc("001000000000001AAA", "A"));
        (await Q.FirstAsync(a => a.Name == "A")).Name.Should().Be("A");
        (await Q.SingleAsync()).Name.Should().Be("A");
        (await Q.SingleAsync(a => a.Name != null)).Name.Should().Be("A");
        (await Q.SingleOrDefaultAsync(a => a.Name != null))!.Name.Should().Be("A");
        _ex.Last.Should().EndWith("LIMIT 2");

        _ex.Rows.Add(Acc("001000000000002AAA", "B"));
        await ((Func<Task>)(() => Q.SingleAsync())).Should().ThrowAsync<InvalidOperationException>().WithMessage("*more than one*");
    }

    [Fact] public async Task AnyAndCountPredicates()
    {
        _ex.TotalSize = 2;
        (await Q.AnyAsync(a => a.Name == "A")).Should().BeTrue();
        _ex.Last.Should().Be("SELECT COUNT() FROM Account WHERE (Name = 'A') LIMIT 1");
        (await Q.CountAsync(a => a.Industry == "T")).Should().Be(2);
        _ex.TotalSize = 0;
        (await Q.AnyAsync()).Should().BeFalse();
    }

    [Fact] public async Task Terminals_OnProjections()
    {
        _ex.Rows.Add(Acc("001000000000001AAA", "A"));
        var projected = Q.Select(a => a.Name);
        (await projected.FirstAsync()).Should().Be("A");
        _ex.TotalSize = 1;
        (await projected.AnyAsync()).Should().BeTrue();
    }

    [Fact] public async Task Terminals_RejectForeignQueryables()
    {
        await ((Func<Task>)(() => new[] { 1 }.AsQueryable().AnyAsync())).Should().ThrowAsync<NotSupportedException>();
    }
}
