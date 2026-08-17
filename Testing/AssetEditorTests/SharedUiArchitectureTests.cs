using System.Xml.Linq;

namespace AssetEditorTests
{

    [TestClass]
    public class SharedUiArchitectureTests
    {
        [TestMethod]
        public void FeatureProjectReference_IsRejected()
        {
            var project = XDocument.Parse(
                """
            <Project>
              <ItemGroup>
                <ProjectReference Include="..\..\Editors\Audio\Editors.Audio.csproj" />
              </ItemGroup>
            </Project>
            """);

            var forbiddenReferences = FindForbiddenProjectReferences(
                project,
                @"C:\repo\Shared\SharedUI",
                @"C:\repo");

            CollectionAssert.AreEqual(
                new[] { @"..\..\Editors\Audio\Editors.Audio.csproj" },
                forbiddenReferences);
        }

        [TestMethod]
        public void SharedUiProject_HasNoFeatureProjectReferences()
        {
            var solutionRoot = FindSolutionRoot();
            var projectDirectory = Path.Combine(
                solutionRoot,
                "Shared",
                "SharedUI");
            var project = XDocument.Load(Path.Combine(
                projectDirectory,
                "Shared.Ui.csproj"));

            var forbiddenReferences = FindForbiddenProjectReferences(
                project,
                projectDirectory,
                solutionRoot);

            Assert.AreEqual(
                0,
                forbiddenReferences.Length,
                string.Join(Environment.NewLine, forbiddenReferences));
        }

        [TestMethod]
        public void SharedCoreMessageBridge_DoesNotReferenceWpf()
        {
            var source = File.ReadAllText(Path.Combine(
                FindSolutionRoot(),
                "Shared",
                "SharedCore",
                "Services",
                "UiMessageBoxBridge.cs"));

            Assert.IsFalse(source.Contains(
                "System.Windows",
                StringComparison.Ordinal));
            Assert.IsFalse(source.Contains(
                "MessageBox.Show",
                StringComparison.Ordinal));
        }

        [TestMethod]
        public void RepositoryXamlFiles_AreWellFormedXml()
        {
            var solutionRoot = FindSolutionRoot();
            var failures = new List<string>();

            foreach (var path in Directory.EnumerateFiles(
                         solutionRoot,
                         "*.xaml",
                         SearchOption.AllDirectories)
                     .Where(path => !IsBuildOutputPath(path)))
            {
                try
                {
                    XDocument.Load(path);
                }
                catch (Exception exception)
                {
                    failures.Add(
                        $"{Path.GetRelativePath(solutionRoot, path)}: " +
                        exception.Message);
                }
            }

            Assert.AreEqual(
                0,
                failures.Count,
                string.Join(Environment.NewLine, failures));
        }

        [TestMethod]
        public void MigratedBusinessClasses_DoNotCreateStandardDialogsDirectly()
        {
            var solutionRoot = FindSolutionRoot();
            string[] migratedFiles =
            [
                "AssetEditor/ViewModels/MenuBarViewModel.cs",
            "AssetEditor/UiCommands/OpenGamePackCommand.cs",
            "Editors/AnimationEditor/MountAnimationCreator/Services/BatchProcessorService.cs",
            "Editors/AnimationFragmentEditor/Editor.AnimationFragmentEditor/AnimationBatchExporter/AnimationBatchExportViewModel.cs",
            "Editors/AnimationFragmentEditor/Editor.AnimationFragmentEditor/AnimationPack/Commands/CreateEmptyWarhammer3AnimSetFileCommand.cs",
            "Editors/AnimationFragmentEditor/Editor.AnimationFragmentEditor/AnimationPack/Commands/CreateExampleAnimationDbCommand.cs",
            "Editors/AnimationFragmentEditor/Editor.AnimationFragmentEditor/AnimationPack/Commands/RenameSelectedFileCommand.cs",
            "Editors/ImportExportEditor/Editors.ImportExport/Importing/Importers/GltfToRmv/GltfImporter.cs",
            "Editors/Kitbashing/KitbasherEditor/UiCommands/MergeObjectsCommand.cs",
            "Editors/Reports/DeepSearch/DeepSearchReport.cs",
            "Shared/SharedUI/BaseDialogs/PackFileTree/ContextMenu/Commands/SavePackFileContainerCommand.cs",
        ];
            string[] forbiddenCalls =
            [
                "new TextInputWindow",
            "ErrorListWindow.ShowDialog",
            "MessageBox.Show",
        ];
            var failures = new List<string>();

            foreach (var relativePath in migratedFiles)
            {
                var text = File.ReadAllText(Path.Combine(
                    solutionRoot,
                    relativePath));
                foreach (var forbiddenCall in forbiddenCalls)
                {
                    if (text.Contains(forbiddenCall, StringComparison.Ordinal))
                        failures.Add($"{relativePath}: {forbiddenCall}");
                }
            }

            Assert.AreEqual(
                0,
                failures.Count,
                string.Join(Environment.NewLine, failures));
        }

        [TestMethod]
        public void GripGridSplitters_UseSharedStyles()
        {
            const string resourcePath =
                "pack://application:,,,/Shared.Ui;component/Common/Styles/GridSplitterStyles.xaml";
            var solutionRoot = FindSolutionRoot();
            var expectations = new Dictionary<string, string[]>
            {
                ["AssetEditor/Views/MainWindow.xaml"] =
                ["{StaticResource AeVerticalGridSplitterStyle}"],
                ["Editors/Audio/AudioEditor/Presentation/AudioEditorView.xaml"] =
                [
                    "{StaticResource AeHorizontalGridSplitterStyle}",
                    "{StaticResource AeHorizontalGridSplitterStyle}",
                    "{StaticResource AeVerticalGridSplitterStyle}",
                ],
                ["Editors/Audio/AudioExplorer/AudioExplorerView.xaml"] =
                ["{StaticResource AeVerticalGridSplitterStyle}"],
                ["Editors/AnimationFragmentEditor/Editor.AnimationFragmentEditor/AnimationPack/AnimationPackView.xaml"] =
                ["{StaticResource AeVerticalGridSplitterStyle}"],
                ["Editors/CscEditor/Editors.CscEditor/Views/CscEditorView.xaml"] =
                [
                    "{StaticResource AeVerticalGridSplitterStyle}",
                    "{StaticResource AeHorizontalGridSplitterStyle}",
                ],
                ["Editors/Kitbashing/KitbasherEditor/Core/KitbasherView.xaml"] =
                [
                    "{StaticResource AeVerticalGridSplitterStyle}",
                    "{StaticResource AeHorizontalGridSplitterStyle}",
                ],
                ["Editors/Shared/Editors.Shared.Core/Common/BaseControl/EditorHostView.xaml"] =
                ["{StaticResource AeVerticalGridSplitterStyle}"],
                ["Editors/TwuiEditor/Editor.Twui/Editor/Presentation/TwuiMainView.xaml"] =
                [
                    "{StaticResource AeVerticalGridSplitterStyle}",
                    "{StaticResource AeVerticalGridSplitterStyle}",
                ],
            };
            XNamespace presentation =
                "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
            var failures = new List<string>();

            var productXamlPaths = new[]
                {
                    "AssetEditor",
                    "Editors",
                    "GameWorld",
                    "Shared",
                }
                .SelectMany(root => Directory.EnumerateFiles(
                    Path.Combine(solutionRoot, root),
                    "*.xaml",
                    SearchOption.AllDirectories))
                .Where(path => !IsBuildOutputPath(path))
                .Select(path => new
                {
                    RelativePath = Path.GetRelativePath(solutionRoot, path)
                        .Replace(Path.DirectorySeparatorChar, '/'),
                    Document = XDocument.Load(path),
                })
                .Where(item => item.Document
                    .Descendants(presentation + "GridSplitter")
                    .Any())
                .ToArray();

            var unexpectedFiles = productXamlPaths
                .Select(item => item.RelativePath)
                .Except(expectations.Keys, StringComparer.Ordinal)
                .ToArray();
            failures.AddRange(unexpectedFiles.Select(path =>
                $"{path}: GridSplitter style expectation missing"));

            var missingFiles = expectations.Keys
                .Except(
                    productXamlPaths.Select(item => item.RelativePath),
                    StringComparer.Ordinal)
                .ToArray();
            failures.AddRange(missingFiles.Select(path =>
                $"{path}: expected GridSplitter not found"));

            foreach (var expectation in expectations)
            {
                var item = productXamlPaths.SingleOrDefault(candidate =>
                    candidate.RelativePath.Equals(
                        expectation.Key,
                        StringComparison.Ordinal));
                if (item is null)
                    continue;

                var document = item.Document;
                var sources = document
                    .Descendants(presentation + "ResourceDictionary")
                    .Select(element => (string?)element.Attribute("Source"));
                if (!sources.Contains(resourcePath, StringComparer.Ordinal))
                    failures.Add($"{expectation.Key}: shared resource missing");

                var splitters = document
                    .Descendants(presentation + "GridSplitter")
                    .ToArray();
                var styles = splitters
                    .Select(element => (string?)element.Attribute("Style"))
                    .ToArray();
                if (!styles.SequenceEqual(expectation.Value))
                {
                    failures.Add(
                        $"{expectation.Key}: unexpected styles " +
                        string.Join(", ", styles.Select(style => style ?? "<none>")));
                }

                if (splitters.Any(element =>
                        element.Element(presentation + "GridSplitter.Template") != null))
                {
                    failures.Add($"{expectation.Key}: inline template remains");
                }

                if (splitters.Any(element =>
                        element.Attribute("Background") != null ||
                        element.Attribute("BorderBrush") != null ||
                        element.Attribute("BorderThickness") != null))
                {
                    failures.Add($"{expectation.Key}: inline visual override remains");
                }
            }

            Assert.AreEqual(
                0,
                failures.Count,
                string.Join(Environment.NewLine, failures));
        }

        private static string[] FindForbiddenProjectReferences(
            XDocument project,
            string projectDirectory,
            string solutionRoot)
        {
            var sharedDirectory = Path.GetFullPath(Path.Combine(
                solutionRoot,
                "Shared"));
            var sharedPrefix = Path.TrimEndingDirectorySeparator(
                sharedDirectory) + Path.DirectorySeparatorChar;

            return project
                .Descendants("ProjectReference")
                .Select(element => element.Attribute("Include")?.Value)
                .Where(include => !string.IsNullOrWhiteSpace(include))
                .Where(include =>
                {
                    var targetPath = Path.GetFullPath(Path.Combine(
                        projectDirectory,
                        include!));
                    return !targetPath.StartsWith(
                        sharedPrefix,
                        StringComparison.OrdinalIgnoreCase);
                })
                .Select(include => include!)
                .ToArray();
        }

        private static bool IsBuildOutputPath(string path)
        {
            return path
                .Split(
                    [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
                    StringSplitOptions.RemoveEmptyEntries)
                .Any(part => part.Equals(
                        "bin",
                        StringComparison.OrdinalIgnoreCase) ||
                    part.Equals(
                        "obj",
                        StringComparison.OrdinalIgnoreCase));
        }

        private static string FindSolutionRoot()
        {
            DirectoryInfo? directory = new(AppContext.BaseDirectory);
            while (directory is not null)
            {
                if (File.Exists(Path.Combine(
                        directory.FullName,
                        "AssetEditor.CN.sln")))
                {
                    return directory.FullName;
                }

                directory = directory.Parent;
            }

            throw new DirectoryNotFoundException(
                "Could not locate AssetEditor.CN.sln.");
        }
    }
}
