using SalesforceCore.Services.Query;

namespace SalesforceCore.Query;

/// <summary>
/// Distance unit for <see cref="SoqlFunctions.Distance"/>.
/// </summary>
public enum DistanceUnit
{
    /// <summary>Miles (<c>'mi'</c>).</summary>
    Miles,
    /// <summary>Kilometers (<c>'km'</c>).</summary>
    Kilometers
}

/// <summary>
/// SOQL-only functions for use inside Salesforce LINQ expressions (<c>Where</c>, <c>OrderBy</c>).
/// Each method is a translation marker: the LINQ provider turns the call into the matching SOQL
/// construct, and calling one directly in client code throws <see cref="InvalidOperationException"/>.
/// </summary>
/// <example>
/// <code>
/// var recent = await data.Query&lt;Account&gt;()
///     .Where(a =&gt; a.CreatedDate &gt;= SoqlFunctions.Literal(DateLiteral.LAST_N_DAYS, 30))
///     .Where(a =&gt; SoqlFunctions.Includes(a.Regions__c, "EMEA", "APAC"))
///     .Where(a =&gt; SoqlFunctions.In(a.Id, data.Query&lt;Contact&gt;().Where(c =&gt; c.Email != null).Select(c =&gt; c.AccountId)))
///     .ToListAsync();
/// </code>
/// </example>
public static class SoqlFunctions
{
    private static InvalidOperationException ClientSide(string name) =>
        new($"SoqlFunctions.{name} can only be used inside a Salesforce LINQ query expression; it is translated to SOQL and cannot run client-side.");

    // ---- Date literals -------------------------------------------------------------------------

    /// <summary>A SOQL date literal such as <c>TODAY</c> or <c>LAST_MONTH</c>, for comparing datetime fields.</summary>
    public static DateTime Literal(DateLiteral literal) => throw ClientSide(nameof(Literal));

    /// <summary>A parameterised SOQL date literal such as <c>LAST_N_DAYS:30</c> or <c>N_DAYS_AGO:3</c>, for datetime fields.</summary>
    public static DateTime Literal(DateLiteral literal, int n) => throw ClientSide(nameof(Literal));

    /// <summary>A SOQL date literal for comparing date (<see cref="System.DateOnly"/>) fields.</summary>
    public static DateOnly LiteralDate(DateLiteral literal) => throw ClientSide(nameof(LiteralDate));

    /// <summary>A parameterised SOQL date literal for comparing date (<see cref="System.DateOnly"/>) fields.</summary>
    public static DateOnly LiteralDate(DateLiteral literal, int n) => throw ClientSide(nameof(LiteralDate));

    // ---- Date functions ------------------------------------------------------------------------

    /// <summary>Any SOQL date function, e.g. <c>CALENDAR_QUARTER(CloseDate)</c>.</summary>
    public static int DatePart(DateFunction function, DateTime? field) => throw ClientSide(nameof(DatePart));

    /// <summary>Any SOQL date function over a date field.</summary>
    public static int DatePart(DateFunction function, DateOnly? field) => throw ClientSide(nameof(DatePart));

    /// <summary><c>CALENDAR_YEAR(field)</c> (also usable as a GROUP BY key).</summary>
    public static int CalendarYear(DateTime? field) => throw ClientSide(nameof(CalendarYear));
    /// <summary><c>CALENDAR_YEAR(field)</c>.</summary>
    public static int CalendarYear(DateOnly? field) => throw ClientSide(nameof(CalendarYear));
    /// <summary><c>CALENDAR_MONTH(field)</c>.</summary>
    public static int CalendarMonth(DateTime? field) => throw ClientSide(nameof(CalendarMonth));
    /// <summary><c>CALENDAR_MONTH(field)</c>.</summary>
    public static int CalendarMonth(DateOnly? field) => throw ClientSide(nameof(CalendarMonth));
    /// <summary><c>CALENDAR_QUARTER(field)</c>.</summary>
    public static int CalendarQuarter(DateTime? field) => throw ClientSide(nameof(CalendarQuarter));
    /// <summary><c>CALENDAR_QUARTER(field)</c>.</summary>
    public static int CalendarQuarter(DateOnly? field) => throw ClientSide(nameof(CalendarQuarter));
    /// <summary><c>FISCAL_YEAR(field)</c>.</summary>
    public static int FiscalYear(DateTime? field) => throw ClientSide(nameof(FiscalYear));
    /// <summary><c>FISCAL_YEAR(field)</c>.</summary>
    public static int FiscalYear(DateOnly? field) => throw ClientSide(nameof(FiscalYear));
    /// <summary><c>FISCAL_QUARTER(field)</c>.</summary>
    public static int FiscalQuarter(DateTime? field) => throw ClientSide(nameof(FiscalQuarter));
    /// <summary><c>FISCAL_QUARTER(field)</c>.</summary>
    public static int FiscalQuarter(DateOnly? field) => throw ClientSide(nameof(FiscalQuarter));
    /// <summary><c>FISCAL_MONTH(field)</c>.</summary>
    public static int FiscalMonth(DateTime? field) => throw ClientSide(nameof(FiscalMonth));
    /// <summary><c>FISCAL_MONTH(field)</c>.</summary>
    public static int FiscalMonth(DateOnly? field) => throw ClientSide(nameof(FiscalMonth));
    /// <summary><c>WEEK_IN_YEAR(field)</c>.</summary>
    public static int WeekInYear(DateTime? field) => throw ClientSide(nameof(WeekInYear));
    /// <summary><c>WEEK_IN_YEAR(field)</c>.</summary>
    public static int WeekInYear(DateOnly? field) => throw ClientSide(nameof(WeekInYear));
    /// <summary><c>WEEK_IN_MONTH(field)</c>.</summary>
    public static int WeekInMonth(DateTime? field) => throw ClientSide(nameof(WeekInMonth));
    /// <summary><c>WEEK_IN_MONTH(field)</c>.</summary>
    public static int WeekInMonth(DateOnly? field) => throw ClientSide(nameof(WeekInMonth));

    /// <summary><c>DAY_ONLY(field)</c>: the date part of a datetime field.</summary>
    public static DateOnly DayOnly(DateTime? field) => throw ClientSide(nameof(DayOnly));

    /// <summary><c>convertTimezone(field)</c>: use inside a date function to evaluate in the user's time zone.</summary>
    public static DateTime ConvertTimezone(DateTime? field) => throw ClientSide(nameof(ConvertTimezone));

    // ---- Picklists -----------------------------------------------------------------------------

    /// <summary><c>field INCLUDES ('a', 'b')</c> for multi-select picklists. Use <c>"a;b"</c> to require both values.</summary>
    public static bool Includes<TField>(TField field, params string[] values) => throw ClientSide(nameof(Includes));

    /// <summary><c>field EXCLUDES ('a', 'b')</c> for multi-select picklists.</summary>
    public static bool Excludes<TField>(TField field, params string[] values) => throw ClientSide(nameof(Excludes));

    /// <summary><c>toLabel(field)</c>: compares against the translated picklist label.</summary>
    public static string ToLabel<TField>(TField field) => throw ClientSide(nameof(ToLabel));

    // ---- Semi-joins / anti-joins -----------------------------------------------------------------

    /// <summary>
    /// Semi-join: <c>field IN (SELECT ... FROM ...)</c>. The subquery must be a Salesforce queryable
    /// that projects exactly one field, e.g. <c>data.Query&lt;Contact&gt;().Select(c =&gt; c.AccountId)</c>.
    /// <c>subquery.Contains(x.Field)</c> translates the same way.
    /// </summary>
    public static bool In<TField>(TField field, IQueryable<TField> subquery) => throw ClientSide(nameof(In));

    /// <summary>Anti-join: <c>field NOT IN (SELECT ... FROM ...)</c>.</summary>
    public static bool NotIn<TField>(TField field, IQueryable<TField> subquery) => throw ClientSide(nameof(NotIn));

    // ---- Text / geolocation -------------------------------------------------------------------

    /// <summary>
    /// Raw <c>field LIKE 'pattern'</c> where <c>%</c> and <c>_</c> in <paramref name="pattern"/> stay wildcards
    /// (quotes and backslashes are still escaped). <c>string.Contains/StartsWith/EndsWith</c> escape wildcards instead.
    /// </summary>
    public static bool Like(string? field, string pattern) => throw ClientSide(nameof(Like));

    /// <summary><c>DISTANCE(field, GEOLOCATION(lat, lon), 'mi'|'km')</c>, usable in Where and OrderBy.</summary>
    public static double Distance(object? locationField, double latitude, double longitude, DistanceUnit unit = DistanceUnit.Miles)
        => throw ClientSide(nameof(Distance));
}
