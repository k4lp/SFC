using System;
using System.Collections.Generic;
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

/// <summary>
/// Tests for the extended LINQ operators (DistinctAsync, AllAsync, LastAsync, etc.)
/// that provide workarounds for SOQL limitations.
/// </summary>
public class ExtendedLinqOperatorTests
{
    [SalesforceObject("Account")]
    private class Account
    {
        public string? Id { get; set; }
        public string? Name { get; set; }
        
        [SalesforceField("Industry")]
        public string? Industry { get; set; }
        
        public bool IsActive { get; set; }
        public DateTime CreatedDate { get; set; }
    }

    #region AllAsync Tests

    [Fact]
    public async Task AllAsync_WhenAllMatch_ShouldReturnTrue()
    {
        var mockDataService = new Mock<IDataService>(MockBehavior.Strict);
        string? capturedSoql = null;

        // Return 0 records that DON'T match (meaning all DO match)
        var result = new SalesforceCore.Models.Data.QueryResult
        {
            TotalSize = 0,
            Records = new List<JsonObject>()
        };

        mockDataService
            .Setup(s => s.QueryAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback<string, CancellationToken>((soql, _) => capturedSoql = soql)
            .ReturnsAsync(result);

        var query = new SalesforceQueryable<Account>(mockDataService.Object);

        var allActive = await query.AllAsync(a => a.IsActive);

        allActive.Should().BeTrue();
        // Should query for NOT IsActive (negated predicate)
        capturedSoql.Should().NotBeNull();
        capturedSoql.Should().Contain("COUNT()");
    }

    [Fact]
    public async Task AllAsync_WhenSomeDontMatch_ShouldReturnFalse()
    {
        var mockDataService = new Mock<IDataService>(MockBehavior.Strict);

        // Return 1 record that doesn't match (meaning not all match)
        var result = new SalesforceCore.Models.Data.QueryResult
        {
            TotalSize = 1,
            Records = new List<JsonObject>
            {
                new() { ["Id"] = "001", ["expr0"] = 1 }
            }
        };

        mockDataService
            .Setup(s => s.QueryAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(result);

        var query = new SalesforceQueryable<Account>(mockDataService.Object);

        var allActive = await query.AllAsync(a => a.IsActive);

        allActive.Should().BeFalse();
    }

    #endregion

    #region LastAsync / LastOrDefaultAsync Tests

    [Fact]
    public async Task LastAsync_WithResults_ShouldReturnLastByOrderDesc()
    {
        var mockDataService = new Mock<IDataService>(MockBehavior.Strict);
        string? capturedSoql = null;

        var result = new SalesforceCore.Models.Data.QueryResult
        {
            Records = new List<JsonObject>
            {
                new()
                {
                    ["Id"] = "001",
                    ["Name"] = "LastAccount",
                    ["CreatedDate"] = "2024-12-25T00:00:00.000+0000"
                }
            }
        };

        mockDataService
            .Setup(s => s.QueryAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback<string, CancellationToken>((soql, _) => capturedSoql = soql)
            .ReturnsAsync(result);

        var query = new SalesforceQueryable<Account>(mockDataService.Object);

        var last = await query.LastAsync(a => a.CreatedDate);

        last.Should().NotBeNull();
        last.Name.Should().Be("LastAccount");
        capturedSoql.Should().Contain("ORDER BY CreatedDate DESC");
        capturedSoql.Should().Contain("LIMIT 1");
    }

    [Fact]
    public async Task LastAsync_WithNoResults_ShouldThrow()
    {
        var mockDataService = new Mock<IDataService>(MockBehavior.Strict);

        var result = new SalesforceCore.Models.Data.QueryResult
        {
            Records = new List<JsonObject>()
        };

        mockDataService
            .Setup(s => s.QueryAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(result);

        var query = new SalesforceQueryable<Account>(mockDataService.Object);

        Func<Task> act = async () => await query.LastAsync(a => a.CreatedDate);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*no elements*");
    }

    [Fact]
    public async Task LastOrDefaultAsync_WithNoResults_ShouldReturnNull()
    {
        var mockDataService = new Mock<IDataService>(MockBehavior.Strict);

        var result = new SalesforceCore.Models.Data.QueryResult
        {
            Records = new List<JsonObject>()
        };

        mockDataService
            .Setup(s => s.QueryAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(result);

        var query = new SalesforceQueryable<Account>(mockDataService.Object);

        var last = await query.LastOrDefaultAsync(a => a.CreatedDate);

        last.Should().BeNull();
    }

    #endregion

    #region ElementAtAsync / ElementAtOrDefaultAsync Tests

    [Fact]
    public async Task ElementAtAsync_WithValidIndex_ShouldReturnElement()
    {
        var mockDataService = new Mock<IDataService>(MockBehavior.Strict);
        string? capturedSoql = null;

        var result = new SalesforceCore.Models.Data.QueryResult
        {
            Records = new List<JsonObject>
            {
                new()
                {
                    ["Id"] = "001",
                    ["Name"] = "FifthAccount"
                }
            }
        };

        mockDataService
            .Setup(s => s.QueryAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback<string, CancellationToken>((soql, _) => capturedSoql = soql)
            .ReturnsAsync(result);

        var query = new SalesforceQueryable<Account>(mockDataService.Object);

        var element = await query.ElementAtAsync(4);

        element.Should().NotBeNull();
        element.Name.Should().Be("FifthAccount");
        capturedSoql.Should().Contain("OFFSET 4");
        capturedSoql.Should().Contain("LIMIT 1");
    }

    [Fact]
    public async Task ElementAtAsync_WithNegativeIndex_ShouldThrow()
    {
        var mockDataService = new Mock<IDataService>(MockBehavior.Strict);
        var query = new SalesforceQueryable<Account>(mockDataService.Object);

        Func<Task> act = async () => await query.ElementAtAsync(-1);

        await act.Should().ThrowAsync<ArgumentOutOfRangeException>()
            .WithMessage("*non-negative*");
    }

    [Fact]
    public async Task ElementAtAsync_WithOutOfRangeIndex_ShouldThrow()
    {
        var mockDataService = new Mock<IDataService>(MockBehavior.Strict);

        var result = new SalesforceCore.Models.Data.QueryResult
        {
            Records = new List<JsonObject>()
        };

        mockDataService
            .Setup(s => s.QueryAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(result);

        var query = new SalesforceQueryable<Account>(mockDataService.Object);

        Func<Task> act = async () => await query.ElementAtAsync(100);

        await act.Should().ThrowAsync<ArgumentOutOfRangeException>()
            .WithMessage("*out of range*");
    }

    [Fact]
    public async Task ElementAtOrDefaultAsync_WithNegativeIndex_ShouldReturnNull()
    {
        var mockDataService = new Mock<IDataService>(MockBehavior.Strict);
        var query = new SalesforceQueryable<Account>(mockDataService.Object);

        var element = await query.ElementAtOrDefaultAsync(-1);

        element.Should().BeNull();
    }

    [Fact]
    public async Task ElementAtOrDefaultAsync_WithOutOfRangeIndex_ShouldReturnNull()
    {
        var mockDataService = new Mock<IDataService>(MockBehavior.Strict);

        var result = new SalesforceCore.Models.Data.QueryResult
        {
            Records = new List<JsonObject>()
        };

        mockDataService
            .Setup(s => s.QueryAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(result);

        var query = new SalesforceQueryable<Account>(mockDataService.Object);

        var element = await query.ElementAtOrDefaultAsync(100);

        element.Should().BeNull();
    }

    #endregion

    #region ConcatAsync Tests

    [Fact]
    public async Task ConcatAsync_ShouldCombineAllRecords()
    {
        var mockDataService = new Mock<IDataService>(MockBehavior.Strict);
        var callCount = 0;

        var result1 = new SalesforceCore.Models.Data.QueryResult
        {
            Records = new List<JsonObject>
            {
                new() { ["Id"] = "001", ["Name"] = "Account1" },
                new() { ["Id"] = "002", ["Name"] = "Account2" }
            }
        };

        var result2 = new SalesforceCore.Models.Data.QueryResult
        {
            Records = new List<JsonObject>
            {
                new() { ["Id"] = "003", ["Name"] = "Account3" }
            }
        };

        mockDataService
            .Setup(s => s.QueryAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => ++callCount == 1 ? result1 : result2);

        var query1 = new SalesforceQueryable<Account>(mockDataService.Object);
        var query2 = new SalesforceQueryable<Account>(mockDataService.Object);

        var combined = await query1.ConcatAsync(query2);

        combined.Should().HaveCount(3);
        combined.Select(a => a.Id).Should().Equal("001", "002", "003");
    }

    #endregion

    #region UnionAsync Tests

    [Fact]
    public async Task UnionAsync_ShouldRemoveDuplicatesById()
    {
        var mockDataService = new Mock<IDataService>(MockBehavior.Strict);
        var callCount = 0;

        var result1 = new SalesforceCore.Models.Data.QueryResult
        {
            Records = new List<JsonObject>
            {
                new() { ["Id"] = "001", ["Name"] = "Account1" },
                new() { ["Id"] = "002", ["Name"] = "Account2" }
            }
        };

        var result2 = new SalesforceCore.Models.Data.QueryResult
        {
            Records = new List<JsonObject>
            {
                new() { ["Id"] = "002", ["Name"] = "Account2" }, // Duplicate
                new() { ["Id"] = "003", ["Name"] = "Account3" }
            }
        };

        mockDataService
            .Setup(s => s.QueryAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => ++callCount == 1 ? result1 : result2);

        var query1 = new SalesforceQueryable<Account>(mockDataService.Object);
        var query2 = new SalesforceQueryable<Account>(mockDataService.Object);

        var union = await query1.UnionAsync(query2);

        union.Should().HaveCount(3); // Not 4, because 002 is deduplicated
        union.Select(a => a.Id).Should().BeEquivalentTo(new[] { "001", "002", "003" });
    }

    #endregion

    #region ExceptAsync Tests

    [Fact]
    public async Task ExceptAsync_ShouldRemoveMatchingRecords()
    {
        var mockDataService = new Mock<IDataService>(MockBehavior.Strict);
        var callCount = 0;

        var result1 = new SalesforceCore.Models.Data.QueryResult
        {
            Records = new List<JsonObject>
            {
                new() { ["Id"] = "001", ["Name"] = "Account1" },
                new() { ["Id"] = "002", ["Name"] = "Account2" },
                new() { ["Id"] = "003", ["Name"] = "Account3" }
            }
        };

        var result2 = new SalesforceCore.Models.Data.QueryResult
        {
            Records = new List<JsonObject>
            {
                new() { ["Id"] = "002", ["Name"] = "Account2" }
            }
        };

        mockDataService
            .Setup(s => s.QueryAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => ++callCount == 1 ? result1 : result2);

        var query1 = new SalesforceQueryable<Account>(mockDataService.Object);
        var query2 = new SalesforceQueryable<Account>(mockDataService.Object);

        var except = await query1.ExceptAsync(query2);

        except.Should().HaveCount(2); // 001 and 003
        except.Select(a => a.Id).Should().BeEquivalentTo(new[] { "001", "003" });
    }

    #endregion

    #region IntersectAsync Tests

    [Fact]
    public async Task IntersectAsync_ShouldReturnOnlyCommonRecords()
    {
        var mockDataService = new Mock<IDataService>(MockBehavior.Strict);
        var callCount = 0;

        var result1 = new SalesforceCore.Models.Data.QueryResult
        {
            Records = new List<JsonObject>
            {
                new() { ["Id"] = "001", ["Name"] = "Account1" },
                new() { ["Id"] = "002", ["Name"] = "Account2" },
                new() { ["Id"] = "003", ["Name"] = "Account3" }
            }
        };

        var result2 = new SalesforceCore.Models.Data.QueryResult
        {
            Records = new List<JsonObject>
            {
                new() { ["Id"] = "002", ["Name"] = "Account2" },
                new() { ["Id"] = "004", ["Name"] = "Account4" }
            }
        };

        mockDataService
            .Setup(s => s.QueryAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => ++callCount == 1 ? result1 : result2);

        var query1 = new SalesforceQueryable<Account>(mockDataService.Object);
        var query2 = new SalesforceQueryable<Account>(mockDataService.Object);

        var intersect = await query1.IntersectAsync(query2);

        intersect.Should().HaveCount(1); // Only 002
        intersect.Select(a => a.Id).Should().Equal("002");
    }

    #endregion

    #region IdBasedEqualityComparer Tests

    [Fact]
    public void IdBasedEqualityComparer_ShouldCompareById()
    {
        var account1 = new Account { Id = "001", Name = "Account1" };
        var account2 = new Account { Id = "001", Name = "DifferentName" };
        var account3 = new Account { Id = "002", Name = "Account3" };

        var list1 = new List<Account> { account1 };
        var list2 = new List<Account> { account2 };

        // Union should recognize 001 as duplicate even with different Name
        var union = list1.Union(list2, new IdBasedEqualityComparer<Account>()).ToList();
        
        union.Should().HaveCount(1);
        
        // Except should remove matching Id
        var except = list1.Except(list2, new IdBasedEqualityComparer<Account>()).ToList();
        except.Should().BeEmpty();

        // Different IDs should not be equal
        var list3 = new List<Account> { account3 };
        var except2 = list1.Except(list3, new IdBasedEqualityComparer<Account>()).ToList();
        except2.Should().HaveCount(1);
    }

    #endregion
}
