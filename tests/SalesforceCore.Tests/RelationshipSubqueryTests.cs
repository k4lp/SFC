using System.Text.Json.Nodes;
using FluentAssertions;
using Moq;
using SalesforceCore.Attributes;
using SalesforceCore.Mapping;
using SalesforceCore.Query;
using SalesforceCore.Services.Data;
using Xunit;

namespace SalesforceCore.Tests;

public class RelationshipSubqueryTests
{
    [SalesforceObject("Account")]
    private class Account
    {
        public string? Id { get; set; }
        public string? Name { get; set; }

        [SalesforceChildRelationship("Contact", "Contacts")]
        public List<Contact>? Contacts { get; set; }
    }

    [SalesforceObject("Contact")]
    private class Contact
    {
        public string? Id { get; set; }

        [SalesforceField("FirstName")]
        public string? FirstName { get; set; }

        [SalesforceField("LastName")]
        public string? LastName { get; set; }
    }

    [Fact]
    public void GetQueryableFields_ShouldExcludeChildRelationshipProperty()
    {
        var fields = SalesforceMapper.GetQueryableFields<Account>().ToList();

        fields.Should().Contain("Id");
        fields.Should().Contain("Name");
        fields.Should().NotContain("Contacts");
    }

    [Fact]
    public void ToSalesforceDictionary_ShouldExcludeChildRelationshipProperty()
    {
        var account = new Account
        {
            Id = "001xxx",
            Name = "Acme",
            Contacts = new List<Contact> { new() { Id = "003xxx", FirstName = "John", LastName = "Doe" } }
        };

        var dict = SalesforceMapper.ToSalesforceDictionary(account);

        dict.Should().ContainKey("Id");
        dict.Should().ContainKey("Name");
        dict.Should().NotContainKey("Contacts");
    }

    [Fact]
    public void FromSalesforce_ShouldMapChildRelationshipSubqueryRecords()
    {
        var json = new JsonObject
        {
            ["Id"] = "001xxx",
            ["Name"] = "Acme",
            ["Contacts"] = new JsonObject
            {
                ["totalSize"] = 1,
                ["done"] = true,
                ["records"] = new JsonArray
                {
                    new JsonObject
                    {
                        ["Id"] = "003xxx",
                        ["FirstName"] = "John",
                        ["LastName"] = "Doe"
                    }
                }
            }
        };

        var result = SalesforceMapper.FromSalesforce<Account>(json);

        result.Contacts.Should().NotBeNull();
        result.Contacts.Should().HaveCount(1);
        if (result.Contacts != null)
        {
            result.Contacts[0].FirstName.Should().Be("John");
            result.Contacts[0].LastName.Should().Be("Doe");
        }
    }

    [Fact]
    public void Include_WithoutFields_ShouldInferFieldsFromChildType()
    {
        var mockDataService = new Mock<IDataService>(MockBehavior.Loose);
        var query = new SalesforceQueryable<Account>(mockDataService.Object);

        var soql = query.Include("Contacts").ToSoql();

        soql.Should().Contain("FROM Account");
        soql.Should().Contain("FROM Contacts");
        soql.Should().Contain("FirstName");
        soql.Should().Contain("LastName");

        // Ensure the relationship isn't selected as a normal field on the parent SELECT list.
        soql.Should().NotContain(", Contacts,");
        soql.Should().NotContain("SELECT Contacts");
    }
}

