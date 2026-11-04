using SalesforceCore.Models.Data;
using System.Collections;
using System.Globalization;
using System.Reflection;
using System.Text;

namespace SalesforceCore.Mapping;

/// <summary>
/// The single source of truth for how .NET values are written to Salesforce (REST/composite JSON, SOQL literals,
/// Bulk API 2.0 CSV) and how Salesforce text values are read back. See docs/20-Data-Type-Mapping.md and docs/21-Wire-Compliance.md.
/// </summary>
/// <remarks>
/// Formats follow the Salesforce documentation: REST API Developer Guide ("Valid Date and DateTime Formats"), SOQL and
/// SOSL Reference ("Date Formats and Date Literals", "Quoted String Escape Sequences"), Bulk API 2.0 Developer Guide
/// ("Valid Date Format in Records", "Create a Job" lineEnding), Object Reference ("Field Types").
/// </remarks>
public static class SalesforceWireFormat
{
    /// <summary>Salesforce's case-safe id alphabet (Object Reference, "ID Field Type").</summary>
    private const string IdSuffixAlphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ012345";

    // ------------------------------------------------------------------ dates and times

    /// <summary>
    /// dateTime for REST/composite/Bulk: <c>yyyy-MM-ddTHH:mm:ss.fffZ</c> in UTC. <see cref="DateTimeKind.Unspecified"/> is
    /// taken as UTC (Salesforce stores dateTime in UTC; nothing in the value says otherwise).
    /// </summary>
    public static string FormatDateTime(DateTime value)
        => ToUtc(value).ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);

    /// <inheritdoc cref="FormatDateTime(DateTime)"/>
    public static string FormatDateTime(DateTimeOffset value)
        => value.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);

    /// <summary>
    /// dateTime as a SOQL literal: <c>yyyy-MM-ddTHH:mm:ssZ</c>, the documented SOQL form (no fractional seconds), so
    /// milliseconds are truncated. Compare against whole seconds, or filter in memory when sub-second precision matters.
    /// </summary>
    public static string FormatSoqlDateTime(DateTime value)
        => ToUtc(value).ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);

    /// <summary>date: <c>yyyy-MM-dd</c>, no time zone.</summary>
    public static string FormatDate(DateOnly value) => value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    /// <summary>time: <c>HH:mm:ss.fffZ</c>.</summary>
    public static string FormatTime(TimeOnly value) => value.ToString("HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);

    /// <summary>
    /// Parses a Salesforce dateTime. Accepts the REST form <c>2024-01-02T03:04:05.000+0000</c> (offset without colon,
    /// which <see cref="System.Text.Json"/> rejects), <c>Z</c>, <c>+hh:mm</c>, and Bulk CSV <c>2024-01-02T03:04:05.000Z</c>.
    /// </summary>
    public static bool TryParseDateTime(string? text, out DateTimeOffset value)
    {
        value = default;
        if (string.IsNullOrWhiteSpace(text)) return false;
        var s = text.Trim();
        // +0000 / -0530 -> +00:00 / -05:30
        if (s.Length > 5 && (s[^5] == '+' || s[^5] == '-') && char.IsDigit(s[^4]) && char.IsDigit(s[^1]) && s[^3] != ':')
        {
            s = s[..^2] + ":" + s[^2..];
        }
        return DateTimeOffset.TryParse(s, CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out value);
    }

    /// <summary>Parses a Salesforce time (<c>HH:mm:ss.fffZ</c>; the trailing Z is optional).</summary>
    public static bool TryParseTime(string? text, out TimeOnly value)
    {
        value = default;
        if (string.IsNullOrWhiteSpace(text)) return false;
        var s = text.Trim().TrimEnd('Z', 'z');
        return TimeOnly.TryParse(s, CultureInfo.InvariantCulture, DateTimeStyles.None, out value);
    }

    private static DateTime ToUtc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Local => value.ToUniversalTime(),
        DateTimeKind.Unspecified => DateTime.SpecifyKind(value, DateTimeKind.Utc),
        _ => value
    };

    // ------------------------------------------------------------------ numbers

    /// <summary>
    /// A double/float as positional digits with its full round-trip precision (no exponent, no rounding to 15 digits).
    /// SOQL numeric literals have no exponent form; 2.5.0 and earlier cast through decimal, which keeps only 15
    /// significant digits (0.1234567890123456789 became 0.123456789012346). Floats use their own shortest form (0.1f
    /// is 0.1, not 0.100000001490116).
    /// </summary>
    /// <exception cref="ArgumentException">NaN or infinity (no Salesforce number field can hold them).</exception>
    public static string FormatNumber(double value)
        => ExpandExponent(Check(value).ToString("R", CultureInfo.InvariantCulture));

    /// <inheritdoc cref="FormatNumber(double)"/>
    public static string FormatNumber(float value)
        => ExpandExponent(((float)Check(value)).ToString("R", CultureInfo.InvariantCulture));

    private static double Check(double value)
    {
        if (double.IsNaN(value) || double.IsInfinity(value))
        {
            throw new ArgumentException($"The number {value.ToString(CultureInfo.InvariantCulture)} cannot be sent to Salesforce.");
        }
        return value;
    }

    internal static string ExpandExponent(string s)
    {
        var e = s.IndexOfAny(new[] { 'E', 'e' });
        if (e < 0) return s;
        var exponent = int.Parse(s[(e + 1)..], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);
        var mantissa = s[..e];
        var negative = mantissa.StartsWith('-');
        if (negative) mantissa = mantissa[1..];
        var dot = mantissa.IndexOf('.');
        var digits = dot < 0 ? mantissa : mantissa.Remove(dot, 1);
        var pointPos = (dot < 0 ? mantissa.Length : dot) + exponent;
        string result;
        if (pointPos <= 0) result = "0." + new string('0', -pointPos) + digits;
        else if (pointPos >= digits.Length) result = digits + new string('0', pointPos - digits.Length);
        else result = digits[..pointPos] + "." + digits[pointPos..];
        if (result.Contains('.')) result = result.TrimEnd('0').TrimEnd('.');
        return (negative ? "-" : "") + result;
    }

    // ------------------------------------------------------------------ picklists

    /// <summary>
    /// multipicklist: values joined by <c>;</c> (Object Reference, "multipicklist"). Accepts any sequence of strings or
    /// enums, or a [Flags] enum (each set flag's <c>[SalesforceValue]</c>/name, in declaration order).
    /// </summary>
    public static string FormatMultiPicklist(object value) => value switch
    {
        string s => s,
        Enum e when IsFlags(e.GetType()) => string.Join(";", FlagValues(e)),
        Enum e => SalesforceMapper.GetEnumSalesforceValue(e),
        IEnumerable seq => string.Join(";", seq.Cast<object?>().Where(v => v != null)
            .Select(v => v is Enum en ? SalesforceMapper.GetEnumSalesforceValue(en) : Convert.ToString(v, CultureInfo.InvariantCulture))),
        _ => Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty
    };

    /// <summary>Splits a multipicklist value (<c>A;B</c>) into its values; empty/null gives an empty array.</summary>
    public static string[] SplitMultiPicklist(string? value)
        => string.IsNullOrEmpty(value) ? Array.Empty<string>() : value.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    internal static bool IsFlags(Type t) => t.IsEnum && t.IsDefined(typeof(FlagsAttribute), false);

    private static IEnumerable<string> FlagValues(Enum value)
    {
        var raw = Convert.ToUInt64(value, CultureInfo.InvariantCulture);
        foreach (var field in value.GetType().GetFields(BindingFlags.Public | BindingFlags.Static))
        {
            var member = (Enum)field.GetValue(null)!;
            var bits = Convert.ToUInt64(member, CultureInfo.InvariantCulture);
            if (bits != 0 && (bits & (bits - 1)) == 0 && (raw & bits) == bits)
            {
                yield return SalesforceMapper.GetEnumSalesforceValue(member);
            }
        }
    }

    /// <summary>Parses <c>A;B</c> into a [Flags] enum; unknown values throw <see cref="ArgumentException"/>.</summary>
    public static object ParseFlags(string value, Type flagsEnum)
    {
        ulong bits = 0;
        foreach (var part in SplitMultiPicklist(value))
        {
            bits |= Convert.ToUInt64(SalesforceMapper.ParseEnumValue(part, flagsEnum), CultureInfo.InvariantCulture);
        }
        return Enum.ToObject(flagsEnum, bits);
    }

    // ------------------------------------------------------------------ ids

    /// <summary>
    /// Converts a 15-character (case-sensitive) id to its 18-character case-safe form; 18-character ids are returned
    /// unchanged. Algorithm: Object Reference, "ID Field Type" / the documented CASESAFEID suffix.
    /// </summary>
    public static string To18CharId(string id)
    {
        ArgumentNullException.ThrowIfNull(id);
        if (id.Length == 18) return id;
        if (id.Length != 15 || !id.All(char.IsAsciiLetterOrDigit))
        {
            throw new ArgumentException($"'{id}' is not a 15- or 18-character Salesforce id.", nameof(id));
        }
        var suffix = new char[3];
        for (var block = 0; block < 3; block++)
        {
            var flags = 0;
            for (var i = 0; i < 5; i++)
            {
                if (char.IsAsciiLetterUpper(id[block * 5 + i])) flags |= 1 << i;
            }
            suffix[block] = IdSuffixAlphabet[flags];
        }
        return id + new string(suffix);
    }

    /// <summary>
    /// True when two ids name the same record: a 15-character id matches its 18-character form; 15-character ids
    /// compare case-sensitively (<c>001A</c> and <c>001a</c> are different records).
    /// </summary>
    public static bool IdEquals(string? a, string? b)
    {
        if (a == null || b == null) return a == b;
        if (a.Length is not (15 or 18) || b.Length is not (15 or 18)) return string.Equals(a, b, StringComparison.Ordinal);
        // The 18-character suffix encodes the case of the first 15, so the 18-character forms compare case-insensitively.
        return string.Equals(To18CharId(a), To18CharId(b), StringComparison.OrdinalIgnoreCase);
    }

    // ------------------------------------------------------------------ SOQL literals

    /// <summary>
    /// A value as a SOQL literal: <c>NULL</c>, <c>TRUE</c>/<c>FALSE</c>, positional numbers, unquoted date/dateTime,
    /// quoted and escaped strings (<c>\\ \' \" \n \r \t \b \f</c>), enum <c>[SalesforceValue]</c>s, and a
    /// parenthesised IN list for sequences.
    /// </summary>
    public static string SoqlLiteral(object? value) => value switch
    {
        null => "NULL",
        string s => $"'{Utilities.SecurityUtils.EscapeSoqlStringLiteral(s)}'",
        char c => $"'{Utilities.SecurityUtils.EscapeSoqlStringLiteral(c.ToString())}'",
        bool b => b ? "TRUE" : "FALSE",
        DateTime dt => FormatSoqlDateTime(dt),
        DateTimeOffset dto => FormatSoqlDateTime(dto.UtcDateTime),
        DateOnly d => FormatDate(d),
        TimeOnly t => FormatTime(t),
        sbyte or byte or short or ushort or int or uint or long or ulong => Convert.ToString(value, CultureInfo.InvariantCulture)!,
        decimal dec => dec.ToString(CultureInfo.InvariantCulture),
        double dbl => FormatNumber(dbl),
        float fl => FormatNumber(fl),
        Enum e => $"'{Utilities.SecurityUtils.EscapeSoqlStringLiteral(SalesforceMapper.GetEnumSalesforceValue(e))}'",
        Guid g => $"'{g}'",
        IEnumerable seq => "(" + string.Join(", ", seq.Cast<object?>().Select(SoqlLiteral)) + ")",
        _ => $"'{Utilities.SecurityUtils.EscapeSoqlStringLiteral(Convert.ToString(value, CultureInfo.InvariantCulture))}'"
    };

    // ------------------------------------------------------------------ Bulk API 2.0 CSV

    /// <summary>
    /// A value as a Bulk API 2.0 CSV cell (before CSV quoting): invariant numbers without exponent, ISO-8601 UTC
    /// dateTime with milliseconds, <c>yyyy-MM-dd</c> dates, <c>HH:mm:ss.fffZ</c> times, lower-case booleans, enum
    /// <c>[SalesforceValue]</c>, multipicklists joined with <c>;</c>.
    /// </summary>
    /// <exception cref="NotSupportedException">byte[] (Bulk API 2.0 does not load binary/base64 fields).</exception>
    public static string CsvValue(object? value) => value switch
    {
        null => string.Empty,
        string s => s,
        bool b => b ? "true" : "false",
        DateTime dt => FormatDateTime(dt),
        DateTimeOffset dto => FormatDateTime(dto),
        DateOnly d => FormatDate(d),
        TimeOnly t => FormatTime(t),
        double dbl => FormatNumber(dbl),
        float fl => FormatNumber(fl),
        Enum e when IsFlags(e.GetType()) => FormatMultiPicklist(e),
        Enum e => SalesforceMapper.GetEnumSalesforceValue(e),
        byte[] => throw new NotSupportedException("Bulk API 2.0 cannot load base64/binary fields; use the REST API (ContentVersion) for files."),
        IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
        IEnumerable seq => FormatMultiPicklist(seq),
        _ => value.ToString() ?? string.Empty
    };

    // ------------------------------------------------------------------ REST record values

    /// <summary>
    /// Normalises one record field value for a REST/composite record body: multipicklist sequences and [Flags] enums
    /// become <c>A;B</c> (Salesforce rejects a JSON array for a multipicklist), dates/times become their wire strings.
    /// byte[] stays as is (serialised as base64, the REST format for base64 fields); nested records/dictionaries stay.
    /// </summary>
    public static object? RecordValue(object? value) => value switch
    {
        null or string or bool or byte[] => value,
        DateTime dt => FormatDateTime(dt),
        DateTimeOffset dto => FormatDateTime(dto),
        DateOnly d => FormatDate(d),
        TimeOnly t => FormatTime(t),
        Enum e when IsFlags(e.GetType()) => FormatMultiPicklist(e),
        IDictionary => value,
        System.Text.Json.Nodes.JsonNode => value,
        IEnumerable seq => FormatMultiPicklist(seq),
        _ => value
    };

    /// <summary>A record body with every value passed through <see cref="RecordValue"/>.</summary>
    public static Dictionary<string, object?> RecordBody(IDictionary<string, object?> record)
    {
        ArgumentNullException.ThrowIfNull(record);
        var body = new Dictionary<string, object?>(record.Count, StringComparer.Ordinal);
        foreach (var (key, value) in record) body[key] = RecordValue(value);
        return body;
    }
}
