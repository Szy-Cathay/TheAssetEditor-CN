using System.Text;
using System.Windows.Controls;
using Editors.Shared.Core.Common;
using Editors.Shared.Core.Common.ReferenceModel;
using GameWorld.Core.Animation;
using GameWorld.Core.SceneNodes;
using GameWorld.Core.Services;
using Moq;
using NUnit.Framework;
using Shared.Core.Events;
using Shared.Core.PackFiles;
using Shared.Core.Services;
using Shared.Core.Settings;
using Shared.GameFormats.AnimationPack;
using Assert = NUnit.Framework.Assert;
using SceneObjectView = AnimationEditor.Common.ReferenceModel.SceneObjectView;

namespace AssetEditorTests;

[NonParallelizable]
public class JsonFragmentSelectionTests
{
    [TestCase("humanoid01", "aecn_diagnostic_local_axes")]
    [TestCase("male01", "inf_flag_default")]
    public void SelectingJsonFragment_KeepsSelectedSourceAndPlayableStandSlot(string skeleton, string name)
    {
        SuperViewWorkspaceLayoutTests.WithTheme(ThemeType.DarkTheme, () =>
        {
            var bytes = Encoding.UTF8.GetBytes($$"""
                {
                  "Properties": { "Combined": false, "FragmentName": "{{name}}", "SkeletonName": "{{skeleton}}" },
                  "SlotData": {
                    "STAND": {
                      "Config": { "BlendInTime": 0, "Disable": false, "EquipmentDisplay": 0,
                                  "Ignore": false, "SelectionWeight": 1, "SingleFrameVariant": false },
                      "Entries": { "#1": { "animation": "animations/battle/diagnostic.anim", "metadata": [] } }
                    }
                  }
                }
                """);
            var fullPath = $"animations/database/battle/fragments/{name}.txt";
            var fragment = ThreeKingdomsAnimationFragment.Read(fullPath, bytes);
            var data = new SceneObject("Fragment selection")
            {
                SkeletonSceneNode = new SkeletonNode(null!),
                Player = new AnimationPlayer(),
            };
            data.SkeletonName.Value = $"animations/skeletons/{skeleton}.anim";
            var files = Mock.Of<IPackFileService>();
            var sceneEditor = new SceneObjectEditor(null!, null!, files, null!, null!, Mock.Of<IEventHub>());
            var model = new SceneObjectViewModel(Mock.Of<IUiCommandFactory>(), files,
                Mock.Of<IStandardDialogs>(), data, "Fragment selection", sceneEditor,
                Mock.Of<ISkeletonAnimationLookUpHelper>(), null!);
            model.FragAndSlotSelection.FragmentList.UpdatePossibleValues([fragment]);
            model.FragAndSlotSelection.FragmentList.Filter = name;
            var view = new SceneObjectView { DataContext = model };
            SuperViewWorkspaceLayoutTests.Layout(view, 720, 700);
            var fragmentCombo = SuperViewWorkspaceLayoutTests.Descendants<ComboBox>(view)
                .Single(combo => combo.DisplayMemberPath == "FileName");
            var slotCombo = SuperViewWorkspaceLayoutTests.Descendants<ComboBox>(view)
                .Single(combo => combo.DisplayMemberPath == "SlotName");

            fragmentCombo.SetCurrentValue(ComboBox.SelectedItemProperty, fragment);
            SuperViewWorkspaceLayoutTests.Layout(view, 720, 700);

            Assert.Multiple(() =>
            {
                Assert.That(fragmentCombo.SelectedItem, Is.SameAs(fragment));
                Assert.That(model.FragAndSlotSelection.FragmentList.SelectedItem, Is.SameAs(fragment));
                Assert.That(fragmentCombo.Text, Is.EqualTo(name + ".txt"));
                Assert.That(slotCombo.Items.Count, Is.EqualTo(1));
                Assert.That(model.FragAndSlotSelection.FragmentSlotList.Values.Select(slot => slot.SlotName),
                    Is.EqualTo(new[] { "STAND" }));
            });
            slotCombo.SetCurrentValue(ComboBox.SelectedItemProperty, fragment.Entries.Single());
            SuperViewWorkspaceLayoutTests.Layout(view, 720, 700);
            Assert.That(slotCombo.Text, Is.EqualTo("STAND"));
            Assert.That(model.FragAndSlotSelection.FragmentList.SelectedItem, Is.SameAs(fragment));
            Assert.That(fragment.FullPath, Is.EqualTo(fullPath.Replace('/', '\\')));
            Assert.That(fragment.PackFileReference.FileName, Is.EqualTo(fragment.FullPath));
        });
    }
}
