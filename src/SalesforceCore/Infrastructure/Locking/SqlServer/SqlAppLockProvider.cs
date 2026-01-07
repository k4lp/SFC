using System.Data;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using SalesforceCore.Infrastructure.Locking;

namespace SalesforceCore.Infrastructure.Locking.SqlServer;

/// <summary>
/// SQL Server distributed lock provider based on <c>sp_getapplock</c>.
/// </summary>
public sealed class SqlAppLockProvider : IDistributedLockProvider
{
    // sp_getapplock @Resource is nvarchar(255)
    private const int MaxResourceNameLength = 255;

    private readonly string _connectionString;
    private readonly ILogger<SqlAppLockProvider> _logger;

    public SqlAppLockProvider(string connectionString, ILogger<SqlAppLockProvider> logger)
    {
        _connectionString = string.IsNullOrWhiteSpace(connectionString)
            ? throw new ArgumentNullException(nameof(connectionString))
            : connectionString;
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<IDistributedLockHandle?> TryAcquireAsync(
        string resourceName,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(resourceName))
            throw new ArgumentNullException(nameof(resourceName));
        if (timeout < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(timeout), "Timeout must be >= 0.");

        var normalizedResource = NormalizeResourceName(resourceName);
        var timeoutMs = timeout == Timeout.InfiniteTimeSpan
            ? -1
            : (int)Math.Min(int.MaxValue, Math.Max(0, timeout.TotalMilliseconds));

        var connection = new SqlConnection(_connectionString);

        try
        {
            await connection.OpenAsync(cancellationToken);

            using var command = connection.CreateCommand();
            command.CommandType = CommandType.StoredProcedure;
            command.CommandText = "sp_getapplock";

            var returnValue = command.Parameters.Add("@RETURN_VALUE", SqlDbType.Int);
            returnValue.Direction = ParameterDirection.ReturnValue;

            command.Parameters.Add(new SqlParameter("@Resource", SqlDbType.NVarChar, MaxResourceNameLength)
            {
                Value = normalizedResource
            });
            command.Parameters.Add(new SqlParameter("@LockMode", SqlDbType.NVarChar, 32) { Value = "Exclusive" });
            command.Parameters.Add(new SqlParameter("@LockOwner", SqlDbType.NVarChar, 32) { Value = "Session" });
            command.Parameters.Add(new SqlParameter("@LockTimeout", SqlDbType.Int) { Value = timeoutMs });

            await command.ExecuteNonQueryAsync(cancellationToken);

            var result = returnValue.Value is int value ? value : Convert.ToInt32(returnValue.Value);

            if (result >= 0)
            {
                _logger.LogDebug("SQL applock acquired. Resource={Resource} Result={Result}", normalizedResource, result);
                return new SqlAppLockHandle(connection, normalizedResource, _logger);
            }

            // Negative codes: timeout/deadlock/cancel/etc.
            _logger.LogDebug("SQL applock not acquired. Resource={Resource} Result={Result}", normalizedResource, result);
            await connection.DisposeAsync();
            return null;
        }
        catch
        {
            await connection.DisposeAsync();
            throw;
        }
    }

    private static string NormalizeResourceName(string resourceName)
    {
        if (resourceName.Length <= MaxResourceNameLength)
        {
            return resourceName;
        }

        var bytes = Encoding.UTF8.GetBytes(resourceName);
        var hash = SHA256.HashData(bytes);
        var hex = ConvertToHex(hash);
        return $"sha256:{hex}";
    }

    private static string ConvertToHex(ReadOnlySpan<byte> bytes)
    {
        var chars = new char[bytes.Length * 2];
        for (int i = 0; i < bytes.Length; i++)
        {
            var b = bytes[i];
            chars[i * 2] = GetHexNibble(b >> 4);
            chars[i * 2 + 1] = GetHexNibble(b & 0xF);
        }
        return new string(chars);
    }

    private static char GetHexNibble(int value)
    {
        return (char)(value < 10 ? ('0' + value) : ('a' + (value - 10)));
    }

    private sealed class SqlAppLockHandle : IDistributedLockHandle
    {
        private readonly SqlConnection _connection;
        private readonly string _resourceName;
        private readonly ILogger _logger;
        private bool _disposed;

        public SqlAppLockHandle(SqlConnection connection, string resourceName, ILogger logger)
        {
            _connection = connection;
            _resourceName = resourceName;
            _logger = logger;
        }

        public string ResourceName => _resourceName;

        public async ValueTask DisposeAsync()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;

            try
            {
                if (_connection.State == ConnectionState.Open)
                {
                    using var command = _connection.CreateCommand();
                    command.CommandType = CommandType.StoredProcedure;
                    command.CommandText = "sp_releaseapplock";

                    var returnValue = command.Parameters.Add("@RETURN_VALUE", SqlDbType.Int);
                    returnValue.Direction = ParameterDirection.ReturnValue;

                    command.Parameters.Add(new SqlParameter("@Resource", SqlDbType.NVarChar, MaxResourceNameLength)
                    {
                        Value = _resourceName
                    });
                    command.Parameters.Add(new SqlParameter("@LockOwner", SqlDbType.NVarChar, 32) { Value = "Session" });

                    await command.ExecuteNonQueryAsync(CancellationToken.None);

                    var result = returnValue.Value is int value ? value : Convert.ToInt32(returnValue.Value);
                    _logger.LogDebug("SQL applock released. Resource={Resource} Result={Result}", _resourceName, result);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to release SQL applock. Resource={Resource}", _resourceName);
            }
            finally
            {
                await _connection.DisposeAsync();
            }
        }
    }
}
