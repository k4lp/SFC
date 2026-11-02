using System.Net;
using System.Text.Json.Nodes;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.Extensions.DependencyInjection;
using SalesforceCore.AspNetCore.Extensions;
using SalesforceCore.E2E.Harness;
using SalesforceCore.Models.Errors;
using SalesforceCore.Models.Metadata;
using SalesforceCore.ModelGenerator;
using SalesforceCore.Services.Files;
using Xunit;

namespace SalesforceCore.E2E;

public class GeneratorFilesAndMiddlewareE2ETests
{
    private static SObjectField F(string name, string type, bool nillable = true) =>
        new() { Name = name, Label = name + " <label> */ \"q\"", Type = type, Nillable = nillable, Createable = true, Updateable = true };

    [Fact]
    public void GeneratedModel_CompilesWithRoslyn_WithoutErrorsOrWarnings()
    {
        var describe = new SObjectDescribe
        {
            Name = "Invoice__c", Label = "Invoice", Queryable = true,
            Fields = new List<SObjectField>
            {
                F("Id", "id", false), F("Invoice__c", "string"), F("Total", "currency"), F("Total__c", "currency"),
                F("class", "string"), F("Due__c", "date"), F("At__c", "time"), F("Loc__c", "location"),
                F("Paid__c", "boolean", false), F("When__c", "datetime"), F("Blob__c", "base64"),
                new() { Name = "Status__c", Label = "S", Type = "picklist", Nillable = true, Createable = true, Updateable = true,
                        PicklistValues = Enumerable.Range(0, 25).Select(i => new PicklistEntry { Value = "v\"" + i, Label = "l", Active = true }).ToList() }
            }
        };
        using var generator = new ModelGenerator.ModelGenerator("https://acme.my.salesforce.com", "token", "v62.0");
        var code = generator.GenerateClass(describe, new GeneratorOptions { Namespace = "Gen.Models" });

        var refs = AppDomain.CurrentDomain.GetAssemblies().Where(a => !a.IsDynamic && a.Location != "")
            .Select(a => MetadataReference.CreateFromFile(a.Location));
        var compilation = CSharpCompilation.Create("generated", new[] { CSharpSyntaxTree.ParseText(code) }, refs,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));
        compilation.GetDiagnostics().Where(d => d.Severity >= DiagnosticSeverity.Warning).Select(d => d.ToString())
            .Should().BeEmpty("2.2.x emitted CS0542 when a field matched the class name and CS8618 for Id");
        code.Should().Contain("v\\\"24", "all picklist values are emitted (2.2.x stopped at 20)");
        code.Should().Contain("[SalesforceField(\"Invoice__c\"");
    }

    [Fact]
    public async Task FileUpload_HonoursConfiguredMaxFileUploadSize()
    {
        await using var h = await E2EHost.StartAsync(new() { ["Salesforce:MaxFileUploadSize"] = "10" });
        var act = () => h.Get<IFileService>().UploadAsync("001000000000001AAA", "a.txt", new byte[11]);
        await act.Should().ThrowAsync<ArgumentException>().WithMessage("*exceeds*");
    }

    [Fact]
    public async Task ExceptionMiddleware_RetryAfter_IsIntegerSeconds_UnderAnyCulture()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddLogging();
        var app = builder.Build();
        app.UseMiddleware<SalesforceCore.AspNetCore.Middleware.SalesforceExceptionMiddleware>();
        app.MapGet("/x", (HttpContext _) =>
        {
            System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo("de-DE");
            throw new SalesforceRateLimitException("slow down", TimeSpan.FromSeconds(1.5));
        });
        await app.StartAsync();
        var response = await app.GetTestClient().GetAsync("/x");
        response.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        response.Headers.GetValues("Retry-After").Single().Should().Be("2");
        await app.DisposeAsync();
    }
}
