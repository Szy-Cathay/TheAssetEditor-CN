using System.Text;
using Editors.Shared.Core.Common;
using Editors.Shared.Core.Common.ReferenceModel;
using GameWorld.Core.Components;
using GameWorld.Core.Services;
using Moq;
using Shared.Core.Events;
using Shared.Core.PackFiles;
using Shared.Core.PackFiles.Models;
using Shared.Core.Services;
using Shared.GameFormats.AnimationPack;
using Shared.GameFormats.AnimationPack.AnimPackFileTypes.Wh3;

namespace Test.Editors.Shared.Core;

public class BinAnimationViewModelTests
{
    [Test]
    public void FragmentDiscovery_CombinesOriginalAnimpackAndJsonWithoutChangingTheirSources()
    {
        const string packedPath = "animations\\database\\battle\\bin\\packed.bin";
        const string jsonPath = "animations\\database\\battle\\fragments\\source.txt";
        const string animpackPath = "animations\\source.animpack";
        var database = new AnimationPackFileDatabase(animpackPath);
        var packedFragment = new AnimationBinWh3(packedPath)
        {
            TableVersion = 2, Name = "packed", SkeletonName = "male01"
        };
        packedFragment.AnimationTableEntries.Add(new AnimationBinEntry
        {
            AnimationId = (uint)AnimationSlotTypeHelper3k.GetfromValue("STAND").Id,
            SelectionWeight = 1,
            AnimationRefs =
            [
                new AnimationBinEntry.AnimationRef
                {
                    AnimationFile = "animations/battle/packed.anim",
                    AnimationMetaFile = "animations/battle/packed.anm.meta",
                    AnimationSoundMetaFile = ""
                }
            ]
        });
        database.AddFile(packedFragment);
        var packedBytes = AnimationPackSerializer.ConvertToBytes(database);
        var packedFile = PackFile.CreateFromBytes("source.animpack", packedBytes);
        var jsonBytes = Encoding.UTF8.GetBytes("""
            {
              "Properties": { "Combined": false, "FragmentName": "source", "SkeletonName": "male01" },
              "SlotData": {
                "STAND": {
                  "Config": { "BlendInTime": 0.15, "Disable": false, "EquipmentDisplay": 3,
                              "Ignore": false, "SelectionWeight": 1, "SingleFrameVariant": false },
                  "Entries": { "#1": { "animation": "animations/battle/source.anim", "metadata": [] } }
                }
              }
            }
            """);
        var jsonFile = PackFile.CreateFromBytes("source.txt", jsonBytes);
        var container = new PackFileContainer("sources")
        {
            FileList = new Dictionary<string, PackFile>
            {
                [animpackPath] = packedFile,
                [jsonPath] = jsonFile
            }
        };
        var pfs = new Mock<IPackFileService>();
        pfs.Setup(service => service.GetAllPackfileContainers()).Returns([container]);
        pfs.Setup(service => service.GetFileEntriesSnapshot(container)).Returns(container.FileList.ToArray());
        pfs.Setup(service => service.GetFullPath(packedFile, It.IsAny<PackFileContainer?>())).Returns(animpackPath);
        pfs.Setup(service => service.FindFile(jsonPath, It.IsAny<PackFileContainer?>())).Returns(jsonFile);
        var events = Mock.Of<IEventHub>();
        var game = new Mock<IWpfGame>();
        game.Setup(world => world.AddComponent(It.IsAny<SceneObject>())).Returns((SceneObject sceneObject) => sceneObject);
        var sceneEditor = new SceneObjectEditor(
            game.Object, new SceneManager(null!, null!, events), pfs.Object,
            new AnimationsContainerComponent(), null!, events);
        var sceneObject = sceneEditor.CreateAsset("main", "Fragment preview", Microsoft.Xna.Framework.Color.White);
        var viewModel = new BinAnimationViewModel(
            sceneEditor, pfs.Object, Mock.Of<ISkeletonAnimationLookUpHelper>(), sceneObject, Mock.Of<IUiCommandFactory>());

        var fragments = viewModel.LoadFragmentsForSkeleton("male01");

        Assert.Multiple(() =>
        {
            Assert.That(fragments.Select(fragment => fragment.Name), Is.EqualTo(new[] { "packed", "source" }));
            Assert.That(fragments[0].Entries.Single().AnimationFile, Is.EqualTo("animations/battle/packed.anim"));
            Assert.That(fragments[0].Entries.Single().MetaFile, Is.EqualTo("animations/battle/packed.anm.meta"));
            Assert.That(fragments[1].Entries.Single().AnimationFile, Is.EqualTo("animations/battle/source.anim"));
            Assert.That(fragments[1].PackFileReference.FileName, Is.EqualTo(jsonPath));
            Assert.That(packedFile.DataSource.ReadData(), Is.EqualTo(packedBytes));
            Assert.That(jsonFile.DataSource.ReadData(), Is.EqualTo(jsonBytes));
        });
        pfs.Verify(service => service.SaveFile(It.IsAny<PackFile>(), It.IsAny<byte[]>()), Times.Never);
    }
}
