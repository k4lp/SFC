using Xunit;
using FluentAssertions;
using System.Text.Json.Nodes;
using SalesforceCore.Mapping;
using System.Text.Json;

namespace SalesforceCore.Tests;

public class RobustnessTests
{
    [Fact]
    public void Mapper_ShouldHandleExtremeNumericValues()
    {
        // Salesforce often returns currency/percent as numbers. 
        // We simulate the JSON payload as a string to ensure System.Text.Json parsing preserves the precision
        // before our mapper sees it.
        var jsonString = @"{
            ""Id"": ""001xxx"",
            ""Revenue"": 123456789012345678.90,
            ""Tiny"": 0.0000000000000000001,
            ""StringNum"": ""123.45""
        }";

        var json = JsonNode.Parse(jsonString);

        var model = SalesforceMapper.FromSalesforce<NumericModel>(json!);

        // Now we expect exact precision because JsonNode should support it if we convert correctly
        model.Revenue.Should().Be(123456789012345678.90m); 
        model.Tiny.Should().Be(0.0000000000000000001m);
        model.StringNum.Should().Be(123.45m);
    }

    [Fact]
    public void Mapper_ShouldHandleDeeplyNestedRelationships()
    {
        var json = JsonNode.Parse(@"
        {
            ""Id"": ""001xxx"",
            ""Name"": ""Parent Account"",
            ""Owner"": {
                ""Name"": ""John Doe"",
                ""Profile"": {
                    ""Name"": ""System Administrator"",
                    ""Permissions"": {
                        ""ViewAllData"": true
                    }
                }
            }
        }");

        var model = SalesforceMapper.FromSalesforce<DeepModel>(json!);

        model.Id.Should().Be("001xxx");
        model.OwnerName.Should().Be("John Doe");
        model.OwnerProfileName.Should().Be("System Administrator");
        // model.CanViewAllData.Should().BeTrue(); // If we map this deeply
    }

    [Fact]
    public async Task Mapper_ShouldHandleConcurrentAccess()
    {
        // Simulate high concurrency to ensure metadata cache is thread-safe
        var tasks = new List<Task>();
        for (int i = 0; i < 100; i++)
        {
            tasks.Add(Task.Run(() => 
            {
                var name = SalesforceMapper.GetObjectName<NumericModel>();
                name.Should().Be("NumericTest");
            }));
        }

        await Task.WhenAll(tasks);
    }

    [SalesforceCore.Attributes.SalesforceObject("NumericTest")]
    public class NumericModel
    {
        public string? Id { get; set; }
        
        [SalesforceCore.Attributes.SalesforceField("Revenue")]
        public decimal? Revenue { get; set; }

        [SalesforceCore.Attributes.SalesforceField("Tiny")]
        public decimal? Tiny { get; set; }

        [SalesforceCore.Attributes.SalesforceField("StringNum")]
        public decimal? StringNum { get; set; }
    }

    public class DeepModel
    {
        public string? Id { get; set; }
        
        [SalesforceCore.Attributes.SalesforceField("Owner.Name")]
        public string? OwnerName { get; set; }

        [SalesforceCore.Attributes.SalesforceField("Owner.Profile.Name")]
        public string? OwnerProfileName { get; set; }
    }
}
