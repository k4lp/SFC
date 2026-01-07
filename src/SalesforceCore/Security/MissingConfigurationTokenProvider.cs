using System.Collections.Generic;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SalesforceCore.Models.Configuration;
using SalesforceCore.Services.Core;

namespace SalesforceCore.Security;

/// <summary>
/// A placeholder token provider that throws an informative exception when used.
/// This prevents dependency injection failures during startup when no token provider is configured,
/// while providing clear guidance to the developer at runtime.
/// </summary>
public class MissingConfigurationTokenProvider : ITokenProvider
{
    private readonly IConfiguration? _configuration;
    private readonly ILogger<MissingConfigurationTokenProvider> _logger;
    private readonly Lazy<string> _errorMessage;

    public MissingConfigurationTokenProvider(
        IConfiguration? configuration = null,
        ILogger<MissingConfigurationTokenProvider>? logger = null)
    {
        _configuration = configuration;
        _logger = logger ?? NullLogger<MissingConfigurationTokenProvider>.Instance;
        _errorMessage = new Lazy<string>(BuildErrorMessage);
    }

    public Task<string?> GetAccessTokenAsync(CancellationToken cancellationToken = default)
    {
        throw CreateException();
    }

    public Task<string?> GetInstanceUrlAsync(CancellationToken cancellationToken = default)
    {
        throw CreateException();
    }

    public Task<string?> RefreshTokenAsync(CancellationToken cancellationToken = default)
    {
        throw CreateException();
    }

    public Task RevokeTokenAsync(CancellationToken cancellationToken = default)
    {
        throw CreateException();
    }

    public Task<bool> IsAuthenticatedAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(false);
    }

    private InvalidOperationException CreateException()
    {
        var message = _errorMessage.Value;
        _logger.LogError("Salesforce authentication is not configured. {Message}", message);
        return new InvalidOperationException(message);
    }

    private string BuildErrorMessage()
    {
        var sb = new StringBuilder();
        sb.AppendLine("No authentication method has been configured for SalesforceCore.");
        sb.AppendLine("This token provider is a placeholder and will always throw.");
        sb.AppendLine();

        var status = GetConfigurationStatus();
        if (!status.HasConfiguration)
        {
            sb.AppendLine("Configuration is not available to analyze. Ensure IConfiguration is registered.");
        }
        else
        {
            sb.AppendLine("Detected configuration:");
            sb.AppendLine($"- {SalesforceOptions.SectionName}: {(status.HasSalesforceSection ? "present" : "missing")}");
            if (status.HasSalesforceSection)
            {
                sb.AppendLine($"  - Salesforce:ClientId: {(status.HasSalesforceClientId ? "set" : "missing")}");
                sb.AppendLine($"  - Salesforce:ClientSecret: {(status.HasSalesforceClientSecret ? "set" : "missing")}");
            }

            sb.AppendLine($"- {JwtTokenProviderOptions.SectionName}: {(status.HasJwtSection ? "present" : "missing")}");
            if (status.HasJwtSection)
            {
                sb.AppendLine($"  - SalesforceJwt:Username: {(status.HasJwtUsername ? "set" : "missing")}");
                sb.AppendLine($"  - SalesforceJwt:PrivateKey: {(status.HasJwtPrivateKey ? "set" : "missing")}");
                sb.AppendLine($"  - SalesforceJwt:PrivateKeyPath: {(status.HasJwtPrivateKeyPath ? "set" : "missing")}");
            }

            sb.AppendLine($"- {ClientCredentialsOptions.SectionName}: {(status.HasClientCredentialsSection ? "present" : "missing")}");
            if (status.HasClientCredentialsSection)
            {
                sb.AppendLine($"  - SalesforceClientCredentials:ClientId: {(status.HasClientCredentialsId ? "set" : "missing")}");
                sb.AppendLine($"  - SalesforceClientCredentials:ClientSecret: {(status.HasClientCredentialsSecret ? "set" : "missing")}");
            }

            var issues = BuildIssues(status);
            if (issues.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("Configuration issues detected:");
                foreach (var issue in issues)
                {
                    sb.AppendLine($"- {issue}");
                }
            }
        }

        sb.AppendLine();
        sb.AppendLine("How to fix:");
        sb.AppendLine("1. Web app (PKCE): call services.AddSalesforceAuthentication(configuration) or services.AddSalesforceCoreMvc(...).");
        sb.AppendLine("   Required: Salesforce:ClientId (ClientSecret optional for PKCE).");
        sb.AppendLine("2. JWT bearer (server-to-server): configure SalesforceJwt:Username and SalesforceJwt:PrivateKey/PrivateKeyPath,");
        sb.AppendLine("   and ensure Salesforce:ClientId is set.");
        sb.AppendLine("3. Client Credentials: configure SalesforceClientCredentials:ClientId and SalesforceClientCredentials:ClientSecret.");
        sb.AppendLine("4. Custom: call services.AddSalesforceTokenProvider<MyCustomProvider>().");

        return sb.ToString().Trim();
    }

    private List<string> BuildIssues(TokenProviderConfigurationStatus status)
    {
        var issues = new List<string>();
        if (!status.HasSalesforceSection)
        {
            issues.Add($"Missing configuration section '{SalesforceOptions.SectionName}'.");
        }
        else if (!status.HasSalesforceClientId)
        {
            issues.Add("Missing required setting: Salesforce:ClientId.");
        }

        if (status.HasJwtSection)
        {
            if (!status.HasJwtUsername)
            {
                issues.Add("Missing required setting: SalesforceJwt:Username.");
            }

            if (!status.HasJwtPrivateKey && !status.HasJwtPrivateKeyPath)
            {
                issues.Add("Missing required setting: SalesforceJwt:PrivateKey or SalesforceJwt:PrivateKeyPath.");
            }

            if (!status.HasSalesforceClientId)
            {
                issues.Add("Missing required setting for JWT flow: Salesforce:ClientId.");
            }
        }

        if (status.HasClientCredentialsSection)
        {
            if (!status.HasClientCredentialsId)
            {
                issues.Add("Missing required setting: SalesforceClientCredentials:ClientId.");
            }

            if (!status.HasClientCredentialsSecret)
            {
                issues.Add("Missing required setting: SalesforceClientCredentials:ClientSecret.");
            }
        }

        return issues;
    }

    private TokenProviderConfigurationStatus GetConfigurationStatus()
    {
        if (_configuration == null)
        {
            return new TokenProviderConfigurationStatus();
        }

        var status = new TokenProviderConfigurationStatus
        {
            HasConfiguration = true
        };

        var salesforceSection = _configuration.GetSection(SalesforceOptions.SectionName);
        status.HasSalesforceSection = salesforceSection.Exists();
        status.HasSalesforceClientId = status.HasSalesforceSection && HasValue(salesforceSection, "ClientId");
        status.HasSalesforceClientSecret = status.HasSalesforceSection && HasValue(salesforceSection, "ClientSecret");

        var jwtSection = _configuration.GetSection(JwtTokenProviderOptions.SectionName);
        status.HasJwtSection = jwtSection.Exists();
        status.HasJwtUsername = status.HasJwtSection && HasValue(jwtSection, "Username");
        status.HasJwtPrivateKey = status.HasJwtSection && HasValue(jwtSection, "PrivateKey");
        status.HasJwtPrivateKeyPath = status.HasJwtSection && HasValue(jwtSection, "PrivateKeyPath");

        var clientCredentialsSection = _configuration.GetSection(ClientCredentialsOptions.SectionName);
        status.HasClientCredentialsSection = clientCredentialsSection.Exists();
        status.HasClientCredentialsId = status.HasClientCredentialsSection && HasValue(clientCredentialsSection, "ClientId");
        status.HasClientCredentialsSecret = status.HasClientCredentialsSection && HasValue(clientCredentialsSection, "ClientSecret");

        return status;
    }

    private static bool HasValue(IConfiguration section, string key)
        => !string.IsNullOrWhiteSpace(section[key]);

    private sealed class TokenProviderConfigurationStatus
    {
        public bool HasConfiguration { get; set; }
        public bool HasSalesforceSection { get; set; }
        public bool HasSalesforceClientId { get; set; }
        public bool HasSalesforceClientSecret { get; set; }
        public bool HasJwtSection { get; set; }
        public bool HasJwtUsername { get; set; }
        public bool HasJwtPrivateKey { get; set; }
        public bool HasJwtPrivateKeyPath { get; set; }
        public bool HasClientCredentialsSection { get; set; }
        public bool HasClientCredentialsId { get; set; }
        public bool HasClientCredentialsSecret { get; set; }
    }
}
