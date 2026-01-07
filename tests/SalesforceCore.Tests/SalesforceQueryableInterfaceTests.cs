using System.Collections.Generic;
using FluentAssertions;
using SalesforceCore.Query;
using Xunit;

namespace SalesforceCore.Tests;

public class SalesforceQueryableInterfaceTests
{
    [Fact]
    public void SalesforceQueryable_ShouldNotImplementIAsyncEnumerable()
    {
        var interfaces = typeof(SalesforceQueryable<SoqlExpressionVisitorTests.Account>).GetInterfaces();

        interfaces.Should().NotContain(i =>
            i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IAsyncEnumerable<>));
    }
}

