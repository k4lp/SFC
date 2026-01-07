using System.Linq;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using SalesforceCore.Attributes;
using SalesforceCore.Query;
using SalesforceCore.Services.Data;
using Xunit;

namespace SalesforceCore.Tests;

public class SalesforceQueryableProjectionTests
{
    [SalesforceObject("Account")]
    private class Account
    {
        public string? Id { get; set; }
        public string? Name { get; set; }

        [SalesforceField("Industry")]
        public string? Industry { get; set; }
    }

    private class AccountDto
    {
        public string? Name { get; set; }
    }

    [Fact]
    public void Select_ScalarProjection_ShouldReturnValues()
    {
        var mockDataService = new Mock<IDataService>(MockBehavior.Strict);
        string? capturedSoql = null;

        var result = new SalesforceCore.Models.Data.QueryResult
        {
            Records = new List<JsonObject>
            {
                new()
                {
                    ["Id"] = "001000000000001",
                    ["Name"] = "Acme"
                },
                new()
                {
                    ["Id"] = "001000000000002",
                    ["Name"] = "Beta"
                }
            }
        };

        mockDataService
            .Setup(s => s.QueryAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback<string, CancellationToken>((soql, _) => capturedSoql = soql)
            .ReturnsAsync(result);

        var query = new SalesforceQueryable<Account>(mockDataService.Object);

        var names = query.Select(a => a.Name).ToList();

        names.Should().Equal("Acme", "Beta");
        capturedSoql.Should().NotBeNull();
        capturedSoql.Should().Contain("SELECT Id, Name FROM Account");
    }

    [Fact]
    public async Task Select_DtoProjection_ToListAsync_ShouldReturnValues()
    {
        var mockDataService = new Mock<IDataService>(MockBehavior.Strict);

        var result = new SalesforceCore.Models.Data.QueryResult
        {
            Records = new List<JsonObject>
            {
                new()
                {
                    ["Id"] = "001000000000003",
                    ["Name"] = "Gamma"
                }
            }
        };

        mockDataService
            .Setup(s => s.QueryAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(result);

        var query = new SalesforceQueryable<Account>(mockDataService.Object);

        var dtos = await query.Select(a => new AccountDto { Name = a.Name })
            .ToListAsync();

        dtos.Should().HaveCount(1);
        dtos[0].Name.Should().Be("Gamma");
    }

    [Fact]
    public void Select_AnonymousProjection_ShouldReturnValues()
    {
        var mockDataService = new Mock<IDataService>(MockBehavior.Strict);

        var result = new SalesforceCore.Models.Data.QueryResult
        {
            Records = new List<JsonObject>
            {
                new()
                {
                    ["Id"] = "001000000000004",
                    ["Name"] = "Delta"
                }
            }
        };

        mockDataService
            .Setup(s => s.QueryAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(result);

        var query = new SalesforceQueryable<Account>(mockDataService.Object);

        var projection = query.Select(a => new { a.Name }).ToList();

        projection.Should().HaveCount(1);
        projection[0].Name.Should().Be("Delta");
    }

    [Fact]
    public void Where_After_Select_ShouldThrow()
    {
        var mockDataService = new Mock<IDataService>(MockBehavior.Strict);
        var query = new SalesforceQueryable<Account>(mockDataService.Object)
            .Select(a => a.Name)
            .Where(name => name == "Acme");

        var act = () => query.ToList();

        act.Should().Throw<NotSupportedException>()
            .WithMessage("*projection*");
    }
}
