using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Text.Json.Nodes;
using System.Threading;
using FluentAssertions;
using Moq;
using SalesforceCore.Attributes;
using SalesforceCore.Query;
using SalesforceCore.Services.Data;
using Xunit;

namespace SalesforceCore.Tests;

public class SalesforceQueryProviderExecuteTests
{
    [SalesforceObject("Account")]
    private class Account
    {
        public string? Id { get; set; }
        public string? Name { get; set; }
        public bool IsActive { get; set; }
    }

    [Fact]
    public void Execute_NonGenericSequence_ReturnsEnumerable()
    {
        var mockDataService = new Mock<IDataService>(MockBehavior.Strict);

        var result = new SalesforceCore.Models.Data.QueryResult
        {
            Records = new List<JsonObject>
            {
                new()
                {
                    ["Id"] = "001000000000001",
                    ["Name"] = "Acme",
                    ["IsActive"] = true
                }
            }
        };

        mockDataService
            .Setup(s => s.QueryAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(result);

        var query = new SalesforceQueryable<Account>(mockDataService.Object)
            .Where(a => a.IsActive);

        var provider = (IQueryProvider)query.Provider;
        var executed = provider.Execute(query.Expression);

        executed.Should().BeAssignableTo<IEnumerable<Account>>();
        var list = ((IEnumerable<Account>)executed!).ToList();
        list.Should().ContainSingle();
        list[0].Name.Should().Be("Acme");
    }

    [Fact]
    public void Execute_NonGenericScalar_ReturnsCount()
    {
        var mockDataService = new Mock<IDataService>(MockBehavior.Strict);

        var result = new SalesforceCore.Models.Data.QueryResult
        {
            TotalSize = 5
        };

        mockDataService
            .Setup(s => s.QueryAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(result);

        var query = new SalesforceQueryable<Account>(mockDataService.Object);

        var countExpression = Expression.Call(
            typeof(Queryable),
            nameof(Queryable.Count),
            new[] { typeof(Account) },
            query.Expression);

        var provider = (IQueryProvider)query.Provider;
        var executed = provider.Execute(countExpression);

        executed.Should().Be(5);
    }
}
