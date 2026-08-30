using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Threading;
using Editors.Audio.AudioEditor.Presentation.AudioFilesExplorer;
using Editors.Audio.AudioEditor.Presentation.Shared.Controls;
using Editors.Audio.AudioEditor.Presentation.Shared.Models;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using Shared.Core.Services;

using Assert = NUnit.Framework.Assert;

namespace AssetEditorTests;

[NonParallelizable]
public class AudioFilesExplorerInteractionTests
{
    [SetUp]
    public void SetUp() => new LocalizationManager().LoadLanguage();

    [Test]
    public void AudioFilesTreeExpander_CollapsesOnFirstMousePress_WhenChildRemainsSelected()
    {
        using var services = new ServiceCollection()
            .AddSingleton(LocalizationManager.Instance)
            .BuildServiceProvider();
        WpfTestApplicationHost.InvokeWithThemeResources(services, () =>
        {
            var directory = AudioFilesTreeNode.CreateContainerNode(
                "audio",
                AudioFilesTreeNodeType.Directory);
            var editedFile = AudioFilesTreeNode.CreateChildNode(
                "edited.wav",
                AudioFilesTreeNodeType.WavFile,
                directory);
            directory.Children.Add(editedFile);
            directory.IsExpanded = true;
            var model = new AudioFilesExplorerTestModel
            {
                AudioFilesTree = [directory],
                SelectedTreeNodes = [editedFile],
            };
            var view = new AudioFilesExplorerView
            {
                DataContext = model,
                Width = 500,
                Height = 300,
            };
            var window = new Window
            {
                Content = view,
                Width = 520,
                Height = 320,
                ShowActivated = false,
                ShowInTaskbar = false,
                WindowStyle = WindowStyle.None,
                Left = -10000,
                Top = -10000,
            };

            try
            {
                window.Show();
                window.UpdateLayout();
                var tree = (MultiSelectTreeView)view.FindName(
                    "AudioFilesTreeView");
                var directoryItem = FindTreeViewItem(tree, directory);
                var editedFileItem = FindTreeViewItem(tree, editedFile);
                editedFileItem.IsSelected = true;
                directoryItem.BringIntoView();
                FlushBindings(view);
                Assert.That(editedFileItem.IsSelected, Is.True);

                var expander = directoryItem.Template.FindName(
                    "Expander",
                    directoryItem) as ToggleButton;
                Assert.That(expander, Is.Not.Null);

                expander!.RaiseEvent(new MouseButtonEventArgs(
                    Mouse.PrimaryDevice,
                    Environment.TickCount,
                    MouseButton.Left)
                {
                    RoutedEvent = Mouse.MouseDownEvent,
                    Source = expander,
                });
                FlushBindings(view);

                Assert.That(directory.IsExpanded, Is.False);
            }
            finally
            {
                window.Close();
            }
        });
    }

    private static TreeViewItem FindTreeViewItem(
        ItemsControl parent,
        AudioFilesTreeNode target)
    {
        var item = TryFindTreeViewItem(parent, target);
        Assert.That(item, Is.Not.Null, target.FileName);
        return item!;
    }

    private static TreeViewItem? TryFindTreeViewItem(
        ItemsControl parent,
        AudioFilesTreeNode target)
    {
        parent.ApplyTemplate();
        parent.UpdateLayout();
        foreach (var node in parent.Items.OfType<AudioFilesTreeNode>())
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
            var child = TryFindTreeViewItem(item, target);
            if (child != null)
                return child;
        }

        return null;
    }

    private static void FlushBindings(FrameworkElement view)
    {
        view.Dispatcher.Invoke(
            () => { },
            DispatcherPriority.DataBind);
        view.UpdateLayout();
    }

    private sealed class AudioFilesExplorerTestModel
    {
        public string AudioFilesExplorerLabel { get; } = "Audio files";

        public ObservableCollection<AudioFilesTreeNode> AudioFilesTree
        {
            get;
            init;
        } = [];

        public ObservableCollection<AudioFilesTreeNode> SelectedTreeNodes
        {
            get;
            init;
        } = [];
    }
}
