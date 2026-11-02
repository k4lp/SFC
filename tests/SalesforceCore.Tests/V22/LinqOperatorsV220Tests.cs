using System.Reflection;
using System.Text.Json.Nodes;
using FluentAssertions;
using SalesforceCore.Query;
using SalesforceCore.Services.Query;
using SalesforceCore.Tests.TestKit;
using Xunit;

namespace SalesforceCore.Tests.V22;

public class LinqOperatorsV22Tests
{
    private readonly RecordingExecutor _ex = new();
    private SalesforceQueryable<V22Account> Q => _ex.Query<V22Account>();
    private static JsonObject Acc(string id, string name) => SalesforceJson.Record("Account", id, ("Name", name));

    [Fact] public void Relationships_AndPicklistHelpers()
    {
        Q.Include("Contacts", "Id", "Email").ToSoql().Should().Contain("(SELECT Id, Email FROM Contacts)");
        Q.IncludeWhere("Contacts", new[] { "Id" }, SoqlCondition.IsNotNull("Email"), 5).ToSoql()
            .Should().Contain("(SELECT Id FROM Contacts WHERE Email != NULL LIMIT 5)");
        Q.WhereIncludes(a => a.Regions, "EMEA").ToSoql().Should().Contain("Regions__c INCLUDES ('EMEA')");
        Q.WhereExcludes(a => a.Regions, "APAC").ToSoql().Should().Contain("Regions__c EXCLUDES ('APAC')");
        Q.Where(a => a.Name != null).WhereCondition(SoqlCondition.IsNull("Industry")).ToSoql()
            .Should().Contain("WHERE ((Name != NULL)) AND (Industry = NULL)");
    }

    [Fact] public void LockingClauses()
    {
        Q.ForUpdate().ToSoql().Should().EndWith("FOR UPDATE");
        Q.ForView().ToSoql().Should().EndWith("FOR VIEW");
        Q.ForReference().ToSoql().Should().EndWith("FOR REFERENCE");
        Q.Select(a => a.Name).ToSoql().Should().Be("SELECT Id, Name FROM Account");
    }

    [Fact] public async Task ElementAt_RespectsEarlierTake()
    {
        await ((Func<Task>)(() => Q.Take(3).ElementAtAsync(5))).Should().ThrowAsync<ArgumentOutOfRangeException>();
        _ex.Calls.Should().BeEmpty("Take(3).Skip(5) is provably empty, so nothing is sent");
        _ex.Rows.Add(Acc("001000000000001AAA", "A"));
        (await Q.OrderBy(a => a.Name).ElementAtAsync(2)).Name.Should().Be("A");
        _ex.Last.Should().EndWith("ORDER BY Name ASC LIMIT 1 OFFSET 2");
        (await Q.ElementAtOrDefaultAsync(-1)).Should().BeNull();
        await ((Func<Task>)(() => Q.ElementAtAsync(-1))).Should().ThrowAsync<ArgumentOutOfRangeException>();
    }

    [Fact] public async Task Last_All_SetOperations()
    {
        _ex.Rows.Add(Acc("001000000000001AAA", "A"));
        (await Q.LastAsync(a => a.CreatedDate)).Name.Should().Be("A");
        _ex.Last.Should().Contain("ORDER BY CreatedDate DESC LIMIT 1");
        (await Q.LastOrDefaultAsync(a => a.Name)).Should().NotBeNull();
        _ex.TotalSize = 0;
        (await Q.AllAsync(a => a.Name != null)).Should().BeTrue();
        _ex.Last.Should().Be("SELECT COUNT() FROM Account WHERE NOT (Name != NULL)");
        (await Q.UnionAsync(Q)).Should().HaveCount(1);
        (await Q.ConcatAsync(Q)).Should().HaveCount(2);
        (await Q.ExceptAsync(Q)).Should().BeEmpty();
        (await Q.IntersectAsync(Q)).Should().HaveCount(1);
        _ex.Rows.Clear();
        await ((Func<Task>)(() => Q.LastAsync(a => a.Name))).Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact] public void SyncLinqOperators_Work()
    {
        _ex.Rows.Add(Acc("001000000000001AAA", "A"));
        _ex.TotalSize = 1;
        Q.Where(a => a.Name == "A").ToList().Should().ContainSingle();
        Q.First().Name.Should().Be("A");
        Q.FirstOrDefault(a => a.Name == "A")!.Name.Should().Be("A");
        Q.Single(a => a.Name == "A").Name.Should().Be("A");
        Q.Count(a => a.Name == "A").Should().Be(1);
        Q.Any(a => a.Name == "A").Should().BeTrue();
        Q.Select(a => a.Name).ToList().Should().Equal("A");
        Q.AsQueryable().ToList().Should().ContainSingle();
    }

    [Fact] public async Task ProjectionsToScalarsAndAnonymousTypes()
    {
        _ex.Rows.Add(SalesforceJson.Record("Account", "001000000000001AAA", ("Name", "A"), ("NumberOfEmployees", 7)));
        (await Q.Select(a => a.Name).ToListAsync()).Should().Equal("A");
        (await Q.Select(a => a.NumberOfEmployees ?? 0).ToListAllAsync()).Should().Equal(7);
        var anon = await Q.Select(a => new { a.Name, Big = a.NumberOfEmployees > 5 }).ToListAsync();
        anon.Single().Big.Should().BeTrue();
        var streamed = new List<string?>();
        await foreach (var n in Q.Select(a => a.Name).ToAsyncEnumerable()) streamed.Add(n);
        streamed.Should().Equal("A");
        await foreach (var a in ((IQueryable<V22Account>)Q).ToAsyncEnumerable()) a.Name.Should().Be("A");
        (await ((IQueryable<V22Account>)Q).ToListAllAsync()).Should().ContainSingle();
        await ((Func<Task>)(() => new[] { 1 }.AsQueryable().ToListAsync())).Should().ThrowAsync<InvalidOperationException>();
    }

    private static string Describe(V22Account a) => a.Name ?? "";

    [Fact] public void ComputedProjections_SelectEveryFieldRead()
    {
        Q.Select(a => a.Name + "/" + a.Industry).ToSoql().Should().Be("SELECT Id, Name, Industry FROM Account");
        Q.Select(a => a.CreatedDate!.Value.Year).ToSoql().Should().Be("SELECT Id, CreatedDate FROM Account");
        Q.Select(a => new { Label = a.Name!.ToUpperInvariant(), a.Regions }).ToSoql().Should().Be("SELECT Id, Name, Regions__c FROM Account");
        Q.Select(a => Describe(a)).ToSoql().Should().StartWith("SELECT Id, Name, Industry, Status");
        ((Action)(() => Q.Select(a => 42).ToSoql())).Should().Throw<NotSupportedException>().WithMessage("*does not read any field*");
        var computed = _ex.Query<V22Contact>().Select(c => c.AccountId!.ToUpper());
        ((Action)(() => Q.Where(a => SoqlFunctions.In(a.Id, computed)).ToSoql())).Should().Throw<NotSupportedException>();
    }

    [Fact] public void NullableValueAndDotNetMembers_InKeys()
    {
        Q.OrderBy(a => a.CreatedDate!.Value).ToSoql().Should().EndWith("ORDER BY CreatedDate ASC");
        ((Action)(() => Q.OrderBy(a => a.Name!.Length).ToSoql())).Should().Throw<NotSupportedException>().WithMessage("*not a Salesforce field*");
    }

    [Fact] public void Unsupported_AfterProjection_AndUnknownOperators()
    {
        ((Action)(() => Q.Select(a => a.Name).Where(n => n == "A").ToSoql())).Should().Throw<NotSupportedException>();
        ((Action)(() => Q.Reverse().ToSoql())).Should().Throw<NotSupportedException>().WithMessage("*Reverse*");
    }

    [Fact] public void EverySoqlFunction_ThrowsWhenCalledClientSide()
    {
        foreach (var m in typeof(SoqlFunctions).GetMethods(BindingFlags.Public | BindingFlags.Static))
        {
            var method = m.IsGenericMethodDefinition ? m.MakeGenericMethod(m.GetGenericArguments().Select(_ => typeof(string)).ToArray()) : m;
            var args = method.GetParameters().Select(p => p.ParameterType.IsValueType ? Activator.CreateInstance(p.ParameterType) : null).ToArray();
            var act = () => method.Invoke(null, args);
            act.Should().Throw<TargetInvocationException>().WithInnerException<InvalidOperationException>();
        }
    }
}
