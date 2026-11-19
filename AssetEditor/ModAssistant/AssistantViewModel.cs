using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using Shared.Core.ModAssistant;
using Shared.Core.ToolCreation;

namespace AssetEditor.ModAssistant;

public partial class AssistantMessage : ObservableObject
{
    public string Role { get; init; } = "";
    public bool IsUser { get; init; }
    public BitmapSource? Attachment { get; init; }
    public string AttachmentName { get; init; } = "";
    [ObservableProperty] private string _text = "";
    [ObservableProperty] private string _outcome = "";
    [ObservableProperty] private string _usage = "";
    [ObservableProperty] private string _targetSummary = "";
    public ObservableCollection<object> Parts { get; } = [];
    public ObservableCollection<AssistantOperationCard> Operations { get; } = [];
    public ObservableCollection<AssistantSource> Sources { get; } = [];
    public void AppendText(string text)
    {
        Text += text;
        if (Parts.LastOrDefault() is not AssistantTextPart part) { part = new AssistantTextPart(); Parts.Add(part); }
        part.Text += text;
    }
    public void AppendReasoning(string text)
    {
        if (Parts.LastOrDefault() is not AssistantReasoningPart part) { part = new AssistantReasoningPart(); Parts.Add(part); }
        part.Text += text;
    }
    public void AddOperation(AssistantOperationCard card) { Operations.Add(card); Parts.Add(card); }
}
public sealed record AssistantSource(string Title, string Url);

public partial class AssistantViewModel : ObservableObject, IEditorInterface, IDisposable
{
    private readonly IAssistantModelClient _client;
    private AssistantSession _currentSession;
    private readonly IAssistantWorkspace _workspace;
    private CancellationTokenSource? _cancellation;
    private string? _imageData;
    private bool _closed;
    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };
    public string DisplayName { get; set; } = AssistantJson.Text("Title");
    public ObservableCollection<AssistantMessage> Messages => CurrentSession.Messages;
    public ObservableCollection<AssistantSession> Sessions { get; } = [];
    public AssistantSession CurrentSession
    {
        get => _currentSession;
        set
        {
            if (IsBusy || value == null || value == _currentSession || !Sessions.Contains(value)) return;
            StoreDraft();
            SetProperty(ref _currentSession, value);
            OnPropertyChanged(nameof(Messages));
            Input = value.Draft; Attachment = value.Attachment; AttachmentName = value.AttachmentName; _imageData = value.ImageData;
            Usage = value.Usage; Status = value.Status;
            Notification = "";
            Panel = "conversation";
            if (IsCompact) HistoryOpen = false;
        }
    }
    [ObservableProperty][NotifyPropertyChangedFor(nameof(ShowHistory), nameof(ShowWorkspace))] private bool _historyOpen = true;
    [ObservableProperty][NotifyPropertyChangedFor(nameof(ShowHistory), nameof(ShowWorkspace))] private bool _isCompact;
    public bool ShowHistory => HistoryOpen;
    public bool ShowWorkspace => !IsCompact || !HistoryOpen;
    [ObservableProperty] private string _panel = "conversation";
    [ObservableProperty] private string _saveSummary = "";
    [ObservableProperty] private string _notification = "";
    public AssistantSettingsViewModel Settings { get; }
    [ObservableProperty][NotifyCanExecuteChangedFor(nameof(SendCommand))] private string _input = "";
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsIdle), nameof(Announcement))]
    [NotifyCanExecuteChangedFor(nameof(SendCommand), nameof(StopCommand), nameof(NewConversationCommand), nameof(RefreshContextCommand), nameof(AttachImageCommand), nameof(PasteImageCommand), nameof(RemoveAttachmentCommand), nameof(UseSuggestionCommand))]
    private bool _isBusy;
    public bool IsIdle => !IsBusy;
    [ObservableProperty] private bool _thinking = true;
    [ObservableProperty] private bool _webEnabled;
    [ObservableProperty][NotifyPropertyChangedFor(nameof(Announcement))] private string _status = AssistantJson.Text("Ready");
    public string Announcement => IsBusy ? "" : Status;
    [ObservableProperty] private string _context = "";
    [ObservableProperty] private string _targetSummary = "";
    [ObservableProperty] private string _usage = "";
    [ObservableProperty][NotifyCanExecuteChangedFor(nameof(RemoveAttachmentCommand))] private BitmapSource? _attachment;
    [ObservableProperty] private string _attachmentName = "";
    public AssistantViewModel(IAssistantModelClient client, IAssistantWorkspace workspace, AssistantSettingsViewModel settings)
    {
        _workspace = workspace; _client = client; Settings = settings;
        _currentSession = new AssistantSession(client, workspace);
        Sessions.Add(_currentSession);
    }
    private void StoreDraft()
    {
        CurrentSession.Draft = Input; CurrentSession.Attachment = Attachment; CurrentSession.AttachmentName = AttachmentName;
        CurrentSession.ImageData = _imageData; CurrentSession.Usage = Usage; CurrentSession.Status = Status;
    }
    [RelayCommand] private void ToggleHistory() => HistoryOpen = !HistoryOpen;
    [RelayCommand] private void ShowPanel(string panel) { Panel = panel; if (IsCompact) HistoryOpen = false; }
    [RelayCommand(CanExecute = nameof(IsIdle))] private void UseSuggestion(string suggestion) => Input = suggestion;
    [RelayCommand]
    private void CopyMessage(AssistantMessage message)
    {
        try { Clipboard.SetText(message.Text); }
        catch (Exception) { Notification = AssistantJson.Text("ClipboardUnavailable"); }
    }
    private bool CanSend() => !IsBusy && !string.IsNullOrWhiteSpace(Input) && !_closed;
    [RelayCommand(CanExecute = nameof(CanSend))]
    private async Task Send()
    {
        Notification = "";
        IsBusy = true;
        _cancellation = new CancellationTokenSource();
        var instruction = Input;
        Input = "";
        if (Messages.Count == 0) CurrentSession.Title = instruction.ReplaceLineEndings(" ") is { Length: > 36 } title ? title[..36] + "…" : instruction.ReplaceLineEndings(" ");
        Messages.Add(new AssistantMessage { IsUser = true, Role = AssistantJson.Text("You"), Text = instruction, Attachment = Attachment, AttachmentName = AttachmentName });
        var answer = new AssistantMessage { Role = DisplayName };
        AssistantOperationCard? pending = null;
        var descriptions = new System.Collections.Generic.Dictionary<string, string>();
        var actualUsage = new JsonObject();
        Usage = "";
        Messages.Add(answer);
        var progress = new Progress<AssistantStreamUpdate>(update =>
        {
            if (_closed) return;
            switch (update.Kind)
            {
                case "text": answer.AppendText(update.Text); break;
                case "reasoning": answer.AppendReasoning(update.Text); break;
                case "checking": Status = update.Text; break;
                case "executing":
                    Status = AssistantJson.Text("Executing");
                    if (update.Data is JsonObject args)
                    {
                        descriptions.TryGetValue(args["operation"]?.GetValue<string>() ?? "", out var description);
                        pending = AssistantOperationCard.Start(update.Text, args, description, ActOnCard);
                        answer.AddOperation(pending);
                    }
                    break;
                case "web": Status = AssistantJson.Text("Searching"); break;
                case "result":
                    if (update.Data is JsonObject result)
                    {
                        if (pending != null) { pending.Complete(result); pending = null; }
                        else answer.AddOperation(new AssistantOperationCard(result, ActOnCard));
                        foreach (var operation in result["operations"] as JsonArray ?? [])
                            if (operation?["name"]?.GetValue<string>() is { } name && operation["description"]?.GetValue<string>() is { } description)
                                descriptions[name] = description;
                    }
                    break;
                case "completed": Status = update.Text; break;
                case "final_state": if (update.Data != null) { SetContext(update.Data); answer.TargetSummary = TargetSummary + " · " + SaveSummary; } break;
                case "usage":
                    if (update.Data is JsonObject usage) AccumulateUsage(actualUsage, usage);
                    Usage = actualUsage.ToJsonString(Indented); answer.Usage = Usage; break;
                case "sources":
                    foreach (var item in update.Data?.AsArray() ?? [])
                    {
                        var citation = item?["url_citation"];
                        var url = citation?["url"]?.GetValue<string>();
                        if (Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme is "https" or "http")
                            answer.Sources.Add(new AssistantSource(citation?["title"]?.GetValue<string>() ?? url!, url!));
                    }
                    break;
            }
        });
        try
        {
            SetContext(await _workspace.GetContextAsync(_cancellation.Token));
            answer.TargetSummary = TargetSummary;
            await CurrentSession.Conversation.RunAsync(instruction, Thinking, WebEnabled, _imageData, progress, _cancellation.Token);
            RemoveAttachment();
        }
        catch (OperationCanceledException) { Status = AssistantJson.Text("CancelledCheckState"); answer.Outcome = Status; }
        catch (Exception exception) { Status = AssistantJson.Text("Failed") + " " + exception.Message; answer.Outcome = Status; }
        finally
        {
            // Drain UI progress before enabling another turn or session switch.
            await Task.Yield();
            if (answer.Outcome.Length > 0) Status = answer.Outcome;
            _cancellation.Dispose(); _cancellation = null; IsBusy = false; StoreDraft();
        }
    }
    [RelayCommand(CanExecute = nameof(IsIdle))]
    private async Task RefreshContext()
    {
        Notification = "";
        try { SetContext(await _workspace.GetContextAsync(CancellationToken.None)); }
        catch (Exception error) { Notification = error.Message; }
    }
    private void SetContext(JsonNode context)
    {
        Context = context.ToJsonString(Indented);
        var project = context["project"]?["name"]?.GetValue<string>() ?? AssistantJson.Text("NoProject");
        var target = context["bound_editor"]?.GetValue<string>();
        var editor = (context["editors"] as JsonArray)?.FirstOrDefault(e => e?["id"]?.GetValue<string>() == target);
        TargetSummary = project + (editor == null ? "" : " · " + editor["name"]?.GetValue<string>());
        var dirty = (context["editors"] as JsonArray)?.Count(e => e?["dirty"]?.GetValue<bool>() == true) ?? 0;
        SaveSummary = context["state_unavailable"]?.GetValue<bool>() == true ? AssistantJson.Text("StateUnavailable") :
            dirty > 0 ? string.Format(AssistantJson.Text("UnsavedCount"), dirty) : AssistantJson.Text("NoUnsavedEditors");
    }
    private static void AccumulateUsage(JsonObject total, JsonObject usage)
    {
        foreach (var item in usage)
            if (item.Value is JsonObject nested)
            {
                if (total[item.Key] is not JsonObject) total[item.Key] = new JsonObject();
                AccumulateUsage(total[item.Key]!.AsObject(), nested);
            }
            else if (item.Value is JsonValue number)
            {
                if (!number.TryGetValue<long>(out var value))
                {
                    if (!number.TryGetValue<int>(out var small)) continue;
                    value = small;
                }
                total[item.Key] = (total[item.Key]?.GetValue<long>() ?? 0) + value;
            }
    }
    private async Task ActOnCard(AssistantOperationCard card, string action)
    {
        if (IsBusy || _closed) return;
        IsBusy = true;
        _cancellation = new CancellationTokenSource();
        try
        {
            _workspace.BeginTask();
            var inspected = await _workspace.InspectAsync(card.Target, _cancellation.Token);
            if (action != "locate" && inspected["revision"]?.GetValue<string>() != card.Revision) throw new InvalidOperationException(AssistantJson.Text("StaleTarget"));
            var actionTarget = card.Target;
            var prefix = card.Operation.Split('.')[0] + ".";
            var candidates = inspected["operations"]!.AsArray().Where(o => o?["unavailable_reason"] == null).ToArray();
            var operation = candidates.FirstOrDefault(o => action == "locate" ? o!["name"]!.GetValue<string>() is "editor.activate" or "resource.locate"
                : o!["name"]!.GetValue<string>().StartsWith(prefix, StringComparison.Ordinal) && (o["name"]!.GetValue<string>().EndsWith("." + action, StringComparison.Ordinal) || o["name"]!.GetValue<string>().EndsWith("." + action + "_settings", StringComparison.Ordinal)));
            if (operation == null && inspected["state"]?["editor"]?["id"]?.GetValue<string>() is { } owner && owner != card.Target)
            {
                actionTarget = owner;
                inspected = await _workspace.InspectAsync(owner, _cancellation.Token);
                operation = inspected["operations"]!.AsArray().FirstOrDefault(o => o?["unavailable_reason"] == null &&
                    (action == "locate" ? o!["name"]!.GetValue<string>() == "editor.activate" : o!["name"]!.GetValue<string>() == "kitbash." + action));
            }
            if (operation == null) throw new InvalidOperationException(AssistantJson.Text("Unavailable"));
            var result = await _workspace.ExecuteAsync(Guid.NewGuid().ToString("N"), operation["name"]!.GetValue<string>(), actionTarget,
                inspected["revision"]!.GetValue<string>(), new JsonObject(), _cancellation.Token);
            var message = new AssistantMessage { Role = DisplayName };
            message.AddOperation(new AssistantOperationCard(result, ActOnCard)); Messages.Add(message);
            Status = message.Operations[0].Status;
            await RefreshContext();
        }
        catch (Exception error) { Notification = error.Message; }
        finally { _cancellation.Dispose(); _cancellation = null; IsBusy = false; }
    }
    [RelayCommand(CanExecute = nameof(IsBusy))] private void Stop() => _cancellation?.Cancel();
    [RelayCommand(CanExecute = nameof(IsIdle))]
    private void NewConversation()
    {
        if (IsBusy) return;
        if (Messages.Count == 0 && Input.Length == 0 && Attachment == null) { Panel = "conversation"; if (IsCompact) HistoryOpen = false; return; }
        var session = new AssistantSession(_client, _workspace);
        Sessions.Insert(0, session);
        CurrentSession = session;
    }
    [RelayCommand(CanExecute = nameof(IsIdle))]
    private void AttachImage()
    {
        if (IsBusy) return;
        var dialog = new OpenFileDialog { Filter = AssistantJson.Text("ImageFilter"), Multiselect = false };
        if (dialog.ShowDialog() != true) return;
        try
        {
            if (new FileInfo(dialog.FileName).Length > 4 * 1024 * 1024) throw new InvalidOperationException();
            using var file = File.OpenRead(dialog.FileName);
            var decoder = BitmapDecoder.Create(file, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            SetAttachment(decoder.Frames[0], Path.GetFileName(dialog.FileName));
        }
        catch (Exception) { Notification = AssistantJson.Text("ImageTooLarge"); }
    }
    [RelayCommand(CanExecute = nameof(IsIdle))]
    private void PasteImage()
    {
        if (IsBusy) return;
        try
        {
            if (Clipboard.ContainsImage() && Clipboard.GetImage() is { } image) SetAttachment(image, AssistantJson.Text("ClipboardImage"));
            else Notification = AssistantJson.Text("NoClipboardImage");
        }
        catch (Exception) { Notification = AssistantJson.Text("ImageTooLarge"); }
    }
    private void SetAttachment(BitmapSource image, string name)
    {
        if (image.PixelWidth > 4096 || image.PixelHeight > 4096) throw new InvalidOperationException();
        using var memory = new MemoryStream();
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(image)); encoder.Save(memory);
        if (memory.Length > 4 * 1024 * 1024) throw new InvalidOperationException();
        image.Freeze(); Attachment = image; AttachmentName = name;
        Notification = "";
        _imageData = "data:image/png;base64," + Convert.ToBase64String(memory.ToArray());
    }
    private bool CanRemoveAttachment() => IsIdle && Attachment != null;
    [RelayCommand(CanExecute = nameof(CanRemoveAttachment))] private void RemoveAttachment() { Attachment = null; AttachmentName = ""; _imageData = null; }
    public void Close() { _closed = true; _cancellation?.Cancel(); }
    public void Dispose() => Close();
}
