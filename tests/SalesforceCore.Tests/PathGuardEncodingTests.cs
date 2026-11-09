using FluentAssertions;
using SalesforceCore.Security;
using Xunit;

namespace SalesforceCore.Tests;

/// <summary>Unreleased review: moving the "://" check to the raw path must not reopen traversal through (double) encoding.</summary>
public class PathGuardEncodingTests
{
    private const string P = "/services/data/v62.0/sobjects/Account/Ext__c/";

    [Theory]
    [InlineData(P + "https%3A%2F%2Fshop.example.com%2Fp%2F1")]
    [InlineData(P + "urn%3Asku%3A%2F%2Fa%2Fb")]
    [InlineData(P + "v1..2")]
    [InlineData(P + "SKU-1.2%2F%C3%A4")]
    public void LegitimateEncodedValues_AreAccepted(string path) => SalesforcePathGuard.TryValidate(path, out _).Should().BeTrue();

    [Theory]
    [InlineData(P + "..")]
    [InlineData(P + "%2e%2e")]
    [InlineData(P + "%252e%252e")]                 // double-encoded
    [InlineData(P + "a%2F..%2F..%2Fapexrest")]     // encoded slash + dot segments
    [InlineData(P + "a%252F..%252F..%252Fx")]      // double-encoded slash
    [InlineData(P + "%2E%2E%5Cx")]                 // encoded backslash
    [InlineData(P + "https://evil.example.com/x")] // raw scheme
    [InlineData("//evil.example.com/x")]
    [InlineData("%2F%2Fevil.example.com/x")]       // encoded protocol-relative
    [InlineData(P + "a%0d%0aX-Evil:1")]
    public void TraversalAndAbsoluteForms_AreRejected(string path) => SalesforcePathGuard.TryValidate(path, out _).Should().BeFalse();

    [Fact]
    public void Segment_ThenEnsure_RoundTripsAUrlShapedValue()
    {
        var path = P + SalesforcePathGuard.Segment("https://shop.example.com/p/1");
        SalesforcePathGuard.Ensure(path).Should().Be(path);
    }
}
