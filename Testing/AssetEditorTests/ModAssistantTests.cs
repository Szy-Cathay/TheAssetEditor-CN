using System.Text;
using System.Net;
using System.Net.Http;
using System.Text.Json.Nodes;
using AssetEditor.ModAssistant;
using Shared.Core.ModAssistant;
using static Shared.Core.ModAssistant.AssistantJson;

namespace AssetEditorTests;

[TestClass]
public class ModAssistantTests
{
    private sealed class Secret : IAssistantSecretStore
    {
        public bool HasKey => true;
        public string Read() => "synthetic-test-only";
        public void Save(string key) => throw new NotSupportedException();
        public void Clear() => throw new NotSupportedException();
    }
    private sealed class ErrorHandler(HttpStatusCode status) : HttpMessageHandler
    {
        public int Calls;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            Calls++;
            Assert.AreEqual("https://api.deepseek.com/chat/completions", request.RequestUri!.AbsoluteUri);
            return Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent("private-server-body synthetic-test-only") });
        }
    }
    [TestMethod]
    [DataRow(401)]
    [DataRow(402)]
    [DataRow(429)]
    [DataRow(500)]
    [DataRow(503)]
    public async Task Assistant_HttpFailuresDoNotRetryOrExposeKeysAndResponseBodies(int status)
    {
        using var handler = new ErrorHandler((HttpStatusCode)status); using var http = new HttpClient(handler);
        var error = await Assert.ThrowsExceptionAsync<InvalidOperationException>(() => new DeepSeekClient(http, new Secret()).CompleteAsync([], [], false, new ProgressSink(), default));
        Assert.AreEqual(1, handler.Calls);
        Assert.IsFalse(error.ToString().Contains("synthetic-test-only"));
        Assert.IsFalse(error.ToString().Contains("private-server-body"));
    }
    private sealed class Provider : IAssistantOperationProvider
    {
        public int Value = 10;
        public int Writes;
        public bool Fail;
        public IEnumerable<AssistantOperation> GetOperations()
        {
            yield return AssistantOperation.Sync("scale", "model", "scale", Schema(("factor", Num(0.1, 2), true)), AssistantEffect.Edit,
                "undo", () => Node(new { Value }), args =>
                {
                    Writes++;
                    Value = (int)(Value * args.Number("factor"));
                    if (Fail) throw new InvalidOperationException("partial");
                    return Node(new { Value });
                });
        }
    }
    private static Task<bool> Allow(AssistantOperation op, JsonObject args, CancellationToken token) => Task.FromResult(true);
    private static string Revision(AssistantExecutor executor) => executor.Inspect("model")["revision"]!.GetValue<string>();

    [TestMethod]
    public async Task Executor_BindsRevisionAndSuppressesRepeatedRelativeMutation()
    {
        var provider = new Provider();
        var executor = new AssistantExecutor([provider]);
        executor.BeginTask();
        var revision = Revision(executor);
        var args = new JsonObject { ["factor"] = .8 };
        var first = await executor.ExecuteAsync("one", "scale", "model", revision, args, Allow, default);
        var retry = await executor.ExecuteAsync("two", "scale", "model", revision, args, Allow, default);
        Assert.AreEqual("completed", first["status"]!.GetValue<string>());
        Assert.IsTrue(JsonNode.DeepEquals(first, retry));
        Assert.AreEqual(8, provider.Value);
        Assert.AreEqual(1, provider.Writes);
        var stale = await executor.ExecuteAsync("three", "scale", "model", revision, new JsonObject { ["factor"] = .5 }, Allow, default);
        Assert.AreEqual("rejected", stale["status"]!.GetValue<string>());
        Assert.AreEqual(1, provider.Writes);
    }

    [TestMethod]
    public async Task Executor_RevalidatesAfterAuthorizationAndRejectsInvalidTypes()
    {
        var provider = new Provider();
        var executor = new AssistantExecutor([provider]);
        var invalid = await executor.ExecuteAsync("bad", "scale", "model", Revision(executor), new JsonObject { ["factor"] = "1" }, Allow, default);
        Assert.AreEqual("invalid_arguments", invalid["status"]!.GetValue<string>());
        var result = await executor.ExecuteAsync("changed", "scale", "model", Revision(executor), new JsonObject { ["factor"] = .5 },
            (_, _, _) => { provider.Value = 99; return Task.FromResult(true); }, default);
        Assert.AreEqual("rejected", result["status"]!.GetValue<string>());
        Assert.AreEqual(0, provider.Writes);
    }

    [TestMethod]
    public async Task Executor_RecordsPartialFailureAndDoesNotRepeatIt()
    {
        var provider = new Provider { Fail = true };
        var executor = new AssistantExecutor([provider]);
        var revision = Revision(executor);
        var args = new JsonObject { ["factor"] = .5 };
        var result = await executor.ExecuteAsync("one", "scale", "model", revision, args, Allow, default);
        Assert.AreEqual("failed", result["status"]!.GetValue<string>());
        Assert.AreEqual(5, result["after"]!["Value"]!.GetValue<int>());
        await executor.ExecuteAsync("two", "scale", "model", revision, args, Allow, default);
        Assert.AreEqual(1, provider.Writes);
    }

    [TestMethod]
    public async Task Executor_CancellationBeforeExecutionDoesNotWrite()
    {
        var provider = new Provider();
        var executor = new AssistantExecutor([provider]);
        using var cancel = new CancellationTokenSource();
        cancel.Cancel();
        await Assert.ThrowsExceptionAsync<TaskCanceledException>(() => executor.ExecuteAsync("one", "scale", "model", Revision(executor), new JsonObject { ["factor"] = .5 }, Allow, cancel.Token));
        Assert.AreEqual(0, provider.Writes);
    }

    [TestMethod]
    public void GuardedHistory_RestoresFailedBatchAndRefusesLaterUserChanges()
    {
        byte[] data = [1, 2];
        var journal = new AssistantUndoJournal(() => data.ToArray(), bytes => data = bytes.ToArray());
        journal.Apply(() => data[0] = 3);
        journal.Undo(); CollectionAssert.AreEqual(new byte[] { 1, 2 }, data);
        journal.Redo(); CollectionAssert.AreEqual(new byte[] { 3, 2 }, data);
        Assert.ThrowsException<InvalidOperationException>(() => journal.Apply(() => { data[1] = 9; throw new InvalidOperationException(); }));
        CollectionAssert.AreEqual(new byte[] { 3, 2 }, data);
        data[1] = 7;
        Assert.IsFalse(journal.CanUndo);
        Assert.ThrowsException<InvalidOperationException>(journal.Undo);
        CollectionAssert.AreEqual(new byte[] { 3, 7 }, data);
    }

    [TestMethod]
    public void KeyStore_EncryptsRoundTripsAndRejectsCorruption()
    {
        var root = Path.Combine(Path.GetTempPath(), "AECN-assistant-key-test-" + Guid.NewGuid().ToString("N"));
        var path = Path.Combine(root, "key.dpapi");
        var store = new AssistantSecretStore(path);
        const string synthetic = "synthetic-test-key-not-a-real-credential";
        try
        {
            Assert.IsFalse(store.HasKey);
            store.Save(synthetic);
            Assert.AreEqual(synthetic, store.Read());
            Assert.IsFalse(Encoding.UTF8.GetString(File.ReadAllBytes(path)).Contains(synthetic));
            File.WriteAllBytes(path, [1, 2, 3]);
            Assert.ThrowsException<System.ComponentModel.Win32Exception>(() => store.Read());
            store.Clear(); Assert.IsFalse(File.Exists(path));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    private sealed class ProgressSink : IProgress<AssistantStreamUpdate>
    {
        public List<AssistantStreamUpdate> Items { get; } = [];
        public void Report(AssistantStreamUpdate value) => Items.Add(value);
    }
    [TestMethod]
    public async Task Stream_CollectsFragmentedArgumentsReasoningCitationsAndActualUsage()
    {
        var events = new[]
        {
            """{"choices":[{"delta":{"reasoning_content":"reason","tool_calls":[{"index":0,"id":"call1","type":"function","function":{"name":"aec_execute","arguments":"{\"target\":"}}]}}]}""",
            """{"choices":[{"delta":{"content":"答复","tool_calls":[{"index":0,"function":{"arguments":"\"model\"}"}}],"annotations":[{"url_citation":{"url":"https://example.com/source","title":"source"}}],"server_tool_calls":[{"id":"web1","signature":"signed-value"}]},"finish_reason":"tool_calls"}],"usage":{"prompt_tokens":12,"completion_tokens":8,"server_tool_use":{"web_search_requests":1}}}""",
            "[DONE]",
        };
        var sink = new ProgressSink();
        var result = await DeepSeekClient.ReadStreamAsync(new StringReader(string.Join("\n", events.Select(e => "data: " + e.Replace("\"choices\":[{", "\"choices\":[{\"index\":0,")))), sink, default);
        Assert.AreEqual("{\"target\":\"model\"}", result.Message["tool_calls"]![0]!["function"]!["arguments"]!.GetValue<string>());
        Assert.AreEqual("reason", result.Message["reasoning_content"]!.GetValue<string>());
        Assert.AreEqual("reason", sink.Items.Single(i => i.Kind == "reasoning").Text);
        Assert.AreEqual("signed-value", result.Message["server_tool_calls"]![0]!["signature"]!.GetValue<string>());
        Assert.AreEqual(12, result.Usage!["prompt_tokens"]!.GetValue<int>());
        Assert.IsTrue(sink.Items.Any(i => i.Kind == "text" && i.Text == "答复"));
    }
    [TestMethod]
    public async Task Stream_IncompleteResponseNeverReturnsExecutableCalls()
    {
        await Assert.ThrowsExceptionAsync<IOException>(() => DeepSeekClient.ReadStreamAsync(new StringReader("data: {\"choices\":[{\"delta\":{\"content\":\"unfinished\"}}]}"), new ProgressSink(), default));
    }
}
