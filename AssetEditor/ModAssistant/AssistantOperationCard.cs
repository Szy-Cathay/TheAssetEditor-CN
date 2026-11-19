using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Shared.Core.ModAssistant;

namespace AssetEditor.ModAssistant;

public sealed record AssistantValueChange(string Field, string Before, string After);

public partial class AssistantOperationCard : ObservableObject
{
    public string Target { get; private set; } = "";
    public string Operation { get; private set; } = "";
    public string Revision { get; private set; } = "";
    [ObservableProperty] private string _title = "";
    [ObservableProperty] private string _status = "";
    [ObservableProperty] private string _detail = "";
    [ObservableProperty] private string _targetLabel = "";
    [ObservableProperty] private string _impact = "";
    [ObservableProperty] private string _error = "";
    [ObservableProperty] private bool _isRunning;
    [ObservableProperty] private bool _canEdit;
    [ObservableProperty] private bool _canLocate;
    public ObservableCollection<AssistantValueChange> Changes { get; } = [];
    public IAsyncRelayCommand LocateCommand { get; }
    public IAsyncRelayCommand UndoCommand { get; }
    public IAsyncRelayCommand RedoCommand { get; }
    private JsonObject? _arguments;

    public AssistantOperationCard(JsonNode result, Func<AssistantOperationCard, string, Task> act) : this(act) => Complete(result);

    private AssistantOperationCard(Func<AssistantOperationCard, string, Task> act)
    {
        LocateCommand = new AsyncRelayCommand(() => act(this, "locate"), () => CanLocate);
        UndoCommand = new AsyncRelayCommand(() => act(this, "undo"), () => CanEdit);
        RedoCommand = new AsyncRelayCommand(() => act(this, "redo"), () => CanEdit);
    }

    public static AssistantOperationCard Start(string function, JsonObject args, string? description, Func<AssistantOperationCard, string, Task> act)
    {
        return new AssistantOperationCard(act)
        {
            Operation = args["operation"]?.GetValue<string>() ?? function,
            Target = args["target"]?.GetValue<string>() ?? "",
            TargetLabel = args["target"]?.GetValue<string>() ?? "",
            Title = description ?? AssistantJson.Text(function switch { "aec_context" => "ReadingContext", "aec_inspect" => "InspectingTarget", _ => "Executing" }),
            Status = AssistantJson.Text("Executing"),
            IsRunning = true,
            _arguments = (JsonObject)args.DeepClone(),
            Detail = args.ToJsonString(new() { WriteIndented = true }),
        };
    }

    public void Complete(JsonNode result)
    {
        IsRunning = false;
        Target = result["target"]?.GetValue<string>() ?? Target;
        Operation = result["operation"]?.GetValue<string>() ?? Operation;
        Revision = result["revision"]?.GetValue<string>() ?? "";
        Title = result["description"]?.GetValue<string>() ?? (Title.Length > 0 ? Title : AssistantJson.Text("OperationResults"));
        var status = result["status"]?.GetValue<string>() ?? "completed";
        Status = AssistantJson.Text(status switch { "completed" => "OperationCompleted", "cancelled" => "Cancelled", "partial" => "Partial", _ => "Failed" });
        Error = result["error"]?.ToString() ?? "";
        var details = new JsonObject { ["arguments"] = _arguments?.DeepClone(), ["result"] = result.DeepClone() };
        Detail = details.ToJsonString(new() { WriteIndented = true });
        var state = result["after"] ?? result["state"] ?? result["before"];
        TargetLabel = state?["editor"]?["name"]?.GetValue<string>() ?? state?["document"]?["Name"]?.GetValue<string>() ??
            state?["path"]?.GetValue<string>() ?? state?["project"]?["name"]?.GetValue<string>() ?? Target;
        Impact = AssistantJson.Text(result["effect"]?.GetValue<string>() switch { "Edit" => "MemoryChange", "Preview" => "PreviewChange", "Disk" => "DiskChange", _ => "ReadResult" });
        CanEdit = result["effect"]?.GetValue<string>() == "Edit" && status == "completed";
        CanLocate = Target.Length > 0;
        LocateCommand.NotifyCanExecuteChanged(); UndoCommand.NotifyCanExecuteChanged(); RedoCommand.NotifyCanExecuteChanged();
        Changes.Clear();
        AddChanges(result["before"], result["after"], "", 0);
    }

    private void AddChanges(JsonNode? before, JsonNode? after, string path, int depth)
    {
        if (Changes.Count >= 8 || depth > 4 || JsonNode.DeepEquals(before, after)) return;
        if (before is JsonObject left && after is JsonObject right)
        {
            foreach (var pair in left.Where(p => right.ContainsKey(p.Key)))
                AddChanges(pair.Value, right[pair.Key], path.Length == 0 ? pair.Key : path + "." + pair.Key, depth + 1);
        }
        else if (before is JsonValue && after is JsonValue)
        {
            static string Short(JsonNode value) => value.ToString() is { Length: > 160 } text ? text[..160] + "…" : value.ToString();
            Changes.Add(new AssistantValueChange(path, Short(before), Short(after)));
        }
    }
}
