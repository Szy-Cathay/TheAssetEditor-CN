using System.Text.Json.Nodes;
using System.Windows.Threading;
using AssetEditor.ModAssistant;
using Shared.Core.ModAssistant;

namespace AssetEditorTests;

[TestClass, DoNotParallelize]
public class ModAssistantAgentUiTests
{
    private sealed class RecordingClient : IAssistantModelClient
    {
        public List<JsonArray> Requests { get; } = [];
        public bool WaitForCancellation { get; set; }
        public bool FailImmediately { get; set; }
        public TaskCompletionSource Started { get; } = new();
        public async Task<AssistantModelReply> CompleteAsync(JsonArray messages, JsonArray tools, bool thinking, IProgress<AssistantStreamUpdate> progress, CancellationToken cancellationToken)
        {
            Requests.Add((JsonArray)messages.DeepClone());
            if (FailImmediately) throw new InvalidOperationException("测试：未配置密钥");
            progress.Report(new AssistantStreamUpdate("reasoning", "检查当前工程和未保存状态。"));
            progress.Report(new AssistantStreamUpdate("text", "**已检查**：模型有未保存修改。"));
            Started.TrySetResult();
            if (WaitForCancellation) await Task.Delay(Timeout.Infinite, cancellationToken);
            return new AssistantModelReply(new JsonObject { ["role"] = "assistant", ["content"] = "**已检查**：模型有未保存修改。" }, "stop", new JsonObject { ["prompt_tokens"] = 10, ["completion_tokens"] = 5 });
        }
    }

    [TestMethod]
    public void Assistant_SessionSwitchPreservesDraftsAndIsolatesModelHistory()
    {
        WpfTestApplicationHost.InvokeWithThemeResources(WpfTestApplicationHost.EmptyServices, () =>
        {
            var client = new RecordingClient();
            using var vm = ModAssistantRenderTests.CreateViewModel(client);
            vm.Input = "检查第一件武器";
            Pump(vm.SendCommand.ExecuteAsync(null));
            var first = vm.CurrentSession;
            Assert.AreEqual(2, vm.Messages.Count);
            Assert.IsInstanceOfType(vm.Messages[1].Parts[0], typeof(AssistantReasoningPart));
            Assert.IsInstanceOfType(vm.Messages[1].Parts[1], typeof(AssistantTextPart));
            Assert.IsTrue(vm.Messages[1].TargetSummary.Contains("1 个编辑器有未保存修改"));
            Assert.IsTrue(vm.Messages[1].Usage.Contains("prompt_tokens"));
            vm.Input = "尚未发送的草稿";
            vm.NewConversationCommand.Execute(null);
            Assert.AreEqual(2, vm.Sessions.Count);
            Assert.AreEqual("", vm.Input);
            Assert.AreEqual(0, vm.Messages.Count);
            vm.Input = "检查第二件武器";
            Pump(vm.SendCommand.ExecuteAsync(null));
            var second = vm.CurrentSession;
            Assert.AreEqual(1, client.Requests[1].Count(m => m?["role"]?.GetValue<string>() == "user"));
            vm.Input = "第二个草稿";
            vm.CurrentSession = first;
            Assert.AreEqual("尚未发送的草稿", vm.Input);
            Assert.AreEqual(2, vm.Messages.Count);
            vm.Input = "继续第一件武器";
            Pump(vm.SendCommand.ExecuteAsync(null));
            var request = client.Requests[2].ToJsonString();
            Assert.IsFalse(request.Contains("检查第二件武器"));
            Assert.AreEqual(2, client.Requests[2].Count(m => m?["role"]?.GetValue<string>() == "user"));
            vm.CurrentSession = second;
            Assert.AreEqual("第二个草稿", vm.Input);
        });
    }

    [TestMethod]
    public void Assistant_StopKeepsPartialAnswerAndRejectsSessionSwitchWhileRunning()
    {
        WpfTestApplicationHost.InvokeWithThemeResources(WpfTestApplicationHost.EmptyServices, () =>
        {
            var client = new RecordingClient();
            using var vm = ModAssistantRenderTests.CreateViewModel(client);
            vm.Input = "第一轮"; Pump(vm.SendCommand.ExecuteAsync(null));
            var first = vm.CurrentSession;
            vm.NewConversationCommand.Execute(null);
            var second = vm.CurrentSession;
            client.WaitForCancellation = true;
            vm.Input = "执行较长任务";
            var task = vm.SendCommand.ExecuteAsync(null);
            Assert.IsTrue(vm.IsBusy);
            Assert.IsFalse(vm.NewConversationCommand.CanExecute(null));
            Assert.IsFalse(vm.SendCommand.CanExecute(null));
            vm.CurrentSession = first;
            Assert.AreSame(second, vm.CurrentSession);
            vm.StopCommand.Execute(null);
            Pump(task);
            Assert.IsFalse(vm.IsBusy);
            Assert.IsFalse(vm.StopCommand.CanExecute(null));
            Assert.IsTrue(vm.Messages.Last().Text.Contains("已检查"));
            Assert.IsFalse(string.IsNullOrWhiteSpace(vm.Messages.Last().Outcome));
            Assert.IsTrue(vm.Messages.Last().TargetSummary.Contains("未保存"));
            Assert.IsTrue(vm.NewConversationCommand.CanExecute(null));
        });
    }

    [TestMethod]
    public void Assistant_ImmediateFailureCannotBeOverwrittenByQueuedCheckingProgress()
    {
        WpfTestApplicationHost.InvokeWithThemeResources(WpfTestApplicationHost.EmptyServices, () =>
        {
            using var vm = ModAssistantRenderTests.CreateViewModel(new RecordingClient { FailImmediately = true });
            vm.Input = "检查当前工程";
            Pump(vm.SendCommand.ExecuteAsync(null));
            Assert.IsFalse(vm.IsBusy);
            Assert.IsTrue(vm.Status.Contains("测试：未配置密钥"));
            Assert.AreEqual(vm.Status, vm.Messages.Last().Outcome);
            Assert.AreEqual(vm.Status, vm.Announcement);
        });
    }

    [TestMethod]
    public void Assistant_ToolTimelineRetainsOrderRealDeltasAndFailures()
    {
        var message = new AssistantMessage();
        message.AppendText("开始检查。");
        var card = AssistantOperationCard.Start("aec_execute", new JsonObject { ["target"] = "mesh-1", ["operation"] = "kitbash.transform" }, "缩放刀刃", (_, _) => Task.CompletedTask);
        message.AddOperation(card);
        Assert.IsTrue(card.IsRunning);
        Assert.IsFalse(card.UndoCommand.CanExecute(null));
        card.Complete(JsonNode.Parse("""
            {"status":"completed","effect":"Edit","revision":"r2","before":{"scale":1,"material":"unchanged"},"after":{"scale":0.8,"material":"unchanged"}}
            """)!);
        message.AppendText("已完成缩放。");
        Assert.AreEqual(3, message.Parts.Count);
        Assert.AreSame(card, message.Parts[1]);
        Assert.AreEqual(1, card.Changes.Count);
        Assert.AreEqual("1", card.Changes[0].Before);
        Assert.AreEqual("0.8", card.Changes[0].After);
        Assert.AreEqual("mesh-1", card.Target);
        Assert.IsTrue(card.CanEdit);
        var failed = AssistantOperationCard.Start("aec_execute", new JsonObject { ["target"] = "mesh-2" }, "替换贴图", (_, _) => Task.CompletedTask);
        failed.Complete(JsonNode.Parse("""{"status":"failed","error":"目标在等待期间被修改"}""")!);
        Assert.AreEqual("目标在等待期间被修改", failed.Error);
        Assert.IsFalse(failed.IsRunning);
        Assert.IsFalse(failed.CanEdit);
        Assert.AreEqual("mesh-2", failed.Target);
    }

    private static void Pump(Task task)
    {
        if (!task.IsCompleted)
        {
            var frame = new DispatcherFrame();
            var timeout = new DispatcherTimer(TimeSpan.FromSeconds(10), DispatcherPriority.Send, (_, _) => frame.Continue = false, Dispatcher.CurrentDispatcher);
            var dispatcher = Dispatcher.CurrentDispatcher;
            _ = task.ContinueWith(_ => dispatcher.BeginInvoke(() => frame.Continue = false), TaskScheduler.Default);
            Dispatcher.PushFrame(frame);
            timeout.Stop();
        }
        Assert.IsTrue(task.IsCompleted, "UI operation did not settle within 10 seconds.");
        task.GetAwaiter().GetResult();
    }
}
