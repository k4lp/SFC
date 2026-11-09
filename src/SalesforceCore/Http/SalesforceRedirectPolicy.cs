using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Http;

namespace SalesforceCore.Http;

/// <summary>
/// unreleased: turns off automatic redirect following on the primary handler of Salesforce HTTP clients.
/// </summary>
/// <remarks>
/// .NET strips the <c>Authorization</c> header when it follows a redirect, but it still re-sends the request:
/// the query string (SOQL) on every method, and on 307/308 the request body (record data). A Salesforce host that
/// redirects an API call to another host would therefore hand customer data to that host before any delegating
/// handler can see the redirect. With following turned off, the 3xx reaches <see cref="SalesforceTransportSecurityHandler"/>,
/// which refuses it. Applies to <see cref="HttpClientHandler"/> and <see cref="SocketsHttpHandler"/>, including a
/// handler supplied through <c>ConfigurePrimaryHttpMessageHandler</c> earlier in the registration order; other
/// handler types are left alone.
/// </remarks>
public static class SalesforceRedirectPolicy
{
    /// <summary>Disables automatic redirects on the client's primary handler.</summary>
    /// <remarks>Implemented as an <see cref="IHttpMessageHandlerBuilderFilter"/> so it runs after every other
    /// configuration action, including <c>ConfigureHttpClientDefaults</c> and later <c>ConfigurePrimaryHttpMessageHandler</c> calls.</remarks>
    public static IHttpClientBuilder DisableAutoRedirect(this IHttpClientBuilder builder)
    {
        builder.Services.Configure<RedirectPolicyNames>(n => n.Names.Add(builder.Name));
        builder.Services.TryAddEnumerable(ServiceDescriptor.Singleton<IHttpMessageHandlerBuilderFilter, Filter>());
        return builder;
    }

    /// <summary>Client names SFC itself creates; covered even when the app never calls <c>AddHttpClient</c> for them
    /// (a plain <c>CreateClient(name)</c> still goes through the handler-builder filter).</summary>
    public static IReadOnlyList<string> SalesforceClientNames { get; } = new[]
    {
        SalesforceHttpClientNames.Api, SalesforceHttpClientNames.Jwt, SalesforceHttpClientNames.ClientCredentials,
        "SalesforceTokenRefresh", "SalesforceTokenRevoke",
    };

    /// <summary>Disables automatic redirects for the given client names (default: <see cref="SalesforceClientNames"/>).</summary>
    public static IServiceCollection AddSalesforceRedirectPolicy(this IServiceCollection services, params string[] names)
    {
        var list = names.Length == 0 ? SalesforceClientNames : names;
        services.Configure<RedirectPolicyNames>(n => { foreach (var x in list) n.Names.Add(x); });
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHttpMessageHandlerBuilderFilter, Filter>());
        return services;
    }

    internal sealed class RedirectPolicyNames
    {
        public HashSet<string> Names { get; } = new(StringComparer.Ordinal);
    }

    private sealed class Filter(Microsoft.Extensions.Options.IOptions<RedirectPolicyNames> names) : IHttpMessageHandlerBuilderFilter
    {
        public Action<HttpMessageHandlerBuilder> Configure(Action<HttpMessageHandlerBuilder> next) => b =>
        {
            next(b);
            if (b.Name != null && names.Value.Names.Contains(b.Name))
            {
                Apply(b.PrimaryHandler);
            }
        };
    }

    /// <summary>Disables automatic redirects on a known handler type; returns false for unknown types.</summary>
    public static bool Apply(HttpMessageHandler handler)
    {
        switch (handler)
        {
            case SocketsHttpHandler s: s.AllowAutoRedirect = false; return true;
            case HttpClientHandler h: h.AllowAutoRedirect = false; return true;
            default: return false;
        }
    }
}
