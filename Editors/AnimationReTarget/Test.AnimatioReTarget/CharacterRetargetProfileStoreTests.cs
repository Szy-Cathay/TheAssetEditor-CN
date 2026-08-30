using Editors.AnimatioReTarget.Editor.BoneHandling;

namespace Test.AnimatioReTarget;

public class CharacterRetargetProfileStoreTests
{
    [Test]
    public void SaveAndLoad_PreservesMappingsForSkeletonPair()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            $"ae-retarget-profile-{Guid.NewGuid():N}");
        var filePath = Path.Combine(directory, "profiles.json");
        try
        {
            var store = CharacterRetargetProfileStore.CreateForFile(filePath);
            store.Save(
                "animations\\skeletons\\humanoid01.anim",
                "external\\yangjian.anim",
                "source-fingerprint",
                "target-fingerprint",
                new Dictionary<int, int>
                {
                    [0] = 0,
                    [2] = 1,
                    [106] = 9,
                },
                new Dictionary<int, CharacterRetargetBoneSettings>
                {
                    [106] = new CharacterRetargetBoneSettings(
                        BoneLengthMultiplier: 1.25f,
                        RotationOffsetX: 10,
                        RotationOffsetY: 20,
                        RotationOffsetZ: 30,
                        TranslationOffsetX: 0.1,
                        TranslationOffsetY: 0.2,
                        TranslationOffsetZ: 0.3,
                        ForceSnapToWorld: true,
                        FreezeTranslation: false,
                        FreezeRotation: false,
                        FreezeRotationZ: true,
                        ApplyTranslation: true,
                        ApplyRotation: true,
                        RelativeTargetBoneIndex: 9),
                });

            var wasLoaded = store.TryLoad(
                "animations\\skeletons\\humanoid01.anim",
                "external\\yangjian.anim",
                "source-fingerprint",
                "target-fingerprint",
                out var mappings);

            Assert.Multiple(() =>
            {
                Assert.That(wasLoaded, Is.True);
                Assert.That(mappings[0], Is.EqualTo(0));
                Assert.That(mappings[2], Is.EqualTo(1));
                Assert.That(mappings[106], Is.EqualTo(9));
            });

            Assert.That(store.TryLoadSettings(
                "animations\\skeletons\\humanoid01.anim",
                "external\\yangjian.anim",
                "source-fingerprint",
                "target-fingerprint",
                out var settings), Is.True);
            Assert.Multiple(() =>
            {
                Assert.That(settings[106].BoneLengthMultiplier, Is.EqualTo(1.25f));
                Assert.That(settings[106].RotationOffsetY, Is.EqualTo(20));
                Assert.That(settings[106].FreezeRotationZ, Is.True);
                Assert.That(settings[106].RelativeTargetBoneIndex, Is.EqualTo(9));
            });
        }
        finally
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
    }

    [Test]
    public void Save_WhenExistingProfileFileIsCorrupt_RefusesToOverwriteIt()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            $"ae-retarget-profile-corrupt-{Guid.NewGuid():N}");
        var filePath = Path.Combine(directory, "profiles.json");
        const string corruptContent = "{ this is not valid json";
        try
        {
            Directory.CreateDirectory(directory);
            File.WriteAllText(filePath, corruptContent);
            var store = CharacterRetargetProfileStore.CreateForFile(filePath);

            var wasSaved = store.Save(
                "source",
                "target",
                "source-fingerprint",
                "target-fingerprint",
                new Dictionary<int, int> { [0] = 0 });

            Assert.Multiple(() =>
            {
                Assert.That(wasSaved, Is.False);
                Assert.That(File.ReadAllText(filePath), Is.EqualTo(corruptContent));
            });
        }
        finally
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
    }
}
