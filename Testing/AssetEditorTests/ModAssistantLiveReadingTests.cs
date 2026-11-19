using System.Net.Http;
using System.Text;
using System.Text.Json.Nodes;
using AssetEditor.ModAssistant;
using Shared.Core.ModAssistant;
using Shared.Core.Services;

namespace AssetEditorTests;

[TestClass, DoNotParallelize]
public class ModAssistantLiveReadingTests
{
    // Opt-in only. Sends synthetic context; never opens a real project or executes mutations.
    [TestMethod]
    public async Task LiveCapabilityReplyUsesReadableProseWithoutStartingAnAudit()
    {
        if (Environment.GetEnvironmentVariable("AECN_ASSISTANT_LIVE_READING") != "1")
            Assert.Inconclusive("Explicit live API opt-in is required.");
        new LocalizationManager().LoadLanguage();
        var secrets = new AssistantSecretStore();
        Assert.IsTrue(secrets.HasKey, "Configure the key in the application, never in test files.");
        using var http = new HttpClient();
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(90));
        var workspace = new SyntheticWorkspace();
        var sink = new Sink();
        var client = new BoundedClient(new DeepSeekClient(http, secrets));
        await new AssistantConversation(client, workspace).RunAsync("你能干什么？", true, false, null, sink, cancellation.Token);
        var reply = sink.Text.ToString();
        var output = Environment.GetEnvironmentVariable("ASSETEDITOR_ASSISTANT_RENDER_OUTPUT");
        if (!string.IsNullOrWhiteSpace(output))
        {
            Directory.CreateDirectory(output);
            await File.WriteAllTextAsync(Path.Combine(output, "live-readability.txt"), reply);
            await File.WriteAllTextAsync(Path.Combine(output, "live-usage.json"), sink.Usage.ToJsonString());
        }
        Assert.IsTrue(reply.Length is > 20 and < 1500, $"Unexpected capability reply length: {reply.Length}");
        Assert.IsFalse(reply.Contains("bound_editor", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(reply.Contains("revision", StringComparison.OrdinalIgnoreCase));
        Assert.AreEqual(0, workspace.Executions, "A capability question must not start an operational audit.");
    }

    private sealed class BoundedClient(IAssistantModelClient inner) : IAssistantModelClient
    {
        private int _calls;
        public Task<AssistantModelReply> CompleteAsync(JsonArray messages, JsonArray tools, bool thinking, IProgress<AssistantStreamUpdate> progress, CancellationToken cancellationToken)
        {
            if (++_calls > 3) throw new InvalidOperationException("The live readability probe permits at most three requests.");
            return inner.CompleteAsync(messages, tools, thinking, progress, cancellationToken);
        }
    }
    private sealed class Sink : IProgress<AssistantStreamUpdate>
    {
        public StringBuilder Text { get; } = new();
        public JsonArray Usage { get; } = [];
        public void Report(AssistantStreamUpdate value)
        {
            if (value.Kind == "text") Text.Append(value.Text);
            if (value.Kind == "usage") Usage.Add(value.Data?.DeepClone());
        }
    }
    private sealed class SyntheticWorkspace : IAssistantWorkspace
    {
        public int Executions;
        public void BeginTask() { }
        public Task<JsonNode> GetContextAsync(CancellationToken token) => Task.FromResult(JsonNode.Parse("""
            {"workspace_target":"workspace","game":"Warhammer3","project":null,"bound_editor":null,"editors":[],"containers":[],"selection":[],
             "capabilities":["查找并打开资源","编辑模型部件与材质","播放和编辑当前支持的动画","编辑特效与音频","保存文档和按工程配置生成Pack"],
             "source":"isolated_test_context"}
            """)!);
        public Task<JsonNode> InspectAsync(string target, CancellationToken token) => Task.FromResult(JsonNode.Parse("""
            {"target":"workspace","revision":"isolated-test","state":{"project":null},"operations":[]}
            """)!);
        public Task<JsonNode> ExecuteAsync(string request, string operation, string target, string revision, JsonObject args, CancellationToken token)
        {
            Executions++;
            throw new InvalidOperationException("This isolated readability probe does not execute operations.");
        }
    }
}
