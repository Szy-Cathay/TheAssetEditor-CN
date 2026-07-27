using System.Reflection;
using GameWorld.Core.Components;
using GameWorld.Core.SceneNodes;
using GameWorld.Core.Services.SceneSaving;
using GameWorld.Core.Services.SceneSaving.Geometry;
using GameWorld.Core.Services.SceneSaving.Geometry.Strategies;
using GameWorld.Core.Services.SceneSaving.Lod;
using GameWorld.Core.Services.SceneSaving.Lod.Strategies;
using GameWorld.Core.Services.SceneSaving.Material;
using GameWorld.Core.Services.SceneSaving.Material.Strategies;
using KitbasherEditor.ViewModels.SaveDialog;
using Moq;
using Shared.Core.Events;
using Shared.Core.PackFiles;
using Shared.Core.Services;
using Shared.Core.Settings;

namespace Test.KitbashEditor
{
    [TestFixture]
    public class SaveDialogViewModelTests
    {
        [Test]
        public void Initialize_DoesNotMarkSettingsAsUserConfirmed()
        {
            var context = CreateContext();

            Initialize(context.ViewModel, context.Settings);

            Assert.That(context.Settings.IsUserInitialized, Is.False);
        }

        [Test]
        public void ApplySettings_MarksSettingsAsUserConfirmed()
        {
            var context = CreateContext();
            Initialize(context.ViewModel, context.Settings);
            context.Settings.IsUserInitialized = false;

            context.ViewModel.ApplySettings();

            Assert.That(context.Settings.IsUserInitialized, Is.True);
        }

        [Test]
        public void Initialize_PreservesExistingLodSettings()
        {
            var context = CreateContext();
            context.Settings.NumberOfLodsToGenerate = 1;
            context.Settings.LodSettingsPerLod =
            [
                new LodGenerationSettings
                {
                    CameraDistance = 321,
                    QualityLvl = 4,
                    LodRectionFactor = 0.75f
                }
            ];

            Initialize(context.ViewModel, context.Settings);

            Assert.Multiple(() =>
            {
                Assert.That(context.Settings.LodSettingsPerLod, Has.Count.EqualTo(1));
                Assert.That(context.Settings.LodSettingsPerLod[0].CameraDistance, Is.EqualTo(321));
                Assert.That(context.Settings.LodSettingsPerLod[0].QualityLvl, Is.EqualTo(4));
            });
        }

        [Test]
        public void EditingLodCount_DoesNotMutateSettingsUntilApply()
        {
            var context = CreateContext();
            context.Settings.RefreshLodSettings();
            Initialize(context.ViewModel, context.Settings);

            context.ViewModel.NumberOfLodsToGenerate = 3;

            Assert.Multiple(() =>
            {
                Assert.That(context.Settings.NumberOfLodsToGenerate, Is.EqualTo(1));
                Assert.That(context.Settings.LodSettingsPerLod, Has.Count.EqualTo(1));
            });

            context.ViewModel.ApplySettings();

            Assert.Multiple(() =>
            {
                Assert.That(context.Settings.NumberOfLodsToGenerate, Is.EqualTo(3));
                Assert.That(context.Settings.LodSettingsPerLod, Has.Count.EqualTo(3));
            });
        }

        [Test]
        public void ChangingOnlySaveVisible_RebuildsLodOverview()
        {
            var context = CreateContext();
            context.Settings.RefreshLodSettings();
            Initialize(context.ViewModel, context.Settings);
            var originalOverview = context.ViewModel.LodNodes.Single();

            context.ViewModel.OnlySaveVisible = !context.ViewModel.OnlySaveVisible;

            Assert.That(context.ViewModel.LodNodes.Single(), Is.Not.SameAs(originalOverview));
        }

        private static void Initialize(
            SaveDialogViewModel viewModel,
            GeometrySaveSettings settings)
        {
            var initializeMethod = typeof(SaveDialogViewModel).GetMethod(
                "Initialize",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(initializeMethod, Is.Not.Null);
            initializeMethod!.Invoke(viewModel, [settings]);
        }

        private static SaveDialogTestContext CreateContext()
        {
            var eventHub = new Mock<IEventHub>();
            var packFileService = new Mock<IPackFileService>();
            var sceneManager = new SceneManager(null!, null!, eventHub.Object);
            sceneManager.RootNode.AddObject(new MainEditableNode(
                SpecialNodes.EditableModel,
                new SkeletonNode(null),
                packFileService.Object));

            var saveService = new SaveService(
                packFileService.Object,
                eventHub.Object,
                new GeometryStrategyProvider([new NoMeshStrategy()]),
                new LodStrategyProvider([new NoLodGeneration()]),
                new MaterialStrategyProvider([new NoWsModelStrategy()]));
            var viewModel = new SaveDialogViewModel(
                sceneManager,
                saveService,
                packFileService.Object,
                Mock.Of<IStandardDialogs>());
            var settings = new GeometrySaveSettings(
                new ApplicationSettingsService(GameTypeEnum.Rome2))
            {
                GeometryOutputType = GeometryStrategy.None,
                MaterialOutputType = MaterialStrategy.None,
                LodGenerationMethod = LodStrategy.NoLodGeneration,
                NumberOfLodsToGenerate = 1,
            };

            return new SaveDialogTestContext(viewModel, settings);
        }

        private sealed record SaveDialogTestContext(
            SaveDialogViewModel ViewModel,
            GeometrySaveSettings Settings);
    }
}
