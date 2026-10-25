using System.Text.Json.Nodes;

namespace SalesforceCore.Tests.TestKit;

/// <summary>Builders for Salesforce-shaped JSON payloads.</summary>
public static class SalesforceJson
{
    /// <summary>A record with <c>attributes</c>.</summary>
    public static JsonObject Record(string type, string id, params (string Field, object? Value)[] fields)
    {
        var o = new JsonObject
        {
            ["attributes"] = new JsonObject { ["type"] = type, ["url"] = $"/services/data/v62.0/sobjects/{type}/{id}" },
            ["Id"] = id
        };
        foreach (var (f, v) in fields) o[f] = v == null ? null : JsonValue.Create(v);
        return o;
    }

    /// <summary>A query result page.</summary>
    public static JsonObject QueryPage(IEnumerable<JsonObject> records, string? nextRecordsUrl = null, int? totalSize = null)
    {
        var arr = new JsonArray(records.Select(r => (JsonNode)r.DeepClone()).ToArray());
        var o = new JsonObject { ["totalSize"] = totalSize ?? arr.Count, ["done"] = nextRecordsUrl == null, ["records"] = arr };
        if (nextRecordsUrl != null) o["nextRecordsUrl"] = nextRecordsUrl;
        return o;
    }

    /// <summary>A Salesforce error array.</summary>
    public static JsonArray Errors(params (string Code, string Message, string[] Fields)[] errors)
        => new(errors.Select(e => (JsonNode)new JsonObject
        {
            ["message"] = e.Message,
            ["errorCode"] = e.Code,
            ["fields"] = new JsonArray(e.Fields.Select(f => (JsonNode)JsonValue.Create(f)!).ToArray())
        }).ToArray());

    /// <summary>Create result.</summary>
    public static JsonObject Created(string id) => new() { ["id"] = id, ["success"] = true, ["errors"] = new JsonArray() };
}

/// <summary>Fluent builder for sObject describe JSON.</summary>
public sealed class DescribeBuilder
{
    private readonly JsonObject _o;
    private readonly JsonArray _fields = new();

    /// <summary>Starts a describe for <paramref name="name"/>.</summary>
    public DescribeBuilder(string name, string? label = null)
    {
        _o = new JsonObject
        {
            ["name"] = name, ["label"] = label ?? name, ["labelPlural"] = (label ?? name) + "s",
            ["keyPrefix"] = "001", ["createable"] = true, ["updateable"] = true, ["deletable"] = true,
            ["queryable"] = true, ["searchable"] = true, ["custom"] = name.EndsWith("__c", StringComparison.Ordinal),
            ["fields"] = _fields, ["childRelationships"] = new JsonArray(), ["recordTypeInfos"] = new JsonArray()
        };
        Field("Id", "id", createable: false, updateable: false, nillable: false);
    }

    /// <summary>Adds a field.</summary>
    public DescribeBuilder Field(string name, string type = "string", bool createable = true, bool updateable = true, bool nillable = true,
        string? label = null, string? relationshipName = null, string[]? referenceTo = null, bool nameField = false, int length = 255)
    {
        _fields.Add(new JsonObject
        {
            ["name"] = name, ["label"] = label ?? name, ["type"] = type, ["createable"] = createable, ["updateable"] = updateable,
            ["nillable"] = nillable, ["filterable"] = true, ["sortable"] = true, ["groupable"] = true, ["accessible"] = true,
            ["nameField"] = nameField, ["length"] = length, ["relationshipName"] = relationshipName,
            ["referenceTo"] = new JsonArray((referenceTo ?? Array.Empty<string>()).Select(r => (JsonNode)JsonValue.Create(r)!).ToArray()),
            ["picklistValues"] = new JsonArray()
        });
        return this;
    }

    /// <summary>Adds a Name field marked as name field.</summary>
    public DescribeBuilder NameField(string name = "Name") => Field(name, "string", nameField: true);

    /// <summary>Builds the JSON.</summary>
    public JsonObject Build() => (JsonObject)_o.DeepClone();
}
