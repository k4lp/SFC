using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SalesforceCore.AspNetCore.Extensions;
using SalesforceCore.E2E.Harness;
using SalesforceCore.RazorTestHost;
using SalesforceCore.Services.Core;
using Xunit;

namespace SalesforceCore.E2E;

/// <summary>
/// Round 5: compiled Razor views (tests/SalesforceCore.RazorTestHost) rendered by a real ASP.NET Core MVC pipeline
/// (TestServer), with SFC tag helpers reading describe metadata from the fake Salesforce over the real HTTP pipeline.
/// </summary>
public class RazorRenderingE2ETests
{
    internal static async Task<(WebApplication app, FakeSalesforce fake)> StartAppAsync()
    {
        var fake = await FakeSalesforce.StartAsync();
        var b = WebApplication.CreateBuilder();
        b.WebHost.UseTestServer();
        b.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Salesforce:Domain"] = "https://login.salesforce.com",
            ["Salesforce:ClientId"] = "cid",
            ["Salesforce:ClientSecret"] = "secret",
            ["Salesforce:ApiVersion"] = "v62.0",
            ["SalesforceClientCredentials:ClientId"] = "cid",
            ["SalesforceClientCredentials:ClientSecret"] = "secret",
        });
        b.Services.AddSalesforceCoreMvc(b.Configuration);
        // Server-to-server token for this probe app: drop the cookie/OIDC provider so the client-credentials one is used.
        foreach (var d in b.Services.Where(d => d.ServiceType == typeof(ITokenProvider) && d.ImplementationType?.Name == "AspNetCoreTokenProvider").ToList())
            b.Services.Remove(d);
        b.Services.AddControllersWithViews().AddApplicationPart(typeof(RazorProbeController).Assembly);
        b.Services.ConfigureHttpClientDefaults(h => h.ConfigurePrimaryHttpMessageHandler(() => fake.Handler));
        var app = b.Build();
        app.MapControllers();
        await app.StartAsync();
        return (app, fake);
    }

    [Fact]
    public async Task PicklistTagHelper_StringModel_RendersEncodedOptionsAndSelectsCurrent()
    {
        var (app, fake) = await StartAppAsync();
        await using var _ = app; await using var __ = fake;
        var html = await app.GetTestClient().GetStringAsync("/razor-probe/picklist-string");

        html.Should().Contain("<select");
        html.Should().NotContain("<b>", "labels and values from Salesforce must be HTML-encoded");
        // HtmlAttributeEncode leaves '>' as-is inside a quoted attribute, which is safe; '<' and '"' must be encoded.
        html.Should().MatchRegex("<option value=\"Tech&quot;&lt;b(>|&gt;)\" selected>Tech &lt;b&gt;</option>");
        html.Should().NotContain(">Old<", "inactive values are not offered");
    }

    [Fact]
    public async Task PicklistTagHelper_EnumModelWithSalesforceValue_SelectsMappedOption()
    {
        var (app, fake) = await StartAppAsync();
        await using var _ = app; await using var __ = fake;
        var html = await app.GetTestClient().GetStringAsync("/razor-probe/picklist-enum");

        html.Should().Contain("name=\"IndustryEnum\"", "asp-for decides the form field name so the post binds back to the model");
        html.Should().MatchRegex("<option value=\"Tech&quot;&lt;b(>|&gt;)\" selected>",
            "an enum bound with [SalesforceValue] must pre-select the Salesforce picklist value it maps to");
    }

    [Fact]
    public async Task FieldGroupTagHelper_PicklistField_RendersARealSelectNotAnUnprocessedTag()
    {
        var (app, fake) = await StartAppAsync();
        await using var _ = app; await using var __ = fake;
        var html = await app.GetTestClient().GetStringAsync("/razor-probe/field-group-picklist");

        html.Should().NotContain("<sf-picklist", "tag helper markup emitted as a string is never processed by Razor; the browser gets an empty unknown element");
        html.Should().Contain("<select");
        html.Should().MatchRegex("<option value=\"Tech&quot;&lt;b(>|&gt;)\" selected>Tech &lt;b&gt;</option>");
    }

    [Fact]
    public async Task TemplatePrefix_IsUsedForNameAndId_AndLabelsPointAtTheRealInput()
    {
        var (app, fake) = await StartAppAsync();
        await using var _ = app; await using var __ = fake;
        var html = await app.GetTestClient().GetStringAsync("/razor-probe/prefixed");

        html.Should().Contain("<select id=\"Deal_IndustryEnum\" name=\"Deal.IndustryEnum\"");
        html.Should().Contain("id=\"Deal_Industry\" name=\"Deal.Industry\"");
        html.Should().Contain("<label for=\"Deal_Industry\"");
        html.Should().NotContain("for=\"Industry\"").And.NotContain("name=\"Industry\"");
        html.Should().NotContain("<b>").And.NotContain("<script>", "metadata labels and help text stay encoded");
    }

    [Fact]
    public async Task LookupTagHelper_UsesModelExpressionAndTemplatePrefix_ForNameAndId()
    {
        var (app, fake) = await StartAppAsync();
        await using var _ = app; await using var __ = fake;
        var html = await app.GetTestClient().GetStringAsync("/razor-probe/lookup");

        html.Should().Contain("id=\"ParentId\"").And.Contain("name=\"ParentId\"");
        html.Should().Contain("id=\"Deal_ParentId\"").And.Contain("name=\"Deal.ParentId\"",
            "inside a template prefix the hidden value must post as Deal.ParentId to bind back");
        html.Should().Contain("data-sf-target=\"Deal_ParentId\"").And.Contain("value=\"001000000000001AAA\"");
        html.Should().Contain("data-sf-target-objects=\"Account\"");
    }

    [Fact]
    public async Task ModelFormTagHelper_WholeModel_RendersBindableNamesAndARealPicklist()
    {
        var (app, fake) = await StartAppAsync();
        await using var _ = app; await using var __ = fake;
        var html = await app.GetTestClient().GetStringAsync("/razor-probe/model-form");

        html.Should().NotContain("name=\".", "asp-model=\"@Model\" has an empty expression name; '.Name' never binds");
        html.Should().Contain("id=\"Name\" name=\"Name\"").And.Contain("name=\"AnnualRevenue\"").And.Contain("<label for=\"Name\"");
        html.Should().NotContain("<sf-picklist", "tag helper markup in a string is not processed by Razor");
        html.Should().Contain("<select id=\"Industry\" name=\"Industry\"");
        html.Should().MatchRegex("<option value=\"Tech&quot;&lt;b(>|&gt;)\" selected>Tech &lt;b&gt;</option>");
        html.Should().NotContain("<i>").And.NotContain("<script>");
    }

    [Fact]
    public async Task PermissionTagHelper_ShowsOnlyContentTheDescribeAllows()
    {
        var (app, fake) = await StartAppAsync();
        await using var _ = app; await using var __ = fake;
        var html = await app.GetTestClient().GetStringAsync("/razor-probe/permission");

        html.Should().Contain("[can-create-account]").And.Contain("[cannot-delete-account]").And.Contain("[can-edit-name]");
        html.Should().NotContain("[can-delete-account]").And.NotContain("[can-edit-id]")
            .And.NotContain("[unknown-field]").And.NotContain("[unknown-object]");
        html.Should().NotContain("<sf-permission");
    }
}
