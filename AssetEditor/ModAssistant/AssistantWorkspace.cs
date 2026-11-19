using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using AssetEditor.Services;
using AssetEditor.UiCommands;
using Shared.Core.ModAssistant;
using Shared.Core.Events;
using Shared.Core.PackFiles;
using Shared.Core.PackFiles.Models;
using Shared.Core.PackFiles.Utility;
using Shared.Core.Services;
using Shared.Core.Settings;
using Shared.Core.ToolCreation;
using Shared.Ui.BaseDialogs.PackFileTree.ContextMenu.Commands;
using Shared.Ui.BaseDialogs.PackFileTree;
using static Shared.Core.ModAssistant.AssistantJson;

namespace AssetEditor.ModAssistant;

public sealed class AssistantWorkspace : IAssistantWorkspace, IAssistantOperationProvider, IDisposable
{
    private readonly IPackFileService _files;
    private readonly IEditorManager _editors;
    private readonly IEditorDatabase _database;
    private readonly ApplicationSettingsService _settings;
    private readonly IStandardDialogs _dialogs;
    private readonly IUiCommandFactory _commands;
    private readonly IFolderProjectOpenService _openProject;
    private readonly IPackFileContainerLoader _loader;
    private readonly IFolderProjectHistoryService _history;
    private readonly IFolderProjectPackGenerationGuard _packGuard;
    private readonly IEnumerable<IAssistantEditorAdapter> _adapters;
    private readonly IEnumerable<IAssistantResourceAdapter> _resourceAdapters;
    private readonly IEnumerable<IAssistantWorkspaceAdapter> _workspaceAdapters;
    private readonly AssistantExecutor _executor;
    private readonly AssistantIds _ids = new();
    private readonly Dictionary<string, (PackFileContainer Owner, PackFile File)> _resources = [];
    private readonly HashSet<string> _historyRoots = new(StringComparer.OrdinalIgnoreCase);
    private PackFileContainer? _boundProject;
    private GameTypeEnum _boundGame;
    private IEditorInterface? _lastEditor;
    private readonly AssistantSelectionContext _selection;
    private readonly IFolderProjectGitOperationCoordinator _coordinator;
    private readonly IFolderProjectUnsavedChangesService _unsaved;
    private readonly IFolderProjectUnsavedChangesPrompt _unsavedPrompt;

    public AssistantWorkspace(IPackFileService files, IEditorManager editors, IEditorDatabase database,
        ApplicationSettingsService settings, IStandardDialogs dialogs, IUiCommandFactory commands,
        IFolderProjectOpenService openProject, IPackFileContainerLoader loader,
        IFolderProjectHistoryService history, IFolderProjectPackGenerationGuard packGuard,
        IEnumerable<IAssistantEditorAdapter> adapters, IEnumerable<IAssistantResourceAdapter> resourceAdapters,
        IEnumerable<IAssistantWorkspaceAdapter> workspaceAdapters, AssistantSelectionContext selection,
        IFolderProjectGitOperationCoordinator coordinator, IFolderProjectUnsavedChangesService unsaved,
        IFolderProjectUnsavedChangesPrompt unsavedPrompt)
    {
        _files = files; _editors = editors; _database = database; _settings = settings; _dialogs = dialogs;
        _commands = commands; _openProject = openProject; _loader = loader; _history = history; _packGuard = packGuard; _adapters = adapters;
        _resourceAdapters = resourceAdapters; _workspaceAdapters = workspaceAdapters;
        _selection = selection; _coordinator = coordinator;
        _unsaved = unsaved; _unsavedPrompt = unsavedPrompt;
        _executor = new AssistantExecutor([this]);
        if (editors is INotifyPropertyChanged notifications)
            notifications.PropertyChanged += OnEditorChanged;
        RememberEditor();
    }

    private void OnEditorChanged(object? sender, PropertyChangedEventArgs args) => RememberEditor();
    public void Dispose()
    {
        if (_editors is INotifyPropertyChanged notifications) notifications.PropertyChanged -= OnEditorChanged;
    }

    private void RememberEditor()
    {
        var editor = _editors.GetAllEditors().ElementAtOrDefault(_editors.GetCurrentEditor());
        if (editor != null && editor is not AssistantViewModel) _lastEditor = editor;
    }
    public void BeginTask()
    {
        _executor.BeginTask();
        _historyRoots.Clear();
        _boundProject = _files.GetEditablePack();
        _boundGame = _settings.CurrentSettings.CurrentGame;
        RememberEditor();
    }
    private static Task<T> OnUi<T>(Func<Task<T>> action, CancellationToken token) =>
        Application.Current == null || Application.Current.Dispatcher.CheckAccess()
            ? action() : Application.Current.Dispatcher.InvokeAsync(action, System.Windows.Threading.DispatcherPriority.Normal, token).Task.Unwrap();
    public Task<JsonNode> GetContextAsync(CancellationToken token) => OnUi(() => Task.FromResult(Context()), token);
    public Task<JsonNode> InspectAsync(string target, CancellationToken token) => OnUi(() => Task.FromResult<JsonNode>(_executor.Inspect(target)), token);
    public Task<JsonNode> ExecuteAsync(string request, string operation, string target, string revision, JsonObject args, CancellationToken token) => OnUi<JsonNode>(async () =>
    {
        if (!ReferenceEquals(_boundProject, _files.GetEditablePack()))
            return new JsonObject { ["status"] = "rejected", ["error"] = Text("ProjectChanged") };
        if (_boundGame != _settings.CurrentSettings.CurrentGame)
            return new JsonObject { ["status"] = "rejected", ["error"] = Text("GameChanged") };
        var result = await _executor.ExecuteAsync(request, operation, target, revision, args, Authorize, token);
        if (result["status"]?.GetValue<string>() == "completed" && operation is "project.open" or "project.create" or "project.import_pack" or "project.restore" or "project.restore_file" or "project.discard_changes" or "project.discard_all" or "project.recover_history")
        {
            _boundProject = _files.GetEditablePack();
            _boundGame = _settings.CurrentSettings.CurrentGame;
        }
        return result;
    }, token);
    private Task<bool> Authorize(AssistantOperation operation, JsonObject args, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        // These operations require the existing concrete confirmation, never a model-supplied boolean.
        return Task.FromResult(operation.Effect is not (AssistantEffect.Destructive or AssistantEffect.History) ||
            _dialogs.ShowYesNoBox(operation.Description + "\n" + operation.TargetId + "\n" + args.ToJsonString(), Text("Confirm")) == ShowMessageBoxResult.OK);
    }
    private JsonNode Context() => Node(new
    {
        workspace_target = "workspace", game = _settings.CurrentSettings.CurrentGame.ToString(),
        project = _files.GetEditablePack() is { } project ? ContainerInfo(project) : null,
        bound_editor = _lastEditor != null && _editors.GetAllEditors().Contains(_lastEditor) ? _ids.Get(_lastEditor) : null,
        editors = _editors.GetAllEditors().Where(x => x is not AssistantViewModel).Select(EditorInfo).ToArray(),
        containers = _files.GetAllPackfileContainers().Select(ContainerInfo).ToArray(),
        selection = ResourceSelection(),
        history_targets = _historyRoots.Select(root => new { root, status = HistoryState(root) }).ToArray(),
        source = "live_editor_state", save_boundary = Text("SaveBoundary"),
    });
    private JsonNode HistoryState(string root)
    {
        try
        {
            var status = _history.GetDisplayStatus(root);
            return Node(new { status.Availability, status.CurrentRestorePointId, status.CanRecover, status.RecoveryReason,
                changes = status.UnrecordedChanges.Count, revision = Revision(Node(status)),
                history_revision = status.CanRecover ? null : Revision(Node(_history.GetRestorePoints(root, 100))) });
        }
        catch (Exception error) { return Node(new { error = error.Message }); }
    }
    private static JsonNode HistorySummary(FolderProjectHistoryStatus status) => Node(new
    {
        status.Availability, status.CurrentRestorePointId, status.CanRecover, status.RecoveryReason, status.IsClean,
        change_count = status.UnrecordedChanges.Count,
    });
    private bool CanRecoverHistory(string root)
    {
        try { return _history.GetDisplayStatus(root).CanRecover; }
        catch { return false; }
    }
    private object[] ResourceSelection() => (_selection.Browser?.SelectedItems ?? []).Take(100).Select(node =>
    {
        if (node.Item != null) _resources[_ids.Get(node.Item)] = (node.FileOwner, node.Item);
        return (object)new { target = node.Item == null ? null : _ids.Get(node.Item), container = _ids.Get(node.FileOwner), path = node.GetFullPath(), type = node.NodeType.ToString() };
    }).ToArray();
    private object ContainerInfo(PackFileContainer container) => new
    {
        id = _ids.Get(container), name = container.Name, path = container.SystemFilePath,
        editable = ReferenceEquals(container, _files.GetEditablePack()) && container is FolderProjectContainer,
        root = (container as FolderProjectContainer)?.ProjectRoot,
        revision = (container as FolderProjectContainer)?.CurrentRevision,
        configuration = container is FolderProjectContainer folder ? Revision(Node(folder.ProjectSettings)) : null,
        output_pack = (container as FolderProjectContainer)?.ProjectSettings.OutputPackPath,
    };
    private object EditorInfo(IEditorInterface editor) => new
    {
        id = _ids.Get(editor), name = editor.DisplayName,
        editor = _database.GetEditorInfos().FirstOrDefault(x => x.ViewModel == editor.GetType())?.EditorEnum.ToString(),
        dirty = (editor as ISaveableEditor)?.HasUnsavedChanges, open = _editors.GetAllEditors().Contains(editor),
        file = editor is IFileEditor file && file.CurrentFile != null ? _files.GetFullPath(file.CurrentFile) : null,
        container = editor is IFileEditor fe && fe.CurrentFile != null && _files.GetPackFileContainer(fe.CurrentFile) is { } owner ? _ids.Get(owner) : null,
    };
    private AssistantOperation WorkspaceOperation(string name, string description, JsonObject schema, AssistantEffect effect, Func<JsonObject, JsonNode> execute,
        Func<string?>? unavailable = null) => AssistantOperation.Sync(name, "workspace", Text(description), schema, effect,
            Text(effect >= AssistantEffect.Disk ? "DiskRecovery" : "NoDataChange"), Context, execute, unavailable);
    private FolderProjectContainer Project() => _files.GetEditablePack() as FolderProjectContainer ?? throw new InvalidOperationException(Text("NoProject"));

    public IEnumerable<AssistantOperation> GetOperations()
    {
        foreach (var operation in OperationsCore())
            yield return operation with { Scope = operation.Scope ?? Node(new { game = _settings.CurrentSettings.CurrentGame.ToString(), target = operation.TargetId,
                project = _files.GetEditablePack() is { } project ? _ids.Get(project) : null }).AsObject() };
    }

    private IEnumerable<AssistantOperation> OperationsCore()
    {
        foreach (var operation in _workspaceAdapters.SelectMany(a => a.GetOperations(_ids)))
            yield return operation with { ReadState = Context };
        yield return WorkspaceOperation("resources.search", "Search", Schema(("query", Str(), true), ("container", Str(), false),
            ("offset", Num(0, 1000000, integer: true), false), ("limit", Num(1, 100, integer: true), false)), AssistantEffect.Query, Search);
        yield return WorkspaceOperation("tools.list", "Tools", Schema(), AssistantEffect.Query, _ => Node(_database.GetEditorInfos()
            .Select(x => new { name = x.EditorEnum.ToString(), title = x.ToolbarName, games = x.SupportedGames.Select(g => g.ToString()), extensions = x.Extensions, standalone = x.AddToolbarButton && x.IsToolbarButtonEnabled })));
        var availableTools = _database.GetEditorInfos().Where(x => x.AddToolbarButton && x.IsToolbarButtonEnabled && x.EditorEnum != EditorEnums.ModAssistant &&
            (x.SupportedGames.Count == 0 || x.SupportedGames.Contains(_settings.CurrentSettings.CurrentGame))).ToArray();
        yield return WorkspaceOperation("tools.open", "OpenTool", Schema(("tool", Str("", availableTools.Select(x => x.EditorEnum.ToString()).ToArray()), true)), AssistantEffect.Preview,
            args => Node(EditorInfo(_editors.Create(Enum.Parse<EditorEnums>(args.String("tool"))))));
        yield return WorkspaceOperation("project.open", "OpenProject", Schema(("path", Str(), true)), AssistantEffect.Preview, args =>
        {
            _openProject.Open(args.String("path"));
            if (!string.Equals(Project().ProjectRoot.TrimEnd('\\'), Path.GetFullPath(args.String("path")).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException(Text("NotCompleted"));
            return Context();
        });
        yield return WorkspaceOperation("project.reference", "OpenReference", Schema(("path", Str(), true)), AssistantEffect.Preview, args =>
        {
            var container = _loader.Load(args.String("path")) ?? throw new InvalidOperationException(Text("NotCompleted"));
            var added = _files.AddReferencePack(container) ?? throw new InvalidOperationException(Text("NotCompleted"));
            return Node(ContainerInfo(added));
        });
        yield return WorkspaceOperation("project.create", "CreateProject", Schema(), AssistantEffect.Disk, _ =>
        {
            var previous = _files.GetEditablePack();
            _commands.Create<CreateFolderProjectCommand>().Execute();
            if (ReferenceEquals(previous, _files.GetEditablePack())) throw new OperationCanceledException();
            return Context();
        });
        yield return WorkspaceOperation("project.import_pack", "ImportPack", Schema(), AssistantEffect.Disk, _ =>
        {
            var previous = _files.GetEditablePack();
            _commands.Create<ImportPackAsFolderProjectCommand>().Execute();
            if (ReferenceEquals(previous, _files.GetEditablePack())) throw new OperationCanceledException();
            return Context();
        });
        yield return WorkspaceOperation("project.create_folder", "CreateFolder", Schema(("path", Str(), true)), AssistantEffect.Disk, args =>
        {
            _files.CreateFolder(Project(), args.String("path"));
            return Node(new { path = args.String("path"), exists = Directory.Exists(Path.Combine(Project().ProjectRoot, args.String("path"))) });
        }, () => _files.GetEditablePack() is FolderProjectContainer ? null : Text("NoProject"));
        foreach (var action in new[] { "rename", "move", "delete" })
            yield return WorkspaceOperation("project.folder." + action, action == "delete" ? "DeleteFolder" : action == "rename" ? "RenameFolder" : "MoveFolder",
                Schema(("path", Str("existing project-relative folder"), true), ("destination", Str("rename: single new name; move: existing project-relative parent"), action != "delete")),
                action == "delete" ? AssistantEffect.Destructive : AssistantEffect.Disk, args =>
                {
                    var project = Project(); var path = args.String("path");
                    var absolute = FolderProjectPathPolicy.ResolveFilePath(project.ProjectRoot, Path.Combine(path, "scope-check"));
                    var directory = Path.GetDirectoryName(absolute)!;
                    if (directory.Equals(project.ProjectRoot, StringComparison.OrdinalIgnoreCase) || !Directory.Exists(directory)) throw new ArgumentException(Text("InvalidParameters"));
                    var prefix = path.Replace('/', '\\').Trim('\\') + "\\";
                    if (_editors.GetAllEditors().OfType<IFileEditor>().Any(e => e.CurrentFile != null && ReferenceEquals(_files.GetPackFileContainer(e.CurrentFile), project) &&
                        _files.GetFullPath(e.CurrentFile, project).StartsWith(prefix, StringComparison.OrdinalIgnoreCase))) throw new InvalidOperationException(Text("CloseBeforeDiskChange"));
                    if (action == "rename") _files.RenameDirectory(project, path, args.String("destination"));
                    else if (action == "move") _files.MoveFolder(project, path, args.String("destination"));
                    else _files.DeleteFolder(project, path);
                    return Node(new { action, path, destination = args.OptionalString("destination"), old_location_exists = Directory.Exists(directory), state = Context() });
                }, () => _files.GetEditablePack() is FolderProjectContainer ? null : Text("NoProject"));
        yield return WorkspaceOperation("project.history", "History", Schema(("limit", Num(1, 100, integer: true), false), ("offset", Num(0, 10000, integer: true), false)), AssistantEffect.Query, args =>
        {
            _historyRoots.Add(Project().ProjectRoot);
            var offset = args.Integer("offset");
            return Node(new { status = HistorySummary(_history.GetDisplayStatus(Project().ProjectRoot)), offset,
                restore_points = _history.GetRestorePoints(Project().ProjectRoot, offset + args.Integer("limit", 30)).Skip(offset) });
        },
            () => _files.GetEditablePack() is FolderProjectContainer ? null : Text("NoProject"));
        yield return WorkspaceOperation("project.recovery_status", "RecoveryStatus", Schema(("root", Str("absolute folder project directory"), true)), AssistantEffect.Query, args =>
        {
            var root = args.String("root");
            if (!Path.IsPathFullyQualified(root) || !Directory.Exists(root) || _historyRoots.Count >= 8 && !_historyRoots.Contains(root))
                throw new ArgumentException(Text("InvalidParameters"));
            root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
            var status = _history.GetDisplayStatus(root);
            _historyRoots.Add(root);
            return Node(new { root, status = HistorySummary(status) });
        });
        yield return WorkspaceOperation("project.history_diff", "HistoryDiff", Schema(("id", Str(), true), ("offset", Num(0, 1000000, integer: true), false)), AssistantEffect.Query,
            args => Node(_history.GetRestorePointChanges(Project().ProjectRoot, args.String("id")).Skip(args.Integer("offset")).Take(100).ToArray()),
            () => _files.GetEditablePack() is FolderProjectContainer ? null : Text("NoProject"));
        yield return WorkspaceOperation("project.changes", "HistoryChanges", Schema(("offset", Num(0, 1000000, integer: true), false)), AssistantEffect.Query, args =>
        {
            _historyRoots.Add(Project().ProjectRoot);
            var status = _history.GetStatus(Project().ProjectRoot);
            return Node(new { status.IsClean, total = status.UnrecordedChanges.Count,
                changes = status.UnrecordedChanges.Skip(args.Integer("offset")).Take(100), editor_state = Context() });
        }, () => _files.GetEditablePack() is FolderProjectContainer ? null : Text("NoProject"));
        yield return new AssistantOperation("project.delete_restore_point", "workspace", Text("DeleteRestorePoint"), Schema(("id", Str(), true)),
            AssistantEffect.History, Text("DiskRecovery"), Context, async (args, token) =>
            {
                var root = Project().ProjectRoot;
                token.ThrowIfCancellationRequested();
                await _coordinator.ExecuteInPlaceTransactionalAsync(root,
                    () => { token.ThrowIfCancellationRequested(); return _history.BeginDeleteRestorePoint(root, args.String("id"), _ => token.ThrowIfCancellationRequested()); },
                    _history.CompleteDeleteRestorePoint, operation => _history.RollbackDeleteRestorePoint(root, operation));
                return Node(new { deleted = args.String("id"), restore_points = _history.GetRestorePoints(root, 100), state = Context() });
            }, () => _files.GetEditablePack() is FolderProjectContainer ? null : Text("NoProject"));
        yield return new AssistantOperation("project.discard_changes", "workspace", Text("DiscardChanges"),
            Schema(("paths", Array(Str("exact project-relative changed path; enumerate project.changes first"), 100), true)),
            AssistantEffect.History, Text("DiskRecovery"), Context, async (args, token) =>
            {
                var root = Project().ProjectRoot;
                var paths = args["paths"]!.AsArray().Select(p => p!.GetValue<string>()).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
                token.ThrowIfCancellationRequested();
                foreach (var path in paths) FolderProjectPathPolicy.ResolveFilePath(root, path);
                var changes = _history.GetStatus(root).UnrecordedChanges;
                var reloadPaths = changes.Where(c => paths.Contains(c.Path, StringComparer.OrdinalIgnoreCase))
                    .SelectMany(c => c.PreviousPath == null ? new[] { c.Path } : new[] { c.Path, c.PreviousPath }).ToArray();
                var result = await FolderProjectFileOperation.ExecuteAsync(_coordinator, _unsaved, Project(), reloadPaths,
                    () => { token.ThrowIfCancellationRequested(); return _history.BeginDiscardChanges(root, paths, _ => token.ThrowIfCancellationRequested()); },
                    _history.CompleteDiscardChanges, operation => _history.RollbackDiscardChanges(root, operation));
                return Node(new { discarded = paths, result, remaining = HistorySummary(_history.GetStatus(root)), state = Context() });
            }, () => _files.GetEditablePack() is FolderProjectContainer ? null : Text("NoProject"));
        yield return new AssistantOperation("project.recover_history", "workspace", Text("RecoverHistory"),
            Schema(("root", Str("absolute folder project directory requiring history recovery"), true)),
            AssistantEffect.History, Text("DiskRecovery"), Context, async (args, token) =>
            {
                var root = args.String("root");
                if (!Path.IsPathFullyQualified(root)) throw new ArgumentException(Text("InvalidParameters"));
                root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
                if (!_historyRoots.Contains(root) || !_history.GetDisplayStatus(root).CanRecover) throw new InvalidOperationException(Text("RecoveryStatusRequired"));
                await _coordinator.ExecuteTransactionalAsync(root,
                    () => { token.ThrowIfCancellationRequested(); return _history.BeginRecoverToSafeState(root, _ => token.ThrowIfCancellationRequested()); },
                    _history.CompleteRecoverToSafeState, _history.RollbackRecoverToSafeState, openWhenComplete: true);
                return Node(new { root, status = HistorySummary(_history.GetDisplayStatus(root)), state = Context() });
            }, () => _historyRoots.Any(CanRecoverHistory) ? null : Text("RecoveryStatusRequired"));
        yield return new AssistantOperation("project.discard_all", "workspace", Text("DiscardAllChanges"),
            Schema(("root", Str("absolute current project directory; all disk changes will be discarded"), true)),
            AssistantEffect.History, Text("DiskRecovery"), Context, async (args, token) =>
            {
                var root = Project().ProjectRoot;
                token.ThrowIfCancellationRequested();
                if (!Path.IsPathFullyQualified(args.String("root")) || !Path.TrimEndingDirectorySeparator(Path.GetFullPath(args.String("root"))).Equals(
                    Path.TrimEndingDirectorySeparator(Path.GetFullPath(root)), StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException(Text("StaleTarget"));
                var paths = _history.GetStatus(root).UnrecordedChanges.Select(c => c.Path).ToArray();
                if (paths.Length == 0) return Node(new { discarded_count = 0, state = Context() });
                await _coordinator.ExecuteTransactionalAsync(root,
                    () => { token.ThrowIfCancellationRequested(); return _history.BeginDiscardChanges(root, paths, _ => token.ThrowIfCancellationRequested()); },
                    _history.CompleteDiscardChanges, operation => _history.RollbackDiscardChanges(root, operation));
                return Node(new { discarded_count = paths.Length, remaining = HistorySummary(_history.GetStatus(root)), state = Context() });
            }, () => _files.GetEditablePack() is FolderProjectContainer ? null : Text("NoProject"));
        yield return WorkspaceOperation("project.create_restore_point", "RestorePoint", Schema(("description", Str(), true)), AssistantEffect.History,
            args =>
            {
                var project = Project();
                if (_unsaved.HasUnsavedChanges(project.ProjectRoot, null))
                {
                    var choice = _unsavedPrompt.Show();
                    if (choice == FolderProjectUnsavedChangesChoice.Cancel || choice == FolderProjectUnsavedChangesChoice.Save &&
                        !_unsaved.SaveUnsavedChanges(project.ProjectRoot, null)) throw new OperationCanceledException();
                }
                if (!ReferenceEquals(project, _files.GetEditablePack())) throw new InvalidOperationException(Text("StaleTarget"));
                project.RefreshFromDisk();
                return Node(new { restore_point = _history.CreateRestorePoint(project.ProjectRoot, args.String("description")), editor_state = Context() });
            },
            () => _files.GetEditablePack() is FolderProjectContainer ? null : Text("NoProject"));
        yield return new AssistantOperation("project.restore", "workspace", Text("RestoreProject"), Schema(("id", Str(), true)), AssistantEffect.History, Text("DiskRecovery"), Context,
            async (args, token) =>
            {
                var root = Project().ProjectRoot;
                token.ThrowIfCancellationRequested();
                var restored = await _coordinator.ExecuteTransactionalAsync(root,
                    () => _history.RestoreProject(root, args.String("id")), _ => { }, result => _history.RollbackProjectRestore(root, result));
                return Node(new { restored, state = Context() });
            }, () => _files.GetEditablePack() is FolderProjectContainer ? null : Text("NoProject"));
        yield return new AssistantOperation("project.restore_file", "workspace", Text("RestoreFile"), Schema(("id", Str(), true), ("path", Str(), true)), AssistantEffect.History, Text("DiskRecovery"), Context,
            async (args, token) =>
            {
                var root = Project().ProjectRoot; token.ThrowIfCancellationRequested();
                var result = await FolderProjectFileOperation.ExecuteAsync(_coordinator, _unsaved, Project(), [args.String("path")],
                    () => _history.BeginRestoreFile(root, args.String("id"), args.String("path")), _history.CompleteRestoreFile, _history.RollbackRestoreFile);
                return Node(new { restored = result, state = Context() });
            }, () => _files.GetEditablePack() is FolderProjectContainer ? null : Text("NoProject"));
        yield return WorkspaceOperation("project.import_files", "ImportFiles", Schema(("files", Array(Schema(("source", Str(), true), ("path", Str(), true)), 100), true)), AssistantEffect.Disk, args =>
        {
            var project = Project();
            long total = 0;
            var writes = args["files"]!.AsArray().Cast<JsonObject>().Select(item =>
            {
                var source = item.String("source");
                if (!Path.IsPathFullyQualified(source)) throw new ArgumentException(Text("InvalidParameters"));
                total += new FileInfo(source).Length;
                if (total > 256 * 1024 * 1024) throw new ArgumentException(Text("ResourceTooLarge"));
                FolderProjectPathPolicy.ResolveFilePath(project.ProjectRoot, item.String("path"));
                return new PackFileWrite(item.String("path"), File.ReadAllBytes(source));
            }).ToArray();
            var saved = _files.ApplyFileWrites(project, writes, false);
            foreach (var write in writes)
                if (!_files.FindFile(write.Path, project)!.DataSource.ReadData().AsSpan().SequenceEqual(write.Content)) throw new InvalidOperationException(Text("ReadbackFailed"));
            return Node(new { paths = saved.Select(f => _files.GetFullPath(f, project)), saved = true, editor_changes_saved = false });
        }, () => _files.GetEditablePack() is FolderProjectContainer ? null : Text("NoProject"));
        yield return new AssistantOperation("project.generate_pack", "workspace", Text("GeneratePack"), Schema(), AssistantEffect.Disk, Text("DiskRecovery"), Context, async (_, token) =>
        {
            var project = Project();
            if (!_packGuard.CanGenerate(project)) throw new OperationCanceledException();
            var output = SavePackFileContainerCommand.SelectOutputPath(project) ?? throw new OperationCanceledException();
            if (!ReferenceEquals(project, _files.GetEditablePack())) throw new InvalidOperationException(Text("StaleTarget"));
            await _files.GenerateFolderProjectPackAsync(project, output, GameInformationDatabase.GetGameById(_settings.CurrentSettings.CurrentGame), token);
            var readback = _loader.Load(output) ?? throw new InvalidOperationException(Text("ReadbackFailed"));
            long verifiedBytes = 0; var verifiedFiles = 0;
            using (var stream = File.OpenRead(output))
                foreach (var entry in _files.GetFileEntriesSnapshot(project).Where(e => !project.IsIgnored(e.Key) && !FolderProjectPathPolicy.IsExcludedPath(e.Key)))
                {
                    if (!readback.FileList.TryGetValue(entry.Key, out var packed)) throw new InvalidOperationException(Text("ReadbackFailed"));
                    if (verifiedBytes + entry.Value.DataSource.Size > 64L * 1024 * 1024) continue;
                    var bytes = packed.DataSource is PackedFileSource source ? source.ReadData(stream) : packed.DataSource.ReadData();
                    if (!bytes.AsSpan().SequenceEqual(entry.Value.DataSource.ReadData())) throw new InvalidOperationException(Text("ReadbackFailed"));
                    verifiedFiles++; verifiedBytes += bytes.Length;
                }
            return Node(new { output, bytes = new FileInfo(output).Length, files = readback.FileList.Count, verified_files = verifiedFiles, verified_bytes = verifiedBytes,
                verification_limit_bytes = 64 * 1024 * 1024, editor_state = Context(), boundary = Text("SaveBoundary") });
        }, () => _files.GetEditablePack() is FolderProjectContainer ? null : Text("NoProject"));

        foreach (var editor in _editors.GetAllEditors().Where(x => x is not AssistantViewModel).ToArray())
        {
            var registration = _database.GetEditorInfos().FirstOrDefault(x => x.ViewModel == editor.GetType());
            bool SupportedGame() => registration == null || registration.SupportedGames.Count == 0 || registration.SupportedGames.Contains(_settings.CurrentSettings.CurrentGame);
            var adapted = _adapters.SelectMany(x => x.GetOperations(editor, _ids)).ToArray();
            object[] Targets(int offset, int limit) => adapted.Where(x => x.TargetId != _ids.Get(editor)).GroupBy(x => x.TargetId).Skip(offset).Take(limit)
                .Select(group => (object)new { target = group.Key, operations = group.Select(x => x.Name).ToArray() }).ToArray();
            JsonNode State() => new JsonObject { ["editor"] = Node(EditorInfo(editor)),
                ["child_targets"] = Node(Targets(0, 20)), ["child_target_count"] = adapted.Where(x => x.TargetId != _ids.Get(editor)).Select(x => x.TargetId).Distinct().Count(),
                ["documents"] = new JsonArray((_editors.GetAllEditors().Contains(editor) ? adapted.Where(x => x.TargetId == _ids.Get(editor)).Select(x => x.ReadState).Distinct().Select(read => read()) : []).ToArray()) };
            var target = _ids.Get(editor);
            var recovery = Text("EditorRecovery");
            yield return AssistantOperation.Sync("editor.targets", target, Text("EditorTargets"), Schema(("offset", Num(0, 1000000, integer: true), false)), AssistantEffect.Query,
                Text("NoDataChange"), State, args => Node(Targets(args.Integer("offset"), 100)));
            yield return AssistantOperation.Sync("editor.activate", target, Text("Activate"), Schema(), AssistantEffect.Preview, Text("NoDataChange"), State,
                _ => { _editors.SetEditorAsCurrent(editor); return Node(EditorInfo(editor)); });
            yield return AssistantOperation.Sync("editor.close", target, Text("Close"), Schema(), AssistantEffect.Preview, recovery, State,
                _ => { _editors.CloseTool(editor); if (_editors.GetAllEditors().Contains(editor)) throw new OperationCanceledException(); return Node(new { closed = true }); });
            if (editor is ISaveableEditor saveable && editor is not IAssistantReadOnlyDocument)
                yield return AssistantOperation.Sync("editor.save", target, Text("Save"), Schema(), AssistantEffect.Disk, Text("DiskRecovery"), State, _ =>
                {
                    if (!saveable.Save()) throw new InvalidOperationException(Text("NotCompleted"));
                    return Node(new { dirty = saveable.HasUnsavedChanges, file = EditorInfo(editor) });
                }, () => SupportedGame() ? null : Text("GameChanged"));
            foreach (var operation in adapted)
            {
                var read = operation.ReadState;
                yield return operation with
                {
                    ReadState = operation.TargetId == target ? State : () => new JsonObject { ["editor"] = Node(EditorInfo(editor)), ["document"] = read() },
                    Scope = Node(new { game = _settings.CurrentSettings.CurrentGame.ToString(), editor = EditorInfo(editor), target = operation.TargetId }).AsObject(),
                    Unavailable = () => SupportedGame() ? operation.Unavailable?.Invoke() : Text("GameChanged"),
                };
            }
        }
        foreach (var entry in _resources.ToArray())
            foreach (var operation in ResourceOperations(entry.Key, entry.Value.Owner, entry.Value.File)) yield return operation;
    }

    private JsonNode Search(JsonObject args)
    {
        var query = args.String("query");
        var containerId = args["container"]?.GetValue<string>();
        var offset = args.Integer("offset");
        var limit = args.Integer("limit", 30);
        var matches = _files.GetAllPackfileContainers().Where(x => containerId == null || _ids.Get(x) == containerId)
            .SelectMany(owner => _files.GetFileEntriesSnapshot(owner).Where(pair => pair.Key.Contains(query, StringComparison.OrdinalIgnoreCase))
                .Select(pair => (Owner: owner, Path: pair.Key, File: pair.Value))).Skip(offset).Take(limit + 1).ToArray();
        var results = matches.Take(limit).Select(match =>
        {
            var id = _ids.Get(match.File);
            _resources[id] = (match.Owner, match.File);
            return new { target = id, path = match.Path, container = _ids.Get(match.Owner), size = match.File.DataSource.Size };
        }).ToArray();
        while (_resources.Count > 300) _resources.Remove(_resources.Keys.First());
        return Node(new { results, next_offset = matches.Length > limit ? (int?)(offset + limit) : null });
    }
    private IEnumerable<AssistantOperation> ResourceOperations(string id, PackFileContainer owner, PackFile file)
    {
        bool Exists() => _files.GetAllPackfileContainers().Contains(owner) && _files.GetFileEntriesSnapshot(owner).Any(x => ReferenceEquals(x.Value, file));
        byte[] Read()
        {
            if (!Exists()) throw new InvalidOperationException(Text("TargetGone"));
            if (file.DataSource.Size > 32 * 1024 * 1024) throw new InvalidOperationException(Text("ResourceTooLarge"));
            return file.DataSource.ReadData();
        }
        JsonNode State() => Node(new { id, container = _ids.Get(owner), path = Exists() ? _files.GetFullPath(file, owner) : null,
            exists = Exists(), bytes = file.DataSource.Size, sha256 = Exists() && file.DataSource.Size <= 32 * 1024 * 1024 ? Convert.ToHexString(SHA256.HashData(Read())) : null,
            open_editors = _editors.GetAllEditors().OfType<IFileEditor>().Where(x => ReferenceEquals(x.CurrentFile, file)).Select(x => _ids.Get(x)).ToArray(),
            source = "saved_resource" });
        AssistantOperation Op(string name, string description, JsonObject schema, AssistantEffect effect, Func<JsonObject, JsonNode> execute) =>
            AssistantOperation.Sync(name, id, Text(description), schema, effect, Text(effect >= AssistantEffect.Disk ? "DiskRecovery" : "NoDataChange"), State, execute, () => Exists() ? null : Text("TargetGone"));
        foreach (var operation in _resourceAdapters.SelectMany(a => a.GetOperations(owner, file, _ids)))
        {
            string? Available() => !Exists() ? Text("TargetGone") :
                _editors.GetAllEditors().OfType<IFileEditor>().Any(x => ReferenceEquals(x.CurrentFile, file) && x is ISaveableEditor { HasUnsavedChanges: true })
                    ? Text("UnsavedSource") : operation.Unavailable?.Invoke();
            yield return operation with { ReadState = State, Unavailable = Available };
        }
        var supportedEditors = _database.GetEditorsForFile(_files.GetFullPath(file, owner)).Where(x =>
            x.SupportedGames.Count == 0 || x.SupportedGames.Contains(_settings.CurrentSettings.CurrentGame)).Select(x => x.EditorEnum).ToArray();
        if (supportedEditors.Length > 0)
            yield return Op("resource.open", "OpenResource", Schema(("editor", Str("", supportedEditors.Select(x => x.ToString()).ToArray()), false)), AssistantEffect.Preview,
                args => Node(EditorInfo(_editors.CreateFromFile(file, args["editor"] is { } type ? Enum.Parse<EditorEnums>(type.GetValue<string>()) : null)
                    ?? throw new OperationCanceledException())));
        yield return Op("resource.copy_to_project", "CopyResource", Schema(("path", Str(), true)), AssistantEffect.Disk, args =>
        {
            if (_editors.GetAllEditors().OfType<IFileEditor>().Any(x => ReferenceEquals(x.CurrentFile, file) && x is ISaveableEditor { HasUnsavedChanges: true }))
                throw new InvalidOperationException(Text("UnsavedSource"));
            var bytes = Read();
            var written = _files.ApplyFileWrites(Project(), [new PackFileWrite(args.String("path"), bytes)], false).Single();
            if (!written.DataSource.ReadData().AsSpan().SequenceEqual(bytes)) throw new InvalidOperationException(Text("ReadbackFailed"));
            _resources[_ids.Get(written)] = (Project(), written);
            return Node(new { target = _ids.Get(written), path = _files.GetFullPath(written, Project()), saved = true, source = "saved_resource" });
        });
        yield return Op("resource.locate", "Locate", Schema(), AssistantEffect.Preview, _ =>
        {
            var browser = _selection.Browser ?? throw new InvalidOperationException(Text("Unavailable"));
            TreeNode? Find(IEnumerable<TreeNode> nodes) => nodes.Select(n => ReferenceEquals(n.Item, file) ? n : Find(n.Children)).FirstOrDefault(n => n != null);
            var node = Find(browser.Files) ?? throw new InvalidOperationException(Text("TargetGone"));
            for (var parent = node.Parent; parent != null; parent = parent.Parent) parent.IsNodeExpanded = true;
            browser.SelectNode(node, PackFileTreeSelectionMode.Replace);
            return State();
        });
        yield return Op("resource.export", "ExportResource", Schema(("output", Str("new absolute file path; never overwrites"), true)), AssistantEffect.Disk, args =>
        {
            if (_editors.GetAllEditors().OfType<IFileEditor>().Any(x => ReferenceEquals(x.CurrentFile, file) && x is ISaveableEditor { HasUnsavedChanges: true }))
                throw new InvalidOperationException(Text("UnsavedSource"));
            var path = args.String("output");
            if (!Path.IsPathFullyQualified(path)) throw new ArgumentException(Text("InvalidParameters"));
            path = FolderProjectPathPolicy.ResolveExternalOutputPath(path);
            foreach (var project in _files.GetAllPackfileContainers().OfType<FolderProjectContainer>())
                FolderProjectPathPolicy.EnsureOutputOutsideProject(project.ProjectRoot, path);
            var bytes = Read();
            using (var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None)) stream.Write(bytes);
            if (!File.ReadAllBytes(path).AsSpan().SequenceEqual(bytes)) throw new InvalidOperationException(Text("ReadbackFailed"));
            return Node(new { output = path, bytes = bytes.Length, sha256 = Convert.ToHexString(SHA256.HashData(bytes)) });
        });
        if (!ReferenceEquals(owner, _files.GetEditablePack()) || owner is not FolderProjectContainer) yield break;
        void RequireClosed()
        {
            if (_editors.GetAllEditors().OfType<IFileEditor>().Any(x => ReferenceEquals(x.CurrentFile, file))) throw new InvalidOperationException(Text("CloseBeforeDiskChange"));
        }
        yield return Op("resource.rename", "Rename", Schema(("name", Str(), true)), AssistantEffect.Disk, args =>
            { RequireClosed(); _files.RenameFile(owner, file, args.String("name")); return State(); });
        yield return Op("resource.move", "Move", Schema(("folder", Str(), true)), AssistantEffect.Disk, args =>
            { RequireClosed(); _files.MoveFile(owner, file, args.String("folder")); return State(); });
        yield return Op("resource.delete", "Delete", Schema(), AssistantEffect.Destructive, _ =>
            { RequireClosed(); _files.DeleteFile(owner, file); return State(); });
    }
}
