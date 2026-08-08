using System.Reflection;
using Editors.AnimationVisualEditors.AnimationKeyframeEditor;
using Editors.Shared.Core.Common;
using Editors.Shared.Core.Common.ReferenceModel;
using GameWorld.Core.Commands;
using GameWorld.Core.Commands.Object;
using GameWorld.Core.Components.Gizmo;
using GameWorld.Core.Components.Selection;
using GameWorld.Core.Rendering.Geometry;
using GameWorld.Core.SceneNodes;
using GameWorld.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Shared.Core.Events;
using Shared.Core.PackFiles;
using Shared.Core.Services;

namespace AssetEditorTests
{
    [TestClass]
    [DoNotParallelize]
    public class GizmoToolboxSubscriptionTests
    {
        [TestMethod]
        public void EnterSelectMode_WhenStateIsUnchanged_SubscribesOnlyOnce()
        {
            using var harness = CreateHarness(useNoOpSelectionCommand: true);
            var state = (BoneSelectionState)harness.SelectionManager.CreateSelectionSate(
                GeometrySelectionMode.Bone,
                harness.Selectable);

            harness.Editor.EnterSelectMode();
            harness.Editor.EnterSelectMode();

            Assert.AreSame(state, harness.SelectionManager.GetState());
            Assert.AreEqual(2, GetSelectionChangedSubscriberCount(state));
            Assert.AreEqual(1, GetBoneModificationSubscriberCount(state));
        }

        [TestMethod]
        public void EnterSelectMode_WhenStateIsReplaced_MovesSubscriptionsToNewState()
        {
            using var harness = CreateHarness(useNoOpSelectionCommand: false);
            harness.SelectionManager.CreateSelectionSate(
                GeometrySelectionMode.Object,
                null!);

            harness.Editor.EnterSelectMode();
            var oldState = harness.SelectionManager.GetState<BoneSelectionState>();

            harness.Editor.EnterSelectMode();
            var newState = harness.SelectionManager.GetState<BoneSelectionState>();

            Assert.IsNotNull(oldState);
            Assert.IsNotNull(newState);
            Assert.AreNotSame(oldState, newState);
            Assert.AreEqual(0, GetSelectionChangedSubscriberCount(oldState));
            Assert.AreEqual(0, GetBoneModificationSubscriberCount(oldState));
            Assert.AreEqual(2, GetSelectionChangedSubscriberCount(newState));
            Assert.AreEqual(1, GetBoneModificationSubscriberCount(newState));
        }

        [TestMethod]
        public void ObjectSelectionModeUndoRedo_WhenBoneStateIsRestored_KeepsSubscriptionsOnCurrentState()
        {
            using var harness = CreateHarness(useNoOpSelectionCommand: true);
            var initialState = (BoneSelectionState)harness.SelectionManager.CreateSelectionSate(
                GeometrySelectionMode.Bone,
                harness.Selectable);
            harness.Editor.EnterSelectMode();

            var modeCommand = new ObjectSelectionModeCommand(harness.SelectionManager);
            modeCommand.Configure(harness.Selectable, GeometrySelectionMode.Object);
            Assert.IsTrue(harness.CommandExecutor.ExecuteCommand(modeCommand));

            var objectState = harness.SelectionManager.GetState<ObjectSelectionState>();
            Assert.IsNotNull(objectState);
            Assert.AreEqual(0, GetSelectionChangedSubscriberCount(initialState));
            Assert.AreEqual(0, GetBoneModificationSubscriberCount(initialState));
            Assert.AreEqual(2, GetSelectionChangedSubscriberCount(objectState));

            Assert.IsTrue(harness.CommandExecutor.Undo());
            var restoredState = harness.SelectionManager.GetState<BoneSelectionState>();
            Assert.IsNotNull(restoredState);
            Assert.AreNotSame(initialState, restoredState);
            Assert.AreEqual(2, GetSelectionChangedSubscriberCount(restoredState));
            Assert.AreEqual(1, GetBoneModificationSubscriberCount(restoredState));

            restoredState.ModifySelection([1], false);
            CollectionAssert.AreEqual(new[] { 1 }, harness.Editor.GetSelectedBones());

            restoredState.CurrentFrame = 7;
            restoredState.TriggerModifiedBoneEvent([2]);
            CollectionAssert.AreEqual(new[] { 2 }, harness.Editor.GetModifiedBones());
            Assert.AreEqual(7, harness.Editor.GetModifiedFrameNr());

            Assert.IsTrue(harness.CommandExecutor.Redo());
            Assert.AreEqual(0, GetSelectionChangedSubscriberCount(restoredState));
            Assert.AreEqual(0, GetBoneModificationSubscriberCount(restoredState));

            Assert.IsTrue(harness.CommandExecutor.Undo());
            var restoredAgainState = harness.SelectionManager.GetState<BoneSelectionState>();
            Assert.IsNotNull(restoredAgainState);
            Assert.AreNotSame(restoredState, restoredAgainState);
            Assert.AreEqual(2, GetSelectionChangedSubscriberCount(restoredAgainState));
            Assert.AreEqual(1, GetBoneModificationSubscriberCount(restoredAgainState));
        }

        private static EditorHarness CreateHarness(bool useNoOpSelectionCommand)
        {
            var eventHub = new Mock<IEventHub>();
            var selectionManager = new SelectionManager(
                eventHub.Object);
            var commandExecutor = new CommandExecutor(eventHub.Object);

            var services = new ServiceCollection();
            services.AddSingleton(selectionManager);
            if (useNoOpSelectionCommand)
                services.AddTransient<ObjectSelectionCommand, NoOpObjectSelectionCommand>();
            else
                services.AddTransient<ObjectSelectionCommand>();
            services.AddTransient<ObjectSelectionModeCommand>();

            var serviceProvider = services.BuildServiceProvider();
            var commandFactory = new CommandFactory(serviceProvider, commandExecutor);
            var gizmoComponent = new AnimationBoneGizmoComponent(
                eventHub.Object,
                null!,
                null!,
                null!,
                commandExecutor,
                null!,
                null!,
                commandFactory,
                selectionManager);
            var selectionComponent = new AnimationBoneSelectionComponent(
                null!,
                null!,
                null!,
                selectionManager,
                commandFactory,
                null!,
                null!,
                null!);
            var boneSelectionHighlight =
                new BoneSelectionHighlightComponent(
                    selectionManager,
                    null!);

            var editor = new AnimationKeyframeEditorViewModel(
                new Mock<IPackFileService>().Object,
                new Mock<ISkeletonAnimationLookUpHelper>().Object,
                selectionComponent,
                null!,
                null!,
                null!,
                commandFactory,
                selectionManager,
                gizmoComponent,
                boneSelectionHighlight,
                commandExecutor,
                new Mock<IFileSaveService>().Object,
                Mock.Of<IStandardDialogs>());

            var root = new GroupNode("rider");
            root.AddObject(new GroupNode("placeholder"));
            var variantRoot = root.AddObject(new GroupNode("variant"));
            var selectable = variantRoot.AddObject(new TestSelectableNode("mesh"));
            var rider = new SceneObject("rider")
            {
                ParentNode = root,
                Player = new GameWorld.Core.Animation.AnimationPlayer(),
            };
            var mount = new SceneObject("mount")
            {
                ParentNode = new GroupNode("mount"),
                Player = new GameWorld.Core.Animation.AnimationPlayer(),
            };

            SetPrivateField(editor, "_rider", rider);
            SetPrivateField(editor, "_mount", mount);
            var ikBone = new SkeletonBoneNode
            {
                BoneIndex = 0,
                BoneName = "root",
            };
            editor.ModelBoneListForIKEndBone.UpdatePossibleValues([ikBone]);
            editor.ModelBoneListForIKEndBone.SelectedItem = ikBone;

            var toolboxType = typeof(AnimationKeyframeEditorViewModel).Assembly.GetType(
                "AnimationEditor.AnimationKeyframeEditor.GizmoToolbox",
                throwOnError: true);
            var toolbox = Activator.CreateInstance(
                toolboxType!,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                binder: null,
                args: [editor],
                culture: null);
            SetPrivateField(editor, "_gizmoToolbox", toolbox);

            var interpolationType = typeof(AnimationKeyframeEditorViewModel).Assembly.GetType(
                "AnimationEditor.AnimationKeyframeEditor.InterpolateBetweenPose",
                throwOnError: true);
            var interpolation = Activator.CreateInstance(
                interpolationType!,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                binder: null,
                args: [editor],
                culture: null);
            SetPrivateField(editor, "_interpolateBetweenPose", interpolation);

            return new EditorHarness(
                editor,
                selectionManager,
                commandExecutor,
                selectable,
                serviceProvider);
        }

        private static int GetSelectionChangedSubscriberCount(ISelectionState state)
        {
            var eventField = state.GetType().GetField(
                "SelectionChanged",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(eventField);
            return (eventField.GetValue(state) as Delegate)?.GetInvocationList().Length ?? 0;
        }

        private static int GetBoneModificationSubscriberCount(BoneSelectionState state)
        {
            var countProperty = typeof(BoneSelectionState).GetProperty(
                "BoneModificationSubscriberCount",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(countProperty);
            return (int)countProperty.GetValue(state)!;
        }

        private static void SetPrivateField(object target, string fieldName, object? value)
        {
            var field = target.GetType().GetField(
                fieldName,
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(field);
            field.SetValue(target, value);
        }

        private sealed record EditorHarness(
            AnimationKeyframeEditorViewModel Editor,
            SelectionManager SelectionManager,
            CommandExecutor CommandExecutor,
            ISelectable Selectable,
            ServiceProvider ServiceProvider) : IDisposable
        {
            public void Dispose()
            {
                ServiceProvider.Dispose();
            }
        }

        private sealed class TestSelectableNode(string name) : SceneNode, ISelectable
        {
            public MeshObject Geometry { get; set; } = null!;
            public bool IsSelectable { get; set; } = true;

            public override ISceneNode CreateCopyInstance()
            {
                return new TestSelectableNode(name);
            }
        }

        private sealed class NoOpObjectSelectionCommand(SelectionManager selectionManager)
            : ObjectSelectionCommand(selectionManager), ICommand
        {
            void ICommand.Execute()
            {
            }
        }
    }
}
