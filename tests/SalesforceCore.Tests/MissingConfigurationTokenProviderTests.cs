using System;
using System.Collections.Generic;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using SalesforceCore.Security;
using Xunit;

namespace SalesforceCore.Tests;

public class MissingConfigurationTokenProviderTests
{
    [Fact]
    public void GetAccessTokenAsync_WithSalesforceClientId_ShouldMentionPkce()
    {
        var config = BuildConfig(new Dictionary<string, string?>
        {
            ["Salesforce:ClientId"] = "client-id"
        });

        var provider = new MissingConfigurationTokenProvider(config, NullLogger<MissingConfigurationTokenProvider>.Instance);

        Action act = () => provider.GetAccessTokenAsync();

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*AddSalesforceAuthentication*");
    }

    [Fact]
    public void GetAccessTokenAsync_WithJwtSectionMissingUsername_ShouldReportMissingKey()
    {
        var config = BuildConfig(new Dictionary<string, string?>
        {
            ["SalesforceJwt:PrivateKey"] = "key"
        });

        var provider = new MissingConfigurationTokenProvider(config, NullLogger<MissingConfigurationTokenProvider>.Instance);

        Action act = () => provider.GetAccessTokenAsync();

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*SalesforceJwt:Username*");
    }

    [Fact]
    public void GetAccessTokenAsync_WithClientCredentialsMissingSecret_ShouldReportMissingKey()
    {
        var config = BuildConfig(new Dictionary<string, string?>
        {
            ["SalesforceClientCredentials:ClientId"] = "client-id"
        });

        var provider = new MissingConfigurationTokenProvider(config, NullLogger<MissingConfigurationTokenProvider>.Instance);

        Action act = () => provider.GetAccessTokenAsync();

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*SalesforceClientCredentials:ClientSecret*");
    }

    private static IConfiguration BuildConfig(Dictionary<string, string?> values)
    {
        return new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();
    }
}
