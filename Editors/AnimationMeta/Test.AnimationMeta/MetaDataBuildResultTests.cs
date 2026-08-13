using Editors.AnimationMeta.SuperView.Visualisation;
using Editors.AnimationMeta.SuperView.Visualisation.Rules;
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
    public class MetaDataBuildResultTests
    {
        [Test]
        public void Build_MissingModel_ReturnsFallbackAndStructuredDiagnostic()
        {
            const string missingModel =
                @"variantmeshes\missing\codex_missing_model.rigid_model_v2";
            var prop = new Prop_v14
            {
                Name = "PROP",
                Version = 14,
                ModelName = missingModel,
                StartTime = 1,
                EndTime = 2,
                Position = new Vector3(4, 5, 6),
            };
            var metadata = new ParsedMetadataFile
            {
                Version = 14,
                Attributes =
                [
                    prop,
                    new ImpactPosition_v10
                    {
                        Name = "IMPACT_POS",
                        Version = 10,
                        Position = new Vector3(1, 2, 3),
                    },
                ]
            };
            var skeleton = new Mock<ISkeletonProvider>();
            skeleton.SetupGet(value => value.Skeleton).Returns(
                CreateSkeleton("root"));
            var builder = new MetaDataBuilder(
                null!,
                null!,
                null!,
                Mock.Of<IPackFileService>(),
                null!);

            var result = builder.Build(
                null,
                metadata,
                null,
                new GroupNode("root"),
                skeleton.Object,
                new AnimationPlayer(),
                null);

            Assert.Multiple(() =>
            {
                Assert.That(result.Instances, Has.Count.EqualTo(2));
                Assert.That(result.AnimationRules, Is.Empty);
                Assert.That(result.Diagnostics, Has.Count.EqualTo(1));
            });
            var diagnostic = result.Diagnostics.Single();
            Assert.Multiple(() =>
            {
                Assert.That(diagnostic.Source, Is.SameAs(prop));
                Assert.That(
                    diagnostic.Owner,
                    Is.EqualTo(MetaDataDocumentOwner.Animation));
                Assert.That(
                    diagnostic.Severity,
                    Is.EqualTo(MetaDataDiagnosticSeverity.Warning));
                Assert.That(
                    diagnostic.ReasonKey,
                    Is.EqualTo("SuperView.Diagnostics.MissingModel"));
                Assert.That(
                    diagnostic.TimeRange,
                    Is.EqualTo(new MetaDataTimeRange(
                        1,
                        2,
                        MetaDataZeroRangeBehavior.WholeAnimation)));
                Assert.That(
                    diagnostic.Position,
                    Is.EqualTo(new Vector3(4, 5, 6)));
                Assert.That(diagnostic.ResourcePath, Is.EqualTo(missingModel));
            });
        }

        [Test]
        public void Build_PersistentThenAnimation_PreservesReferenceAndRuleOrder()
        {
            var persistentPreview = new ImpactPosition_v10
            {
                Name = "IMPACT_POS",
                Version = 10,
                Position = new Vector3(1, 2, 3),
            };
            var persistentRule = new Transform_v10
            {
                Name = "TRANSFORM",
                Version = 10,
                TargetNode = 0,
            };
            var animationPreview = new TargetPos_10
            {
                Name = "TARGET_POS",
                Version = 10,
                Position = new Vector3(4, 5, 6),
            };
            var animationRule = new Transform_v10
            {
                Name = "TRANSFORM",
                Version = 10,
                TargetNode = 1,
            };
            var builder = CreateBuilder();
            var skeleton = new Mock<ISkeletonProvider>();
            skeleton.SetupGet(value => value.Skeleton).Returns(
                CreateSkeleton("root", "target"));

            var result = builder.Build(
                new ParsedMetadataFile
                {
                    Version = 10,
                    Attributes = [persistentPreview, persistentRule],
                },
                new ParsedMetadataFile
                {
                    Version = 10,
                    Attributes = [animationPreview, animationRule],
                },
                null,
                new GroupNode("root"),
                skeleton.Object,
                new AnimationPlayer(),
                null);

            Assert.Multiple(() =>
            {
                Assert.That(
                    result.Instances.Cast<IMetaDataPreview>()
                        .Select(preview => preview.Source),
                    Is.EqualTo(new ParsedMetadataAttribute[]
                    {
                        persistentPreview,
                        persistentRule,
                        animationPreview,
                        animationRule,
                    }));
                Assert.That(
                    result.AnimationRules,
                    Has.All.TypeOf<TransformBoneRule>());
                Assert.That(result.AnimationRules, Has.Count.EqualTo(2));
                Assert.That(result.Diagnostics, Is.Empty);
            });
        }

        [Test]
        public void Build_DisablePersistent_ExcludesPersistentInstancesAndRules()
        {
            var persistentPreview = new ImpactPosition_v10
            {
                Name = "IMPACT_POS",
                Version = 10,
            };
            var persistentRule = new Transform_v10
            {
                Name = "TRANSFORM",
                Version = 10,
            };
            var animationPreview = new TargetPos_10
            {
                Name = "TARGET_POS",
                Version = 10,
            };
            var builder = CreateBuilder();

            var result = builder.Build(
                new ParsedMetadataFile
                {
                    Version = 10,
                    Attributes = [persistentPreview, persistentRule],
                },
                new ParsedMetadataFile
                {
                    Version = 10,
                    Attributes =
                    [
                        new DisablePersistant_v10
                        {
                            Name = "DISABLE_PERSISTENT",
                            Version = 10,
                        },
                        animationPreview,
                    ],
                },
                null,
                new GroupNode("root"),
                null!,
                new AnimationPlayer(),
                null);

            Assert.Multiple(() =>
            {
                Assert.That(
                    result.Instances.Cast<IMetaDataPreview>().Single().Source,
                    Is.SameAs(animationPreview));
                Assert.That(result.AnimationRules, Is.Empty);
            });
        }

        [Test]
        public void Build_UnsupportedAttribute_PreservesSourceWithoutDiagnostic()
        {
            var unknown = new ParsedUnknownMetadataAttribute
            {
                Name = "CODEX_UNKNOWN",
                Version = 99,
                Data = [1, 2, 3],
            };
            var originalData = unknown.Data.ToArray();
            var metadata = new ParsedMetadataFile
            {
                Version = 99,
                Attributes = [unknown],
            };

            var result = CreateBuilder().Build(
                null,
                metadata,
                null,
                new GroupNode("root"),
                null!,
                new AnimationPlayer(),
                null);

            Assert.Multiple(() =>
            {
                Assert.That(result.Instances, Is.Empty);
                Assert.That(result.AnimationRules, Is.Empty);
                Assert.That(result.Diagnostics, Is.Empty);
                Assert.That(metadata.Attributes.Single(), Is.SameAs(unknown));
                Assert.That(unknown.Data, Is.EqualTo(originalData));
            });
        }

        [Test]
        public void Build_MissingDockAnimation_ReturnsDiagnosticAndOtherRule()
        {
            var dock = new DockEquipmentRWaist_v10
            {
                Name = "DOCK_EQPT_RWAIST",
                Version = 10,
            };
            var transform = new Transform_v10
            {
                Name = "TRANSFORM",
                Version = 10,
            };
            var fragment = new Mock<IAnimationBinGenericFormat>();
            fragment.SetupGet(value => value.Entries).Returns(
            [
                new AnimationBinEntryGenericFormat
                {
                    SlotName = "DOCK_EQUIPMENT_RIGHT_WAIST",
                    AnimationFile = @"animations\missing\dock.anim",
                }
            ]);
            var skeleton = new Mock<ISkeletonProvider>();
            skeleton.SetupGet(value => value.Skeleton).Returns(
                CreateSkeleton("root"));

            var result = CreateBuilder().Build(
                null,
                new ParsedMetadataFile
                {
                    Version = 10,
                    Attributes = [dock, transform],
                },
                null,
                new GroupNode("root"),
                skeleton.Object,
                new AnimationPlayer(),
                fragment.Object);

            Assert.Multiple(() =>
            {
                Assert.That(
                    result.AnimationRules.Single(),
                    Is.TypeOf<TransformBoneRule>());
                Assert.That(result.Diagnostics, Has.Count.EqualTo(1));
                Assert.That(result.Diagnostics.Single().Source, Is.SameAs(dock));
                Assert.That(
                    result.Diagnostics.Single().ReasonKey,
                    Is.EqualTo("SuperView.Diagnostics.MissingAnimation"));
            });
        }

        [Test]
        public void Build_MissingEffectAndBone_ReturnsDiagnosticsAndOtherPreview()
        {
            var effect = new Effect_v11
            {
                Name = "EFFECT",
                Version = 11,
                VfxName = "codex_missing_effect",
                Tracking = true,
                NodeIndex = 9,
                Position = new Vector3(4, 5, 6),
                Orientation = new Vector4(0, 0, 0, 1),
            };
            var impact = new ImpactPosition_v10
            {
                Name = "IMPACT_POS",
                Version = 10,
                Position = new Vector3(1, 2, 3),
            };
            var skeleton = new Mock<ISkeletonProvider>();
            skeleton.SetupGet(value => value.Skeleton).Returns(
                CreateSkeleton("root"));

            var result = CreateBuilder().Build(
                null,
                new ParsedMetadataFile
                {
                    Version = 10,
                    Attributes = [effect, impact],
                },
                null,
                new GroupNode("root"),
                skeleton.Object,
                new AnimationPlayer(),
                null);

            Assert.Multiple(() =>
            {
                Assert.That(result.Instances, Has.Count.EqualTo(2));
                Assert.That(
                    result.Diagnostics.Select(diagnostic =>
                        diagnostic.ReasonKey),
                    Is.EquivalentTo(new[]
                    {
                        "SuperView.Diagnostics.MissingEffect",
                        "SuperView.Diagnostics.MissingBone",
                    }));
                Assert.That(
                    result.Diagnostics,
                    Has.All.Property(nameof(MetaDataBuildDiagnostic.Source))
                        .SameAs(effect));
                Assert.That(
                    result.Diagnostics,
                    Has.All.Property(nameof(MetaDataBuildDiagnostic.Owner))
                        .EqualTo(MetaDataDocumentOwner.Animation));
            });
        }

        [Test]
        public void Build_RuleReadFailure_ReturnsDiagnosticAndOtherRule()
        {
            var dock = new DockEquipmentRWaist_v10
            {
                Name = "DOCK_EQPT_RWAIST",
                Version = 10,
            };
            var transform = new Transform_v10
            {
                Name = "TRANSFORM",
                Version = 10,
            };
            var fragment = new Mock<IAnimationBinGenericFormat>();
            fragment.SetupGet(value => value.Entries)
                .Throws(new InvalidOperationException("codex test failure"));
            var skeleton = new Mock<ISkeletonProvider>();
            skeleton.SetupGet(value => value.Skeleton).Returns(
                CreateSkeleton("root"));

            var result = CreateBuilder().Build(
                null,
                new ParsedMetadataFile
                {
                    Version = 10,
                    Attributes = [dock, transform],
                },
                null,
                new GroupNode("root"),
                skeleton.Object,
                new AnimationPlayer(),
                fragment.Object);

            Assert.Multiple(() =>
            {
                Assert.That(
                    result.AnimationRules.Single(),
                    Is.TypeOf<TransformBoneRule>());
                Assert.That(result.Diagnostics, Has.Count.EqualTo(1));
                Assert.That(result.Diagnostics.Single().Source, Is.SameAs(dock));
                Assert.That(
                    result.Diagnostics.Single().ReasonKey,
                    Is.EqualTo("SuperView.Diagnostics.RuleUnavailable"));
            });
        }

        [Test]
        public void Build_DiagnosticContextReadFailure_StillReturnsDiagnostics()
        {
            var effect = new ThrowingEffectMeta
            {
                Name = "EFFECT",
                Version = 99,
            };

            MetaDataBuildResult? result = null;
            Assert.DoesNotThrow(() =>
            {
                result = CreateBuilder().Build(
                    null,
                    new ParsedMetadataFile
                    {
                        Version = 99,
                        Attributes = [effect],
                    },
                    null,
                    new GroupNode("root"),
                    null!,
                    new AnimationPlayer(),
                    null);
            });

            Assert.Multiple(() =>
            {
                Assert.That(result!.Instances, Has.Count.EqualTo(1));
                Assert.That(
                    result.Diagnostics.Select(diagnostic => diagnostic.ReasonKey),
                    Is.EqualTo(new[] { "SuperView.Diagnostics.MissingEffect" }));
                Assert.That(
                    result.Diagnostics,
                    Has.All.Property(nameof(MetaDataBuildDiagnostic.Source))
                        .SameAs(effect));
                Assert.That(result.Diagnostics.Single().Position, Is.Null);
            });
        }

        [TestCaseSource(nameof(RulePreviewSources))]
        public void BuildPreview_RuleSource_RequiresFullBuild(
            ParsedMetadataAttribute source)
        {
            var result = CreateBuilder().BuildPreview(
                source,
                MetaDataDocumentOwner.Animation,
                false,
                new GroupNode("root"),
                null!,
                new AnimationPlayer());

            Assert.Multiple(() =>
            {
                Assert.That(result.IsSupported, Is.False);
                Assert.That(result.Instance, Is.Null);
                Assert.That(result.Diagnostics, Is.Empty);
            });
        }

        private static IEnumerable<ParsedMetadataAttribute>
            RulePreviewSources()
        {
            yield return new Transform_v10
            {
                Name = "TRANSFORM",
                Version = 10,
            };
            yield return new DockEquipmentRWaist_v10
            {
                Name = "DOCK_EQPT_RWAIST",
                Version = 10,
            };
        }

        private static MetaDataBuilder CreateBuilder() => new(
            null!,
            null!,
            null!,
            Mock.Of<IPackFileService>(),
            null!);

        private static GameSkeleton CreateSkeleton(params string[] boneNames)
        {
            var skeletonFile = new AnimationFile
            {
                Header = new AnimationFile.AnimationHeader
                {
                    SkeletonName = "TestSkeleton"
                },
                Bones = boneNames.Select((name, index) =>
                    new AnimationFile.BoneInfo
                    {
                        Name = name,
                        ParentId = index - 1,
                    }).ToArray(),
            };
            var skeletonFrame = new AnimationFile.Frame();
            foreach (var _ in boneNames)
            {
                skeletonFrame.Transforms.Add(new RmvVector3(Vector3.Zero));
                skeletonFrame.Quaternion.Add(new RmvVector4(0, 0, 0, 1));
            }
            var skeletonPart = new AnimationFile.AnimationPart();
            skeletonPart.DynamicFrames.Add(skeletonFrame);
            skeletonFile.AnimationParts.Add(skeletonPart);
            return new GameSkeleton(skeletonFile, null!);
        }

        private sealed class ThrowingEffectMeta : ParsedMetadataAttribute, IEffectMeta
        {
            public string VfxName { get; set; } = "codex_missing_effect";
            public int NodeIndex { get; set; }
            public bool Tracking { get; set; }
            public Vector3 Position
            {
                get => throw new InvalidOperationException("codex test failure");
                set { }
            }
            public Vector4 Orientation { get; set; }
            public float EffectStartTime { get; set; }
            public float EffectEndTime { get; set; }
        }
    }
}
