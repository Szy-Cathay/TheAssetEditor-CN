using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Threading;
using Editors.KitbasherEditor.ChildEditors.MeshFitter;
using GameWorld.Core.Animation;
using GameWorld.Core.Components;
using GameWorld.Core.SceneNodes;
using Microsoft.Xaml.Behaviors;
using Moq;
using NUnit.Framework;
using Shared.Core.Events;
using Shared.GameFormats.Animation;
using Shared.GameFormats.RigidModel.Transforms;
using Shared.Ui.Common.Behaviors;
using Shared.Ui.Editors.BoneMapping;
using Assert = NUnit.Framework.Assert;

namespace AssetEditorTests;

[NonParallelizable]
public class BoneMappingSelectionTests
{
    [Test]
    public void ClearMapping_DeselectsNestedTargetContainerAndAllowsTheSameBoneToBeMappedAgain()
    {
        WpfTestApplicationHost.InvokeWithThemeResources(WpfTestApplicationHost.EmptyServices, () =>
        {
            var (model, source, target) = CreateMapping();
            var (tree, behavior, targetContainer) = CreateTargetTree(model, target);
            try
            {
                targetContainer.SetCurrentValue(TreeViewItem.IsSelectedProperty, true);
                DrainBindings();
                Assert.That(model.ParentModelBones.SelectedItem, Is.SameAs(target));
                Assert.That(source.MappedBoneIndex.Value, Is.EqualTo(target.BoneIndex.Value));

                model.ClearBindingSelf();
                DrainBindings();

                Assert.That(model.MeshBones.SelectedItem, Is.SameAs(source));
                Assert.That(model.ParentModelBones.SelectedItem, Is.Null);
                Assert.That(source.MappedBoneIndex.Value, Is.EqualTo(-1));
                Assert.That(tree.SelectedItem, Is.Null);
                Assert.That(targetContainer.IsSelected, Is.False);
                Assert.That(BindingOperations.IsDataBound(behavior,
                    BindableSelectedItemBehavior.SelectedItemProperty), Is.True);

                targetContainer.SetCurrentValue(TreeViewItem.IsSelectedProperty, true);
                DrainBindings();
                Assert.That(tree.SelectedItem, Is.SameAs(target));
                Assert.That(model.ParentModelBones.SelectedItem, Is.SameAs(target));
                Assert.That(source.MappedBoneIndex.Value, Is.EqualTo(target.BoneIndex.Value));
            }
            finally
            {
                Interaction.GetBehaviors(tree).Remove(behavior);
            }
        });
    }

    [Test]
    public void NonNullBoundSelection_DoesNotProgrammaticallyChangeTheSelectedTreeContainer()
    {
        WpfTestApplicationHost.InvokeWithThemeResources(WpfTestApplicationHost.EmptyServices, () =>
        {
            var (model, _, target) = CreateMapping();
            var (tree, behavior, targetContainer) = CreateTargetTree(model, target);
            try
            {
                var root = (TreeViewItem)tree.ItemContainerGenerator.ContainerFromIndex(0);
                root.SetCurrentValue(TreeViewItem.IsSelectedProperty, true);
                DrainBindings();

                model.ParentModelBones.SelectedItem = target;
                DrainBindings();

                Assert.That(tree.SelectedItem, Is.SameAs(model.ParentModelBones.PossibleValues[0]));
                Assert.That(root.IsSelected, Is.True);
                Assert.That(targetContainer.IsSelected, Is.False);
                Assert.That(model.ParentModelBones.SelectedItem, Is.SameAs(target));
            }
            finally
            {
                Interaction.GetBehaviors(tree).Remove(behavior);
            }
        });
    }

    [TestCase(false)]
    [TestCase(true)]
    public void MeshFitter_ClearMappingKeepsOffsetsAndUnfinishedInputWhileRefreshingHighlights(bool unfinishedInput)
    {
        WpfTestApplicationHost.InvokeWithThemeResources(WpfTestApplicationHost.EmptyServices, () =>
        {
            var file = CreateSkeletonFile();
            var targetSkeleton = GameSkeleton.CreateFromAnimationFile(file, new AnimationPlayer());
            var targetNode = new SkeletonNode(targetSkeleton);
            var scene = new SceneManager(null!, null!, Mock.Of<IEventHub>());
            scene.RootNode.AddObject(new MainEditableNode(SpecialNodes.EditableModel, targetNode, null!));
            var source = new AnimatedBone(1, "spine_0");
            var target = new AnimatedBone(1, "spine_0");
            var sourceRoot = new AnimatedBone(0, "root");
            var targetRoot = new AnimatedBone(0, "root");
            sourceRoot.Children.Add(source);
            targetRoot.Children.Add(target);
            using var model = new MeshFitterViewModel(null!, new AnimationsContainerComponent(), scene);
            model.Initialize(new RemappedAnimatedBoneConfiguration
            {
                MeshBones = [sourceRoot], ParentModelBones = [targetRoot],
            }, [], targetSkeleton, file);
            model.MeshBones.SelectedItem = source;
            model.ParentModelBones.SelectedItem = target;
            Assert.That(targetNode.SelectedBoneIndex, Is.EqualTo(1));
            model.BonePositionOffset.Set(1.25f, -0.5f, 0.75f);
            model.BoneRotationOffset.Set(10, -20, 30);
            model.BoneScaleFactor.Value = 1.5;
            var expectedPosition = model.BonePositionOffset.GetAsVector3();
            var expectedRotation = model.BoneRotationOffset.GetAsVector3();
            var expectedScale = (float)model.BoneScaleFactor.Value;
            Assert.That(source.BonePosOffset, Is.EqualTo(expectedPosition));
            Assert.That(source.BoneRotOffset, Is.EqualTo(expectedRotation));
            Assert.That(source.BoneScaleOffset, Is.EqualTo(expectedScale));
            if (unfinishedInput)
            {
                model.BonePositionOffset.X.TextValue = "-";
                Assert.That(model.HasValidTransform, Is.False);
            }
            var expectedInput = new[]
            {
                model.BonePositionOffset.X.TextValue, model.BonePositionOffset.Y.TextValue,
                model.BonePositionOffset.Z.TextValue, model.BoneRotationOffset.X.TextValue,
                model.BoneRotationOffset.Y.TextValue, model.BoneRotationOffset.Z.TextValue,
                model.BoneScaleFactor.TextValue,
            };

            model.ClearBindingSelf();

            Assert.That(source.MappedBoneIndex.Value, Is.EqualTo(-1));
            Assert.That(targetNode.SelectedBoneIndex, Is.EqualTo(-1));
            Assert.That(model.MeshBones.SelectedItem, Is.SameAs(source));
            Assert.That(scene.RootNode.Children.OfType<SkeletonNode>().Single().SelectedBoneIndex, Is.EqualTo(1));
            Assert.That(model.IsBoneSelected.Value, Is.True);
            Assert.That(source.BonePosOffset, Is.EqualTo(expectedPosition));
            Assert.That(source.BoneRotOffset, Is.EqualTo(expectedRotation));
            Assert.That(source.BoneScaleOffset, Is.EqualTo(expectedScale));
            Assert.That(new[]
            {
                model.BonePositionOffset.X.TextValue, model.BonePositionOffset.Y.TextValue,
                model.BonePositionOffset.Z.TextValue, model.BoneRotationOffset.X.TextValue,
                model.BoneRotationOffset.Y.TextValue, model.BoneRotationOffset.Z.TextValue,
                model.BoneScaleFactor.TextValue,
            }, Is.EqualTo(expectedInput));
            if (unfinishedInput)
            {
                Assert.That(model.BonePositionOffset.X.TextValue, Is.EqualTo("-"));
                Assert.That(model.HasValidTransform, Is.False);
            }
        });
    }

    private static (BoneMappingViewModel Model, AnimatedBone Source, AnimatedBone Target) CreateMapping()
    {
        var source = new AnimatedBone(7, "mesh_spine");
        var target = new AnimatedBone(2, "spine_0");
        source.IsUsedByCurrentModel.Value = true;
        target.IsUsedByCurrentModel.Value = true;
        var targetRoot = new AnimatedBone(0, "root");
        targetRoot.Children.Add(target);
        var model = new BoneMappingViewModel();
        model.Initialize(new RemappedAnimatedBoneConfiguration
        {
            MeshBones = [source], ParentModelBones = [targetRoot],
        });
        Assert.That(target.IsVisible.Value, Is.True);
        Assert.That(targetRoot.IsVisible.Value, Is.True);
        return (model, source, target);
    }

    private static (TreeView Tree, BindableSelectedItemBehavior Behavior, TreeViewItem Container)
        CreateTargetTree(BoneMappingViewModel model, AnimatedBone target)
    {
        var expanded = new Style(typeof(TreeViewItem), (Style)Application.Current.FindResource("AeTree.Item"));
        expanded.Setters.Add(new Setter(TreeViewItem.IsExpandedProperty, true));
        var label = new FrameworkElementFactory(typeof(TextBlock));
        label.SetBinding(TextBlock.TextProperty, new Binding("Name.Value"));
        var template = new HierarchicalDataTemplate(typeof(AnimatedBone))
        {
            ItemsSource = new Binding(nameof(AnimatedBone.Children)),
            ItemContainerStyle = expanded,
            VisualTree = label,
        };
        var tree = new TreeView
        {
            Style = (Style)Application.Current.FindResource("AeTree.View"),
            Width = 420, Height = 300, ItemsSource = model.ParentModelBones.Values,
            ItemTemplate = template, ItemContainerStyle = expanded,
        };
        var behavior = new BindableSelectedItemBehavior();
        BindingOperations.SetBinding(behavior, BindableSelectedItemBehavior.SelectedItemProperty,
            new Binding("ParentModelBones.SelectedItem")
            {
                Source = model, Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged,
            });
        Interaction.GetBehaviors(tree).Add(behavior);
        tree.ApplyTemplate();
        tree.Measure(new Size(420, 300));
        tree.Arrange(new Rect(0, 0, 420, 300));
        tree.UpdateLayout();
        tree.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
        tree.UpdateLayout();
        var container = tree.ItemContainerGenerator.ContainerFromItemRecursive(target);
        Assert.That(container, Is.Not.Null, "The nested TreeViewItem must actually be generated.");
        return (tree, behavior, container);
    }

    private static void DrainBindings() =>
        Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.DataBind);

    private static AnimationFile CreateSkeletonFile()
    {
        var file = new AnimationFile
        {
            Header = new AnimationFile.AnimationHeader { SkeletonName = "bone_mapping_test" },
            Bones = [
                new() { Id = 0, Name = "root", ParentId = -1 },
                new() { Id = 1, Name = "spine_0", ParentId = 0 },
            ],
        };
        var frame = new AnimationFile.Frame();
        frame.Transforms.AddRange([new RmvVector3(), new RmvVector3(0, 1, 0)]);
        frame.Quaternion.AddRange([new RmvVector4(0, 0, 0, 1), new RmvVector4(0, 0, 0, 1)]);
        var part = new AnimationFile.AnimationPart();
        part.TranslationMappings.AddRange([new(0), new(1)]);
        part.RotationMappings.AddRange([new(0), new(1)]);
        part.DynamicFrames.Add(frame);
        file.AnimationParts.Add(part);
        return file;
    }
}
