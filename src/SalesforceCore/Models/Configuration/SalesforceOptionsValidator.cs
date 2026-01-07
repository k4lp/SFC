using Microsoft.Extensions.Options;

namespace SalesforceCore.Models.Configuration;

internal sealed class SalesforceOptionsValidator : IValidateOptions<SalesforceOptions>
{
    public ValidateOptionsResult Validate(string? name, SalesforceOptions options)
    {
        if (options == null) throw new ArgumentNullException(nameof(options));

        var failures = new List<string>();

        if (options.MaxResponseContentBufferSize <= 0)
        {
            failures.Add("Salesforce:MaxResponseContentBufferSize must be > 0.");
        }

        if (options.CacheProvider == Services.Caching.CacheProviderType.SqlServer)
        {
            ValidateSqlCache(options, failures);
        }

        return failures.Count > 0
            ? ValidateOptionsResult.Fail(failures)
            : ValidateOptionsResult.Success;
    }

    private static void ValidateSqlCache(SalesforceOptions options, List<string> failures)
    {
        if (options.CacheCleanupInterval <= TimeSpan.Zero)
        {
            failures.Add("Salesforce:CacheCleanupInterval must be > 0 when CacheProvider=SqlServer.");
        }

        var wb = options.SqlCacheWriteBehind ?? new SqlCacheWriteBehindOptions();

        if (wb.Capacity <= 0)
        {
            failures.Add("Salesforce:SqlCacheWriteBehind:Capacity must be > 0.");
        }

        if (wb.MaxBatchSize <= 0)
        {
            failures.Add("Salesforce:SqlCacheWriteBehind:MaxBatchSize must be > 0.");
        }

        if (wb.FlushInterval <= TimeSpan.Zero)
        {
            failures.Add("Salesforce:SqlCacheWriteBehind:FlushInterval must be > 0.");
        }

        if (wb.SlidingExpirationRefreshThresholdSeconds < 0)
        {
            failures.Add("Salesforce:SqlCacheWriteBehind:SlidingExpirationRefreshThresholdSeconds must be >= 0.");
        }

        if (wb.CleanupGracePeriod < TimeSpan.Zero)
        {
            failures.Add("Salesforce:SqlCacheWriteBehind:CleanupGracePeriod must be >= 0.");
        }

        if (wb.Enabled && wb.CleanupGracePeriod < wb.FlushInterval)
        {
            failures.Add(
                "Salesforce:SqlCacheWriteBehind:CleanupGracePeriod must be >= FlushInterval when write-behind is enabled " +
                "to avoid deleting entries before buffered sliding-expiration refresh is flushed.");
        }

        if (string.IsNullOrWhiteSpace(options.SqlCacheEncryptionKey))
        {
            if (!options.AllowInsecureSqlCacheKeyDerivation)
            {
                failures.Add(
                    "Salesforce:SqlCacheEncryptionKey is required when CacheProvider=SqlServer. " +
                    "Generate one with Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)) and store it in a secure vault. " +
                    "For development only, set Salesforce:AllowInsecureSqlCacheKeyDerivation=true to enable deterministic derivation.");
            }
        }
        else
        {
            try
            {
                var key = Convert.FromBase64String(options.SqlCacheEncryptionKey);
                if (key.Length != 32)
                {
                    failures.Add("Salesforce:SqlCacheEncryptionKey must decode to exactly 32 bytes (256 bits).");
                }
            }
            catch (FormatException)
            {
                failures.Add("Salesforce:SqlCacheEncryptionKey must be valid base64.");
            }
        }
    }
}
