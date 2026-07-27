using GameWorld.Core.Components;
using GameWorld.Core.SceneNodes;
using GameWorld.Core.Services.SceneSaving;
using GameWorld.Core.Services.SceneSaving.Lod.Strategies;
using Moq;
using Shared.Core.Events;

namespace GameWorld.Core.Test.Services.SceneSaving
{
    [TestFixture]
    public class LodGeneratorBaseTests
    {
        [Test]
        public void CreateLodsForRootNode_RemovesEachExistingLodOnlyOnce()
        {
            var eventHub = new Mock<IEventHub>();
            var sceneManager = new SceneManager(null!, null!, eventHub.Object);
            var model = sceneManager.RootNode.AddObject(new Rmv2ModelNode("Model"));
            model.AddObject(new Rmv2LodNode("Lod 0", 0));
            model.AddObject(CreateLodWithChild(1));
            model.AddObject(CreateLodWithChild(2));

            new TestLodGenerator().CreateLodsForRootNode(model, [new LodGenerationSettings()]);

            Assert.That(model.GetLodNodes().Select(x => x.LodValue), Is.EqualTo(new[] { 0 }));
            eventHub.Verify(
                x => x.Publish(It.IsAny<SceneObjectRemovedEvent>()),
                Times.Exactly(4));
        }

        static Rmv2LodNode CreateLodWithChild(int index)
        {
            var lod = new Rmv2LodNode($"Lod {index}", index);
            lod.AddObject(new GroupNode("Child"));
            return lod;
        }

        sealed class TestLodGenerator : LodGeneratorBase
        {
            protected override void ReduceMesh(Rmv2MeshNode rmv2MeshNode, float deductionRatio)
            {
            }
        }
    }
}
