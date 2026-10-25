namespace SalesforceCore.Http;

/// <summary>
/// Names of the HttpClients registered by <c>AddSalesforceCore</c>. Use them with
/// <c>services.AddHttpClient(name)</c> to customise the pipeline (handlers, primary handler, proxies, certificates).
/// </summary>
public static class SalesforceHttpClientNames
{
    /// <summary>The REST/data API client used by <see cref="Services.Core.ISalesforceClient"/>.</summary>
    public const string Api = "SalesforceCore.Api";

    /// <summary>OAuth token endpoint client used by the JWT bearer provider.</summary>
    public const string Jwt = "SalesforceJwt";

    /// <summary>OAuth token endpoint client used by the client-credentials provider.</summary>
    public const string ClientCredentials = "SalesforceClientCredentials";
}
