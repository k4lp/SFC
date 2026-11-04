namespace SalesforceCore.Services.Caching.SqlServer;

/// <summary>2.7.0: SQL Server identifier quoting (QUOTENAME semantics) for the few raw SQL statements the cache runs.</summary>
public static class SqlIdentifier
{
    /// <summary>Wraps <paramref name="name"/> in brackets, doubling any <c>]</c>. Rejects empty names, NUL and over-long names.</summary>
    public static string Quote(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length > 128 || name.Contains('\0'))
        {
            throw new ArgumentException($"Invalid SQL Server identifier '{name}'.", nameof(name));
        }
        return "[" + name.Replace("]", "]]", StringComparison.Ordinal) + "]";
    }

    /// <summary>Returns <c>[schema].[table]</c>, or <c>[table]</c> when no schema is set.</summary>
    public static string Qualify(string? schema, string table)
        => string.IsNullOrWhiteSpace(schema) ? Quote(table) : $"{Quote(schema)}.{Quote(table)}";
}
