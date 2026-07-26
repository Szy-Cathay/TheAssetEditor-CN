using System.Text;

namespace Test.TestingUtility.TestUtility
{
    public static class PathHelper
    {
        /// <summary>
        /// Find the repository root from the test directory.
        /// </summary>        
        private static string FindRepositoryRoot()
        {
            DirectoryInfo? current = new(TestContext.CurrentContext.TestDirectory);
            while (current != null)
            {
                if (File.Exists(Path.Combine(current.FullName, "AssetEditor.CN.sln")))
                    return current.FullName;

                current = current.Parent;
            }

            throw new DirectoryNotFoundException("Unable to locate AssetEditor.CN.sln.");
        }

        public static string GetDataFolder(string folder)
        {
            var currentDirectory = TestContext.CurrentContext.TestDirectory;
            var fullPath = Path.Combine(FindRepositoryRoot(), folder).ToLower();

            if (Directory.Exists(fullPath) == false)
                throw new Exception($"Unable to find data directory {fullPath}. TestFolder : {currentDirectory}. InputFolder: {folder}");

            return fullPath;
        }

        public static string GetProjectOutputPath(string projectDirectory, string outputFolder, string? testDirectory = null)
        {
            var targetFrameworkDirectory = new DirectoryInfo(testDirectory ?? TestContext.CurrentContext.TestDirectory);
            var configurationDirectory = targetFrameworkDirectory.Parent
                ?? throw new DirectoryNotFoundException($"Unable to determine build configuration from {targetFrameworkDirectory.FullName}.");

            return Path.Combine(
                projectDirectory,
                "bin",
                configurationDirectory.Name,
                targetFrameworkDirectory.Name,
                outputFolder);
        }

        public static string GetDataFile(string fileName, string subDir = "Data")
        {
            var fullPath = Path.Combine(FindRepositoryRoot(), subDir, fileName);

            if (File.Exists(fullPath) == false)
                throw new Exception($"Unable to find data file {fileName}");

            return fullPath;
        }

        public static byte[] GetFileAsBytes(string path)
        {
            var fullPath = GetDataFile(path);
            var bytes = File.ReadAllBytes(fullPath);
            return bytes; ;
        }

        public static string GetFileContentAsString(string path)
        {
            var bytes = GetFileAsBytes(path);
            return Encoding.UTF8.GetString(bytes);
        }

    }
}
