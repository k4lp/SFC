using FluentAssertions;
using Xunit;

namespace SalesforceCore.Tests.V2;

public class ApiVersionTests
{
    [Theory]
    [InlineData("v62.0", 62)]
    [InlineData("62.0", 62)]
    [InlineData("62", 62)]
    [InlineData(" V58.0 ", 58)]
    public void Parse_AcceptsCommonForms(string input, int major)
    {
        var v = SalesforceApiVersion.Parse(input);
        v.Major.Should().Be(major);
        v.ToString().Should().Be($"v{major}.0");
        v.DataPath.Should().Be($"/services/data/v{major}.0");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("v62.1")]
    [InlineData("latest")]
    [InlineData("v30.0")]
    [InlineData("v62.0/../")]
    [InlineData("1234")]
    public void TryParse_RejectsInvalid(string? input)
    {
        SalesforceApiVersion.TryParse(input, out _).Should().BeFalse();
        if (input != null) FluentActions.Invoking(() => SalesforceApiVersion.Parse(input)).Should().Throw<FormatException>();
    }

    [Fact]
    public void Comparison_And_Default()
    {
        (SalesforceApiVersion.Parse("v61.0") < SalesforceApiVersion.Parse("v62.0")).Should().BeTrue();
        (SalesforceApiVersion.Parse("v62.0") >= SalesforceApiVersion.Parse("62")).Should().BeTrue();
        SalesforceApiVersion.Default.ToString().Should().Be(SalesforceConstants.DefaultApiVersion);
        string s = SalesforceApiVersion.Parse("60");
        s.Should().Be("v60.0");
        FluentActions.Invoking(() => new SalesforceApiVersion(5)).Should().Throw<ArgumentOutOfRangeException>();
    }
}
