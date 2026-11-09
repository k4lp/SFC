using System.Reflection;
using System.Runtime.Loader;
using System.Text.Json.Nodes;
using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using SalesforceCore.E2E.Harness;
using SalesforceCore.Models.Metadata;
using SalesforceCore.ModelGenerator;
using SalesforceCore.Services.Data;
using Xunit;

namespace SalesforceCore.E2E;

/// <summary>Round 5: sf-gen output compiled with Roslyn, loaded, and then used through ITypedDataService (write, read, LINQ).</summary>
public class GeneratedModelUsageE2ETests
{
    private static Type Compile(string code, string typeName)
    {
        var refs = AppDomain.CurrentDomain.GetAssemblies().Where(a => !a.IsDynamic && a.Location != "")
            .Select(a => MetadataReference.CreateFromFile(a.Location));
        var compilation = CSharpCompilation.Create("gen" + Guid.NewGuid().ToString("N"), new[] { CSharpSyntaxTree.ParseText(code) }, refs,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));
        using var ms = new MemoryStream();
        var emit = compilation.Emit(ms);
        emit.Success.Should().BeTrue(string.Join("\n", emit.Diagnostics));
        ms.Position = 0;
        return AssemblyLoadContext.Default.LoadFromStream(ms).GetTypes().Single(t => t.Name == typeName);
    }

    private static async Task<T> Invoke<T>(object target, string method, Type generic, params object?[] args)
    {
        var m = target.GetType().GetMethods().Single(x => x.Name == method && x.IsGenericMethodDefinition && x.GetParameters().Length == args.Length)
            .MakeGenericMethod(generic);
        var task = (Task)m.Invoke(target, args)!;
        await task;
        return (T)task.GetType().GetProperty("Result")!.GetValue(task)!;
    }

    [Fact]
    public async Task GeneratedModel_WritesReadsAndFilters_ThroughTheTypedApi()
    {
        var describe = new SObjectDescribe
        {
            Name = "Invoice__c", Label = "Invoice", Queryable = true, Createable = true, Updateable = true,
            Fields = new List<SObjectField>
            {
                new() { Name = "Id", Type = "id", Label = "Id", Nillable = false },
                new() { Name = "Name", Type = "string", Label = "Name", Nillable = true, Createable = true, Updateable = true },
                new() { Name = "Total__c", Type = "currency", Label = "Total", Nillable = true, Createable = true, Updateable = true },
                new() { Name = "Due__c", Type = "date", Label = "Due", Nillable = true, Createable = true, Updateable = true },
                new() { Name = "When__c", Type = "datetime", Label = "When", Nillable = true, Createable = true, Updateable = true },
                new() { Name = "Paid__c", Type = "boolean", Label = "Paid", Nillable = false, Createable = true, Updateable = true },
                new() { Name = "Status__c", Type = "picklist", Label = "Status", Nillable = true, Createable = true, Updateable = true,
                        PicklistValues = new() { new() { Value = "Closed Won", Label = "Closed Won", Active = true }, new() { Value = "Open", Label = "Open", Active = true } } },
                new() { Name = "Formula__c", Type = "double", Label = "F", Nillable = true, Createable = false, Updateable = false, Calculated = true },
            }
        };
        using var generator = new ModelGenerator.ModelGenerator("https://acme.my.salesforce.com", "token", "v62.0");
        var code = generator.GenerateClass(describe, new GeneratorOptions { Namespace = "Gen.R5" });
        var type = Compile(code, "Invoice__c");

        await using var h = await E2EHost.StartAsync();
        var typed = h.Get<ITypedDataService>();
        var record = Activator.CreateInstance(type)!;
        void Set(string prop, object? v) => type.GetProperty(prop)!.SetValue(record, v);
        object? Get(object o, string prop) => type.GetProperty(prop)!.GetValue(o);
        var statusProp = type.GetProperty("Status__c")!;
        var statusType = Nullable.GetUnderlyingType(statusProp.PropertyType) ?? statusProp.PropertyType;
        object closedWon = statusType.IsEnum ? Enum.GetValues(statusType).Cast<object>().First(v => v.ToString()!.Contains("Closed")) : "Closed Won";

        Set("Name", "Gen 1");
        Set("Total__c", 1234.5m);
        Set("Due__c", new DateOnly(2026, 11, 7));
        Set("When__c", new DateTimeOffset(2026, 11, 7, 10, 0, 0, TimeSpan.FromHours(5.5)));
        Set("Paid__c", true);
        statusProp.SetValue(record, closedWon);
        type.GetProperty("Formula__c")!.SetValue(record, 9.9m);

        var id = await Invoke<string>(typed, "CreateAsync", type, record, CancellationToken.None);
        var sent = h.Fake.Records[id];
        sent["Status__c"]!.GetValue<string>().Should().Be("Closed Won");
        sent["Due__c"]!.GetValue<string>().Should().Be("2026-11-07");
        sent["When__c"]!.GetValue<string>().Should().Be("2026-11-07T04:30:00.000Z");
        sent["Total__c"]!.ToJsonString().Should().Be("1234.5");
        sent.ContainsKey("Formula__c").Should().BeFalse("calculated fields are not createable");

        h.Fake.QueryRows.Add(JsonNode.Parse($$"""
        {"attributes":{"type":"Invoice__c"},"Id":"{{id}}","Name":"Gen 1","Total__c":1234.5,"Due__c":"2026-11-07",
         "When__c":"2026-11-07T04:30:00.000+0000","Paid__c":true,"Status__c":"Closed Won","Formula__c":9.9}
        """)!.AsObject());
        var list = await Invoke<System.Collections.IList>(typed, "GetAllAsync", type, null, CancellationToken.None);
        list.Count.Should().Be(1);
        var back = list[0]!;
        Get(back, "Status__c").Should().Be(closedWon);
        Get(back, "Due__c").Should().Be(new DateOnly(2026, 11, 7));
        Get(back, "Total__c").Should().Be(1234.5m);
        Get(back, "Paid__c").Should().Be(true);
        Get(back, "When__c").Should().Be(new DateTimeOffset(2026, 11, 7, 4, 30, 0, TimeSpan.Zero));
        Get(back, "Formula__c").Should().Be(9.9m);
        h.Fake.Requests.Last(r => r.Contains("/query")).Should().Contain("Invoice__c");
    }
}
