using System.Xml.Linq;

namespace AssetEditorTests;

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
