using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace SalesforceCore.Http;

/// <summary>
/// Pipeline handler that logs every Salesforce call (method, path, status, elapsed) through <see cref="ILogger"/>.
/// Never logs query strings (they contain SOQL with user data), headers or bodies, so tokens and record data can't leak.
/// Success is logged at Debug, client errors at Information, server errors at Warning.
/// </summary>
public sealed class SalesforceLoggingHandler : DelegatingHandler
{
    private readonly ILogger<SalesforceLoggingHandler> _logger;

    /// <summary>Creates the handler.</summary>
    public SalesforceLoggingHandler(ILogger<SalesforceLoggingHandler> logger) => _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    /// <inheritdoc/>
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var path = request.RequestUri?.AbsolutePath ?? "?";
        var sw = Stopwatch.StartNew();
        try
        {
            var response = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
            var status = (int)response.StatusCode;
            var level = status >= 500 ? LogLevel.Warning : status >= 400 ? LogLevel.Information : LogLevel.Debug;
            if (_logger.IsEnabled(level))
            {
                _logger.Log(level, "Salesforce {Method} {Path} -> {Status} in {ElapsedMs} ms", request.Method.Method, path, status, sw.ElapsedMilliseconds);
            }
            return response;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Salesforce {Method} {Path} failed after {ElapsedMs} ms", request.Method.Method, path, sw.ElapsedMilliseconds);
            throw;
        }
    }
}
