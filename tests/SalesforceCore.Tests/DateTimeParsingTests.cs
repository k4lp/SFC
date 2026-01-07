using System.Text.Json.Nodes;
using FluentAssertions;
using SalesforceCore.Attributes;
using SalesforceCore.Mapping;
using Xunit;

namespace SalesforceCore.Tests;

public class DateTimeParsingTests
{
    private class NullableDateModel
    {
        [SalesforceField("CreatedDate")]
        public DateTimeOffset? CreatedDate { get; set; }
    }

    private class NonNullableDateModel
    {
        [SalesforceField("CreatedDate")]
        public DateTimeOffset CreatedDate { get; set; }
    }

    [Fact]
    public void FromSalesforce_EmptyDateTimeOffsetString_Nullable_ShouldReturnNull()
    {
        var json = new JsonObject
        {
            ["CreatedDate"] = ""
        };

        var result = SalesforceMapper.FromSalesforce<NullableDateModel>(json);
        result.CreatedDate.Should().BeNull();
    }

    [Fact]
    public void FromSalesforce_EmptyDateTimeOffsetString_NonNullable_ShouldThrow()
    {
        var json = new JsonObject
        {
            ["CreatedDate"] = ""
        };

        var act = () => SalesforceMapper.FromSalesforce<NonNullableDateModel>(json);
        act.Should().Throw<InvalidOperationException>();
    }
}

