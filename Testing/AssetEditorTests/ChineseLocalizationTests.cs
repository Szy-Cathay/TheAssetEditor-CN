using System.Text.Json;

namespace AssetEditorTests
{
    [TestClass]
    public class ChineseLocalizationTests
    {
        [TestMethod]
        public void BuildOutput_ContainsOnlyChineseLanguageFile()
        {
            var languageFiles = Directory
                .GetFiles(AppContext.BaseDirectory, "Language_*.json")
                .Select(Path.GetFileName)
                .OrderBy(name => name)
                .ToArray();

            CollectionAssert.AreEqual(new[] { "Language_Cn.json" }, languageFiles);
        }

        [TestMethod]
        public void ChineseLanguageFile_ContainsCnEditionTitle()
        {
            var path = Path.Combine(AppContext.BaseDirectory, "Language_Cn.json");
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            var title = document.RootElement.GetProperty("Title.AppTitle").GetString();

            Assert.AreEqual("Asset Editor 国区版 v{0}", title);
        }
    }
}
