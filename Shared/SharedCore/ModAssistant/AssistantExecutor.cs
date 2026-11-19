using System.Text.Json.Nodes;

namespace Shared.Core.ModAssistant;

public sealed class AssistantExecutor(IEnumerable<IAssistantOperationProvider> providers)
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Dictionary<string, (string Fingerprint, JsonObject Result)> _completed = [];
    private readonly Dictionary<string, JsonObject> _mutations = [];
    private int _executions;

    public void BeginTask()
    {
        if (_gate.CurrentCount == 0) throw new InvalidOperationException(AssistantJson.Text("Busy"));
        _completed.Clear();
        _mutations.Clear();
        _executions = 0;
    }

    public IReadOnlyList<AssistantOperation> Operations() => providers.SelectMany(provider => provider.GetOperations()).ToArray();

    public JsonObject Inspect(string target)
    {
        var operations = Operations().Where(x => x.TargetId == target).ToArray();
        if (operations.Length == 0) throw new InvalidOperationException(AssistantJson.Text("TargetGone"));
        var state = operations[0].ReadState();
        return new JsonObject
        {
            ["target"] = target, ["revision"] = AssistantJson.Revision(state), ["state"] = state,
            ["operations"] = new JsonArray(operations.Select(x => (JsonNode)x.Describe()).ToArray()),
        };
    }

    public async Task<JsonObject> ExecuteAsync(string requestId, string name, string target, string revision,
        JsonObject args, Func<AssistantOperation, JsonObject, CancellationToken, Task<bool>> authorize,
        CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var fingerprint = AssistantJson.Revision(new JsonObject
                { ["name"] = name, ["target"] = target, ["revision"] = revision, ["arguments"] = args.DeepClone() });
            if (_completed.TryGetValue(requestId, out var completed))
            {
                if (completed.Fingerprint != fingerprint) return Error("DuplicateConflict");
                return (JsonObject)completed.Result.DeepClone();
            }
            if (++_executions > 48) return Error("CallLimit");
            var operation = Operations().SingleOrDefault(x => x.Name == name && x.TargetId == target);
            if (operation == null) return Error("TargetGone");
            AssistantJson.Validate(args, operation.Parameters);
            var reason = operation.Unavailable?.Invoke();
            if (reason != null) return new JsonObject { ["status"] = "unavailable", ["error"] = reason };
            if (operation.Effect != AssistantEffect.Query && _mutations.TryGetValue(fingerprint, out var duplicate))
                return (JsonObject)duplicate.DeepClone();
            var before = operation.ReadState();
            if (operation.Effect != AssistantEffect.Query && revision != AssistantJson.Revision(before)) return Error("StaleTarget");
            if (!await authorize(operation, args, cancellationToken)) return Error("NotAuthorized");
            cancellationToken.ThrowIfCancellationRequested();
            // A dialog or an asynchronous authorization may have yielded to user edits.
            if (operation.Effect != AssistantEffect.Query && !JsonNode.DeepEquals(before, operation.ReadState())) return Error("StaleTarget");
            JsonObject result;
            try
            {
                using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                deadline.CancelAfter(TimeSpan.FromMinutes(5));
                var details = await operation.Execute(args, deadline.Token);
                var after = ReadAfter(operation);
                var outcome = details is JsonObject detailObject ? detailObject["outcome"]?.GetValue<string>() : null;
                result = new JsonObject
                {
                    ["status"] = outcome is "partial" or "failed" or "cancelled" ? outcome : after["state_unavailable"] != null ? "executed_unverified" : "completed", ["operation"] = name, ["target"] = target,
                    ["description"] = operation.Description, ["effect"] = operation.Effect.ToString(), ["arguments"] = args.DeepClone(),
                    ["result"] = details, ["before"] = before, ["after"] = after,
                    ["revision"] = AssistantJson.Revision(after), ["recovery"] = operation.Recovery,
                };
            }
            catch (OperationCanceledException)
            {
                result = new JsonObject { ["status"] = "cancelled", ["operation"] = name, ["target"] = target, ["before"] = before, ["after"] = ReadAfter(operation), ["error"] = AssistantJson.Text("CancelledCheckState") };
            }
            catch (Exception exception)
            {
                result = new JsonObject { ["status"] = "failed", ["target"] = target, ["before"] = before,
                    ["description"] = operation.Description, ["effect"] = operation.Effect.ToString(), ["arguments"] = args.DeepClone(),
                    ["operation"] = name, ["after"] = ReadAfter(operation), ["error"] = exception.Message, ["recovery"] = operation.Recovery };
            }
            // Failed writes are remembered too: a network retry must never repeat a partial write.
            _completed[requestId] = (fingerprint, (JsonObject)result.DeepClone());
            if (operation.Effect != AssistantEffect.Query) _mutations[fingerprint] = (JsonObject)result.DeepClone();
            return result;
        }
        catch (ArgumentException exception) { return new JsonObject { ["status"] = "invalid_arguments", ["error"] = exception.Message }; }
        finally { _gate.Release(); }
    }

    private static JsonObject Error(string key) => new() { ["status"] = "rejected", ["error"] = AssistantJson.Text(key) };
    private static JsonNode ReadAfter(AssistantOperation operation)
    {
        try { return operation.ReadState(); }
        catch (Exception error) { return new JsonObject { ["state_unavailable"] = true, ["error"] = error.Message }; }
    }
}
