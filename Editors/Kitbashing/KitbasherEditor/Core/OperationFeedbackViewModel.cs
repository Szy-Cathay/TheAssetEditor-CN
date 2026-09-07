using System.Windows;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using Editors.KitbasherEditor.ChildEditors.PinTool.Commands;
using Editors.KitbasherEditor.ChildEditors.ReRiggingTool;
using Editors.KitbasherEditor.Commands;
using Editors.KitbasherEditor.ViewModels.SceneNodeEditor;
using GameWorld.Core.Commands.Bone;
using GameWorld.Core.Commands.Bone.Clipboard;
using GameWorld.Core.Commands.Edge;
using GameWorld.Core.Commands.Face;
using GameWorld.Core.Commands.Object;
using GameWorld.Core.Commands.Vertex;
using GameWorld.Core.Services;
using Shared.Core.Events;
using Shared.Core.Services;

namespace Editors.KitbasherEditor.Core;

public sealed partial class OperationFeedbackViewModel : ObservableObject, IDisposable
{
    private readonly IEventHub _eventHub;
    private readonly DispatcherTimer _timer;
    private bool _active;
    private bool _disposed;

    [ObservableProperty] private string _text = "";
    [ObservableProperty] private bool _isVisible;

    public OperationFeedbackViewModel(IEventHub eventHub)
    {
        _eventHub = eventHub;
        _timer = new DispatcherTimer(DispatcherPriority.Background, Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher)
        {
            Interval = TimeSpan.FromSeconds(3),
        };
        _timer.Tick += OnTimerTick;
        _eventHub.Register<CommandStackChangedEvent>(this, OnChanged);
        _eventHub.Register<CommandStackUndoEvent>(this, OnUndo);
    }

    public void Activate() => _active = !_disposed;

    public void Deactivate()
    {
        _active = false;
        Hide();
    }

    private void OnChanged(CommandStackChangedEvent notification) =>
        Show(notification.CommandType, notification.IsRedo ? "Kitbash.Operation.Redone" : "Kitbash.Operation.Completed");

    private void OnUndo(CommandStackUndoEvent notification) =>
        Show(notification.CommandType, "Kitbash.Operation.Undone");

    private void Show(Type? commandType, string messageKey)
    {
        if (_disposed || !_active)
            return;
        if (!_timer.Dispatcher.CheckAccess())
        {
            _timer.Dispatcher.BeginInvoke(() => Show(commandType, messageKey));
            return;
        }

        _timer.Stop();
        Text = LocalizationManager.Instance.GetFormat(messageKey, GetDescription(commandType));
        IsVisible = true;
        _timer.Start();
    }

    private void OnTimerTick(object? sender, EventArgs e) => Hide();

    private void Hide()
    {
        _timer.Stop();
        IsVisible = false;
        Text = "";
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        Deactivate();
        _timer.Tick -= OnTimerTick;
        _eventHub.UnRegister(this);
    }

    public static string GetDescription(Type? commandType)
    {
        if (commandType?.IsGenericType == true)
            commandType = commandType.GetGenericTypeDefinition();
        return LocalizationManager.Instance.Get(commandType != null && DescriptionKeys.TryGetValue(commandType, out var key)
            ? key
            : "Kitbash.Operation.Other");
    }

    private static readonly IReadOnlyDictionary<Type, string> DescriptionKeys = new Dictionary<Type, string>
    {
        [typeof(ObjectSelectionCommand)] = "Kitbash.Operation.SelectObjects",
        [typeof(ObjectSelectionModeCommand)] = "Kitbash.Operation.SelectionMode",
        [typeof(VertexSelectionCommand)] = "Kitbash.Operation.SelectVertices",
        [typeof(EdgeSelectionCommand)] = "Kitbash.Operation.SelectEdges",
        [typeof(FaceSelectionCommand)] = "Kitbash.Operation.SelectFaces",
        [typeof(BoneSelectionCommand)] = "Kitbash.Operation.SelectBones",
        [typeof(TransformVertexCommand)] = "Kitbash.Operation.TransformSelection",
        [typeof(TransformBoneCommand)] = "Kitbash.Operation.TransformBones",
        [typeof(ResetTransformBoneCommand)] = "Kitbash.Operation.ResetBones",
        [typeof(DuplicateObjectCommand)] = "Kitbash.Menu.Tools.Duplicate",
        [typeof(DeleteObjectsCommand)] = "Kitbash.Menu.Tools.Delete",
        [typeof(CombineMeshCommand)] = "Kitbash.Menu.Tools.Merge",
        [typeof(DivideObjectIntoSubmeshesCommand)] = "Kitbash.Menu.Tools.Split",
        [typeof(ReduceMeshCommand)] = "Kitbash.Menu.Tools.ReduceMesh",
        [typeof(GrowMeshCommand)] = "Kitbash.Operation.GrowMesh",
        [typeof(GroupObjectsCommand)] = "Kitbash.Menu.Tools.GroupSelection",
        [typeof(UnGroupObjectsCommand)] = "Kitbash.Menu.Tools.UngroupSelection",
        [typeof(AddObjectsToGroupCommand)] = "Kitbash.Operation.AddToGroup",
        [typeof(SortSceneNodesCommand)] = "Kitbash.CommandHint.SortSceneNodes",
        [typeof(MakeNodeEditableCommand)] = "Kitbash.CommandHint.MakeNodeEditable",
        [typeof(CreateStaticMeshFromAnimationCommand)] = "Kitbash.CommandHint.CreateStaticMeshFromAnimation",
        [typeof(CreateAnimatedMeshPoseCommand)] = "Kitbash.CommandHint.CreateStaticMeshFromAnimation",
        [typeof(ConvertFacesToVertexSelectionCommand)] = "Kitbash.Menu.Tools.FaceToVertex",
        [typeof(DeleteFaceCommand)] = "Kitbash.Operation.DeleteFaces",
        [typeof(DuplicateFacesCommand)] = "Kitbash.Operation.DuplicateFaces",
        [typeof(AssignMaterialFromOtherMeshCommand)] = "Kitbash.Menu.Tools.CopyMaterial",
        [typeof(ConstructPrimitiveCommand)] = "Kitbash.CommandHint.ConstructPrimitive",
        [typeof(RemapBoneIndexesCommand)] = "Kitbash.Operation.RemapBones",
        [typeof(PinMeshToVertexCommand)] = "Kitbash.CommandHint.PinMeshesToVertex",
        [typeof(SkinWrapRiggingCommand)] = "Kitbash.CommandHint.SkinWrapRigging",
        [typeof(SceneNodePropertyChangeCommand<>)] = "Kitbash.CommandHint.EditSidebarProperty",
        [typeof(DuplicateFrameBoneCommand)] = "Kitbash.Operation.DuplicateFrame",
        [typeof(DeleteFrameBoneCommand)] = "Kitbash.Operation.DeleteFrame",
        [typeof(PasteWholeTransformBoneCommand)] = "Kitbash.Operation.PasteBones",
        [typeof(PasteIntoSelectedBonesTransformBoneCommand)] = "Kitbash.Operation.PasteBones",
        [typeof(PasteWholeTransformFromClipboardBoneCommand)] = "Kitbash.Operation.PasteBones",
        [typeof(PasteWholeInRangeTransformFromClipboardBoneCommand)] = "Kitbash.Operation.PasteBones",
        [typeof(PasteIntoSelectedBonesTransformFromClipboardBoneCommand)] = "Kitbash.Operation.PasteBones",
        [typeof(PasteIntoSelectedBonesInRangeTransformFromClipboardBoneCommand)] = "Kitbash.Operation.PasteBones",
        [typeof(InterpolateFramesBoneCommand)] = "Kitbash.Operation.InterpolateFrames",
        [typeof(InterpolateFramesSelectedBonesBoneCommand)] = "Kitbash.Operation.InterpolateFrames",
    };
}
