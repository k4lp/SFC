using System.IO.Compression;
using System.Net.Http.Headers;
using Microsoft.Extensions.Options;
using SalesforceCore.Models.Configuration;

namespace SalesforceCore.Http;

/// <summary>
/// Opt-in (<see cref="SalesforceOptions.EnableResponseCompression"/>) gzip support that works with any primary handler:
/// asks Salesforce for gzip and decompresses the response stream. A no-op when the option is off or when the primary
/// handler already decompresses (no Content-Encoding reaches this handler).
/// </summary>
public sealed class SalesforceCompressionHandler : DelegatingHandler
{
    private readonly IOptionsMonitor<SalesforceOptions> _options;

    /// <summary>Creates the handler.</summary>
    public SalesforceCompressionHandler(IOptionsMonitor<SalesforceOptions> options) => _options = options;

    /// <inheritdoc/>
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (!_options.CurrentValue.EnableResponseCompression)
        {
            return await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
        }

        if (request.Headers.AcceptEncoding.Count == 0)
        {
            request.Headers.AcceptEncoding.Add(new StringWithQualityHeaderValue("gzip"));
        }

        var response = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
        var encoding = response.Content?.Headers.ContentEncoding;
        if (response.Content == null || encoding == null || !encoding.Contains("gzip", StringComparer.OrdinalIgnoreCase))
        {
            return response;
        }

        var original = response.Content;
        var compressed = await original.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        var decompressed = new StreamContent(new SizeLimitedReadStream(new GZipStream(compressed, CompressionMode.Decompress), _options.CurrentValue.MaxDecompressedResponseBytes));
        foreach (var header in original.Headers)
        {
            if (header.Key.Equals("Content-Encoding", StringComparison.OrdinalIgnoreCase) ||
                header.Key.Equals("Content-Length", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            decompressed.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

        // The new content owns the decompressing stream; disposing the response disposes it and the inner stream.
        response.Content = decompressed;
        return response;
    }
}
