using System.Text.Json.Nodes;
using Shared.Core.ModAssistant;
using static Shared.Core.ModAssistant.AssistantJson;

namespace AssetEditorTests;

[TestClass]
public class ModAssistantConversationTests
{
    private sealed class Workspace : IAssistantWorkspace
    {
        public List<string> Executed { get; } = [];
        public bool CancelAfterFirst;
        public CancellationTokenSource? Cancellation;
        public void BeginTask() { }
        public Task<JsonNode> GetContextAsync(CancellationToken token) => Task.FromResult(Node(new { project = "p", dirty = Executed.Count > 0 }));
        public Task<JsonNode> InspectAsync(string target, CancellationToken token) => Task.FromResult(Node(new { target, revision = "rev" }));
        public Task<JsonNode> ExecuteAsync(string request, string operation, string target, string revision, JsonObject args, CancellationToken token)
        {
            Executed.Add(operation);
            if (CancelAfterFirst) Cancellation!.Cancel();
            return Task.FromResult(Node(new { status = "completed", target, operation, after = new { dirty = true } }));
        }
    }
    private sealed class Model(Func<int, JsonArray, AssistantModelReply> respond) : IAssistantModelClient
    {
        public int Calls;
        public List<JsonArray> Requests { get; } = [];
        public Task<AssistantModelReply> CompleteAsync(JsonArray messages, JsonArray tools, bool thinking, IProgress<AssistantStreamUpdate> progress, CancellationToken cancellationToken)
        {
            Requests.Add((JsonArray)messages.DeepClone());
            Assert.IsTrue(tools.Any(t => t!["type"]!.GetValue<string>() == "function"));
            return Task.FromResult(respond(Calls++, messages));
        }
    }
    private sealed class Sink : IProgress<AssistantStreamUpdate>
    {
        public List<AssistantStreamUpdate> Items { get; } = [];
        public void Report(AssistantStreamUpdate value) => Items.Add(value);
    }
    private static AssistantModelReply Calls(params (string Id, string Operation)[] calls) => new(new JsonObject
    {
        ["role"] = "assistant", ["content"] = null, ["reasoning_content"] = "retained-thinking",
        ["server_tool_calls"] = new JsonArray(new JsonObject { ["id"] = "web", ["signature"] = "retained-signature" }),
        ["tool_calls"] = new JsonArray(calls.Select(c => (JsonNode)new JsonObject { ["id"] = c.Id, ["type"] = "function",
            ["function"] = new JsonObject { ["name"] = "aec_execute", ["arguments"] = new JsonObject { ["target"] = "doc", ["operation"] = c.Operation,
                ["revision"] = "rev", ["arguments"] = new JsonObject() }.ToJsonString() } }).ToArray())
    }, "tool_calls", null);
    private static AssistantModelReply Done() => new(new JsonObject { ["role"] = "assistant", ["content"] = "已根据工具结果完成。" }, "stop", new JsonObject { ["prompt_tokens"] = 20, ["completion_tokens"] = 10 });

    [TestMethod]
    public async Task Assistant_ExplicitScreenshotDoesNotConsumeTheTextHistoryLimit()
    {
        var model = new Model((_, _) => Done());
        var image = "data:image/png;base64," + new string('A', 1_500_000);
        await new AssistantConversation(model, new Workspace()).RunAsync("说明截图", true, false, image, new Sink(), default);
        Assert.AreEqual(1, model.Calls);
        Assert.AreEqual(image, model.Requests[0].Single(m => m!["role"]!.GetValue<string>() == "user")!["content"]![1]!["image_url"]!["url"]!.GetValue<string>());
    }

    [TestMethod]
    public async Task Assistant_ConversationExecutesMultipleRoundsAndReturnsEveryToolResultWithThinkingSignatures()
    {
        var workspace = new Workspace(); var sink = new Sink();
        var model = new Model((round, messages) => round switch { 0 => Calls(("1", "edit"), ("2", "preview")), 1 => Calls(("3", "save")), _ => Done() });
        await new AssistantConversation(model, workspace).RunAsync("修改、预览并保存", true, true, null, sink, default);
        CollectionAssert.AreEqual(new[] { "edit", "preview", "save" }, workspace.Executed);
        var secondRequest = model.Requests[1];
        Assert.AreEqual(2, secondRequest.Count(m => m!["role"]!.GetValue<string>() == "tool"));
        var assistant = secondRequest.Last(m => m!["role"]!.GetValue<string>() == "assistant")!;
        Assert.AreEqual("retained-thinking", assistant["reasoning_content"]!.GetValue<string>());
        Assert.AreEqual("retained-signature", assistant["server_tool_calls"]![0]!["signature"]!.GetValue<string>());
        Assert.AreEqual(3, sink.Items.Count(i => i.Kind == "result"));
        Assert.IsTrue(sink.Items.Any(i => i.Kind == "final_state"));
    }
    [TestMethod]
    public async Task Assistant_CancelledBatchCompletesProtocolWithoutRunningRemainingCallsAndCanContinue()
    {
        using var cancellation = new CancellationTokenSource();
        var workspace = new Workspace { CancelAfterFirst = true, Cancellation = cancellation };
        var model = new Model((round, _) => round == 0 ? Calls(("1", "edit"), ("2", "save")) : Done());
        var conversation = new AssistantConversation(model, workspace);
        await Assert.ThrowsExceptionAsync<OperationCanceledException>(() => conversation.RunAsync("编辑", true, false, null, new Sink(), cancellation.Token));
        Assert.AreEqual(1, workspace.Executed.Count);
        workspace.CancelAfterFirst = false;
        await conversation.RunAsync("说明刚才的状态", true, false, null, new Sink(), default);
        var results = model.Requests[1].Where(m => m!["role"]!.GetValue<string>() == "tool").ToArray();
        Assert.AreEqual(2, results.Length);
        Assert.IsTrue(results[1]!["content"]!.GetValue<string>().Contains("cancelled"));
    }
    [TestMethod]
    public async Task Assistant_TruncatedToolReplyNeverExecutesAndDoesNotCorruptNextTurn()
    {
        var workspace = new Workspace();
        var model = new Model((round, _) => round == 0 ? Calls(("1", "edit")) with { FinishReason = "length" } : Done());
        var conversation = new AssistantConversation(model, workspace);
        await Assert.ThrowsExceptionAsync<InvalidOperationException>(() => conversation.RunAsync("编辑", false, false, null, new Sink(), default));
        await conversation.RunAsync("继续说明", false, false, null, new Sink(), default);
        Assert.AreEqual(0, workspace.Executed.Count);
        Assert.IsFalse(model.Requests[1].Any(m => m?["tool_calls"] != null));
    }
}
