using Editors.AnimationMeta.SuperView.Visualisation;
using Moq;
using Shared.Core.PackFiles;
using Shared.GameFormats.AnimationMeta.Definitions;
using Shared.GameFormats.AnimationMeta.Parsing;

namespace Test.AnimationMeta
{
    [TestFixture]
    public class MetaDataBuilderMissingResourceTests
    {
        [Test]
        public void Create_MissingAnimatedPropModel_SkipsPreview()
        {
            const string missingModel =
                @"variantmeshes\missing\codex_missing_model.rigid_model_v2";
            const string secondMissingModel =
                @"variantmeshes\missing\codex_missing_model_2.rigid_model_v2";
            var packFileService = new Mock<IPackFileService>();
            var builder = new MetaDataBuilder(
                null!,
                null!,
                null!,
                packFileService.Object,
                null!);
            var metadata = new ParsedMetadataFile
            {
                Version = 2,
                Attributes =
                [
                    new AnimatedProp_v14
                    {
                        Name = "ANIMATED_PROP",
                        Version = 14,
                        ModelName = missingModel,
                        AnimationName =
                            @"animations\missing\codex_missing_animation.anim"
                    },
                    new AnimatedProp_v14
                    {
                        Name = "ANIMATED_PROP",
                        Version = 14,
                        ModelName = secondMissingModel,
                        AnimationName =
                            @"animations\missing\codex_missing_animation_2.anim"
                    }
                ]
            };

            var instances = builder.Create(
                null,
                metadata,
                null,
                null!,
                null!,
                null!,
                null!);

            Assert.That(instances, Is.Empty);
            packFileService.Verify(
                service => service.FindFile(missingModel, null),
                Times.Once);
            packFileService.Verify(
                service => service.FindFile(secondMissingModel, null),
                Times.Once);
        }
    }
}
