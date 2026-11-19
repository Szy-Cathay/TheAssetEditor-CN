using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Shared.Core.Services;
using Shared.Core.ToolCreation;
using Shared.Core.PackFiles.Models;

namespace Shared.Core.ModAssistant;

public enum AssistantEffect { Query, Preview, Edit, Disk, Destructive, History }

// Object identities belong to this application session, never to list positions or focus.
public sealed class AssistantIds
{
    private readonly ConditionalWeakTable<object, Identity> _ids = new();
    private sealed record Identity(string Value);
    public string Get(object value) => _ids.GetValue(value, _ => new(Guid.NewGuid().ToString("N"))).Value;
}

public interface IAssistantOperationProvider
{
    IEnumerable<AssistantOperation> GetOperations();
}

public interface IAssistantEditorAdapter
{
    IEnumerable<AssistantOperation> GetOperations(IEditorInterface editor, AssistantIds ids);
}

// Some legacy editors implement ISaveableEditor with a placeholder Save method.
public interface IAssistantReadOnlyDocument { }

public interface IAssistantResourceAdapter
{
    IEnumerable<AssistantOperation> GetOperations(PackFileContainer owner, PackFile file, AssistantIds ids);
}

public interface IAssistantWorkspaceAdapter
{
    IEnumerable<AssistantOperation> GetOperations(AssistantIds ids);
}

public sealed record AssistantOperation(
    string Name,
    string TargetId,
    string Description,
    JsonObject Parameters,
    AssistantEffect Effect,
    string Recovery,
    Func<JsonNode> ReadState,
    Func<JsonObject, CancellationToken, Task<JsonNode>> Execute,
    Func<string?>? Unavailable = null,
    JsonObject? Scope = null)
{
    public JsonObject Describe() => new()
    {
        ["name"] = Name, ["target"] = TargetId, ["description"] = Description,
        ["parameters"] = Parameters.DeepClone(), ["effect"] = Effect.ToString(),
        ["modifies_data"] = Effect >= AssistantEffect.Edit,
        ["writes_disk"] = Effect >= AssistantEffect.Disk,
        ["recovery"] = Recovery, ["unavailable_reason"] = Unavailable?.Invoke(), ["scope"] = Scope?.DeepClone(),
    };

    public static AssistantOperation Sync(string name, string target, string description,
        JsonObject parameters, AssistantEffect effect, string recovery, Func<JsonNode> read,
        Func<JsonObject, JsonNode> execute, Func<string?>? unavailable = null) =>
        new(name, target, description, parameters, effect, recovery, read,
            (args, token) => { token.ThrowIfCancellationRequested(); return Task.FromResult(execute(args)); }, unavailable);
}

public static class AssistantJson
{
    public static JsonNode Node(object? value) => JsonSerializer.SerializeToNode(value)!;
    public static string Text(string key) => LocalizationManager.Instance?.Get("Assistant." + key) ?? "Assistant." + key;
    public static string String(this JsonObject value, string key) => value[key]?.GetValue<string>()
        ?? throw new ArgumentException(Text("InvalidParameters") + ": " + key);
    public static string OptionalString(this JsonObject value, string key, string fallback = "") => value[key]?.GetValue<string>() ?? fallback;
    public static int Integer(this JsonObject value, string key, int fallback = 0) => value[key]?.GetValue<int>() ?? fallback;
    public static double Number(this JsonObject value, string key, double fallback = 0) => value[key]?.Deserialize<double>() ?? fallback;
    public static bool Boolean(this JsonObject value, string key, bool fallback = false) => value[key]?.GetValue<bool>() ?? fallback;
    public static JsonObject Schema(params (string Name, JsonObject Schema, bool Required)[] fields) => new()
    {
        ["type"] = "object", ["additionalProperties"] = false,
        ["properties"] = new JsonObject(fields.Select(x => KeyValuePair.Create<string, JsonNode?>(x.Name, x.Schema))),
        ["required"] = new JsonArray(fields.Where(x => x.Required).Select(x => (JsonNode?)JsonValue.Create(x.Name)).ToArray()),
    };
    public static JsonObject Str(string description = "", params string[] values)
    {
        var result = new JsonObject { ["type"] = "string", ["maxLength"] = 32768, ["description"] = description };
        if (values.Length > 0) result["enum"] = Node(values);
        return result;
    }
    public static JsonObject Num(double minimum, double maximum, string unit = "", bool integer = false) => new()
        { ["type"] = integer ? "integer" : "number", ["minimum"] = minimum, ["maximum"] = maximum, ["description"] = unit };
    public static JsonObject Bool() => new() { ["type"] = "boolean" };
    public static JsonObject Array(JsonObject item, int maximum = 100) => new()
        { ["type"] = "array", ["items"] = item, ["minItems"] = 1, ["maxItems"] = maximum };
    public static string Revision(JsonNode state) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(state.ToJsonString())));

    // The locally enforced subset is also the only schema subset adapters may publish.
    public static void Validate(JsonNode? value, JsonObject schema, string path = "arguments", int depth = 0)
    {
        if (depth > 12 || value == null) throw new ArgumentException(Text("InvalidParameters") + ": " + path);
        var element = JsonSerializer.SerializeToElement(value);
        var type = schema["type"]!.GetValue<string>();
        var valid = type switch
        {
            "object" => value is JsonObject,
            "array" => value is JsonArray,
            "string" => element.ValueKind == JsonValueKind.String,
            "boolean" => element.ValueKind is JsonValueKind.True or JsonValueKind.False,
            "number" => element.ValueKind == JsonValueKind.Number && element.TryGetDouble(out var n) && double.IsFinite(n),
            "integer" => element.ValueKind == JsonValueKind.Number && element.TryGetInt32(out _),
            _ => false,
        };
        if (!valid) throw new ArgumentException(Text("InvalidParameters") + ": " + path);
        if (schema["enum"] is JsonArray options && !options.Any(option => JsonNode.DeepEquals(option, value)))
            throw new ArgumentException(Text("InvalidParameters") + ": " + path);
        if (type == "object")
        {
            var obj = (JsonObject)value;
            var properties = (JsonObject)schema["properties"]!;
            foreach (var required in (JsonArray)schema["required"]!)
                if (!obj.ContainsKey(required!.GetValue<string>())) throw new ArgumentException(Text("InvalidParameters") + ": " + required);
            foreach (var pair in obj)
            {
                if (properties[pair.Key] is not JsonObject child) throw new ArgumentException(Text("InvalidParameters") + ": " + pair.Key);
                Validate(pair.Value, child, path + "." + pair.Key, depth + 1);
            }
        }
        else if (type == "array")
        {
            var array = (JsonArray)value;
            if (array.Count < (schema["minItems"]?.GetValue<int>() ?? 0) || array.Count > (schema["maxItems"]?.GetValue<int>() ?? 100))
                throw new ArgumentException(Text("InvalidParameters") + ": " + path);
            foreach (var item in array) Validate(item, (JsonObject)schema["items"]!, path, depth + 1);
        }
        else if (type is "number" or "integer")
        {
            var number = element.GetDouble();
            if (number < (schema["minimum"]?.GetValue<double>() ?? double.MinValue) || number > (schema["maximum"]?.GetValue<double>() ?? double.MaxValue))
                throw new ArgumentException(Text("InvalidParameters") + ": " + path);
        }
        else if (type == "string" && element.GetString()!.Length > (schema["maxLength"]?.GetValue<int>() ?? 32768))
            throw new ArgumentException(Text("InvalidParameters") + ": " + path);
    }
}
