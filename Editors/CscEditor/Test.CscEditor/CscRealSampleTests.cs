using Editors.CscEditor.Data;

namespace Test.CscEditor
{
    public class CscRealSampleTests
    {
        [Test]
        public void Extracted_game_samples_load_save_and_reload_stably()
        {
            var sampleDirectory = Environment.GetEnvironmentVariable("AE_CSC_SAMPLE_DIR");
            if (string.IsNullOrWhiteSpace(sampleDirectory) || !Directory.Exists(sampleDirectory))
                Assert.Ignore("Set AE_CSC_SAMPLE_DIR to a directory containing extracted .csc files.");

            var files = Directory.GetFiles(sampleDirectory, "*.csc", SearchOption.AllDirectories);
            Assert.That(files, Is.Not.Empty);

            foreach (var file in files)
            {
                var original = CscScene.Load(File.ReadAllBytes(file));
                var firstWrite = CscSceneWriter.Write(original);
                var reloaded = CscScene.Load(firstWrite);
                var secondWrite = CscSceneWriter.Write(reloaded);

                AssertEquivalent(original, reloaded, file);
                Assert.That(secondWrite, Is.EqualTo(firstWrite), $"{file}: second save was not stable");
            }
        }

        static void AssertEquivalent(CscScene expected, CscScene actual, string file)
        {
            Assert.Multiple(() =>
            {
                Assert.That(actual.RootVersion, Is.EqualTo(expected.RootVersion), $"{file}: ROOT version");
                Assert.That(actual.Duration, Is.EqualTo(expected.Duration), $"{file}: duration");
                Assert.That(actual.Elements.Count, Is.EqualTo(expected.Elements.Count), $"{file}: element count");
            });

            var expectedElements = expected.AllElementsIncludingNested().ToDictionary(element => element.Id);
            var actualElements = actual.AllElementsIncludingNested().ToDictionary(element => element.Id);
            Assert.That(actualElements.Keys, Is.EquivalentTo(expectedElements.Keys), $"{file}: element ids");

            foreach (var (id, expectedElement) in expectedElements)
            {
                var actualElement = actualElements[id];
                Assert.Multiple(() =>
                {
                    Assert.That(actualElement.Kind, Is.EqualTo(expectedElement.Kind), $"{file}: element {id} kind");
                    Assert.That(actualElement.AssetPath, Is.EqualTo(expectedElement.AssetPath), $"{file}: element {id} asset");
                    Assert.That(actualElement.Parent?.Id, Is.EqualTo(expectedElement.Parent?.Id), $"{file}: element {id} parent");
                    Assert.That(actualElement.AttachBoneIndex, Is.EqualTo(expectedElement.AttachBoneIndex), $"{file}: element {id} attach");
                    Assert.That(
                        actualElement.Children.Select(child => child.Id),
                        Is.EqualTo(expectedElement.Children.Select(child => child.Id)),
                        $"{file}: element {id} child order");
                    Assert.That(actualElement.Begin, Is.EqualTo(expectedElement.Begin), $"{file}: element {id} begin");
                    Assert.That(actualElement.End, Is.EqualTo(expectedElement.End), $"{file}: element {id} end");
                    Assert.That(
                        actualElement.PeriodSpeedMultiplier,
                        Is.EqualTo(expectedElement.PeriodSpeedMultiplier),
                        $"{file}: element {id} speed");
                    Assert.That(
                        actualElement.AllChannelKeyframeCounts(),
                        Is.EqualTo(expectedElement.AllChannelKeyframeCounts()),
                        $"{file}: element {id} channel keyframes");
                });
            }
        }
    }
}
