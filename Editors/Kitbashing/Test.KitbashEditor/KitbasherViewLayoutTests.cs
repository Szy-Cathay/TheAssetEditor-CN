using System.IO;
using System.Xml.Linq;

namespace Test.KitbashEditor
{
    [TestFixture]
    public class KitbasherViewLayoutTests
    {
        [Test]
        public void RightPanelSplitter_DeclaresIndependentColumnResizeCursor()
        {
            var document = XDocument.Load(GetRepositoryFilePath(
                "Editors",
                "Kitbashing",
                "KitbasherEditor",
                "Core",
                "KitbasherView.xaml"));
            XNamespace presentation =
                "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
            var splitter = document
                .Descendants(presentation + "GridSplitter")
                .Single(element =>
                    (string?)element.Attribute("Grid.Column") == "1" &&
                    (string?)element.Attribute("Grid.RowSpan") == "5");

            Assert.Multiple(() =>
            {
                Assert.That(
                    (string?)splitter.Attribute("Cursor"),
                    Is.EqualTo("SizeWE"));
                Assert.That(
                    (string?)splitter.Attribute("ResizeDirection"),
                    Is.EqualTo("Columns"));
                Assert.That(
                    (string?)splitter.Attribute("ResizeBehavior"),
                    Is.EqualTo("PreviousAndNext"));
            });
        }

        [Test]
        public void MenuBar_MenuItemsBindEnabledState()
        {
            var document = XDocument.Load(GetRepositoryFilePath(
                "Editors",
                "Kitbashing",
                "KitbasherEditor",
                "Core",
                "MenuBarViews",
                "MenuBarView.xaml"));
            XNamespace presentation =
                "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
            var menuItemStyle = document
                .Descendants(presentation + "Menu.Resources")
                .Descendants(presentation + "Style")
                .Single();
            var enabledSetter = menuItemStyle
                .Elements(presentation + "Setter")
                .SingleOrDefault(element =>
                    (string?)element.Attribute("Property") == "IsEnabled");

            Assert.Multiple(() =>
            {
                Assert.That(enabledSetter, Is.Not.Null);
                Assert.That(
                    (string?)enabledSetter?.Attribute("Value"),
                    Is.EqualTo(
                        "{Binding Action.IsActionEnabled.Value, UpdateSourceTrigger=PropertyChanged}"));
            });
        }

        [Test]
        public void MenuBar_ToolbarControlsPreserveViewportFocus()
        {
            var document = XDocument.Load(GetRepositoryFilePath(
                "Editors",
                "Kitbashing",
                "KitbasherEditor",
                "Core",
                "MenuBarViews",
                "MenuBarView.xaml"));
            XNamespace presentation =
                "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
            var toolbarButtons = document
                .Descendants()
                .Where(element =>
                    element.Name == presentation + "Button" ||
                    element.Name == presentation + "RadioButton")
                .Where(element =>
                    element.Descendants(presentation + "Image").Any())
                .ToList();

            Assert.That(toolbarButtons, Is.Not.Empty);
            Assert.That(
                toolbarButtons.All(element =>
                    (string?)element.Attribute("Focusable") == "False"),
                Is.True);
            Assert.That(
                toolbarButtons.All(element =>
                    (string?)element.Attribute(
                        "AutomationProperties.Name") ==
                    "{Binding Action.ToolTipAttribute.Value}"),
                Is.True);
        }

        [Test]
        public void MenuBar_ViewportShadingButtonsAreGroupedAndAccessible()
        {
            var document = XDocument.Load(GetRepositoryFilePath(
                "Editors",
                "Kitbashing",
                "KitbasherEditor",
                "Core",
                "MenuBarViews",
                "MenuBarView.xaml"));
            XNamespace presentation =
                "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
            var buttons = document
                .Descendants(presentation + "RadioButton")
                .Where(element =>
                    (string?)element.Attribute("GroupName") ==
                    "ViewportShading")
                .ToList();

            Assert.Multiple(() =>
            {
                Assert.That(buttons, Has.Count.EqualTo(3));
                Assert.That(
                    buttons.All(element =>
                        (string?)element.Attribute("Focusable") ==
                        "False"),
                    Is.True);
                Assert.That(
                    buttons.All(element =>
                        (string?)element.Attribute("ToolTip") ==
                        (string?)element.Attribute(
                            "AutomationProperties.Name")),
                    Is.True);
                Assert.That(
                    buttons.Select(element =>
                        (string?)element.Attribute("IsChecked")),
                    Is.EquivalentTo(new[]
                    {
                        "{Binding ViewportShading.IsWireframe, Mode=TwoWay, UpdateSourceTrigger=PropertyChanged}",
                        "{Binding ViewportShading.IsSolid, Mode=TwoWay, UpdateSourceTrigger=PropertyChanged}",
                        "{Binding ViewportShading.IsMaterialPreview, Mode=TwoWay, UpdateSourceTrigger=PropertyChanged}",
                    }));
            });
        }

        [Test]
        public void MenuBar_FalloffInputAcceptsOnlyPositiveDecimals()
        {
            var document = XDocument.Load(GetRepositoryFilePath(
                "Editors",
                "Kitbashing",
                "KitbasherEditor",
                "Core",
                "MenuBarViews",
                "MenuBarView.xaml"));
            XNamespace presentation =
                "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
            XNamespace behaviors =
                "clr-namespace:Shared.Ui.Common.Behaviors;assembly=Shared.Ui";
            var falloffTextBox = document
                .Descendants(presentation + "TextBox")
                .Single(element =>
                    (string?)element.Attribute(
                        XName.Get("Name",
                            "http://schemas.microsoft.com/winfx/2006/xaml")) ==
                    "FalloffTextBox");
            var inputBehavior = falloffTextBox
                .Descendants(behaviors + "TextBoxInputBehavior")
                .SingleOrDefault();

            Assert.Multiple(() =>
            {
                Assert.That(inputBehavior, Is.Not.Null);
                Assert.That(
                    (string?)inputBehavior?.Attribute("InputMode"),
                    Is.EqualTo("DecimalInput"));
                Assert.That(
                    (string?)inputBehavior?.Attribute(
                        "JustPositivDecimalInput"),
                    Is.EqualTo("True"));
            });
        }

        [Test]
        public void MenuBar_UnloadedHandlerReleasesWindowKeyboardEvents()
        {
            var document = XDocument.Load(GetRepositoryFilePath(
                "Editors",
                "Kitbashing",
                "KitbasherEditor",
                "Core",
                "MenuBarViews",
                "MenuBarView.xaml"));

            Assert.That(
                (string?)document.Root?.Attribute("Unloaded"),
                Is.EqualTo("UserControl_Unloaded"));
        }

        [Test]
        public void MenuBar_FalloffCommitDefersViewportFocus()
        {
            var source = File.ReadAllText(GetRepositoryFilePath(
                "Editors",
                "Kitbashing",
                "KitbasherEditor",
                "Core",
                "MenuBarViews",
                "MenuBarView.xaml.cs"));

            Assert.Multiple(() =>
            {
                Assert.That(
                    source,
                    Does.Contain("Dispatcher.BeginInvoke("));
                Assert.That(
                    source,
                    Does.Contain("DispatcherPriority.Input"));
            });
        }

        [Test]
        public void SceneNodeEditor_ShowsEmptyStateWhenNoEditorIsSelected()
        {
            var document = XDocument.Load(GetRepositoryFilePath(
                "Editors",
                "Kitbashing",
                "KitbasherEditor",
                "Core",
                "SceneNodeEditor",
                "SceneNodeEditorView.xaml"));
            XNamespace presentation =
                "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

            var emptyState = document
                .Descendants(presentation + "TextBlock")
                .Single(element =>
                    (string?)element.Attribute("Text") ==
                    "{Binding EmptyStateText}");
            var nullTrigger = emptyState
                .Descendants(presentation + "DataTrigger")
                .Single();

            Assert.Multiple(() =>
            {
                Assert.That(
                    (string?)nullTrigger.Attribute("Binding"),
                    Is.EqualTo("{Binding CurrentEditor}"));
                Assert.That(
                    (string?)nullTrigger.Attribute("Value"),
                    Is.EqualTo("{x:Null}"));
            });
        }

        [Test]
        public void SkeletonTree_StartsCollapsedAndCommitsScaleOnFocusLoss()
        {
            var document = XDocument.Load(GetRepositoryFilePath(
                "Editors",
                "Kitbashing",
                "KitbasherEditor",
                "Core",
                "SceneNodeEditor",
                "Nodes",
                "SkeletonNode",
                "SkeletonView.xaml"));
            XNamespace presentation =
                "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

            var scaleTextBox = document
                .Descendants(presentation + "TextBox")
                .Single();
            var expansionSetter = document
                .Descendants(presentation + "Setter")
                .Single(element =>
                    (string?)element.Attribute("Property") == "IsExpanded");

            Assert.Multiple(() =>
            {
                Assert.That(
                    (string?)scaleTextBox.Attribute("Text"),
                    Does.Contain("UpdateSourceTrigger=LostFocus"));
                Assert.That(
                    (string?)expansionSetter.Attribute("Value"),
                    Is.EqualTo("False"));
            });
        }

        [Test]
        public void ShaderTexturePath_CommitsOnFocusLoss()
        {
            var document = XDocument.Load(GetRepositoryFilePath(
                "GameWorld",
                "View3D",
                "Utility",
                "UserInterface",
                "ShaderTextureView.xaml"));
            XNamespace presentation =
                "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
            var pathTextBox = document
                .Descendants(presentation + "TextBox")
                .Single(element => element.Attribute("Text") != null);

            Assert.That(
                (string?)pathTextBox.Attribute("Text"),
                Does.Contain("UpdateSourceTrigger=LostFocus"));
        }

        [Test]
        public void ModelMaterialPanels_CollapseWhenCapabilityIsUnavailable()
        {
            var document = XDocument.Load(GetRepositoryFilePath(
                "Editors",
                "Kitbashing",
                "KitbasherEditor",
                "Core",
                "SceneNodeEditor",
                "Nodes",
                "MeshNode",
                "Mesh.Material",
                "ModelMaterialView.xaml"));
            XNamespace presentation =
                "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
            var capabilityNames = new[]
            {
                "MetalRough",
                "SpecGloss",
                "AdvanceRvmMaterial",
                "Blood",
                "Tint",
                "Emissive"
            };

            foreach (var capabilityName in capabilityNames)
            {
                var control = document
                    .Descendants(presentation + "ContentControl")
                    .Single(element =>
                        (string?)element.Attribute("Content") ==
                        $"{{Binding {capabilityName}}}");

                Assert.That(
                    (string?)control.Attribute("Visibility"),
                    Is.EqualTo(
                        $"{{Binding {capabilityName}, Converter={{StaticResource NullToVisibilityConverter}}}}"),
                    capabilityName);
            }
        }

        [Test]
        public void FactionPreview_OnlyExposesWorkingPreviewControls()
        {
            var document = XDocument.Load(GetRepositoryFilePath(
                "Editors",
                "Kitbashing",
                "KitbasherEditor",
                "Core",
                "SceneNodeEditor",
                "Nodes",
                "MeshNode",
                "Mesh.Material",
                "Tint",
                "TintView.xaml"));
            XNamespace presentation =
                "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
            XNamespace colourPicker =
                "clr-namespace:Shared.Ui.BaseDialogs.ColourPickerButton;assembly=Shared.Ui";
            XNamespace mathViews =
                "clr-namespace:CommonControls.MathViews;assembly=Shared.Ui";

            Assert.Multiple(() =>
            {
                Assert.That(
                    document.Descendants(presentation + "CheckBox"),
                    Has.Exactly(1).Items);
                Assert.That(
                    document.Descendants(colourPicker + "ColourPickerButtonView"),
                    Has.Exactly(3).Items);
                Assert.That(
                    document.Descendants(presentation + "TextBox"),
                    Is.Empty);
                Assert.That(
                    document.Descendants(mathViews + "Vector4View"),
                    Is.Empty);
            });
        }

        [Test]
        public void WeightedMaterialDiagnostics_AreCollapsedAndReadOnly()
        {
            var document = XDocument.Load(GetRepositoryFilePath(
                "Editors",
                "Kitbashing",
                "KitbasherEditor",
                "Core",
                "SceneNodeEditor",
                "Nodes",
                "MeshNode",
                "Mesh.WeighterMaterial",
                "WeightedMaterialView.xaml"));
            XNamespace presentation =
                "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
            var rootExpander = document
                .Descendants(presentation + "Expander")
                .First();
            var header = rootExpander
                .Elements(presentation + "Expander.Header")
                .Descendants(presentation + "StackPanel")
                .First();

            Assert.Multiple(() =>
            {
                Assert.That(
                    (string?)rootExpander.Attribute("IsExpanded"),
                    Is.EqualTo("False"));
                Assert.That(
                    rootExpander.Attribute("ToolTip"),
                    Is.Null);
                Assert.That(
                    (string?)header.Attribute("ToolTip"),
                    Is.EqualTo(
                        "{loc:Loc WeightedMaterial.ToolTip}"));
                Assert.That(
                    rootExpander.Descendants(presentation + "Button"),
                    Is.Empty);
            });
        }

        [Test]
        public void MeshAdvancedSettings_StartCollapsedWithGuidance()
        {
            var document = XDocument.Load(GetRepositoryFilePath(
                "Editors",
                "Kitbashing",
                "KitbasherEditor",
                "Core",
                "SceneNodeEditor",
                "Nodes",
                "MeshNode",
                "Mesh.Geometry",
                "MeshView.xaml"));
            XNamespace presentation =
                "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
            var advancedExpander = document
                .Descendants(presentation + "Expander")
                .Single(expander =>
                    expander
                        .Elements(presentation + "Expander.Header")
                        .Descendants(presentation + "TextBlock")
                        .Any(text =>
                            (string?)text.Attribute("Text") ==
                            "{loc:Loc Mesh.Advanced}"));
            var header = advancedExpander
                .Elements(presentation + "Expander.Header")
                .Descendants(presentation + "TextBlock")
                .Single();

            Assert.Multiple(() =>
            {
                Assert.That(
                    (string?)advancedExpander.Attribute("IsExpanded"),
                    Is.EqualTo("False"));
                Assert.That(
                    advancedExpander.Attribute("ToolTip"),
                    Is.Null);
                Assert.That(
                    (string?)header.Attribute("ToolTip"),
                    Is.EqualTo("{loc:Loc Mesh.Advanced.ToolTip}"));
            });
        }

        [Test]
        public void GuidanceTooltips_AppearOnlyOnSectionHeaders()
        {
            var sceneNodeEditorDirectory = GetRepositoryFilePath(
                "Editors",
                "Kitbashing",
                "KitbasherEditor",
                "Core",
                "SceneNodeEditor");
            XNamespace presentation =
                "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
            var tooltipValues = new List<string>();

            foreach (var filePath in Directory.GetFiles(
                sceneNodeEditorDirectory,
                "*.xaml",
                SearchOption.AllDirectories))
            {
                var document = XDocument.Load(filePath);
                var tooltipOwners = document
                    .Descendants()
                    .Where(element => element.Attribute("ToolTip") != null);

                foreach (var tooltipOwner in tooltipOwners)
                {
                    tooltipValues.Add(
                        (string)tooltipOwner.Attribute("ToolTip")!);
                    Assert.That(
                        tooltipOwner
                            .Ancestors(presentation + "Expander.Header")
                            .Any(),
                        Is.True,
                        $"{Path.GetRelativePath(sceneNodeEditorDirectory, filePath)}: {tooltipOwner.Name.LocalName}");
                }
            }

            Assert.That(
                tooltipValues,
                Is.EquivalentTo(new[]
                {
                    "{loc:Loc AdvancedRmvMaterial.ToolTip}",
                    "{loc:Loc Emissive.ToolTip}",
                    "{loc:Loc Mesh.Advanced.ToolTip}",
                    "{loc:Loc MeshAnim.AnimationMatrix.ToolTip}",
                    "{loc:Loc MetalRough.ToolTip}",
                    "{loc:Loc ModelMaterial.SelectedShader.ToolTip}",
                    "{loc:Loc SpecGloss.ToolTip}",
                    "{loc:Loc Tint.ToolTip}",
                    "{loc:Loc WeightedMaterial.ToolTip}"
                }));
        }

        private static string GetRepositoryFilePath(params string[] pathParts)
        {
            var directory = new DirectoryInfo(
                TestContext.CurrentContext.TestDirectory);
            while (directory != null)
            {
                if (File.Exists(Path.Combine(
                    directory.FullName,
                    "AssetEditor.CN.sln")))
                {
                    return Path.Combine(
                        [directory.FullName, .. pathParts]);
                }

                directory = directory.Parent;
            }

            throw new DirectoryNotFoundException(
                "Unable to locate the AssetEditor.CN repository root.");
        }
    }
}
