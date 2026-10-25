using System.Globalization;

namespace SalesforceCore;

/// <summary>
/// A Salesforce REST API version (e.g. <c>v62.0</c>). Accepts <c>"v62.0"</c>, <c>"62.0"</c> or <c>"62"</c>
/// and always renders in the canonical <c>vNN.0</c> form used in <c>/services/data/vNN.0/...</c> URLs.
/// </summary>
public readonly record struct SalesforceApiVersion : IComparable<SalesforceApiVersion>
{
    /// <summary>Lowest version accepted (Salesforce retired versions below 31.0).</summary>
    public const int MinimumMajor = 31;

    /// <summary>The library default API version.</summary>
    public static SalesforceApiVersion Default { get; } = new(62);

    /// <summary>Major version number (e.g. 62).</summary>
    public int Major { get; }

    /// <summary>Creates a version from its major number.</summary>
    public SalesforceApiVersion(int major)
    {
        if (major < MinimumMajor || major > 999)
            throw new ArgumentOutOfRangeException(nameof(major), major, $"Salesforce API version must be between {MinimumMajor} and 999.");
        Major = major;
    }

    /// <summary>Parses <c>v62.0</c>, <c>62.0</c> or <c>62</c>.</summary>
    public static SalesforceApiVersion Parse(string value)
        => TryParse(value, out var v) ? v : throw new FormatException($"'{value}' is not a valid Salesforce API version (expected e.g. 'v62.0').");

    /// <summary>Tries to parse <c>v62.0</c>, <c>62.0</c> or <c>62</c>.</summary>
    public static bool TryParse(string? value, out SalesforceApiVersion version)
    {
        version = default;
        if (string.IsNullOrWhiteSpace(value)) return false;
        var s = value.Trim();
        if (s.StartsWith('v') || s.StartsWith('V')) s = s[1..];
        if (s.EndsWith(".0", StringComparison.Ordinal)) s = s[..^2];
        if (s.Length is 0 or > 3 || !s.All(char.IsAsciiDigit)) return false;
        var major = int.Parse(s, NumberStyles.None, CultureInfo.InvariantCulture);
        if (major < MinimumMajor) return false;
        version = new SalesforceApiVersion(major);
        return true;
    }

    /// <summary>Canonical form, e.g. <c>v62.0</c>.</summary>
    public override string ToString() => Major == 0 ? Default.ToString() : $"v{Major.ToString(CultureInfo.InvariantCulture)}.0";

    /// <summary>The data API base path, e.g. <c>/services/data/v62.0</c>.</summary>
    public string DataPath => $"/services/data/{this}";

    /// <inheritdoc/>
    public int CompareTo(SalesforceApiVersion other) => Major.CompareTo(other.Major);

    /// <summary>Compares versions.</summary>
    public static bool operator <(SalesforceApiVersion a, SalesforceApiVersion b) => a.Major < b.Major;
    /// <summary>Compares versions.</summary>
    public static bool operator >(SalesforceApiVersion a, SalesforceApiVersion b) => a.Major > b.Major;
    /// <summary>Compares versions.</summary>
    public static bool operator <=(SalesforceApiVersion a, SalesforceApiVersion b) => a.Major <= b.Major;
    /// <summary>Compares versions.</summary>
    public static bool operator >=(SalesforceApiVersion a, SalesforceApiVersion b) => a.Major >= b.Major;

    /// <summary>Implicit conversion to the canonical string.</summary>
    public static implicit operator string(SalesforceApiVersion v) => v.ToString();
}
