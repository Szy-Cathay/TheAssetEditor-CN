using Editors.AnimationMeta.SuperView.Visualisation;
using Editors.Shared.Core.Common;
using GameWorld.Core.Animation;
using GameWorld.Core.SceneNodes;
using Microsoft.Xna.Framework;
using Moq;
using Shared.Core.PackFiles;
using Shared.GameFormats.Animation;
using Shared.GameFormats.AnimationMeta.Definitions;
using Shared.GameFormats.AnimationMeta.Parsing;
using Shared.GameFormats.AnimationPack;
using Shared.GameFormats.RigidModel.Transforms;

namespace Test.AnimationMeta
{
    [TestFixture]
    public class MetaDataBuilderMissingResourceTests
    {
        [Test]
        public void Build_MissingAnimatedPropModel_UsesSpatialMarkerFallback()
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

            var skeleton = new Mock<ISkeletonProvider>();
            skeleton.SetupGet(value => value.Skeleton).Returns(
                CreateSkeleton("root"));
            var instances = builder.Build(
                null,
                metadata,
                null,
                new GroupNode("root"),
                skeleton.Object,
                new AnimationPlayer(),
                null!).Instances;

            Assert.That(
                instances.OfType<ISpatialMetaDataPreview>().Count(),
                Is.EqualTo(2));
            packFileService.Verify(
                service => service.FindFile(missingModel, null),
                Times.Once);
            packFileService.Verify(
                service => service.FindFile(secondMissingModel, null),
                Times.Once);
        }

        [Test]
        public void Build_MissingOrdinaryPropModel_UsesSpatialMarkerFallback()
        {
            const string missingModel =
                @"variantmeshes\missing\ordinary_prop.rigid_model_v2";
            var packFileService = new Mock<IPackFileService>();
            var builder = new MetaDataBuilder(
                null!,
                null!,
                null!,
                packFileService.Object,
                null!);
            var metadata = new ParsedMetadataFile
            {
                Version = 14,
                Attributes =
                [
                    new Prop_v14
                    {
                        Name = "PROP",
                        Version = 14,
                        ModelName = missingModel,
                        StartTime = 1,
                        EndTime = 2,
                    }
                ]
            };

            var skeleton = new Mock<ISkeletonProvider>();
            skeleton.SetupGet(value => value.Skeleton).Returns(
                CreateSkeleton("root"));
            var instances = builder.Build(
                null,
                metadata,
                null,
                new GroupNode("root"),
                skeleton.Object,
                new AnimationPlayer(),
                null!).Instances;

            Assert.That(
                instances.OfType<ISpatialMetaDataPreview>().Count(),
                Is.EqualTo(1));
            packFileService.Verify(
                service => service.FindFile(missingModel, null),
                Times.Once);
        }

        [Test]
        public void Build_MissingDockAnimation_SkipsOnlyDockRule()
        {
            const string missingAnimation =
                @"animations\missing\dock_equipment.anim";
            var packFileService = new Mock<IPackFileService>();
            var builder = new MetaDataBuilder(
                null!,
                null!,
                null!,
                packFileService.Object,
                null!);
            var metadata = new ParsedMetadataFile
            {
                Version = 10,
                Attributes =
                [
                    new DockEquipmentRWaist_v10
                    {
                        Name = "DOCK_EQPT_RWAIST",
                        Version = 10,
                    }
                ]
            };
            var fragment = new Mock<IAnimationBinGenericFormat>();
            fragment.SetupGet(value => value.Entries).Returns(
            [
                new AnimationBinEntryGenericFormat
                {
                    SlotName = "DOCK_EQUIPMENT_RIGHT_WAIST",
                    AnimationFile = missingAnimation,
                }
            ]);
            var rootPlayer = new AnimationPlayer();
            var skeleton = new Mock<ISkeletonProvider>();
            skeleton.SetupGet(value => value.Skeleton).Returns(
                CreateSkeleton("root"));

            var result = builder.Build(
                null,
                metadata,
                null,
                new GroupNode("root"),
                skeleton.Object,
                rootPlayer,
                fragment.Object);
            Assert.That(rootPlayer.AnimationRules, Is.Empty);
            Assert.That(result.AnimationRules, Is.Empty);
            packFileService.Verify(
                service => service.FindFile(missingAnimation, null),
                Times.Once);
        }

        [Test]
        public void Build_InvalidSplashAttack_PreservesValidLocatorPreview()
        {
            var builder = new MetaDataBuilder(
                null!,
                null!,
                null!,
                Mock.Of<IPackFileService>(),
                null!);
            var metadata = new ParsedMetadataFile
            {
                Version = 10,
                Attributes =
                [
                    new ImpactPosition_v10
                    {
                        Name = "IMPACT_POS",
                        Version = 10,
                        Position = new Vector3(1, 2, 3),
                    },
                    new SplashAttack_v10
                    {
                        Name = "SPLASH_ATTACK",
                        Version = 10,
                        StartPosition = Vector3.Zero,
                        EndPosition = Vector3.Zero,
                        AoeShape = 0,
                        AngleForCone = 45,
                    }
                ]
            };

            var instances = builder.Build(
                null,
                metadata,
                null,
                new GroupNode("root"),
                null!,
                new AnimationPlayer(),
                null!).Instances;

            Assert.That(instances, Has.Count.EqualTo(1));
        }

        [Test]
        public void Build_TimedCombatLocators_AreVisibleOnlyDuringGameplayWindow()
        {
            var builder = new MetaDataBuilder(
                null!,
                null!,
                null!,
                Mock.Of<IPackFileService>(),
                null!);
            var metadata = new ParsedMetadataFile
            {
                Version = 10,
                Attributes =
                [
                    new ImpactPosition_v10
                    {
                        Name = "IMPACT_POS",
                        Version = 10,
                        StartTime = 1,
                        EndTime = 2,
                        Position = new Vector3(1, 2, 3),
                    },
                    new TargetPos_10
                    {
                        Name = "TARGET_POS",
                        Version = 10,
                        StartTime = 1,
                        EndTime = 2,
                        Position = new Vector3(2, 3, 4),
                    },
                    new FirePos_v10
                    {
                        Name = "FIRE_POS",
                        Version = 10,
                        StartTime = 1,
                        EndTime = 2,
                        Position = new Vector3(3, 4, 5),
                    },
                    new SplashAttack_v10
                    {
                        Name = "SPLASH_ATTACK",
                        Version = 10,
                        StartTime = 1,
                        EndTime = 2,
                        StartPosition = Vector3.Zero,
                        EndPosition = new Vector3(0, 0, 2),
                        AoeShape = 1,
                        WidthForCorridor = 1,
                    },
                ]
            };
            var root = new GroupNode("root");

            var instances = builder.Build(
                null,
                metadata,
                null,
                root,
                null!,
                new AnimationPlayer(),
                null).Instances;
            Assert.Multiple(() =>
            {
                Assert.That(instances, Has.Count.EqualTo(4));
                Assert.That(root.Children, Has.Count.EqualTo(4));
            });

            foreach (var instance in instances)
                instance.Update(0);
            Assert.That(root.Children, Has.All.Property("IsVisible").False);

            foreach (var instance in instances)
                instance.Update(1.5f);
            Assert.That(root.Children, Has.All.Property("IsVisible").True);

            foreach (var instance in instances)
                instance.Update(2.5f);
            Assert.That(root.Children, Has.All.Property("IsVisible").False);
        }

        [Test]
        public void Build_InstantCombatLocators_AreVisibleForOneAnimationFrame()
        {
            var builder = new MetaDataBuilder(
                null!,
                null!,
                null!,
                Mock.Of<IPackFileService>(),
                null!);
            var metadata = new ParsedMetadataFile
            {
                Version = 10,
                Attributes =
                [
                    new ImpactPosition_v10
                    {
                        Name = "IMPACT_POS",
                        Version = 10,
                        StartTime = 0.5f,
                        EndTime = 0.5f,
                        Position = new Vector3(1, 2, 3),
                    },
                    new TargetPos_10
                    {
                        Name = "TARGET_POS",
                        Version = 10,
                        StartTime = 0.5f,
                        EndTime = 0.5f,
                        Position = new Vector3(2, 3, 4),
                    },
                    new FirePos_v10
                    {
                        Name = "FIRE_POS",
                        Version = 10,
                        StartTime = 0.5f,
                        EndTime = 0.5f,
                        Position = new Vector3(3, 4, 5),
                    },
                    new SplashAttack_v10
                    {
                        Name = "SPLASH_ATTACK",
                        Version = 10,
                        StartTime = 0.5f,
                        EndTime = 0.5f,
                        StartPosition = Vector3.Zero,
                        EndPosition = new Vector3(0, 0, 2),
                        AoeShape = 1,
                        WidthForCorridor = 1,
                    },
                ]
            };
            var root = new GroupNode("root");
            var player = new AnimationPlayer();
            var clip = new AnimationClip();
            for (var frameIndex = 0; frameIndex < 30; frameIndex++)
                clip.DynamicFrames.Add(new AnimationClip.KeyFrame());
            clip.PlayTimeInSec = 1;
            player.SetAnimation(clip, null!);

            var instances = builder.Build(
                null,
                metadata,
                null,
                root,
                null!,
                player,
                null).Instances;

            foreach (var instance in instances)
                instance.Update(0.49f);
            Assert.That(root.Children, Has.All.Property("IsVisible").False);

            foreach (var instance in instances)
                instance.Update(0.51f);
            Assert.That(root.Children, Has.All.Property("IsVisible").True);

            foreach (var instance in instances)
                instance.Update(0.54f);
            Assert.That(root.Children, Has.All.Property("IsVisible").False);

            foreach (var preview in instances.OfType<ICombatMetaDataPreview>())
                preview.ShowForEntireAnimation = true;
            Assert.That(root.Children, Has.All.Property("IsVisible").True);

            foreach (var preview in instances.OfType<ICombatMetaDataPreview>())
                preview.ShowForEntireAnimation = false;
            Assert.That(root.Children, Has.All.Property("IsVisible").False);
        }

        [Test]
        public void Build_LegacyCombatLocators_CreatesEverySupportedPreview()
        {
            var builder = new MetaDataBuilder(
                null!,
                null!,
                null!,
                Mock.Of<IPackFileService>(),
                null!);
            var metadata = new ParsedMetadataFile
            {
                Version = 2,
                Attributes =
                [
                    new ImpactPosition_v2
                    {
                        Name = "IMPACT_POS",
                        Version = 2,
                        Position = new Vector3(1, 0, 0),
                    },
                    new TargetPos_0
                    {
                        Name = "TARGET_POS",
                        Version = 0,
                        Position = new Vector3(2, 0, 0),
                    },
                    new FirePos_v0
                    {
                        Name = "FIRE_POS",
                        Version = 0,
                        Position = new Vector3(3, 0, 0),
                    },
                    new FirePos_v2
                    {
                        Name = "FIRE_POS",
                        Version = 2,
                        Position = new Vector3(4, 0, 0),
                    },
                    new SplashAttack_v3
                    {
                        Name = "SPLASH_ATTACK",
                        Version = 3,
                        StartPosition = Vector3.Zero,
                        EndPosition = new Vector3(0, 0, 2),
                        AoeShape = 1,
                        WidthForCorridor = 1,
                    },
                ]
            };
            var root = new GroupNode("root");

            var instances = builder.Build(
                null,
                metadata,
                null,
                root,
                null!,
                new AnimationPlayer(),
                null).Instances;

            Assert.Multiple(() =>
            {
                Assert.That(instances, Has.Count.EqualTo(5));
                Assert.That(root.Children, Has.Count.EqualTo(5));
                Assert.That(
                    root.Children.Select(node => node.Name),
                    Is.EquivalentTo(
                    [
                        "ImpactPos",
                        "TargetPos",
                        "FirePos",
                        "FirePos",
                        "SplashAttack_0",
                    ]));
            });
        }

        [Test]
        public void Build_CombatLocators_ExposeIndependentVisibilityAndFocusPositions()
        {
            var builder = new MetaDataBuilder(
                null!,
                null!,
                null!,
                Mock.Of<IPackFileService>(),
                null!);
            var impactPosition = new Vector3(1, 2, 3);
            var targetPosition = new Vector3(4, 5, 6);
            var firePosition = new Vector3(7, 8, 9);
            var splashStart = new Vector3(0, 0, 2);
            var splashEnd = new Vector3(0, 0, 6);
            var impactAttribute = new ImpactPosition_v10
            {
                Name = "IMPACT_POS",
                Version = 10,
                Position = impactPosition,
            };
            var metadata = new ParsedMetadataFile
            {
                Version = 10,
                Attributes =
                [
                    impactAttribute,
                    new TargetPos_10
                    {
                        Name = "TARGET_POS",
                        Version = 10,
                        Position = targetPosition,
                    },
                    new FirePos_v10
                    {
                        Name = "FIRE_POS",
                        Version = 10,
                        Position = firePosition,
                    },
                    new SplashAttack_v10
                    {
                        Name = "SPLASH_ATTACK",
                        Version = 10,
                        StartPosition = splashStart,
                        EndPosition = splashEnd,
                        AoeShape = 1,
                        WidthForCorridor = 1,
                    },
                ]
            };
            var root = new GroupNode("root");

            var instances = builder.Build(
                null,
                metadata,
                null,
                root,
                null!,
                new AnimationPlayer(),
                null).Instances;
            var previews = instances.Cast<ICombatMetaDataPreview>().ToList();
            var impact = previews.Single(
                preview => preview.Category == CombatMetaDataPreviewCategory.Impact);
            impact.IsEnabled = false;
            foreach (var instance in instances)
                instance.Update(0);

            Assert.Multiple(() =>
            {
                Assert.That(
                    previews.Select(preview => preview.Category),
                    Is.EquivalentTo(
                    [
                        CombatMetaDataPreviewCategory.Impact,
                        CombatMetaDataPreviewCategory.Target,
                        CombatMetaDataPreviewCategory.Fire,
                        CombatMetaDataPreviewCategory.Splash,
                    ]));
                Assert.That(impact.FocusPosition, Is.EqualTo(impactPosition));
                Assert.That(
                    previews.Single(preview =>
                        preview.Category == CombatMetaDataPreviewCategory.Target)
                        .FocusPosition,
                    Is.EqualTo(targetPosition));
                Assert.That(
                    previews.Single(preview =>
                        preview.Category == CombatMetaDataPreviewCategory.Fire)
                        .FocusPosition,
                    Is.EqualTo(firePosition));
                Assert.That(
                    previews.Single(preview =>
                        preview.Category == CombatMetaDataPreviewCategory.Splash)
                        .FocusPosition,
                    Is.EqualTo((splashStart + splashEnd) / 2));
                Assert.That(
                    root.Children.Single(node => node.Name == "ImpactPos").IsVisible,
                    Is.False);
                Assert.That(
                    root.Children.Where(node => node.Name != "ImpactPos"),
                    Has.All.Property("IsVisible").True);
            });

            var movedImpactPosition = new Vector3(9, 8, 7);
            impactAttribute.Position = movedImpactPosition;
            instances.Single(instance => ReferenceEquals(
                ((ICombatMetaDataPreview)instance).Source,
                impactAttribute)).Update(0);
            Assert.That(
                impact.FocusPosition,
                Is.EqualTo(movedImpactPosition));
        }

        [Test]
        public void Build_SplashAttack_UsesOneLiveCoordinateSpace()
        {
            var builder = new MetaDataBuilder(
                null!,
                null!,
                null!,
                Mock.Of<IPackFileService>(),
                null!);
            var splash = new SplashAttack_v10
            {
                Name = "SPLASH_ATTACK",
                Version = 10,
                StartPosition = new Vector3(1, 2, 3),
                EndPosition = new Vector3(1, 2, 7),
                AoeShape = 1,
                WidthForCorridor = 1,
            };
            var root = new GroupNode("root");
            var instance = builder.Build(
                    null,
                    new ParsedMetadataFile
                    {
                        Version = 10,
                        Attributes = [splash],
                    },
                    splash,
                    root,
                    null!,
                    new AnimationPlayer(),
                    null).Instances
                .Single();
            var preview = (ICombatMetaDataPreview)instance;
            var node = root.Children.Single();

            instance.Update(0);
            Assert.Multiple(() =>
            {
                Assert.That(
                    preview.FocusPosition,
                    Is.EqualTo(new Vector3(1, 2, 5)));
                Assert.That(node.ModelMatrix, Is.EqualTo(Matrix.Identity));
            });

            splash.StartPosition = new Vector3(4, 5, 6);
            splash.EndPosition = new Vector3(4, 5, 10);
            instance.Update(0);

            Assert.Multiple(() =>
            {
                Assert.That(
                    preview.FocusPosition,
                    Is.EqualTo(new Vector3(4, 5, 8)));
                Assert.That(node.ModelMatrix, Is.EqualTo(Matrix.Identity));
            });
        }

        [Test]
        public void Build_TrackedEffect_UsesLiveBoneRelativeLocatorPosition()
        {
            var builder = new MetaDataBuilder(
                null!,
                null!,
                null!,
                Mock.Of<IPackFileService>(),
                null!);
            var boneTranslation = new Vector3(10, 20, 30);
            var effect = new Effect_v11
            {
                Name = "EFFECT",
                Version = 11,
                Tracking = true,
                NodeIndex = 0,
                Position = new Vector3(1, 2, 3),
                Orientation = Quaternion.Identity.ToVector4(),
                StartTime = 0,
                EndTime = 1,
            };
            var metadata = new ParsedMetadataFile
            {
                Version = 10,
                Attributes = [effect]
            };
            var skeleton = new Mock<ISkeletonProvider>();
            skeleton.SetupGet(value => value.Skeleton).Returns(
                CreateSkeleton("effect_bone", boneTranslation));

            var preview = builder.Build(
                    null,
                    metadata,
                    effect,
                    new GroupNode("root"),
                    skeleton.Object,
                    new AnimationPlayer(),
                    null).Instances
                .OfType<ISpatialMetaDataPreview>()
                .Single();

            ((IMetaDataInstance)preview).Update(0);
            Assert.That(
                preview.FocusPosition,
                Is.EqualTo(effect.Position + boneTranslation));

            effect.Position = new Vector3(4, 5, 6);
            ((IMetaDataInstance)preview).Update(0);
            Assert.That(
                preview.FocusPosition,
                Is.EqualTo(effect.Position + boneTranslation));
        }

        private static GameSkeleton CreateSkeleton(
            string boneName,
            Vector3? translation = null)
        {
            var skeletonFile = new AnimationFile
            {
                Header = new AnimationFile.AnimationHeader
                {
                    SkeletonName = "TestSkeleton"
                },
                Bones =
                [
                    new AnimationFile.BoneInfo
                    {
                        Name = boneName,
                        ParentId = -1
                    }
                ]
            };
            var skeletonFrame = new AnimationFile.Frame();
            skeletonFrame.Transforms.Add(new RmvVector3(
                translation ?? Vector3.Zero));
            skeletonFrame.Quaternion.Add(new RmvVector4(0, 0, 0, 1));
            var skeletonPart = new AnimationFile.AnimationPart();
            skeletonPart.DynamicFrames.Add(skeletonFrame);
            skeletonFile.AnimationParts.Add(skeletonPart);
            return new GameSkeleton(skeletonFile, null!);
        }
    }
}
