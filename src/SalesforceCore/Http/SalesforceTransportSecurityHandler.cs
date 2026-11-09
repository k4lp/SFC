using Microsoft.Extensions.Options;
using SalesforceCore.Models.Configuration;
using SalesforceCore.Models.Errors;

namespace SalesforceCore.Http;

/// <summary>
/// 2.7.0: transport guard on the Salesforce API pipeline.
/// <list type="bullet">
/// <item>A request that carries an <c>Authorization</c> header must use HTTPS (opt-out:
/// <see cref="SalesforceOptions.AllowInsecureHttp"/>, for local test proxies only).</item>
/// <item>If the primary handler followed a redirect to a different host, the response is discarded and a
/// <see cref="SalesforceConnectionException"/> is thrown, so data from an unexpected host is never parsed
/// (Salesforce REST APIs do not redirect API calls across hosts).</item>
/// </list>
/// </summary>
public sealed class SalesforceTransportSecurityHandler : DelegatingHandler
{
    private readonly IOptionsMonitor<SalesforceOptions> _options;

    /// <summary>Creates the handler.</summary>
    public SalesforceTransportSecurityHandler(IOptionsMonitor<SalesforceOptions> options) => _options = options;

    /// <inheritdoc/>
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var uri = request.RequestUri;
        var options = _options.CurrentValue;
        if (uri != null && request.Headers.Authorization != null && !options.AllowInsecureHttp &&
            !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            throw new SalesforceAuthException(
                $"Refusing to send a Salesforce access token over {uri.Scheme}. Use HTTPS (or set SalesforceOptions.AllowInsecureHttp for local test proxies only).");
        }

        var originalHost = uri?.IdnHost;
        var response = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
        // unreleased: redirects are not followed (SalesforceRedirectPolicy); a 3xx pointing at another host is refused here.
        if ((int)response.StatusCode is >= 300 and < 400 && response.Headers.Location is { } location && uri != null)
        {
            var target = location.IsAbsoluteUri ? location : new Uri(uri, location);
            if (!string.Equals(target.IdnHost, originalHost, StringComparison.OrdinalIgnoreCase))
            {
                response.Dispose();
                throw new SalesforceConnectionException(
                    $"Salesforce request to '{originalHost}' was redirected to a different host ('{target.IdnHost}'); the redirect was not followed.");
            }
        }
        var finalHost = response.RequestMessage?.RequestUri?.IdnHost;
        if (originalHost != null && finalHost != null && !string.Equals(originalHost, finalHost, StringComparison.OrdinalIgnoreCase))
        {
            response.Dispose();
            throw new SalesforceConnectionException(
                $"Salesforce request to '{originalHost}' was redirected to a different host ('{finalHost}'); the response was discarded.");
        }
        return response;
    }
}
