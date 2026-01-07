using System.Web;
using Xunit;
using FluentAssertions;
using Moq;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;
using SalesforceCore.Services.Data;
using SalesforceCore.Services.Core;
using SalesforceCore.Models.Data;
using RecordAttributes = SalesforceCore.Models.Data.RecordAttributes;

namespace SalesforceCore.Tests;

public class SearchServiceTests
{
    private readonly Mock<ISalesforceClient> _mockClient;
    private readonly Mock<ILogger<SearchService>> _mockLogger;
    private readonly SearchService _service;

    public SearchServiceTests()
    {
        _mockClient = new Mock<ISalesforceClient>();
        _mockLogger = new Mock<ILogger<SearchService>>();
        _service = new SearchService(_mockClient.Object, _mockLogger.Object);
    }

    [Fact]
    public async Task SearchAsync_WithRawSosl_ShouldCallApi()
    {
        // Arrange
        var sosl = "FIND {test*} IN ALL FIELDS RETURNING Account(Name), Contact(LastName)";
        var expectedResponse = new SearchResult
        {
            SearchRecords = new List<SearchRecord>
            {
                new SearchRecord { Id = "001xxx", Attributes = new RecordAttributes { Type = "Account" } },
                new SearchRecord { Id = "003xxx", Attributes = new RecordAttributes { Type = "Contact" } }
            }
        };

        _mockClient.Setup(c => c.GetAsync<SearchResult>(
                It.Is<string>(s => s.Contains("search/?q=")),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedResponse);

        // Act
        var result = await _service.SearchAsync(sosl);

        // Assert
        result.Should().NotBeNull();
        result.SearchRecords.Should().HaveCount(2);
        _mockClient.Verify(c => c.GetAsync<SearchResult>(
            It.Is<string>(s => HttpUtility.UrlDecode(s).Contains("FIND {test*}")),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SearchAsync_WithBuilder_ShouldGenerateCorrectSosl()
    {
        // Arrange
        var builder = new SoslBuilder()
            .Find("Acme")
            .In(SearchScope.AllFields)
            .Returning("Account", "Name", "Industry")
            .Returning("Contact", "FirstName", "LastName")
            .WithLimit(10);

        var expectedResponse = new SearchResult { SearchRecords = new List<SearchRecord>() };

        _mockClient.Setup(c => c.GetAsync<SearchResult>(
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedResponse);

        // Act
        await _service.SearchAsync(builder);

        // Assert
        _mockClient.Verify(c => c.GetAsync<SearchResult>(
            It.Is<string>(s => 
                HttpUtility.UrlDecode(s).Contains("FIND {Acme}") && 
                HttpUtility.UrlDecode(s).Contains("IN ALL FIELDS") &&
                HttpUtility.UrlDecode(s).Contains("RETURNING Account(Name, Industry)") &&
                HttpUtility.UrlDecode(s).Contains("Contact(FirstName, LastName)") &&
                HttpUtility.UrlDecode(s).Contains("LIMIT 10")),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SearchAsync_EmptyQuery_ShouldThrowArgumentException()
    {
        // Arrange
        var sosl = "";

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(() => _service.SearchAsync(sosl));
    }
}