using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using SalesforceCore.E2E.Harness;
using SalesforceCore.Services.Data;
using Xunit;

namespace SalesforceCore.E2E;

/// <summary>
/// Round 5: redirects across two real HTTPS Kestrel listeners. "acme.my.salesforce.com" and "login.salesforce.com" resolve
/// to listener A; "evil.example.com" to listener B. A answers every API call with a redirect to B. The assertion is on
/// what B received, not just on the exception the caller sees.
/// </summary>
public class RedirectTwoListenerE2ETests
{
    private sealed record Seen(string Method, string Path, string? Authorization, string Body);

    private static X509Certificate2 SelfSigned()
    {
        using var rsa = RSA.Create(2048);
        var req = new CertificateRequest("CN=localhost", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        using var c = req.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));
        return X509CertificateLoader.LoadPkcs12(c.Export(X509ContentType.Pfx), null);
    }

    private static async Task<(WebApplication app, int port)> Listen(X509Certificate2 cert, RequestDelegate handler)
    {
        var b = WebApplication.CreateBuilder();
        b.WebHost.ConfigureKestrel(k => k.Listen(IPAddress.Loopback, 0, o => o.UseHttps(cert)));
        var app = b.Build();
        app.Run(handler);
        await app.StartAsync();
        var port = new Uri(app.Urls.First()).Port;
        return (app, port);
    }

    [Theory]
    [InlineData(302, "GET")]
    [InlineData(307, "GET")]
    [InlineData(307, "POST")]
    [InlineData(308, "PATCH")]
    public async Task ApiRedirectToForeignHost_NoTokenAndNoRecordDataReachIt(int status, string method)
    {
        using var cert = SelfSigned();
        var foreign = new ConcurrentQueue<Seen>();
        var (evil, evilPort) = await Listen(cert, async ctx =>
        {
            using var r = new StreamReader(ctx.Request.Body);
            foreign.Enqueue(new Seen(ctx.Request.Method, ctx.Request.Path, ctx.Request.Headers.Authorization.FirstOrDefault(), await r.ReadToEndAsync()));
            ctx.Response.ContentType = "application/json";
            await ctx.Response.WriteAsync("{\"totalSize\":0,\"done\":true,\"records\":[],\"id\":\"001000000000001AAA\",\"success\":true}");
        });
        var (sf, sfPort) = await Listen(cert, async ctx =>
        {
            if (ctx.Request.Path.StartsWithSegments("/services/oauth2/token"))
            {
                ctx.Response.ContentType = "application/json";
                await ctx.Response.WriteAsync("{\"access_token\":\"00Dsecret!TOKEN\",\"instance_url\":\"https://acme.my.salesforce.com\",\"token_type\":\"Bearer\"}");
                return;
            }
            ctx.Response.StatusCode = status;
            ctx.Response.Headers.Location = $"https://evil.example.com{ctx.Request.Path}{ctx.Request.QueryString}";
        });
        try
        {
            var handler = new SocketsHttpHandler
            {
                SslOptions = { RemoteCertificateValidationCallback = (_, presented, _, _) => presented != null && presented.GetCertHashString() == cert.GetCertHashString() },
                ConnectCallback = async (ctx, ct) =>
                {
                    var port = ctx.DnsEndPoint.Host == "evil.example.com" ? evilPort : sfPort;
                    var s = new Socket(SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
                    await s.ConnectAsync(new IPEndPoint(IPAddress.Loopback, port), ct);
                    return new NetworkStream(s, ownsSocket: true);
                },
            };
            await using var h = await E2EHost.StartAsync(services: sc =>
                sc.ConfigureHttpClientDefaults(b => b.ConfigurePrimaryHttpMessageHandler(() => handler)));
            var data = h.Get<IDataService>();

            Func<Task> act = method switch
            {
                "GET" => () => data.QueryAsync("SELECT Id FROM Account"),
                "POST" => () => data.CreateRecordAsync("Account", new Dictionary<string, object?> { ["Name"] = "Secret Customer PII" }),
                _ => () => data.UpdateRecordAsync("Account", "001000000000001AAA", new Dictionary<string, object?> { ["Name"] = "Secret Customer PII" }),
            };
            var ex = await act.Should().ThrowAsync<Exception>();

            foreign.Should().NotContain(s => s.Authorization != null, "the access token must never reach a foreign host");
            foreign.Should().BeEmpty("no request (record data, query text, SOQL) may be sent to a host the caller did not target");
            ex.Which.ToString().Should().NotContain("00Dsecret!TOKEN");
        }
        finally
        {
            await evil.DisposeAsync();
            await sf.DisposeAsync();
        }
    }

    private static async Task<(WebApplication sf, WebApplication evil, ConcurrentQueue<Seen> foreign, ConcurrentQueue<Seen> local, SocketsHttpHandler handler)> TwoListeners(
        X509Certificate2 cert, int status, bool sameHost)
    {
        var foreign = new ConcurrentQueue<Seen>();
        var local = new ConcurrentQueue<Seen>();
        var (evil, evilPort) = await Listen(cert, async ctx =>
        {
            using var r = new StreamReader(ctx.Request.Body);
            foreign.Enqueue(new Seen(ctx.Request.Method, ctx.Request.Path, ctx.Request.Headers.Authorization.FirstOrDefault(), await r.ReadToEndAsync()));
            ctx.Response.ContentType = "application/json";
            await ctx.Response.WriteAsync("{\"totalSize\":0,\"done\":true,\"records\":[]}");
        });
        var (sf, sfPort) = await Listen(cert, async ctx =>
        {
            using var r = new StreamReader(ctx.Request.Body);
            local.Enqueue(new Seen(ctx.Request.Method, ctx.Request.Path, ctx.Request.Headers.Authorization.FirstOrDefault(), await r.ReadToEndAsync()));
            if (ctx.Request.Path.StartsWithSegments("/services/oauth2/token"))
            {
                ctx.Response.ContentType = "application/json";
                await ctx.Response.WriteAsync("{\"access_token\":\"00Dsecret!TOKEN\",\"instance_url\":\"https://acme.my.salesforce.com\",\"token_type\":\"Bearer\"}");
                return;
            }
            if (ctx.Request.Path.StartsWithSegments("/moved"))
            {
                ctx.Response.ContentType = "application/json";
                await ctx.Response.WriteAsync("{\"totalSize\":0,\"done\":true,\"records\":[]}");
                return;
            }
            ctx.Response.StatusCode = status;
            ctx.Response.Headers.Location = sameHost ? "/moved" + ctx.Request.Path : $"https://evil.example.com{ctx.Request.Path}";
        });
        var handler = new SocketsHttpHandler
        {
            SslOptions = { RemoteCertificateValidationCallback = (_, presented, _, _) => presented != null && presented.GetCertHashString() == cert.GetCertHashString() },
            ConnectCallback = async (ctx, ct) =>
            {
                var port = ctx.DnsEndPoint.Host == "evil.example.com" ? evilPort : sfPort;
                var s = new Socket(SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
                await s.ConnectAsync(new IPEndPoint(IPAddress.Loopback, port), ct);
                return new NetworkStream(s, ownsSocket: true);
            },
        };
        return (sf, evil, foreign, local, handler);
    }

    [Theory]
    [InlineData(307)]
    [InlineData(302)]
    public async Task SameHostRedirect_IsAnHonestError_NotASilentSuccess(int status)
    {
        using var cert = SelfSigned();
        var (sf, evil, foreign, local, handler) = await TwoListeners(cert, status, sameHost: true);
        try
        {
            await using var h = await E2EHost.StartAsync(services: sc =>
                sc.ConfigureHttpClientDefaults(b => b.ConfigurePrimaryHttpMessageHandler(() => handler)));
            var act = () => h.Get<IDataService>().CreateRecordAsync("Account", new Dictionary<string, object?> { ["Name"] = "x" });

            await act.Should().ThrowAsync<Exception>("a redirect is not a created record");
            local.Should().NotContain(s => s.Path.StartsWith("/moved"), "the redirect is not followed");
            foreign.Should().BeEmpty();
        }
        finally { await evil.DisposeAsync(); await sf.DisposeAsync(); }
    }

    [Theory]
    [InlineData("SalesforceTokenRevoke")]
    [InlineData("SalesforceTokenRefresh")]
    [InlineData("SalesforceJwt")]
    [InlineData("SalesforceClientCredentials")]
    [InlineData("SalesforceCore.Api")]
    public async Task EverySalesforceClient_FromAddSalesforceCoreMvc_DoesNotFollowA307WithItsBody(string clientName)
    {
        using var cert = SelfSigned();
        var (sf, evil, foreign, _, handler) = await TwoListeners(cert, 307, sameHost: false);
        try
        {
            var config = new Microsoft.Extensions.Configuration.ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Salesforce:Domain"] = "https://login.salesforce.com", ["Salesforce:ClientId"] = "cid", ["Salesforce:ApiVersion"] = "v62.0",
            }).Build();
            var sc = new ServiceCollection();
            sc.AddLogging();
            sc.AddSingleton<Microsoft.Extensions.Configuration.IConfiguration>(config);
            SalesforceCore.AspNetCore.Extensions.ServiceCollectionExtensions.AddSalesforceCoreMvc(sc, config);
            // App-level customisation registered last: it must not re-enable redirects.
            sc.ConfigureHttpClientDefaults(b => b.ConfigurePrimaryHttpMessageHandler(() => handler));
            await using var sp = sc.BuildServiceProvider();
            using var client = sp.GetRequiredService<IHttpClientFactory>().CreateClient(clientName);

            using var body = new FormUrlEncodedContent(new Dictionary<string, string> { ["token"] = "5Aep-refresh-token" });
            HttpResponseMessage? res = null;
            try { res = await client.PostAsync("https://acme.my.salesforce.com/services/oauth2/revoke", body); }
            catch (Exception) { /* an exception is an honest failure too */ }

            foreign.Should().BeEmpty($"{clientName} must not replay the POST body to another host");
            if (res != null) ((int)res.StatusCode).Should().Be(307, "the redirect is surfaced, not followed");
            res?.Dispose();
        }
        finally { await evil.DisposeAsync(); await sf.DisposeAsync(); }
    }
}
