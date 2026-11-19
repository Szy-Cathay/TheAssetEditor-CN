using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;

namespace Shared.Core.ModAssistant;

public interface IAssistantSecretStore
{
    bool HasKey { get; }
    string Read();
    void Save(string key);
    void Clear();
}

public sealed record AssistantStreamUpdate(string Kind, string Text, JsonNode? Data = null);
public sealed record AssistantModelReply(JsonObject Message, string FinishReason, JsonNode? Usage);

public interface IAssistantModelClient
{
    Task<AssistantModelReply> CompleteAsync(JsonArray messages, JsonArray tools, bool thinking,
        IProgress<AssistantStreamUpdate> progress, CancellationToken cancellationToken);
}

public sealed class DeepSeekClient(HttpClient http, IAssistantSecretStore secrets) : IAssistantModelClient
{
    public async Task<AssistantModelReply> CompleteAsync(JsonArray messages, JsonArray tools, bool thinking,
        IProgress<AssistantStreamUpdate> progress, CancellationToken cancellationToken)
    {
        var key = secrets.Read();
        if (string.IsNullOrWhiteSpace(key)) throw new InvalidOperationException(AssistantJson.Text("MissingKey"));
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromMinutes(4));
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.deepseek.com/chat/completions");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
        var body = new JsonObject
        {
            ["model"] = "deepseek-flash",
            ["messages"] = messages.DeepClone(),
            ["tools"] = tools.DeepClone(),
            ["thinking"] = new JsonObject { ["type"] = thinking ? "enabled" : "disabled" },
            ["stream"] = true,
            ["stream_options"] = new JsonObject { ["include_usage"] = true },
            ["max_tokens"] = 8192,
        };
        request.Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");
        try
        {
            using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                var code = (int)response.StatusCode;
                throw new InvalidOperationException(code is 400 or 401 or 402 or 403 or 408 or 413 or 422 or 429 or 500 or 503 or 504
                    ? AssistantJson.Text("Http" + code) : AssistantJson.Text("ServerError") + " (" + code + ")");
            }
            await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token).ConfigureAwait(false);
            using var reader = new StreamReader(stream, Encoding.UTF8);
            return await ReadStreamAsync(reader, progress, timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        { throw new TimeoutException(AssistantJson.Text("Timeout")); }
        catch (HttpRequestException)
        { throw new InvalidOperationException(AssistantJson.Text("NetworkError")); }
    }

    public static async Task<AssistantModelReply> ReadStreamAsync(TextReader reader,
        IProgress<AssistantStreamUpdate> progress, CancellationToken cancellationToken)
    {
        var content = new StringBuilder();
        var reasoning = new StringBuilder();
        var calls = new SortedDictionary<int, JsonObject>();
        var message = new JsonObject { ["role"] = "assistant" };
        JsonNode? usage = null;
        string? finish = null;
        var total = 0;
        var done = false;
        while (await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false) is { } line)
        {
            cancellationToken.ThrowIfCancellationRequested();
            total += line.Length;
            if (total > 4_000_000) throw new InvalidOperationException(AssistantJson.Text("ResponseTooLarge"));
            if (!line.StartsWith("data:", StringComparison.Ordinal)) continue;
            var data = line[5..].Trim();
            if (data == "[DONE]") { done = true; break; }
            if (data.Length == 0) continue;
            var chunk = JsonNode.Parse(data)!.AsObject();
            if (chunk["usage"] is { } counters) usage = counters.DeepClone();
            if (chunk["error"] != null) throw new InvalidOperationException(AssistantJson.Text("ServerError"));
            foreach (var choice in chunk["choices"]?.AsArray() ?? [])
            {
                if (choice?["index"]?.GetValue<int>() != 0) continue;
                if (choice["server_tool_event"] is { } serverEvent)
                    progress.Report(new("web", "", serverEvent.DeepClone()));
                if (choice["finish_reason"] is { } reason) finish = reason.GetValue<string>();
                if (choice["delta"] is not JsonObject delta) continue;
                if (delta["content"] is { } text)
                {
                    var part = text.GetValue<string>();
                    content.Append(part);
                    progress.Report(new("text", part));
                }
                if (delta["reasoning_content"] is { } thought)
                {
                    var part = thought.GetValue<string>();
                    reasoning.Append(part);
                    progress.Report(new("reasoning", part));
                }
                foreach (var field in new[] { "server_tool_calls", "annotations" })
                    if (delta[field] is { } value) message[field] = value.DeepClone();
                foreach (var call in delta["tool_calls"]?.AsArray() ?? [])
                {
                    var index = call!["index"]!.GetValue<int>();
                    if (index is < 0 or > 47) throw new InvalidOperationException(AssistantJson.Text("CallLimit"));
                    if (!calls.TryGetValue(index, out var assembled))
                    {
                        assembled = new JsonObject { ["type"] = "function", ["function"] = new JsonObject { ["name"] = "", ["arguments"] = "" } };
                        calls.Add(index, assembled);
                    }
                    if (call["id"] is { } id) assembled["id"] = id.DeepClone();
                    if (call["function"] is JsonObject function)
                        foreach (var field in new[] { "name", "arguments" })
                            if (function[field] is { } fragment)
                                assembled["function"]![field] = assembled["function"]![field]!.GetValue<string>() + fragment.GetValue<string>();
                }
            }
        }
        if (!done || finish == null) throw new IOException(AssistantJson.Text("IncompleteResponse"));
        message["content"] = content.ToString();
        if (reasoning.Length > 0) message["reasoning_content"] = reasoning.ToString();
        if (calls.Count > 0)
        {
            if (calls.Values.Any(x => x["id"] == null)) throw new IOException(AssistantJson.Text("IncompleteResponse"));
            message["tool_calls"] = new JsonArray(calls.Values.Select(x => (JsonNode)x).ToArray());
        }
        return new(message, finish, usage);
    }
}
