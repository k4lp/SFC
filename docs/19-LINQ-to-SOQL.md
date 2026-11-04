# LINQ to SOQL

`SalesforceCore.Query` turns C# LINQ into SOQL. This guide lists every supported operator, the SOQL it produces, what
runs client-side, and what is rejected (with a clear `NotSupportedException`). It reflects **2.2.0**.

```csharp
using SalesforceCore.Query;
using SalesforceCore.Services.Query;   // DateLiteral, DateFunction, SoqlCondition

var accounts = await data.Query<Account>()          // data: IDataService or any ISoqlQueryExecutor
    .Where(a => a.Industry == "Technology" && a.AnnualRevenue > 1_000_000)
    .OrderByDescending(a => a.AnnualRevenue).NullsLast()
    .Take(50)
    .ToListAsync();
```

Every query can show its SOQL without running: `query.ToSoql()` (entity queries and projections) and
`query.ToAggregateSoql(g => ...)` (aggregates).

## Contents

1. [Running a query](#running-a-query)
2. [Filtering (`Where`)](#filtering-where)
3. [SOQL functions (`SoqlFunctions`)](#soql-functions-soqlfunctions)
4. [Semi-joins and anti-joins](#semi-joins-and-anti-joins)
5. [Projection (`Select`)](#projection-select)
6. [Ordering, paging](#ordering-and-paging)
7. [Counting and existence](#counting-and-existence)
8. [Aggregates, GROUP BY, HAVING, ROLLUP/CUBE](#aggregates)
9. [Relationships](#relationships)
10. [Clauses: FOR, WITH, queryAll](#clauses-for-with-queryall)
11. [Translation table](#translation-table)
12. [Not supported](#not-supported)
13. [Testing and custom executors](#testing-and-custom-executors)

## Running a query

| Call | Behaviour |
|---|---|
| `ToListAsync()` | First page only (up to 2,000 rows) |
| `ToListAllAsync()` | Follows `nextRecordsUrl` until done |
| `ToAsyncEnumerable()` | Streams every page (`await foreach`), fetching the next page only when needed |
| `FirstAsync / FirstOrDefaultAsync / SingleAsync / SingleOrDefaultAsync` (+ predicate overloads) | `LIMIT 1` / `LIMIT 2` |
| `AnyAsync`, `CountAsync`, `LongCountAsync` (+ predicate overloads) | `SELECT COUNT()`, nothing downloaded |
| Sync `ToList()`, `First()`, `Count()`, `Any()` … | Same SOQL. Safe under a `SynchronizationContext` (2.1+) but prefer async |

Since 2.2 `ToListAsync`, `ToListAllAsync` and `ToAsyncEnumerable` work on projections to any type (strings, numbers,
anonymous types), not only entity classes.

## Filtering (`Where`)

Several `Where` calls are ANDed. Comparisons may have the field on either side (`5 < a.Employees` becomes
`Employees > 5`).

| C# | SOQL |
|---|---|
| `a.Name == "Acme"`, `!=`, `<`, `<=`, `>`, `>=` | `Name = 'Acme'` … (values escaped, invariant culture) |
| `a.Name == null`, `a.CloseDate != null` (nullable too) | `Name = NULL`, `CloseDate != NULL` |
| `&&`, `\|\|`, `!` | `AND`, `OR`, `NOT` |
| `a.IsActive`, `!a.IsActive` | `IsActive = TRUE`, `NOT (IsActive = TRUE)` |
| `a.Name.Contains("x")` / `StartsWith` / `EndsWith` | `Name LIKE '%x%'` / `'x%'` / `'%x'` (`%` and `_` in the value are escaped) |
| `a.Name.Equals("x")` | `Name = 'x'` |
| `string.IsNullOrEmpty(a.Name)`, `IsNullOrWhiteSpace` | `Name = NULL` (Salesforce stores empty text as null) |
| `ids.Contains(a.Id)` (array, `List<>`, `HashSet<>`, any `IEnumerable`) | `Id IN ('…', '…')` |
| `!ids.Contains(a.Id)` | `Id NOT IN (…)` |
| empty list `Contains` / `!Contains` | `(Id = NULL)` (always false) / `(Id != NULL)` (always true) |
| `a.Account.Industry == "Tech"` | `Account.Industry = 'Tech'` (parent dot-notation) |
| `a.CreatedDate.Year == 2026` (`.Month`, `.Day`, `.Hour`, `.DayOfWeek`, `.DayOfYear`) | `CALENDAR_YEAR(CreatedDate) = 2026` … |
| `a.CreatedDate.Value.Year` on a nullable field | Same as above (`.Value` is ignored) |
| Enum values (nullable or not, either side, in `IN` lists) | The `[SalesforceValue]` name, or the enum name (2.2.0 sent the ordinal for non-nullable enums) |
| `double`/`float` values | Positional digits (`0.0000001`), never exponent form; NaN/Infinity throw |

**Values computed client-side.** Anything that does not touch the lambda parameter is evaluated once and sent as a
literal: `DateTime.UtcNow.AddDays(-7)`, `new DateOnly(2026, 1, 1)`, `x * y + 1`, `flag ? "A" : "B"`,
`name.ToUpperInvariant()`, helper calls.

**Date vs datetime.** `DateTime` values are sent as UTC datetimes (`2026-10-23T12:00:00Z`) and `DateOnly` values as dates
(`2026-01-01`). Compare a Salesforce *Date* field with a `DateOnly`, because Salesforce rejects a datetime literal against
a date field.

## SOQL functions (`SoqlFunctions`)

`SoqlFunctions` methods are markers. The provider translates them; calling one outside a query throws
`InvalidOperationException`.

| C# | SOQL |
|---|---|
| `a.CreatedDate >= SoqlFunctions.Literal(DateLiteral.TODAY)` | `CreatedDate >= TODAY` |
| `SoqlFunctions.Literal(DateLiteral.LAST_N_DAYS, 30)` | `LAST_N_DAYS:30` (`N_DAYS_AGO:3` …) |
| `a.CloseDate < SoqlFunctions.LiteralDate(DateLiteral.THIS_YEAR)` | `CloseDate < THIS_YEAR` (for `DateOnly` fields) |
| `SoqlFunctions.CalendarQuarter(a.CloseDate) == 2` | `CALENDAR_QUARTER(CloseDate) = 2` |
| `FiscalYear`, `FiscalQuarter`, `FiscalMonth`, `WeekInYear`, `WeekInMonth` | `FISCAL_YEAR(…)` … |
| `SoqlFunctions.DayOnly(a.CreatedDate) == new DateOnly(2026,10,30)` | `DAY_ONLY(CreatedDate) = 2026-10-30` |
| `SoqlFunctions.DatePart(DateFunction.HOUR_IN_DAY, SoqlFunctions.ConvertTimezone(a.CreatedDate)) < 9` | `HOUR_IN_DAY(convertTimezone(CreatedDate)) < 9` |
| `SoqlFunctions.Includes(a.Regions, "EMEA", "APAC;LATAM")` | `Regions__c INCLUDES ('EMEA', 'APAC;LATAM')` (`;` = both) |
| `SoqlFunctions.Excludes(a.Regions, "EMEA")` | `Regions__c EXCLUDES ('EMEA')` |
| `SoqlFunctions.ToLabel(a.Status) == "Offen"` | `toLabel(Status) = 'Offen'` |
| `SoqlFunctions.Like(a.Name, "Ac%_e")` | `Name LIKE 'Ac%_e'` (your wildcards kept, quotes escaped) |
| `SoqlFunctions.Distance(a.Location, 37.77, -122.42, DistanceUnit.Kilometers) < 20` | `DISTANCE(Location__c, GEOLOCATION(37.77, -122.42), 'km') < 20` |
| `OrderBy(a => SoqlFunctions.Distance(a.Location, lat, lon))` | `ORDER BY DISTANCE(…, 'mi') ASC` |

Invalid inputs fail fast: a literal that needs N but has none (or the reverse), a negative N, a latitude or longitude
out of range, or `Includes` with no values.

## Semi-joins and anti-joins

Both forms translate to a nested SELECT:

```csharp
var withEmail = data.Query<Contact>().Where(c => c.Email != null).Select(c => c.AccountId);

await data.Query<Account>().Where(a => SoqlFunctions.In(a.Id, withEmail)).ToListAsync();
await data.Query<Account>().Where(a => withEmail.Contains(a.Id)).ToListAsync();      // same
// WHERE Id IN (SELECT AccountId FROM Contact WHERE (Email != NULL))

await data.Query<Account>().Where(a => !withEmail.Contains(a.Id)).ToListAsync();     // or SoqlFunctions.NotIn
// WHERE Id NOT IN (SELECT AccountId FROM Contact WHERE (Email != NULL))
```

The subquery must be a Salesforce queryable that selects exactly one plain field, with `Where` only. `OrderBy`, `Take`,
`Skip`, `Include`, the FOR/WITH clauses and computed selects are rejected, because Salesforce rejects them inside
semi-joins. A local `IQueryable` (for example `list.AsQueryable()`) becomes an ordinary `IN (...)` list.

## Projection (`Select`)

| C# | SELECT |
|---|---|
| `Select(a => a.Name)` | `Id, Name` |
| `Select(a => new { a.Name, a.Account.Name })` | `Id, Name, Account.Name` |
| `Select(a => new Dto { Name = a.Name })` | `Id, Name` |
| **2.2:** `Select(a => a.First + " " + a.Last)`, `new { Big = a.Employees > 5 }`, `a.CreatedDate.Value.Year` | every field the expression reads (`Id, First, Last` …). The expression runs client-side on the fetched rows |
| `Select(a => Helper(a))` | all mapped fields |

`Id` is always selected. A projection that reads no field at all (`Select(a => 42)`) throws an error. After `Select`
you can still use `Skip/Take/First/Single/Count/Any` and the WITH/FOR/IncludeDeleted markers, but not `Where` or
`OrderBy`. Put those before the `Select`.

## Ordering and paging

| C# | SOQL |
|---|---|
| `OrderBy(a => a.Name).ThenByDescending(a => a.CreatedDate)` | `ORDER BY Name ASC, CreatedDate DESC` |
| `.OrderBy(a => a.Name).NullsFirst()` / `.NullsLast()` | `ORDER BY Name ASC NULLS FIRST` (applies to the preceding key) |
| `SortBy` / `ThenSortBy` (+`Descending`) | Same, without `IOrderedQueryable` casts |
| `Take(n)` / `Skip(k)` | `LIMIT n` / `OFFSET k` |

LINQ semantics are preserved (fixed in 2.2):

| LINQ | SOQL |
|---|---|
| `Take(5).Skip(2)` | `LIMIT 3 OFFSET 2` |
| `Take(10).Take(5)`, `Take(5).Take(10)` | `LIMIT 5` |
| `Skip(2).Skip(3)` | `OFFSET 5` |
| `Take(0)` | no request at all, empty result |

`Where`, `OrderBy`/`ThenBy` and predicate terminals (`First(p)`, `Count(p)`, `Any(p)`, `Single(p)`) after `Take`/`Skip`
throw `NotSupportedException` (2.2.1). SOQL always filters and sorts before `LIMIT`/`OFFSET`, so `Take(5).Where(p)`
would return "5 rows matching p" instead of "the rows among the first 5 that match p". Put them before `Take`/`Skip`.

Salesforce caps `OFFSET` at 2,000; `Skip(k)` with `k > 2000` throws before the request (2.2.1), except under
`Count`/`Any`, which never send OFFSET. For deep paging, use `ToAsyncEnumerable()` or keyset filters (`Where(a => a.Id > lastId)`).

## Counting and existence

`CountAsync()`, `LongCountAsync()` and `AnyAsync()` send `SELECT COUNT()` and never download rows. With `Skip`/`Take`
the count is clamped locally: `Count(Skip(k).Take(n)) = clamp(total - k, 0, n)`. `Any()` after `Skip(k)` asks whether
more than `k` rows exist. `AllAsync(p)` is `COUNT() WHERE NOT (p) == 0`.

## Aggregates

Scalar helpers use the query's `Where` and `WITH` only. Its `OrderBy` is ignored. Since 2.2.1 `Take`/`Skip` before any
aggregate helper (`Sum/Average/Min/Max`, `GroupBy*`, `DistinctAsync`, `CountDistinctAsync`, `AggregateAsync`) throws:
SOQL cannot aggregate a limited subset, and 2.2.0 silently aggregated every matching row. In 2.2 the filter is
read from the expression tree, not by cutting SOQL text.

```csharp
decimal total = await q.SumAsync(o => o.Amount);        // SELECT SUM(Amount) sum FROM Opportunity WHERE …
decimal avg   = await q.AverageAsync(o => o.Amount);
var first     = await q.MinAsync(o => o.CloseDate);
var last      = await q.MaxAsync(o => o.CloseDate);
int owners    = await q.CountDistinctAsync(o => o.OwnerId);   // COUNT_DISTINCT(OwnerId)
List<string?> stages = await q.DistinctAsync(o => o.StageName); // GROUP BY StageName
```

For GROUP BY, use the fluent `AggregateAsync`. Every field comes from an expression and every operator is checked
against a whitelist, so the builder never accepts raw SOQL.

```csharp
var rows = await data.Query<Opportunity>()
    .Where(o => o.IsClosed)
    .AggregateAsync(g => g
        .GroupBy(o => o.StageName)
        .GroupBy(o => SoqlFunctions.CalendarYear(o.CloseDate), "yr")   // any SoqlFunctions date function
        .Count()                                  // COUNT(Id) cnt
        .Sum(o => o.Amount, "total")
        .Average(o => o.Amount, "avg")
        .CountDistinct(o => o.AccountId, "accounts")
        .HavingCount(">", 5)
        .Having(AggregateFunction.Sum, o => o.Amount, ">=", 100000)
        .OrderByAggregate(AggregateFunction.Sum, o => o.Amount, descending: true)
        .Limit(10));

foreach (var r in rows)
    Console.WriteLine($"{r.Get<string>("StageName")} {r.Get<int>("yr")}: {r.Get<decimal>("total")}");
```

* `.Rollup()` / `.Cube()` give `GROUP BY ROLLUP(a, b)` / `CUBE(a, b)`. Subtotal rows have null keys.
* `ToAggregateSoql(g => …)` returns the SOQL without running it.
* `AggregateResult.Get<T>` converts numbers to `int`, `long`, `decimal`, `double` and their nullable forms, parses
  enums (by `[SalesforceValue]` or member name) and dates, and returns `default` if a non-enum value can't be
  converted. *2.2 fix: values used to stay wrapped as `JsonNode`, so `Get<decimal>` returned 0.* *2.5 fix: enum keys
  (`DistinctAsync`, `GroupBy`, `Min`/`Max`) used `Enum.Parse` and silently returned the first member for values such as
  "Closed Won"; an enum value matching no member now throws `InvalidCastException`.*
* `GroupByFieldAsync(selector)` and `GroupByWithAggregatesAsync(selector, ct, specs…)` (pre-2.2 APIs) remain.

## Relationships

```csharp
// Parent fields: dot-notation anywhere (Where, Select, OrderBy)
.Where(c => c.Account.Industry == "Tech")

// Child subqueries
data.Query<Account>().Include("Contacts", "Id", "Email")
// SELECT …, (SELECT Id, Email FROM Contacts) FROM Account
data.Query<Account>().IncludeWhere("Contacts", new[] { "Id" }, SoqlCondition.IsNotNull("Email"), limit: 5)
// (SELECT Id FROM Contacts WHERE Email != NULL LIMIT 5)
```

If `Include` gets no field list, the fields come from the child type's mapped properties, falling back to `Id, Name`.

## Clauses: FOR, WITH, queryAll

| Operator | SOQL / behaviour |
|---|---|
| `.ForUpdate()` / `.ForView()` / `.ForReference()` | `FOR UPDATE` / `FOR VIEW` / `FOR REFERENCE` |
| `.WithSecurityEnforced()` | `WITH SECURITY_ENFORCED`. Salesforce errors when FLS/CRUD would hide data |
| `.WithUserMode()` / `.WithSystemMode()` | `WITH USER_MODE` / `WITH SYSTEM_MODE` |
| `.IncludeDeleted()` | Uses the REST `queryAll` resource, so recycle-bin and archived rows are returned. Combine with `Where(a => a.IsDeleted)` |

These are opt-in pass-throughs. The library does no access checks of its own; Salesforce enforces access. The `WITH`
clause is also applied to `Count`, `Any` and aggregate queries built from the same query. `IncludeDeleted()` needs an
executor that implements `ISoqlDeletedRowsQueryExecutor`. The built-in `DataService` does, and custom executors without
it get a clear `NotSupportedException`. On the builder, `SoqlBuilder` has `WithSecurityEnforced()`, `WithUserMode()`
and `WithSystemMode()`.

## Translation table

| LINQ | SOQL |
|---|---|
| `Where(p)` | `WHERE p` |
| `Select(x => x.F)` | `SELECT Id, F` |
| `OrderBy / ThenBy (+Descending)` | `ORDER BY F ASC/DESC` |
| `.NullsFirst() / .NullsLast()` | `NULLS FIRST / NULLS LAST` |
| `Take(n)` / `Skip(k)` | `LIMIT n` / `OFFSET k` |
| `First / FirstOrDefault` | `LIMIT 1` |
| `Single / SingleOrDefault` | `LIMIT 2` (detects duplicates) |
| `Count / LongCount / Any` | `SELECT COUNT() …` (`LIMIT 1` for Any) |
| `All(p)` | `SELECT COUNT() … WHERE NOT (p)` |
| `SumAsync / AverageAsync / MinAsync / MaxAsync` | `SELECT SUM(F) sum …` |
| `CountDistinctAsync` | `SELECT COUNT_DISTINCT(F) cnt …` |
| `DistinctAsync(x => x.F)` | `SELECT F … GROUP BY F` |
| `AggregateAsync(g => g.GroupBy(...)…)` | `GROUP BY [ROLLUP/CUBE] … HAVING … ORDER BY … LIMIT` |
| `list.Contains(x.F)` / `!list.Contains(x.F)` | `F IN (…)` / `F NOT IN (…)` |
| `sub.Contains(x.F)`, `SoqlFunctions.In/NotIn` | `F IN (SELECT …)` / `NOT IN (SELECT …)` |
| `SoqlFunctions.Includes/Excludes` | `INCLUDES (…)` / `EXCLUDES (…)` |
| `s.Contains/StartsWith/EndsWith` | `LIKE` with escaped wildcards |
| `SoqlFunctions.Like` | `LIKE` with your wildcards |
| `x.Date.Year` … / `SoqlFunctions.CalendarQuarter` … | `CALENDAR_YEAR(Date)` … |
| `SoqlFunctions.Literal(...)` | `TODAY`, `LAST_N_DAYS:30` … |
| `SoqlFunctions.Distance(...)` | `DISTANCE(F, GEOLOCATION(lat, lon), 'mi')` |
| `SoqlFunctions.ToLabel(x.F)` | `toLabel(F)` |
| `Include / IncludeWhere` | child subquery `(SELECT … FROM Rel …)` |
| `ForUpdate / ForView / ForReference` | `FOR …` |
| `WithSecurityEnforced / WithUserMode / WithSystemMode` | `WITH …` |
| `IncludeDeleted` | `/queryAll/` endpoint |
| `WhereCondition(SoqlCondition)` | any condition from the type-safe condition builder |
| `ToSoql()` | the SOQL text |

## Not supported

Each of these throws a `NotSupportedException` whose message names the expression:

* Arithmetic or functions over fields: `a.Amount * 2 > 10`, `a.Name.ToUpper() == "X"`, `a.Name.Length`. SOQL can't
  express them. Compute the value outside the query, or use `SoqlFunctions`.
* Comparing two fields (`a.A == a.B`). SOQL compares fields only against literals. (2.2.0 sent invalid SOQL; 2.2.1 throws.)
* `GroupBy`, `Join`, `SelectMany`, `Reverse`, `Distinct`, `Union` and other LINQ operators the provider doesn't handle.
  Use `AggregateAsync`, `DistinctAsync`, semi-joins, or `UnionAsync`/`ExceptAsync`/`IntersectAsync`, which merge
  results client-side by Id.
* `Where` or `OrderBy` after `Select`.
* A stand-alone condition with no field (`Where(a => list.Contains("C"))`). Evaluate it before you build the query.
* `TYPEOF` and `FORMAT()`/`convertCurrency()` in SELECT. Use `SoqlBuilder.SelectFormat`/`SelectConvertCurrency`/
  `SelectToLabel` with `IDataService.QueryAsync` for those.

## Testing and custom executors

The provider needs only `ISoqlQueryExecutor` (three methods). Use a fake to assert on the generated SOQL:

```csharp
sealed class Recorder : ISoqlQueryExecutor
{
    public List<string> Soql { get; } = new();
    public Task<QueryResult> QueryAsync(string soql, CancellationToken ct = default)
    { Soql.Add(soql); return Task.FromResult(new QueryResult { Done = true }); }
    public Task<List<JsonObject>> QueryAllAsync(string soql, CancellationToken ct = default)
    { Soql.Add(soql); return Task.FromResult(new List<JsonObject>()); }
    public async IAsyncEnumerable<JsonObject> QueryAllAsyncEnumerable(string soql, [EnumeratorCancellation] CancellationToken ct = default)
    { Soql.Add(soql); await Task.CompletedTask; yield break; }
}

var rec = new Recorder();
await rec.Query<Account>().Where(a => a.Name == "Acme").Take(1).ToListAsync();
Assert.Equal("SELECT Id, Name, … FROM Account WHERE (Name = 'Acme') LIMIT 1", rec.Soql[0]);
```

To support `IncludeDeleted()`, also implement `ISoqlDeletedRowsQueryExecutor`.
