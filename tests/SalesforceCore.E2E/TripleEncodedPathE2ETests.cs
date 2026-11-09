using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using SalesforceCore.E2E.Harness;
using SalesforceCore.Services.Apex;
using Xunit;

namespace SalesforceCore.E2E;

/// <summary>
/// Unreleased review: SalesforcePathGuard decodes twice. A triple-encoded traversal ("%25252e%25252e") therefore passes
/// the guard. This sends one through the real client pipeline to a real HTTPS Kestrel listener and records what the
/// server receives, to show whether it can climb out of /services/apexrest/.
/// Finding: it is an inert literal. HttpClient sends the escapes unchanged, an HTTP server decodes the path once, and
/// the request is routed as the literal segment "%252e%252e..." under /services/apexrest/. Reaching a parent path
/// would need a server that percent-decodes the same path three times before resolving dot segments; that is
/// residual risk for a misbehaving proxy, not Salesforce behaviour this harness can prove.
/// </summary>
public class TripleEncodedPathE2ETests
{
    [Fact]
    public async Task TripleEncodedTraversal_OverRealHttps_IsAnInertLiteralSegmentUnderApexRest()
    {
        using var rsa = RSA.Create(2048);
        using var tmp = new CertificateRequest("CN=localhost", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1)
            .CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));
        using var cert = X509CertificateLoader.LoadPkcs12(tmp.Export(X509ContentType.Pfx), null);
        var seen = new ConcurrentQueue<(string Path, string Raw)>();
        var b = WebApplication.CreateBuilder();
        b.WebHost.ConfigureKestrel(k => k.Listen(IPAddress.Loopback, 0, o => o.UseHttps(cert)));
        var app = b.Build();
        app.Run(async ctx =>
        {
            ctx.Response.ContentType = "application/json";
            if (ctx.Request.Path.StartsWithSegments("/services/oauth2/token"))
            {
                await ctx.Response.WriteAsync("{\"access_token\":\"00Dx!T\",\"instance_url\":\"https://acme.my.salesforce.com\",\"token_type\":\"Bearer\"}");
                return;
            }
            seen.Enqueue((ctx.Request.Path.Value!, ctx.Features.Get<IHttpRequestFeature>()!.RawTarget));
            await ctx.Response.WriteAsync("{}");
        });
        await app.StartAsync();
        var port = new Uri(app.Urls.First()).Port;
        try
        {
            var handler = new SocketsHttpHandler
            {
                SslOptions = { RemoteCertificateValidationCallback = (_, c, _, _) => c != null && c.GetCertHashString() == cert.GetCertHashString() },
                ConnectCallback = async (_, ct) =>
                {
                    var sock = new Socket(SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
                    await sock.ConnectAsync(new IPEndPoint(IPAddress.Loopback, port), ct);
                    return new NetworkStream(sock, ownsSocket: true);
                },
            };
            await using var h = await E2EHost.StartAsync(services: sc =>
                sc.ConfigureHttpClientDefaults(x => x.ConfigurePrimaryHttpMessageHandler(() => handler)));

            await h.Get<IApexService>().GetNodeAsync("/Orders/%25252e%25252e%25252f%25252e%25252e%25252fdata%25252fv62.0%25252fsobjects");

            var (path, raw) = seen.Should().ContainSingle().Subject;
            raw.Should().Be("/services/apexrest/Orders/%25252e%25252e%25252f%25252e%25252e%25252fdata%25252fv62.0%25252fsobjects",
                "the request line on the wire carries the escapes unchanged; the client neither decodes nor normalises them");
            path.Should().Be("/services/apexrest/Orders/%252e%252e%252f%252e%252e%252fdata%252fv62.0%252fsobjects",
                "Kestrel decodes once; the routed path is still one literal segment under apexrest, not /services/data");
        }
        finally { await app.DisposeAsync(); }
    }

    [Theory]
    [InlineData("/Orders/%2e%2e%2f%2e%2e%2fdata")]
    [InlineData("/Orders/%252e%252e%252fdata")]
    public async Task SingleAndDoubleEncodedTraversal_AreRejectedBeforeAnyRequest(string path)
    {
        await using var h = await E2EHost.StartAsync();
        var before = h.Fake.Requests.Count(r => r.Contains("/services/apexrest/"));
        var act = () => h.Get<IApexService>().GetNodeAsync(path);
        await act.Should().ThrowAsync<ArgumentException>();
        h.Fake.Requests.Count(r => r.Contains("/services/apexrest/")).Should().Be(before);
    }
}
