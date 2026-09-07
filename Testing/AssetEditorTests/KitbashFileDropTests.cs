using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Editors.KitbasherEditor.ViewModels;
using KitbasherEditor.Views;
using Moq;
using NUnit.Framework;
using Shared.Core.Events;
using Shared.Core.PackFiles.Models;
using Shared.Ui.BaseDialogs.PackFileTree;
using Shared.Ui.Common;
using Shared.Ui.Common.DataTemplates;
using Shared.Ui.Common.ValueConverters;
using NUnitAssert = NUnit.Framework.Assert;

namespace AssetEditorTests;

[NonParallelizable]
public class KitbashFileDropTests
{
    [TestCase("model.rigid_model_v2")]
    [TestCase("model.wsmodel")]
    [TestCase("model.variantmeshdefinition")]
    public void Drop_FileTreeSelection_ImportsModel(string name)
    {
        WithView((view, target) =>
        {
            var node = FileNode(name);
            // The tree passes a List<TreeNode>, even when only one file is selected.
            var data = new DataObject(new List<TreeNode> { node });
            var drop = RaiseDragEvent(target.Scene, data, DragDrop.DropEvent);

            NUnitAssert.That(target.Imported, Is.EqualTo(new[] { node }));
            NUnitAssert.That(drop.Handled, Is.True);
            NUnitAssert.That(drop.Effects, Is.EqualTo(DragDropEffects.None),
                "Importing must leave the source files in the file tree.");
        });
    }

    [Test]
    public void Drop_MixedSelectionWithAnotherFormatFirst_ImportsOnlySupportedFilesOnce()
    {
        WithView((view, target) =>
        {
            var models = new[] { FileNode("a.rigid_model_v2"), FileNode("b.wsmodel"), FileNode("c.variantmeshdefinition") };
            var directory = new TreeNode("folder.wsmodel", NodeType.Directory, new PackFileContainer("Test"), null);
            var data = new DataObject(DataFormats.UnicodeText, "Unrelated description");
            data.SetData(new List<TreeNode> { models[0], FileNode("texture.dds"), directory, models[1], models[2], models[0] });

            RaiseDragEvent(target.Scene, data, DragDrop.DropEvent);

            NUnitAssert.That(target.Imported, Is.EqualTo(models));
        });
    }

    [TestCase(false)]
    [TestCase(true)]
    public void Drop_SingleNodeAndArrayPayloads_KeepWorking(bool arrayPayload)
    {
        WithView((view, target) =>
        {
            var node = FileNode("model.wsmodel");
            var data = new DataObject(arrayPayload ? new[] { node } : (object)node);

            RaiseDragEvent(target.Scene, data, DragDrop.DropEvent);

            NUnitAssert.That(target.Imported, Is.EqualTo(new[] { node }));
        });
    }

    [TestCase(false, DragDropEffects.Move)]
    [TestCase(true, DragDropEffects.Move)]
    [TestCase(true, DragDropEffects.Copy)]
    public void DragOver_SupportedSelection_ShowsAllowedCursorWithoutImporting(bool entering, DragDropEffects allowedEffects)
    {
        WithView((view, target) =>
        {
            var data = new DataObject(new List<TreeNode> { FileNode("model.rigid_model_v2") });
            var drag = RaiseDragEvent(target.Scene, data,
                entering ? DragDrop.DragEnterEvent : DragDrop.DragOverEvent, allowedEffects);

            NUnitAssert.That(drag.Handled, Is.True);
            NUnitAssert.That(drag.Effects, Is.EqualTo(allowedEffects));
            NUnitAssert.That(target.Imported, Is.Empty);
        });
    }

    [TestCase("empty")]
    [TestCase("empty-selection")]
    [TestCase("unsupported")]
    [TestCase("missing-file")]
    public void DragAndDrop_InvalidData_RejectsWithoutImporting(string payload)
    {
        WithView((view, target) =>
        {
            var data = payload switch
            {
                "empty-selection" => new DataObject(new List<TreeNode>()),
                "unsupported" => new DataObject(new List<TreeNode> { FileNode("texture.dds") }),
                "missing-file" => new DataObject(new TreeNode("missing.wsmodel", NodeType.File, new PackFileContainer("Test"), null)),
                _ => new DataObject(),
            };

            var drag = RaiseDragEvent(target.Scene, data, DragDrop.DragOverEvent);
            RaiseDragEvent(target.Scene, data, DragDrop.DropEvent);

            NUnitAssert.That(drag.Effects, Is.EqualTo(DragDropEffects.None));
            NUnitAssert.That(target.Imported, Is.Empty);
        });
    }

    private static TreeNode FileNode(string name) =>
        new(name, NodeType.File, new PackFileContainer("Test"), null, PackFile.CreateFromBytes(name, []));

    private static DragEventArgs RaiseDragEvent(UIElement source, IDataObject data, RoutedEvent routedEvent,
        DragDropEffects allowedEffects = DragDropEffects.Move)
    {
        // WPF constructs these args internally during an OLE drag operation.
        var args = (DragEventArgs)Activator.CreateInstance(typeof(DragEventArgs),
            BindingFlags.Instance | BindingFlags.NonPublic, null,
            new object[] { data, DragDropKeyStates.LeftMouseButton, allowedEffects, source, new Point(10, 10) }, null)!;
        args.RoutedEvent = routedEvent;
        source.RaiseEvent(args);
        return args;
    }

    private static void WithView(Action<KitbasherView, DropTarget> action)
    {
        WpfTestApplicationHost.InvokeWithThemeResources(WpfTestApplicationHost.EmptyServices, () =>
        {
            Application.Current.Resources["BoolToCollapsedConverter"] = new BoolToVisibilityConverter
            {
                TrueValue = Visibility.Visible,
                FalseValue = Visibility.Collapsed,
            };
            Application.Current.Resources["ViewTemplateDataSelector"] = new ViewTemplateDataSelector();
            var target = new DropTarget();
            var view = new KitbasherView { DataContext = target };
            var window = new Window
            {
                Style = new Style(typeof(Window)), Content = view, Width = 1000, Height = 600,
                ShowActivated = false, ShowInTaskbar = false, Left = -10000, Top = -10000,
            };
            try
            {
                window.Show();
                window.UpdateLayout();
                NUnitAssert.That(target.Scene.IsLoaded, Is.True);
                NUnitAssert.That(target.Scene.AllowDrop, Is.True);
                action(view, target);
            }
            finally
            {
                window.Close();
            }
        });
    }

    private sealed class DropTarget : IDropTarget<TreeNode>
    {
        private readonly KitbashViewDropHandler _handler = new(Mock.Of<IUiCommandFactory>());
        public List<TreeNode> Imported { get; } = [];
        public GridLength LeftColumnWidth { get; set; } = new(3, GridUnitType.Star);
        public GridLength RightColumnWidth { get; set; } = new(1, GridUnitType.Star);
        public Border Scene { get; } = new() { Background = Brushes.Transparent };
        public object? MenuBar => null;
        public object? SceneNodeEditor => null;
        public object? OperationFeedback => null;

        public bool AllowDrop(TreeNode node, TreeNode targeNode = null!) => _handler.AllowDrop(node, targeNode);

        public bool Drop(TreeNode node, TreeNode targeNode = null!)
        {
            Imported.Add(node);
            return true;
        }
    }
}
