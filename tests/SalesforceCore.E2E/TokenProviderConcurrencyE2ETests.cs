using System.Collections.Concurrent;
using System.Security.Claims;
using System.Text.Json.Nodes;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SalesforceCore.AspNetCore.Extensions;
using SalesforceCore.E2E.Harness;
using SalesforceCore.Services.Data;
using Xunit;

namespace SalesforceCore.E2E;

/// <summary>
/// Round 5: AspNetCoreTokenProvider inside a real ASP.NET Core host with cookie authentication. Each test user has its
/// own access/refresh token pair; the fake Salesforce only accepts the latest access token per user and (optionally)
/// rotates refresh tokens, so a second refresh with an already-used refresh token fails like a real org with rotation.
/// </summary>
public class TokenProviderConcurrencyE2ETests
{
    private sealed class Org
    {
        public readonly ConcurrentDictionary<string, string> ValidAccess = new();   // token -> user
        public readonly ConcurrentDictionary<string, string> ValidRefresh = new();  // refresh token -> user
        public readonly ConcurrentQueue<string> RefreshCalls = new();               // refresh tokens presented
        public bool Rotate;
        /// <summary>Raw JSON for issued_at; null omits the property.</summary>
        public string? IssuedAtJson = "\"" + "1278448384422" + "\"";
        public int Counter;
        /// <summary>Holds the first N rejected (401) API calls until N have arrived, so all of them carry the stale token.</summary>
        public int HoldRejected;
        private int _rejected;
        private readonly TaskCompletionSource _allRejected = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task OnRejectedAsync()
        {
            if (HoldRejected <= 0) return;
            if (Interlocked.Increment(ref _rejected) >= HoldRejected) _allRejected.TrySetResult();
            await _allRejected.Task.WaitAsync(TimeSpan.FromSeconds(10));
        }
    }

    private static async Task<(WebApplication app, FakeSalesforce fake, Org org)> StartAsync(bool rotate, int holdRejected = 0, Action<Org>? setup = null)
    {
        var fake = await FakeSalesforce.StartAsync();
        var org = new Org { Rotate = rotate, HoldRejected = holdRejected };
        setup?.Invoke(org);
        fake.TokenResponder = async (ctx, form) =>
        {
            var rt = form["refresh_token"].ToString();
            org.RefreshCalls.Enqueue(rt);
            if (form["grant_type"] != "refresh_token" || !org.ValidRefresh.TryGetValue(rt, out var user))
            {
                ctx.Response.StatusCode = 400;
                ctx.Response.ContentType = "application/json";
                await ctx.Response.WriteAsync("{\"error\":\"invalid_grant\",\"error_description\":\"expired access/refresh token\"}");
                return;
            }
            var n = Interlocked.Increment(ref org.Counter);
            var access = $"00Dfresh!{user}-{n}";
            org.ValidAccess[access] = user;
            var body = new JsonObject
            {
                ["access_token"] = access,
                ["instance_url"] = FakeSalesforce.InstanceUrl,
                ["token_type"] = "Bearer",
            };
            if (org.IssuedAtJson != null) body["issued_at"] = JsonNode.Parse(org.IssuedAtJson);
            if (org.Rotate)
            {
                org.ValidRefresh.TryRemove(rt, out _);
                var next = $"rt-{user}-{n}";
                org.ValidRefresh[next] = user;
                body["refresh_token"] = next;
            }
            ctx.Response.ContentType = "application/json";
            await ctx.Response.WriteAsync(body.ToJsonString());
        };
        fake.Intercept = async ctx =>
        {
            if (!ctx.Request.Path.Value!.TrimEnd('/').EndsWith("/query")) return false;
            var token = ctx.Request.Headers.Authorization.ToString().Replace("Bearer ", "");
            if (!org.ValidAccess.TryGetValue(token, out var user))
            {
                await org.OnRejectedAsync();
                ctx.Response.StatusCode = 401;
                ctx.Response.ContentType = "application/json";
                await ctx.Response.WriteAsync("[{\"message\":\"Session expired or invalid\",\"errorCode\":\"INVALID_SESSION_ID\"}]");
                return true;
            }
            ctx.Response.ContentType = "application/json";
            await ctx.Response.WriteAsync(new JsonObject
            {
                ["totalSize"] = 1, ["done"] = true,
                ["records"] = new JsonArray(new JsonObject { ["attributes"] = new JsonObject { ["type"] = "Account" }, ["Id"] = "001000000000001AAA", ["Name"] = user }),
            }.ToJsonString());
            return true;
        };

        var b = WebApplication.CreateBuilder();
        b.WebHost.UseTestServer();
        b.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Salesforce:Domain"] = "https://login.salesforce.com",
            ["Salesforce:ClientId"] = "cid",
            ["Salesforce:ClientSecret"] = "secret",
            ["Salesforce:ApiVersion"] = "v62.0",
            ["Salesforce:MaxRetries"] = "1",
            ["Salesforce:RetryBaseDelay"] = "00:00:00.001",
        });
        b.Services.AddSalesforceCoreMvc(b.Configuration);
        b.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme).AddCookie();
        b.Services.ConfigureHttpClientDefaults(h => h.ConfigurePrimaryHttpMessageHandler(() => fake.Handler));
        var app = b.Build();
        app.UseAuthentication();
        app.MapGet("/login/{user}", async (HttpContext ctx, string user) =>
        {
            var id = new ClaimsIdentity(new[] { new Claim(ClaimTypes.NameIdentifier, "005" + user), new Claim(ClaimTypes.Name, user) }, CookieAuthenticationDefaults.AuthenticationScheme);
            var props = new AuthenticationProperties();
            props.StoreTokens(new[]
            {
                new AuthenticationToken { Name = "access_token", Value = $"00Dstale!{user}" },
                new AuthenticationToken { Name = "refresh_token", Value = $"rt-{user}-0" },
            });
            props.Items["instance_url"] = FakeSalesforce.InstanceUrl;
            org.ValidRefresh[$"rt-{user}-0"] = user;
            await ctx.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(id), props);
        });
        app.MapGet("/whoami", async (IDataService data) =>
        {
            try
            {
                var r = await data.QueryAsync("SELECT Id, Name FROM Account");
                return Results.Text(r.Records[0]["Name"]?.ToString());
            }
            catch (SalesforceCore.Models.Errors.SalesforceAuthException ex)
            {
                return Results.Text(ex.Message, statusCode: 401);
            }
        });
        await app.StartAsync();
        return (app, fake, org);
    }

    private static async Task<string> LoginAsync(HttpClient c, string user)
    {
        var res = await c.GetAsync($"/login/{user}");
        res.EnsureSuccessStatusCode();
        return string.Join("; ", res.Headers.GetValues("Set-Cookie").Select(v => v.Split(';')[0]));
    }

    private static async Task<(int status, string body)> CallAsync(HttpClient c, string cookie)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, "/whoami");
        req.Headers.Add("Cookie", cookie);
        using var res = await c.SendAsync(req);
        return ((int)res.StatusCode, await res.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task TwoUsersExpiredAtOnce_EachRefreshesWithOwnRefreshToken_AndSeesOwnData()
    {
        for (var round = 0; round < 5; round++)
        {
            var (app, fake, org) = await StartAsync(rotate: false, holdRejected: 2);
            await using var _ = app; await using var __ = fake;
            var c = app.GetTestClient();
            var alice = await LoginAsync(c, "alice");
            var bob = await LoginAsync(c, "bob");

            var results = await Task.WhenAll(CallAsync(c, alice), CallAsync(c, bob));

            results[0].Should().Be((200, "alice"), $"refresh calls: {string.Join(",", org.RefreshCalls)}; token requests {fake.TokenRequests}");
            results[1].Should().Be((200, "bob"));
            org.RefreshCalls.Should().BeEquivalentTo(new[] { "rt-alice-0", "rt-bob-0" });
        }
    }

    [Fact]
    public async Task SameUserTwoParallelRequests_WithRefreshTokenRotation_BothSucceed()
    {
        for (var round = 0; round < 5; round++)
        {
            var (app, fake, org) = await StartAsync(rotate: true, holdRejected: 2);
            await using var _ = app; await using var __ = fake;
            var c = app.GetTestClient();
            var alice = await LoginAsync(c, "alice");

            var results = await Task.WhenAll(CallAsync(c, alice), CallAsync(c, alice));

            results.Should().AllSatisfy(r => r.Should().Be((200, "alice")),
                "the second request waited on the per-session refresh lock while the first refreshed; it must reuse that result, not replay the rotated refresh token");
            org.RefreshCalls.Should().HaveCount(1, "one expiry needs one refresh");
        }
    }

    [Fact]
    public async Task RevokedRefreshToken_FailsCleanly_WithoutLeakingTokens()
    {
        var (app, fake, org) = await StartAsync(rotate: false);
        await using var _ = app; await using var __ = fake;
        var c = app.GetTestClient();
        var alice = await LoginAsync(c, "alice");
        org.ValidRefresh.Clear(); // session revoked in Salesforce

        var (status, body) = await CallAsync(c, alice).WaitAsync(TimeSpan.FromSeconds(30));

        status.Should().BeGreaterThanOrEqualTo(400);
        body.Should().NotContain("rt-alice").And.NotContain("00Dstale");
        org.RefreshCalls.Should().HaveCount(1, "an invalid_grant must not be retried in a loop");
    }

    /// <summary>
    /// issued_at forms: the documented string ("1278448384422", Salesforce Help, "OAuth 2.0 Hybrid App Refresh Token
    /// Flow" / "OAuth 2.0 Web Server Flow" JSON samples), a JSON number, and absent.
    /// </summary>
    [Theory]
    [InlineData("\"1278448384422\"")]
    [InlineData("1278448384422")]
    [InlineData(null)]
    public async Task Refresh_AcceptsEachIssuedAtForm(string? issuedAt)
    {
        var (app, fake, org) = await StartAsync(rotate: false, setup: o => o.IssuedAtJson = issuedAt);
        await using var _ = app; await using var __ = fake;
        var c = app.GetTestClient();
        var alice = await LoginAsync(c, "alice");

        (await CallAsync(c, alice)).Should().Be((200, "alice"));
        org.RefreshCalls.Should().ContainSingle();
    }
}
