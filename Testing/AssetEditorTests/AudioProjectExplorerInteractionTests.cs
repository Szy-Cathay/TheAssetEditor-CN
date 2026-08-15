using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Threading;
using Editors.Audio.AudioEditor.Core;
using Editors.Audio.AudioEditor.Events;
using Editors.Audio.AudioEditor.Presentation.AudioProjectExplorer;
using Editors.Audio.AudioEditor.Presentation.Shared.Models;
using Editors.Audio.Shared.AudioProject.Models;
using Editors.Audio.Shared.GameInformation.Warhammer3;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using Shared.Core.Services;

using Assert = NUnit.Framework.Assert;

namespace AssetEditorTests;

[NonParallelizable]
public class AudioProjectExplorerInteractionTests
{
    [SetUp]
    public void SetUp() => new LocalizationManager().LoadLanguage();

    [Test]
    public void GeneralFiltersAndButtons_UpdateTheRenderedTree()
    {
        using var services = new ServiceCollection()
            .AddSingleton(LocalizationManager.Instance)
            .BuildServiceProvider();
        WpfTestApplicationHost.InvokeWithThemeResources(services, () =>
        {
            var (viewModel, view, window) = CreateView();
            try
            {
                var actionEventType = Flatten(viewModel.AudioProjectTree)
                    .First(node => node.Type ==
                        AudioProjectTreeNodeType.ActionEventType);
                var actionItem = FindTreeViewItem(view, actionEventType);
                var showEdited = FindCheckBox(view, "ShowEditedItemsOnly");
                var showAction = FindCheckBox(view, "ShowActionEvents");
                var showDialogue = FindCheckBox(view, "ShowDialogueEvents");
                var collapseOrExpand = FindButton(
                    view,
                    "CollapseOrExpandTreeCommand");
                var reset = FindButton(view, "ResetFiltersCommand");

                SetChecked(showAction, false);
                FlushBindings(view);
                Assert.Multiple(() =>
                {
                    Assert.That(
                        BindingOperations.GetBindingExpression(
                            actionItem,
                            UIElement.VisibilityProperty),
                        Is.Not.Null);
                    Assert.That(viewModel.ShowActionEvents, Is.False);
                    Assert.That(actionEventType.IsVisible, Is.False);
                    Assert.That(
                        actionItem.Visibility,
                        Is.EqualTo(Visibility.Collapsed));
                });

                SetChecked(showAction, true);
                SetChecked(showDialogue, false);
                Assert.That(viewModel.ShowDialogueEvents, Is.False);

                SetChecked(showEdited, true);
                Assert.That(viewModel.ShowEditedItemsOnly, Is.True);

                Execute(collapseOrExpand);
                Assert.That(
                    Flatten(viewModel.AudioProjectTree)
                        .Where(node => node.IsVisible)
                        .All(node => !node.IsExpanded),
                    Is.True);

                Execute(collapseOrExpand);
                Assert.That(
                    Flatten(viewModel.AudioProjectTree)
                        .Where(node => node.IsVisible)
                        .All(node => node.IsExpanded),
                    Is.True);

                Execute(reset);
                FlushBindings(view);
                Assert.Multiple(() =>
                {
                    Assert.That(viewModel.ShowEditedItemsOnly, Is.False);
                    Assert.That(viewModel.ShowActionEvents, Is.True);
                    Assert.That(viewModel.ShowDialogueEvents, Is.True);
                    Assert.That(actionEventType.IsVisible, Is.True);
                    Assert.That(
                        actionItem.Visibility,
                        Is.EqualTo(Visibility.Visible));
                });
            }
            finally
            {
                viewModel.Dispose();
                window.Close();
            }
        });
    }

    [Test]
    public void DialogueFilters_UpdateTheRenderedTree()
    {
        using var services = new ServiceCollection()
            .AddSingleton(LocalizationManager.Instance)
            .BuildServiceProvider();
        WpfTestApplicationHost.InvokeWithThemeResources(services, () =>
        {
            var (viewModel, view, window) = CreateView();
            try
            {
                var dialogueEventBranches = Flatten(
                        viewModel.AudioProjectTree)
                    .Where(node => node.Type ==
                        AudioProjectTreeNodeType.DialogueEvents)
                    .ToList();
                var dialogueEvents = dialogueEventBranches.Single(node =>
                    node.GameSoundBank == Wh3SoundBank.CampaignVO);
                viewModel.SelectedNode = Flatten(viewModel.AudioProjectTree)
                    .Single(node => node.Type ==
                        AudioProjectTreeNodeType.StateGroup);
                view.UpdateLayout();

                var typeComboBox = (ComboBox)view.FindName(
                    "DialogueEventTypeFilterComboBox");
                var profileComboBox = (ComboBox)view.FindName(
                    "DialogueEventProfileFilterComboBox");
                Assert.Multiple(() =>
                {
                    Assert.That(typeComboBox.IsEnabled, Is.True);
                    Assert.That(typeComboBox.Items.Count, Is.GreaterThan(0));
                    Assert.That(profileComboBox.IsEnabled, Is.True);
                    Assert.That(profileComboBox.Items.Count, Is.GreaterThan(0));
                });
                var type = FindPartialType(dialogueEvents.GameSoundBank);
                var profile = FindPartialProfile(dialogueEvents.GameSoundBank);

                SetSelectedItem(typeComboBox, type);
                foreach (var branch in dialogueEventBranches)
                {
                    AssertDialogueVisibility(
                        view,
                        branch,
                        node => Wh3DialogueEventInformation.Information
                            .Single(item => item.Name == node.Name)
                            .DialogueEventTypes.Contains(type));
                }

                SetSelectedItem(
                    typeComboBox,
                    Wh3DialogueEventType.TypeShowAll);
                SetSelectedItem(profileComboBox, profile);
                foreach (var branch in dialogueEventBranches)
                {
                    AssertDialogueVisibility(
                        view,
                        branch,
                        node => Wh3DialogueEventInformation.Information
                            .Single(item => item.Name == node.Name)
                            .UnitProfiles.Contains(profile));
                }

                Execute(FindButton(view, "ResetFiltersCommand"));
                foreach (var branch in dialogueEventBranches)
                    AssertDialogueVisibility(view, branch, _ => true);
            }
            finally
            {
                viewModel.Dispose();
                window.Close();
            }
        });
    }

    private static (
        AudioProjectExplorerViewModel ViewModel,
        AudioProjectExplorerView View,
        Window Window) CreateView()
    {
        var dialogueDefinitions = Wh3DialogueEventInformation.Information
            .Where(item => item.SoundBank == Wh3SoundBank.CampaignVO)
            .ToList();
        var campaignDialogueSoundBank = new SoundBank(
            "campaign_vo_test",
            Wh3SoundBank.CampaignVO,
            "english(uk)")
        {
            DialogueEvents = dialogueDefinitions
                .Select(item => new DialogueEvent(item.Name))
                .ToList(),
        };
        var battleDialogueSoundBank = new SoundBank(
            "battle_vo_test",
            Wh3SoundBank.BattleVO,
            "english(uk)")
        {
            DialogueEvents = Wh3DialogueEventInformation.Information
                .Where(item => item.SoundBank == Wh3SoundBank.BattleVO)
                .Select(item => new DialogueEvent(item.Name))
                .ToList(),
        };
        var actionSoundBank = new SoundBank(
            "battle_individual_magic_test",
            Wh3SoundBank.BattleIndividualMagic,
            "sfx");
        var audioProject = new AudioProjectFile
        {
            SoundBanks =
            [
                actionSoundBank,
                campaignDialogueSoundBank,
                battleDialogueSoundBank
            ],
            StateGroups =
            [
                StateGroup.CreateForAudioProjectFile("VO_Actor", [])
            ],
        };
        var eventHub = new TestEventHub();
        var state = new AudioEditorStateService
        {
            AudioProject = audioProject,
            AudioProjectFileName = "audio_test.aproj",
        };
        var viewModel = new AudioProjectExplorerViewModel(
            eventHub,
            state,
            new AudioProjectTreeBuilderService(state),
            new AudioProjectTreeFilterService(state));
        eventHub.Publish(new AudioProjectLoadedEvent());
        SetExpanded(viewModel.AudioProjectTree, true);

        var view = new AudioProjectExplorerView
        {
            DataContext = viewModel,
            Width = 900,
            Height = 700,
        };
        var window = new Window
        {
            Content = view,
            Width = 920,
            Height = 720,
            ShowActivated = false,
            ShowInTaskbar = false,
            WindowStyle = WindowStyle.None,
            Left = -10000,
            Top = -10000,
        };
        window.Show();
        window.UpdateLayout();
        return (viewModel, view, window);
    }

    private static CheckBox FindCheckBox(
        DependencyObject root,
        string bindingPath) =>
        FindVisualDescendants<CheckBox>(root).Single(checkBox =>
            BindingOperations.GetBindingExpression(
                    checkBox,
                    ToggleButton.IsCheckedProperty)
                ?.ParentBinding.Path.Path == bindingPath);

    private static Button FindButton(
        DependencyObject root,
        string bindingPath) =>
        FindVisualDescendants<Button>(root).Single(button =>
            BindingOperations.GetBindingExpression(
                    button,
                    ButtonBase.CommandProperty)
                ?.ParentBinding.Path.Path == bindingPath);

    private static void SetChecked(CheckBox checkBox, bool value)
    {
        checkBox.IsChecked = value;
        checkBox.GetBindingExpression(ToggleButton.IsCheckedProperty)
            ?.UpdateSource();
    }

    private static void SetSelectedItem(
        ComboBox comboBox,
        object value)
    {
        comboBox.SelectedItem = value;
        comboBox.GetBindingExpression(Selector.SelectedValueProperty)
            ?.UpdateSource();
        comboBox.GetBindingExpression(Selector.SelectedItemProperty)
            ?.UpdateSource();
    }

    private static void Execute(Button button)
    {
        Assert.That(button.Command, Is.Not.Null);
        button.Command.Execute(button.CommandParameter);
    }

    private static TreeViewItem FindTreeViewItem(
        DependencyObject root,
        AudioProjectTreeNode node)
    {
        var treeView = FindVisualDescendants<TreeView>(root).Single();
        var item = FindTreeViewItem(treeView, node);
        Assert.That(item, Is.Not.Null);
        return item!;
    }

    private static TreeViewItem? FindTreeViewItem(
        ItemsControl parent,
        AudioProjectTreeNode target)
    {
        parent.ApplyTemplate();
        parent.UpdateLayout();
        foreach (var node in parent.Items.OfType<AudioProjectTreeNode>())
        {
            var item = parent.ItemContainerGenerator.ContainerFromItem(node)
                as TreeViewItem;
            if (item == null)
                continue;
            if (ReferenceEquals(node, target))
                return item;

            item.IsExpanded = true;
            item.ApplyTemplate();
            item.UpdateLayout();
            var child = FindTreeViewItem(item, target);
            if (child != null)
                return child;
        }

        return null;
    }

    private static void AssertDialogueVisibility(
        AudioProjectExplorerView view,
        AudioProjectTreeNode dialogueEvents,
        Func<AudioProjectTreeNode, bool> expectedVisibility)
    {
        FlushBindings(view);
        foreach (var node in dialogueEvents.Children)
        {
            Assert.Multiple(() =>
            {
                Assert.That(node.IsVisible, Is.EqualTo(expectedVisibility(node)));
                Assert.That(
                    FindTreeViewItem(view, node).Visibility,
                    Is.EqualTo(expectedVisibility(node)
                        ? Visibility.Visible
                        : Visibility.Collapsed));
            });
        }
    }

    private static void FlushBindings(FrameworkElement view)
    {
        view.Dispatcher.Invoke(
            () => { },
            DispatcherPriority.DataBind);
        view.UpdateLayout();
    }

    private static Wh3DialogueEventType FindPartialType(
        Wh3SoundBank soundBank) =>
        Enum.GetValues<Wh3DialogueEventType>()
            .Where(type => type != Wh3DialogueEventType.TypeShowAll)
            .First(type =>
            {
                var matches = Wh3DialogueEventInformation.Information
                    .Count(item => item.SoundBank == soundBank &&
                        item.DialogueEventTypes.Contains(type));
                var total = Wh3DialogueEventInformation.Information
                    .Count(item => item.SoundBank == soundBank);
                return matches > 0 && matches < total;
            });

    private static Wh3DialogueEventUnitProfile FindPartialProfile(
        Wh3SoundBank soundBank) =>
        Enum.GetValues<Wh3DialogueEventUnitProfile>()
            .Where(profile => profile !=
                Wh3DialogueEventUnitProfile.ProfileShowAll)
            .First(profile =>
            {
                var matches = Wh3DialogueEventInformation.Information
                    .Count(item => item.SoundBank == soundBank &&
                        item.UnitProfiles.Contains(profile));
                var total = Wh3DialogueEventInformation.Information
                    .Count(item => item.SoundBank == soundBank);
                return matches > 0 && matches < total;
            });

    private static IEnumerable<AudioProjectTreeNode> Flatten(
        IEnumerable<AudioProjectTreeNode> nodes)
    {
        foreach (var node in nodes)
        {
            yield return node;
            foreach (var child in Flatten(node.Children))
                yield return child;
        }
    }

    private static void SetExpanded(
        IEnumerable<AudioProjectTreeNode> nodes,
        bool value)
    {
        foreach (var node in Flatten(nodes))
            node.IsExpanded = value;
    }

    private static IEnumerable<T> FindVisualDescendants<T>(
        DependencyObject root)
        where T : DependencyObject
    {
        for (var index = 0;
             index < VisualTreeHelper.GetChildrenCount(root);
             index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            if (child is T match)
                yield return match;
            foreach (var descendant in FindVisualDescendants<T>(child))
                yield return descendant;
        }
    }
}
