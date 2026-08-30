using Editors.AnimationMeta.Presentation;
using Editors.AnimationMeta.SuperView;
using Editors.AnimationMeta.SuperView.Editing;
using Editors.AnimationMeta.SuperView.Visualisation;
using Editors.AnimationMeta.SuperView.Visualisation.Instances;
using Editors.Shared.Core.Common;
using GameWorld.Core.Components.Rendering;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Xna.Framework;
using Shared.Core.Events;
using Shared.Core.Events.Global;
using Shared.Core.Misc;
using Shared.Core.PackFiles.Models;
using Shared.Core.Services;
using Shared.Core.ToolCreation;
using Shared.GameFormats.AnimationMeta.Definitions;
using Shared.GameFormats.AnimationMeta.Parsing;
using Test.TestingUtility.Shared;
using Test.TestingUtility.TestUtility;

namespace Test.AnimationMeta
{
    public class MetaDataEditorViewModelTests
    {
        [Test]
        public void MetaDataEditor_OpenAndVerify()
        {
            var packFile = PathHelper.GetDataFile("Throt.pack");

            var runner = new AssetEditorTestRunner();
            runner.CreateCaContainer();
            var outputPackFile = runner.LoadPackFile(packFile, true);

            var filePath = @"animations/battle/humanoid17/throt_whip_catcher/attacks/hu17_whip_catcher_attack_05.anm.meta";
            var metaPackFile = runner.PackFileService.FindFile(filePath);
            var editor = runner.CommandFactory
                .Create<OpenEditorCommand>()
                .Execute<MetaDataEditorViewModel>(metaPackFile!, Shared.Core.ToolCreation.EditorEnums.Meta_Editor);

            Assert.That(editor.ParsedFile, Is.Not.Null);
            Assert.That(editor.ParsedFile.Attributes.Count, Is.EqualTo(7));
            Assert.That(editor.ParsedFile.Attributes[0], Is.InstanceOf<AnimatedProp_v14>());
            Assert.That(editor.ParsedFile.Attributes[4], Is.InstanceOf<SplashAttack_v10>());
            Assert.That(editor.HasUnsavedChanges, Is.False);

            var splashVariables = editor.Tags[4].Variables;
            Assert.Multiple(() =>
            {
                Assert.That(
                    splashVariables.Single(variable =>
                        variable.PropertyName == "StartTime").IsStartTime,
                    Is.True);
                Assert.That(
                    splashVariables.Single(variable =>
                        variable.PropertyName == "EndTime").IsEndTime,
                    Is.True);
                Assert.That(
                    splashVariables.Single(variable =>
                        variable.PropertyName == "StartPosition")
                        .IsCombatPositionAnchor,
                    Is.True);
                Assert.That(
                    splashVariables.Single(variable =>
                        variable.PropertyName == "EndPosition")
                        .IsCombatPositionAnchor,
                    Is.False);
            });

            editor.SaveActionCommand.Execute(null);

            var savedFile = runner.PackFileService.FindFile(filePath, outputPackFile);
            Assert.That(savedFile, Is.Not.Null);
        }


        [Test]
        public void MetaDataEditor_OpenModifyAndSave()
        {
            var packFile = PathHelper.GetDataFile("Throt.pack");

            var runner = new AssetEditorTestRunner();
            runner.CreateCaContainer();
            var outputPackFile = runner.LoadPackFile(packFile, true);

            var filePath = @"animations/battle/humanoid17/throt_whip_catcher/attacks/hu17_whip_catcher_attack_05.anm.meta";
            var metaPackFile = runner.PackFileService.FindFile(filePath);
            var editor = runner.CommandFactory
                .Create<OpenEditorCommand>()
                .Execute<MetaDataEditorViewModel>(metaPackFile!, Shared.Core.ToolCreation.EditorEnums.Meta_Editor);

            Assert.That(editor.ParsedFile, Is.Not.Null);


            //SplashAttack_v10 - Filter - string
            var strValue = "customstr";
            editor.Tags[4].Variables[3].ValueAsString = strValue;

            //SplashAttack_v10 - AoeShape - int
            var intValue = 1;
            Assert.That(
                editor.Tags[4].Variables[5],
                Is.InstanceOf<ChoiceAttributeViewModel>());
            editor.Tags[4].Variables[5].ValueAsString = intValue.ToString();

            //SplashAttack_v10 - EndPosition - Vector 3
            var vectorValue = 120;
            (editor.Tags[4].Variables[7] as VectorAttributeViewModel)!.Value.X.Value = vectorValue;

            // HasUnsavedChanges should be signaled via events in superview, but editor itself doesn't track it automatically.
            // Ensure saving works and the saved file exists in output pack
            editor.SaveActionCommand.Execute(null);

            var savedFile = runner.PackFileService.FindFile(filePath, outputPackFile);
            Assert.That(savedFile, Is.Not.Null);

            // Reload the file and verify
            var parser = runner.GetRequiredServiceInCurrentEditorScope<MetaDataFileParser>();
            var parsedFile = parser.ParseFile(savedFile);
            Assert.That(parsedFile, Is.Not.Null);

            var splashAttack = parsedFile.Attributes[4] as SplashAttack_v10;
            Assert.That(splashAttack, Is.Not.Null);
            Assert.That(splashAttack.Filter, Is.EqualTo(strValue));
            Assert.That(splashAttack.AoeShape, Is.EqualTo(intValue));
            Assert.That(splashAttack.EndPosition.X, Is.EqualTo(vectorValue));
        }

        [Test]
        public void CombatMetaData3dMove_SaveAndReloadPreservesPosition()
        {
            var packFile = PathHelper.GetDataFile("Throt.pack");

            var runner = new AssetEditorTestRunner();
            runner.CreateCaContainer();
            var outputPackFile = runner.LoadPackFile(packFile, true);

            var filePath = @"animations/battle/humanoid17/throt_whip_catcher/attacks/hu17_whip_catcher_attack_05.anm.meta";
            var metaPackFile = runner.PackFileService.FindFile(filePath);
            var editor = runner.CommandFactory
                .Create<OpenEditorCommand>()
                .Execute<MetaDataEditorViewModel>(
                    metaPackFile!,
                    Shared.Core.ToolCreation.EditorEnums.Meta_Editor);
            var splashAttack = editor.ParsedFile!.Attributes
                .OfType<SplashAttack_v10>()
                .First();
            var originalEnd = splashAttack.EndPosition;
            var delta = new Vector3(3, 2, 1);
            var eventHub = runner
                .GetRequiredServiceInCurrentEditorScope<IEventHub>();
            var session = new CombatMetaDataEditSession(eventHub);

            Assert.That(
                session.SetTarget(
                    editor,
                    splashAttack,
                    CombatMetaDataPoint.SplashEnd),
                Is.True);
            Assert.That(session.BeginGesture(), Is.True);
            Assert.That(session.Translate(delta), Is.True);
            Assert.That(session.EndGesture(), Is.True);
            Assert.That(session.HasUnsavedChanges, Is.True);

            editor.SaveActionCommand.Execute(null);
            session.MarkSaved(editor);

            var savedFile = runner.PackFileService.FindFile(
                filePath,
                outputPackFile);
            var parser = runner
                .GetRequiredServiceInCurrentEditorScope<MetaDataFileParser>();
            var reloaded = parser.ParseFile(savedFile!);
            var reloadedSplash = reloaded!.Attributes
                .OfType<SplashAttack_v10>()
                .First();

            Assert.Multiple(() =>
            {
                Assert.That(
                    reloadedSplash.EndPosition,
                    Is.EqualTo(originalEnd + delta));
                Assert.That(session.HasUnsavedChanges, Is.False);
            });
        }

        [Test]
        public void MetaDataEditor_DeleteAndSave()
        {
            var packFile = PathHelper.GetDataFile("Throt.pack");

            var runner = new AssetEditorTestRunner();
            runner.CreateCaContainer();
            var outputPackFile = runner.LoadPackFile(packFile, true);

            var filePath = @"animations/battle/humanoid17/throt_whip_catcher/attacks/hu17_whip_catcher_attack_05.anm.meta";
            var metaPackFile = runner.PackFileService.FindFile(filePath);
            var editor = runner.CommandFactory
                .Create<OpenEditorCommand>()
                .Execute<MetaDataEditorViewModel>(metaPackFile!, Shared.Core.ToolCreation.EditorEnums.Meta_Editor);

            Assert.That(editor.ParsedFile, Is.Not.Null);
            var initialCount = editor.Tags.Count;
            Assert.That(initialCount, Is.GreaterThan(0));

            // Select last tag and delete
            editor.SelectedTag = editor.Tags.Last();
            editor.DeleteActionCommand.Execute(null);

            Assert.That(editor.Tags.Count, Is.EqualTo(initialCount - 1));

            editor.SaveActionCommand.Execute(null);

            var savedFile = runner.PackFileService.FindFile(filePath, outputPackFile);
            Assert.That(savedFile, Is.Not.Null);

            // Reload the file and verify
            var parser = runner.GetRequiredServiceInCurrentEditorScope<MetaDataFileParser>();
            var parsedFile = parser.ParseFile(savedFile);
            Assert.That(parsedFile, Is.Not.Null);
            Assert.That(parsedFile.Attributes.Count, Is.EqualTo(initialCount - 1));
        }

        [Test]
        public void MetaDataEditor_MoveUpAndSave()
        {
            var packFile = PathHelper.GetDataFile("Throt.pack");

            var runner = new AssetEditorTestRunner();
            runner.CreateCaContainer();
            var outputPackFile = runner.LoadPackFile(packFile, true);

            var filePath = @"animations/battle/humanoid17/throt_whip_catcher/attacks/hu17_whip_catcher_attack_05.anm.meta";
            var metaPackFile = runner.PackFileService.FindFile(filePath);
            var editor = runner.CommandFactory
                .Create<OpenEditorCommand>()
                .Execute<MetaDataEditorViewModel>(metaPackFile!, Shared.Core.ToolCreation.EditorEnums.Meta_Editor);

            Assert.That(editor.ParsedFile, Is.Not.Null);

            // Move second entry up
            editor.SelectedTag = editor.Tags[4];
            editor.MoveUpActionCommand.Execute(null);

            editor.SaveActionCommand.Execute(null);

            var savedFile = runner.PackFileService.FindFile(filePath, outputPackFile);
            Assert.That(savedFile, Is.Not.Null);

            // Reload the file and verify
            var parser = runner.GetRequiredServiceInCurrentEditorScope<MetaDataFileParser>();
            var parsedFile = parser.ParseFile(savedFile);
            Assert.That(parsedFile, Is.Not.Null);
            Assert.That(parsedFile.Attributes[3], Is.InstanceOf<SplashAttack_v10>());
        }

        [Test]
        public void MetaDataEditor_CopyPaste_AddsNewTag()
        {
            var packFile = PathHelper.GetDataFile("Throt.pack");

            var runner = new AssetEditorTestRunner();
            runner.CreateCaContainer();
            var outputPackFile = runner.LoadPackFile(packFile, true);

            var filePath = @"animations/battle/humanoid17/throt_whip_catcher/attacks/hu17_whip_catcher_attack_05.anm.meta";
            var metaPackFile = runner.PackFileService.FindFile(filePath);
            var editor = runner.CommandFactory
                .Create<OpenEditorCommand>()
                .Execute<MetaDataEditorViewModel>(metaPackFile!, Shared.Core.ToolCreation.EditorEnums.Meta_Editor);

            Assert.That(editor.ParsedFile, Is.Not.Null);

            var initialCount = editor.Tags.Count;
            Assert.That(initialCount, Is.GreaterThan(0));

            // Select a known tag (SplashAttack at index 4) and copy
            var indexToCopy = 4;
            var originalType = editor.ParsedFile.Attributes[indexToCopy].GetType();

            editor.Tags[indexToCopy].IsSelected = true;
            editor.CopyActionCommand.Execute(null);

            // Paste
            editor.PasteActionCommand.Execute(null);

            // View should be updated
            Assert.That(editor.Tags.Count, Is.EqualTo(initialCount + 1));
            Assert.That(editor.ParsedFile.Attributes.Count, Is.EqualTo(initialCount + 1));
            Assert.That(editor.HasUnsavedChanges, Is.True);

            var pasted = editor.ParsedFile.Attributes.Last();
            Assert.That(pasted, Is.InstanceOf(originalType));
        }

        [Test]
        public void MetaDataEditor_UpdateViewAfterStructuralChange_MarksEditorDirty()
        {
            var packFile = PathHelper.GetDataFile("Throt.pack");
            var runner = new AssetEditorTestRunner();
            runner.CreateCaContainer();
            runner.LoadPackFile(packFile, true);

            var filePath = @"animations/battle/humanoid17/throt_whip_catcher/attacks/hu17_whip_catcher_attack_05.anm.meta";
            var metaPackFile = runner.PackFileService.FindFile(filePath);
            var editor = runner.CommandFactory
                .Create<OpenEditorCommand>()
                .Execute<MetaDataEditorViewModel>(metaPackFile!, Shared.Core.ToolCreation.EditorEnums.Meta_Editor);

            editor.ParsedFile!.Attributes.Add(new Time
            {
                Name = "TIME",
                Version = 10
            });
            editor.UpdateView();

            Assert.That(editor.HasUnsavedChanges, Is.True);
        }

        [Test]
        public void MetaDataEditor_EmptyPaste_PreservesExistingEdits()
        {
            var packFile = PathHelper.GetDataFile("Throt.pack");
            var runner = new AssetEditorTestRunner();
            runner.CreateCaContainer();
            runner.LoadPackFile(packFile, true);

            var filePath = @"animations/battle/humanoid17/throt_whip_catcher/attacks/hu17_whip_catcher_attack_05.anm.meta";
            var metaPackFile = runner.PackFileService.FindFile(filePath);
            var editor = runner.CommandFactory
                .Create<OpenEditorCommand>()
                .Execute<MetaDataEditorViewModel>(
                    metaPackFile!,
                    Shared.Core.ToolCreation.EditorEnums.Meta_Editor);
            var originalTag = editor.Tags[4];
            var editedVariable = originalTag.Variables[3];
            editedVariable.ValueAsString = "CODEX_VALID_FILTER";
            Assert.That(editor.HasUnsavedChanges, Is.True);
            runner.GetRequiredServiceInCurrentEditorScope<CopyPasteManager>()
                .Clear();

            editor.PasteActionCommand.Execute(null);

            Assert.Multiple(() =>
            {
                Assert.That(editor.Tags, Does.Contain(originalTag));
                Assert.That(
                    editedVariable.ValueAsString,
                    Is.EqualTo("CODEX_VALID_FILTER"));
                Assert.That(editor.HasUnsavedChanges, Is.True);
            });
        }

        [Test]
        public void SuperView_ChildStructuralChange_NotifiesHostDirtyState()
        {
            var runner = new AssetEditorTestRunner();
            var editorCreator =
                runner.ServiceProvider.GetRequiredService<IEditorCreator>();
            var editor = (SuperViewViewModel)editorCreator.Create(
                EditorEnums.SuperView_Editor);
            var changedProperties = new List<string?>();
            editor.PropertyChanged += (_, args) =>
                changedProperties.Add(args.PropertyName);

            try
            {
                editor.MetaEditor.HasUnsavedChanges = true;

                Assert.That(
                    changedProperties,
                    Does.Contain(nameof(SuperViewViewModel.HasUnsavedChanges)));
            }
            finally
            {
                editor.Close();
            }
        }

        [Test]
        public void SuperView_SaveWithMissingMetadataFiles_DoesNotFail()
        {
            var runner = new AssetEditorTestRunner();
            var editorCreator =
                runner.ServiceProvider.GetRequiredService<IEditorCreator>();
            var editor = (SuperViewViewModel)editorCreator.Create(
                EditorEnums.SuperView_Editor);

            try
            {
                Assert.That(editor.Save(), Is.True);
            }
            finally
            {
                editor.Close();
            }
        }

        [Test]
        public void MetaDataEditor_CreateNewFile_CreatesEmptyVersionTwoMetadata()
        {
            const string path = @"animations\battle\codex\new_preview.anm.meta";
            var runner = new AssetEditorTestRunner();
            runner.CreateOutputPack();
            var editorCreator =
                runner.ServiceProvider.GetRequiredService<IEditorCreator>();
            var superView = (SuperViewViewModel)editorCreator.Create(
                EditorEnums.SuperView_Editor);

            try
            {
                var created = superView.MetaEditor.CreateNewFile(path);
                var saved = superView.Save();

                Assert.Multiple(() =>
                {
                    Assert.That(created, Is.True);
                    Assert.That(saved, Is.True);
                    Assert.That(superView.MetaEditor.ParsedFile, Is.Not.Null);
                    Assert.That(superView.MetaEditor.ParsedFile!.Version, Is.EqualTo(2));
                    Assert.That(superView.MetaEditor.Tags, Is.Empty);
                    Assert.That(runner.PackFileService.FindFile(path), Is.Not.Null);
                });
            }
            finally
            {
                superView.Close();
            }
        }

        [Test]
        public void SuperView_MissingReferencedMetadata_CreatesBothFilesAtReferencedPaths()
        {
            const string animationPath =
                @"animations\battle\codex\missing_animation.anm.meta";
            const string persistentPath =
                @"animations\battle\codex\missing_persistent.meta";
            var runner = new AssetEditorTestRunner();
            runner.CreateOutputPack();
            var editorCreator =
                runner.ServiceProvider.GetRequiredService<IEditorCreator>();
            var superView = (SuperViewViewModel)editorCreator.Create(
                EditorEnums.SuperView_Editor);

            try
            {
                var selection = superView.SceneObjects.Single()
                    .FragAndSlotSelection;
                selection.MetaDataName = animationPath;
                selection.MetaDataPersistName = persistentPath;

                Assert.Multiple(() =>
                {
                    Assert.That(superView.CanCreateAnimationMetaFile, Is.True);
                    Assert.That(superView.CanCreatePersistentMetaFile, Is.True);
                });

                superView.CreateAnimationMetaFile();
                superView.CreatePersistentMetaFile();

                Assert.Multiple(() =>
                {
                    Assert.That(
                        runner.PackFileService.FindFile(animationPath),
                        Is.Not.Null);
                    Assert.That(
                        runner.PackFileService.FindFile(persistentPath),
                        Is.Not.Null);
                    Assert.That(superView.HasAnimationMetaFile, Is.True);
                    Assert.That(superView.HasPersistentMetaFile, Is.True);
                });
            }
            finally
            {
                superView.Close();
            }
        }

        [Test]
        public void SuperView_LoadedAnimationMetadata_KeepsExistingTagsVisible()
        {
            const string metaPath =
                @"animations\battle\humanoid17\throt_whip_catcher\attacks\hu17_whip_catcher_attack_05.anm.meta";
            var runner = new AssetEditorTestRunner();
            runner.CreateCaContainer();
            runner.LoadPackFile(PathHelper.GetDataFile("Throt.pack"), true);
            var metaFile = runner.PackFileService.FindFile(metaPath);
            var editorCreator =
                runner.ServiceProvider.GetRequiredService<IEditorCreator>();
            var superView = (SuperViewViewModel)editorCreator.Create(
                EditorEnums.SuperView_Editor);
            var sceneObjectEditor =
                runner.GetRequiredServiceInCurrentEditorScope<SceneObjectEditor>();

            try
            {
                var sceneObject = superView.SceneObjects.Single();
                sceneObjectEditor.SetMetaFile(sceneObject.Data, metaFile, null);
                sceneObject.FragAndSlotSelection.MetaDataName = metaPath;
                sceneObject.FragAndSlotSelection.MetaDataPersistName = null;
                superView.SelectedTabControllerIndex = 1;

                Assert.Multiple(() =>
                {
                    Assert.That(metaFile, Is.Not.Null);
                    Assert.That(superView.MetaEditor.Tags, Has.Count.EqualTo(7));
                    Assert.That(superView.HasAnimationMetaFile, Is.True);
                    Assert.That(superView.CanCreateAnimationMetaFile, Is.False);
                    Assert.That(
                        superView.IsAnimationMetaReferenceMissing,
                        Is.False);
                });
            }
            finally
            {
                superView.Close();
            }
        }

        [Test]
        public void SuperView_SelectedPreviewAttribute_FollowsActiveMetadataTab()
        {
            var runner = new AssetEditorTestRunner();
            var editorCreator =
                runner.ServiceProvider.GetRequiredService<IEditorCreator>();
            var superView = (SuperViewViewModel)editorCreator.Create(
                EditorEnums.SuperView_Editor);
            var eventHub =
                runner.GetRequiredServiceInCurrentEditorScope<IEventHub>();
            var persistentAttribute = new ImpactPosition_v10
            {
                Name = "IMPACT_POS",
                Version = 10,
            };
            var animationAttribute = new TargetPos_10
            {
                Name = "TARGET_POS",
                Version = 10,
            };

            try
            {
                var persistentTag = new MetaDataEntry(
                    persistentAttribute,
                    "",
                    eventHub,
                    true);
                var animationTag = new MetaDataEntry(
                    animationAttribute,
                    "",
                    eventHub,
                    true);
                superView.PersistentMetaEditor.Tags.Add(persistentTag);
                superView.MetaEditor.Tags.Add(animationTag);
                superView.PersistentMetaEditor.SelectedTag = persistentTag;
                superView.MetaEditor.SelectedTag = animationTag;

                superView.SelectedTabControllerIndex = 0;
                Assert.That(
                    superView.SelectedPreviewAttribute,
                    Is.SameAs(persistentAttribute));

                superView.SelectedTabControllerIndex = 1;
                Assert.That(
                    superView.SelectedPreviewAttribute,
                    Is.SameAs(animationAttribute));
            }
            finally
            {
                superView.Close();
            }
        }

        [Test]
        public void SuperView_SelectedTimedTag_ExposesRangeAndJumpsToBothBoundaries()
        {
            var runner = new AssetEditorTestRunner();
            var editorCreator =
                runner.ServiceProvider.GetRequiredService<IEditorCreator>();
            var superView = (SuperViewViewModel)editorCreator.Create(
                EditorEnums.SuperView_Editor);
            var eventHub =
                runner.GetRequiredServiceInCurrentEditorScope<IEventHub>();
            var timedAttribute = new FirePos_v10
            {
                Name = "FIRE_POS",
                Version = 10,
                StartTime = 1.25f,
                EndTime = 2.5f,
            };
            var effectAttribute = new Effect_v11
            {
                Name = "EFFECT",
                Version = 11,
                StartTime = 3,
                EndTime = 4,
            };

            try
            {
                var timedTag = new MetaDataEntry(
                    timedAttribute,
                    "",
                    eventHub,
                    true);
                var effectTag = new MetaDataEntry(
                    effectAttribute,
                    "",
                    eventHub,
                    true);
                superView.MetaEditor.Tags.Add(timedTag);
                superView.MetaEditor.Tags.Add(effectTag);
                superView.SelectedTabControllerIndex = 1;
                superView.MetaEditor.SelectedTag = timedTag;

                Assert.Multiple(() =>
                {
                    Assert.That(
                        superView.HasSelectedMetaDataTimeRange,
                        Is.True);
                    Assert.That(
                        superView.SelectedMetaDataStartTimeSeconds,
                        Is.EqualTo(1.25f));
                    Assert.That(
                        superView.SelectedMetaDataEndTimeSeconds,
                        Is.EqualTo(2.5f));
                    Assert.That(
                        superView.SelectedMetaDataIsActive,
                        Is.False);
                    Assert.That(
                        superView.IsEffectMetaDataSelected,
                        Is.False);
                });

                superView.JumpToSelectedMetaDataStartAction();
                Assert.Multiple(() =>
                {
                    Assert.That(
                        superView.SceneObjects.Single().Data.Player.CurrentTime,
                        Is.EqualTo(TimeSpan.FromMilliseconds(1250)));
                    Assert.That(
                        superView.SelectedMetaDataIsActive,
                        Is.True);
                });

                superView.JumpToSelectedMetaDataEndAction();
                Assert.That(
                    superView.SceneObjects.Single().Data.Player.CurrentTime,
                    Is.EqualTo(TimeSpan.FromMilliseconds(2500)));

                superView.MetaEditor.SelectedTag = effectTag;
                Assert.That(superView.IsEffectMetaDataSelected, Is.True);
            }
            finally
            {
                superView.Close();
            }
        }

        [Test]
        public void SuperView_VectorAndOrientationEdits_NotifyAfterModification()
        {
            var runner = new AssetEditorTestRunner();
            var editorCreator =
                runner.ServiceProvider.GetRequiredService<IEditorCreator>();
            var editor = (SuperViewViewModel)editorCreator.Create(
                EditorEnums.SuperView_Editor);

            try
            {
                var eventHub =
                    runner.GetRequiredServiceInCurrentEditorScope<IEventHub>();
                var metadataEntry = new MetaDataEntry(
                    new AnimatedProp_v14
                    {
                        Name = "ANIMATED_PROP",
                        Version = 14
                    },
                    "",
                    eventHub,
                    true);
                editor.MetaEditor.Tags.Add(metadataEntry);
                editor.MetaEditor.HasUnsavedChanges = false;
                var vectorVariable = metadataEntry.Variables
                    .OfType<VectorAttributeViewModel>()
                    .First();
                var orientationVariable = metadataEntry.Variables
                    .OfType<OrientationAttributeViewModel>()
                    .First();
                Assert.That(editor.HasUnsavedChanges, Is.False);
                bool? dirtyStateAtNotification = null;
                editor.PropertyChanged += (_, args) =>
                {
                    if (args.PropertyName ==
                        nameof(SuperViewViewModel.HasUnsavedChanges))
                    {
                        dirtyStateAtNotification =
                            editor.HasUnsavedChanges;
                    }
                };

                vectorVariable.Value.X.TextValue = "1";

                Assert.Multiple(() =>
                {
                    Assert.That(dirtyStateAtNotification, Is.True);
                    Assert.That(editor.HasUnsavedChanges, Is.True);
                });

                editor.MetaEditor.HasUnsavedChanges = false;
                dirtyStateAtNotification = null;
                orientationVariable.Value.X.TextValue = "1";

                Assert.Multiple(() =>
                {
                    Assert.That(dirtyStateAtNotification, Is.True);
                    Assert.That(editor.HasUnsavedChanges, Is.True);
                });
            }
            finally
            {
                editor.Close();
            }
        }

        [Test]
        public void SuperView_GizmoPreview_RefreshesSplashMarkerImmediately()
        {
            var runner = new AssetEditorTestRunner();
            var editorCreator =
                runner.ServiceProvider.GetRequiredService<IEditorCreator>();
            var superView = (SuperViewViewModel)editorCreator.Create(
                EditorEnums.SuperView_Editor);
            var session = runner
                .GetRequiredServiceInCurrentEditorScope<
                    CombatMetaDataEditSession>();
            var source = new SplashAttack_v10
            {
                Name = "SPLASH_ATTACK",
                Version = 10,
                StartPosition = new Vector3(1, 2, 3),
                EndPosition = new Vector3(1, 2, 7),
            };
            var refreshCount = 0;
            var node = new GameWorld.Core.SceneNodes.SimpleDrawableNode(
                "splash");
            var sceneObject = superView.SceneObjects.Single();
            var preview = new CombatMetaDataInstance(
                source,
                CombatMetaDataPreviewCategory.Splash,
                () => (source.StartPosition + source.EndPosition) / 2,
                node,
                true,
                _ => { },
                sceneObject.Data.Player,
                refreshVisual: () => refreshCount++);
            sceneObject.Data.MetaDataItems.Add(preview);

            try
            {
                Assert.That(
                    session.SetTarget(
                        superView.MetaEditor,
                        source,
                        CombatMetaDataPoint.SplashStart),
                    Is.True);
                Assert.That(session.BeginGesture(), Is.True);
                Assert.That(
                    session.Translate(new Vector3(2, 0, 0)),
                    Is.True);

                Assert.That(refreshCount, Is.EqualTo(1));
            }
            finally
            {
                sceneObject.Data.MetaDataItems.Remove(preview);
                superView.Close();
            }
        }

        [Test]
        public void SuperView_CombatPreviewControls_ToggleCategoryDisplayModeAndFocusSelectedTag()
        {
            const string metaPath =
                @"animations\battle\codex\combat_preview_controls.anm.meta";
            var impactPosition = new Vector3(1, 2, 3);
            var targetPosition = new Vector3(4, 5, 6);
            var runner = new AssetEditorTestRunner();
            runner.CreateOutputPack();
            var editorCreator =
                runner.ServiceProvider.GetRequiredService<IEditorCreator>();
            var superView = (SuperViewViewModel)editorCreator.Create(
                EditorEnums.SuperView_Editor);

            try
            {
                var parser = runner.GetRequiredServiceInCurrentEditorScope<
                    MetaDataFileParser>();
                var fileSaveService =
                    runner.GetRequiredServiceInCurrentEditorScope<
                        IFileSaveService>();
                var sceneObjectEditor =
                    runner.GetRequiredServiceInCurrentEditorScope<
                        SceneObjectEditor>();
                var camera = runner.GetRequiredServiceInCurrentEditorScope<
                    ArcBallCamera>();
                var metadata = new ParsedMetadataFile
                {
                    Version = 2,
                    Attributes =
                    [
                        new ImpactPosition_v10
                        {
                            Name = "IMPACT_POS",
                            Version = 10,
                            Position = impactPosition,
                        },
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
                            Position = new Vector3(7, 8, 9),
                        },
                        new SplashAttack_v10
                        {
                            Name = "SPLASH_ATTACK",
                            Version = 10,
                            StartPosition = new Vector3(10, 11, 12),
                            EndPosition = new Vector3(13, 14, 15),
                        },
                    ]
                };
                var bytes = parser.GenerateBytes(metadata.Version, metadata);
                var metaFile = fileSaveService.Save(metaPath, bytes, false);
                var sceneObject = superView.SceneObjects.Single();
                sceneObject.FragAndSlotSelection.MetaDataName = metaPath;
                sceneObjectEditor.SetMetaFile(sceneObject.Data, metaFile, null);
                superView.SelectedTabControllerIndex = 1;
                superView.MetaEditor.SelectedTag = superView.MetaEditor.Tags
                    .Single(tag => tag._input.Name == "IMPACT_POS");

                superView.ShowImpactPositions = false;
                camera.LookAt = Vector3.Zero;
                superView.FocusSelectedMetaDataAction();

                var previews = sceneObject.Data.MetaDataItems
                    .OfType<ICombatMetaDataPreview>()
                    .ToList();
                Assert.Multiple(() =>
                {
                    Assert.That(
                        superView.ShowCombatMetaDataDuringActiveTime,
                        Is.True);
                    Assert.That(
                        superView.ShowCombatMetaDataForEntireAnimation,
                        Is.False);
                    Assert.That(
                        previews,
                        Has.All.Property("ShowForEntireAnimation").False);
                    Assert.That(
                        superView.IsCombatMetaData3dEditingEnabled,
                        Is.False);
                    Assert.That(superView.IsImpactMetaDataSelected, Is.True);
                    Assert.That(superView.IsTargetMetaDataSelected, Is.False);
                    Assert.That(superView.IsFireMetaDataSelected, Is.False);
                    Assert.That(superView.IsSplashMetaDataSelected, Is.False);
                    Assert.That(
                        superView.CanEditSelectedCombatMetaData,
                        Is.True);
                    Assert.That(
                        superView.HasSelectedSceneMarkerSettings,
                        Is.True);
                });

                superView.ShowCombatMetaDataForEntireAnimation = true;
                Assert.Multiple(() =>
                {
                    Assert.That(
                        superView.ShowCombatMetaDataDuringActiveTime,
                        Is.False);
                    Assert.That(
                        previews.Single(preview =>
                            preview.Category ==
                            CombatMetaDataPreviewCategory.Impact)
                            .ShowForEntireAnimation,
                        Is.True);
                    Assert.That(
                        previews.Where(preview =>
                            preview.Category !=
                            CombatMetaDataPreviewCategory.Impact),
                        Has.All.Property("ShowForEntireAnimation").False);
                    Assert.That(
                        previews.Single(preview =>
                            preview.Category ==
                            CombatMetaDataPreviewCategory.Impact).IsEnabled,
                        Is.False);
                    Assert.That(
                        previews.Single(preview =>
                            preview.Category ==
                            CombatMetaDataPreviewCategory.Target).IsEnabled,
                        Is.True);
                    Assert.That(camera.LookAt, Is.EqualTo(impactPosition));
                });

                superView.IsCombatMetaData3dEditingEnabled = true;
                superView.MetaEditor.SelectedTag = superView.MetaEditor.Tags
                    .Single(tag => tag._input.Name == "TARGET_POS");
                Assert.Multiple(() =>
                {
                    Assert.That(
                        superView.IsCombatMetaData3dEditingEnabled,
                        Is.False);
                    Assert.That(superView.IsImpactMetaDataSelected, Is.False);
                    Assert.That(superView.IsTargetMetaDataSelected, Is.True);
                    Assert.That(superView.IsFireMetaDataSelected, Is.False);
                    Assert.That(superView.IsSplashMetaDataSelected, Is.False);
                    Assert.That(
                        superView.CanEditSelectedCombatMetaData,
                        Is.True);
                    Assert.That(
                        superView.ShowCombatMetaDataForEntireAnimation,
                        Is.False);
                });
                superView.IsCombatMetaData3dEditingEnabled = true;

                superView.MetaEditor.SelectedTag = superView.MetaEditor.Tags
                    .Single(tag => tag._input.Name == "FIRE_POS");
                Assert.Multiple(() =>
                {
                    Assert.That(
                        superView.IsCombatMetaData3dEditingEnabled,
                        Is.False);
                    Assert.That(superView.IsImpactMetaDataSelected, Is.False);
                    Assert.That(superView.IsTargetMetaDataSelected, Is.False);
                    Assert.That(superView.IsFireMetaDataSelected, Is.True);
                    Assert.That(superView.IsSplashMetaDataSelected, Is.False);
                    Assert.That(
                        superView.CanEditSelectedCombatMetaData,
                        Is.True);
                });

                superView.MetaEditor.SelectedTag = superView.MetaEditor.Tags
                    .Single(tag => tag._input.Name == "SPLASH_ATTACK");
                Assert.Multiple(() =>
                {
                    Assert.That(
                        superView.IsCombatMetaData3dEditingEnabled,
                        Is.False);
                    Assert.That(superView.IsImpactMetaDataSelected, Is.False);
                    Assert.That(superView.IsTargetMetaDataSelected, Is.False);
                    Assert.That(superView.IsFireMetaDataSelected, Is.False);
                    Assert.That(superView.IsSplashMetaDataSelected, Is.True);
                    Assert.That(superView.EditSplashStart, Is.True);
                    Assert.That(superView.EditSplashEnd, Is.False);
                    Assert.That(
                        superView.CanEditSelectedCombatMetaData,
                        Is.True);
                });

                Assert.Multiple(() =>
                {
                    Assert.That(
                        superView.CanToggleAnimationMetaPreviewVisibility,
                        Is.True);
                    Assert.That(
                        superView.CanToggleAnimationMetaDisplayTime,
                        Is.True);
                    Assert.That(
                        superView.CanToggleAnimationMeta3D,
                        Is.True);
                });

                superView.EnableAllAnimationMeta3D = true;
                foreach (var tag in superView.MetaEditor.Tags)
                {
                    superView.MetaEditor.SelectedTag = tag;
                    Assert.That(
                        superView.IsCombatMetaData3dEditingEnabled,
                        Is.True);
                }

                superView.EnableAllAnimationMeta3D = false;
                foreach (var tag in superView.MetaEditor.Tags)
                {
                    superView.MetaEditor.SelectedTag = tag;
                    Assert.That(
                        superView.IsCombatMetaData3dEditingEnabled,
                        Is.False);
                }

                superView.ShowAllAnimationMetaPreviews = false;
                Assert.That(previews, Has.All.Property("IsEnabled").False);
                superView.ShowAllAnimationMetaPreviews = true;
                Assert.That(previews, Has.All.Property("IsEnabled").True);

                superView.ShowAllAnimationMetaForEntireAnimation = true;
                Assert.That(
                    previews,
                    Has.All.Property("ShowForEntireAnimation").True);
                superView.ShowAllAnimationMetaForEntireAnimation = false;
                Assert.That(
                    previews,
                    Has.All.Property("ShowForEntireAnimation").False);
            }
            finally
            {
                superView.Close();
            }
        }

        [Test]
        public void SuperView_AnimationMetaBatchControls_DoNotChangePersistentMetaPreviews()
        {
            const string animationPath =
                @"animations\battle\codex\batch_animation.anm.meta";
            const string persistentPath =
                @"animations\battle\codex\batch_persistent.anm.meta";
            var runner = new AssetEditorTestRunner();
            runner.CreateOutputPack();
            var editorCreator =
                runner.ServiceProvider.GetRequiredService<IEditorCreator>();
            var superView = (SuperViewViewModel)editorCreator.Create(
                EditorEnums.SuperView_Editor);

            try
            {
                var parser = runner.GetRequiredServiceInCurrentEditorScope<
                    MetaDataFileParser>();
                var fileSaveService =
                    runner.GetRequiredServiceInCurrentEditorScope<
                        IFileSaveService>();
                var sceneObjectEditor =
                    runner.GetRequiredServiceInCurrentEditorScope<
                        SceneObjectEditor>();
                var animationMetadata = new ParsedMetadataFile
                {
                    Version = 2,
                    Attributes =
                    [
                        new ImpactPosition_v10
                        {
                            Name = "IMPACT_POS",
                            Version = 10,
                            Position = new Vector3(1, 2, 3),
                        },
                    ]
                };
                var persistentMetadata = new ParsedMetadataFile
                {
                    Version = 2,
                    Attributes =
                    [
                        new TargetPos_10
                        {
                            Name = "TARGET_POS",
                            Version = 10,
                            Position = new Vector3(4, 5, 6),
                        },
                    ]
                };
                var animationFile = fileSaveService.Save(
                    animationPath,
                    parser.GenerateBytes(
                        animationMetadata.Version,
                        animationMetadata),
                    false);
                var persistentFile = fileSaveService.Save(
                    persistentPath,
                    parser.GenerateBytes(
                        persistentMetadata.Version,
                        persistentMetadata),
                    false);
                var sceneObject = superView.SceneObjects.Single();
                sceneObject.FragAndSlotSelection.MetaDataName = animationPath;
                sceneObject.FragAndSlotSelection.MetaDataPersistName =
                    persistentPath;
                sceneObjectEditor.SetMetaFile(
                    sceneObject.Data,
                    animationFile,
                    persistentFile);

                var animationSource = superView.MetaEditor.Tags.Single()._input;
                var persistentSource =
                    superView.PersistentMetaEditor.Tags.Single()._input;
                var animationPreview = sceneObject.Data.MetaDataItems
                    .OfType<ICombatMetaDataPreview>()
                    .Single(preview =>
                        ReferenceEquals(preview.Source, animationSource));
                var persistentPreview = sceneObject.Data.MetaDataItems
                    .OfType<ICombatMetaDataPreview>()
                    .Single(preview =>
                        ReferenceEquals(preview.Source, persistentSource));

                superView.ShowAllAnimationMetaPreviews = false;
                superView.ShowAllAnimationMetaForEntireAnimation = true;
                superView.SelectedTabControllerIndex = 0;
                superView.PersistentMetaEditor.SelectedTag =
                    superView.PersistentMetaEditor.Tags.Single();
                superView.IsCombatMetaData3dEditingEnabled = true;
                superView.SelectedTabControllerIndex = 1;
                superView.EnableAllAnimationMeta3D = true;
                superView.EnableAllAnimationMeta3D = false;
                superView.SelectedTabControllerIndex = 0;

                Assert.Multiple(() =>
                {
                    Assert.That(animationPreview.IsEnabled, Is.False);
                    Assert.That(persistentPreview.IsEnabled, Is.True);
                    Assert.That(
                        animationPreview.ShowForEntireAnimation,
                        Is.True);
                    Assert.That(
                        persistentPreview.ShowForEntireAnimation,
                        Is.False);
                    Assert.That(
                        superView.IsCombatMetaData3dEditingEnabled,
                        Is.True);
                });
            }
            finally
            {
                superView.Close();
            }
        }

        [Test]
        public void SuperView_SelectingCombatTag_UpdatesHighlightWithoutRecreatingPreviews()
        {
            const string metaPath =
                @"animations\battle\codex\combat_preview_selection.anm.meta";
            var runner = new AssetEditorTestRunner();
            runner.CreateOutputPack();
            var editorCreator =
                runner.ServiceProvider.GetRequiredService<IEditorCreator>();
            var superView = (SuperViewViewModel)editorCreator.Create(
                EditorEnums.SuperView_Editor);

            try
            {
                var parser = runner.GetRequiredServiceInCurrentEditorScope<
                    MetaDataFileParser>();
                var fileSaveService =
                    runner.GetRequiredServiceInCurrentEditorScope<
                        IFileSaveService>();
                var sceneObjectEditor =
                    runner.GetRequiredServiceInCurrentEditorScope<
                        SceneObjectEditor>();
                var metadata = new ParsedMetadataFile
                {
                    Version = 2,
                    Attributes =
                    [
                        new ImpactPosition_v10
                        {
                            Name = "IMPACT_POS",
                            Version = 10,
                            Position = new Vector3(1, 2, 3),
                        },
                        new TargetPos_10
                        {
                            Name = "TARGET_POS",
                            Version = 10,
                            Position = new Vector3(4, 5, 6),
                        },
                    ]
                };
                var bytes = parser.GenerateBytes(metadata.Version, metadata);
                var metaFile = fileSaveService.Save(metaPath, bytes, false);
                var sceneObject = superView.SceneObjects.Single();
                sceneObject.FragAndSlotSelection.MetaDataName = metaPath;
                sceneObjectEditor.SetMetaFile(sceneObject.Data, metaFile, null);
                superView.SelectedTabControllerIndex = 1;

                superView.MetaEditor.SelectedTag = superView.MetaEditor.Tags
                    .Single(tag => tag._input.Name == "IMPACT_POS");
                var impactPreview = sceneObject.Data.MetaDataItems
                    .OfType<ICombatMetaDataPreview>()
                    .Single(preview => preview.Category ==
                        CombatMetaDataPreviewCategory.Impact);
                var targetPreview = sceneObject.Data.MetaDataItems
                    .OfType<ICombatMetaDataPreview>()
                    .Single(preview => preview.Category ==
                        CombatMetaDataPreviewCategory.Target);

                superView.MetaEditor.SelectedTag = superView.MetaEditor.Tags
                    .Single(tag => tag._input.Name == "TARGET_POS");

                Assert.Multiple(() =>
                {
                    Assert.That(sceneObject.Data.MetaDataItems
                        .OfType<ICombatMetaDataPreview>()
                        .Single(preview => preview.Category ==
                            CombatMetaDataPreviewCategory.Impact),
                        Is.SameAs(impactPreview));
                    Assert.That(sceneObject.Data.MetaDataItems
                        .OfType<ICombatMetaDataPreview>()
                        .Single(preview => preview.Category ==
                            CombatMetaDataPreviewCategory.Target),
                        Is.SameAs(targetPreview));
                    Assert.That(impactPreview.IsSelected, Is.False);
                    Assert.That(targetPreview.IsSelected, Is.True);
                });
            }
            finally
            {
                superView.Close();
            }
        }

        [Test]
        public void SuperView_EditingCombatTag_RecreatesOnlyChangedPreview()
        {
            const string metaPath =
                @"animations\battle\codex\combat_preview_refresh.anm.meta";
            var runner = new AssetEditorTestRunner();
            runner.CreateOutputPack();
            var editorCreator =
                runner.ServiceProvider.GetRequiredService<IEditorCreator>();
            var superView = (SuperViewViewModel)editorCreator.Create(
                EditorEnums.SuperView_Editor);

            try
            {
                var parser = runner.GetRequiredServiceInCurrentEditorScope<
                    MetaDataFileParser>();
                var fileSaveService =
                    runner.GetRequiredServiceInCurrentEditorScope<
                        IFileSaveService>();
                var sceneObjectEditor =
                    runner.GetRequiredServiceInCurrentEditorScope<
                        SceneObjectEditor>();
                var metadata = new ParsedMetadataFile
                {
                    Version = 2,
                    Attributes =
                    [
                        new ImpactPosition_v10
                        {
                            Name = "IMPACT_POS",
                            Version = 10,
                            Position = new Vector3(1, 2, 3),
                        },
                        new TargetPos_10
                        {
                            Name = "TARGET_POS",
                            Version = 10,
                            Position = new Vector3(4, 5, 6),
                        },
                    ]
                };
                var bytes = parser.GenerateBytes(metadata.Version, metadata);
                var metaFile = fileSaveService.Save(metaPath, bytes, false);
                var sceneObject = superView.SceneObjects.Single();
                sceneObject.FragAndSlotSelection.MetaDataName = metaPath;
                sceneObjectEditor.SetMetaFile(sceneObject.Data, metaFile, null);
                superView.SelectedTabControllerIndex = 1;
                var impactTag = superView.MetaEditor.Tags
                    .Single(tag => tag._input.Name == "IMPACT_POS");
                superView.MetaEditor.SelectedTag = impactTag;
                var impactPreview = sceneObject.Data.MetaDataItems
                    .OfType<ICombatMetaDataPreview>()
                    .Single(preview => preview.Category ==
                        CombatMetaDataPreviewCategory.Impact);
                var targetPreview = sceneObject.Data.MetaDataItems
                    .OfType<ICombatMetaDataPreview>()
                    .Single(preview => preview.Category ==
                        CombatMetaDataPreviewCategory.Target);

                var position = impactTag.Variables
                    .OfType<VectorAttributeViewModel>()
                    .Single();
                position.Value.X.TextValue = "9";

                var refreshedImpactPreview = sceneObject.Data.MetaDataItems
                    .OfType<ICombatMetaDataPreview>()
                    .Single(preview => preview.Category ==
                        CombatMetaDataPreviewCategory.Impact);
                Assert.Multiple(() =>
                {
                    Assert.That(refreshedImpactPreview,
                        Is.Not.SameAs(impactPreview));
                    Assert.That(refreshedImpactPreview.FocusPosition,
                        Is.EqualTo(new Vector3(9, 2, 3)));
                    Assert.That(refreshedImpactPreview.IsSelected, Is.True);
                    Assert.That(sceneObject.Data.MetaDataItems
                        .OfType<ICombatMetaDataPreview>()
                        .Single(preview => preview.Category ==
                            CombatMetaDataPreviewCategory.Target),
                        Is.SameAs(targetPreview));
                });
            }
            finally
            {
                superView.Close();
            }
        }

        [Test]
        public void SuperView_FullRebuild_ReplacesInstancesRulesAndDiagnostics()
        {
            const string firstPath =
                @"animations\battle\codex\preview_lifecycle_first.anm.meta";
            const string secondPath =
                @"animations\battle\codex\preview_lifecycle_second.anm.meta";
            var runner = new AssetEditorTestRunner();
            runner.CreateOutputPack();
            var editorCreator =
                runner.ServiceProvider.GetRequiredService<IEditorCreator>();
            var superView = (SuperViewViewModel)editorCreator.Create(
                EditorEnums.SuperView_Editor);

            try
            {
                var parser = runner.GetRequiredServiceInCurrentEditorScope<
                    MetaDataFileParser>();
                var fileSaveService =
                    runner.GetRequiredServiceInCurrentEditorScope<
                        IFileSaveService>();
                var sceneObjectEditor =
                    runner.GetRequiredServiceInCurrentEditorScope<
                        SceneObjectEditor>();
                var first = new ParsedMetadataFile
                {
                    Version = 2,
                    Attributes =
                    [
                        new Effect_v2
                        {
                            Name = "EFFECT",
                            Version = 2,
                            VfxName = "codex_missing_lifecycle_effect",
                            Position = new Vector3(1, 2, 3),
                            Orientation = new Vector4(0, 0, 0, 1),
                            NodeIndex = -1,
                        },
                        new Transform_v10
                        {
                            Name = "TRANSFORM",
                            Version = 10,
                        },
                    ],
                };
                var second = new ParsedMetadataFile
                {
                    Version = 2,
                    Attributes =
                    [
                        new TargetPos_10
                        {
                            Name = "TARGET_POS",
                            Version = 10,
                            Position = new Vector3(7, 8, 9),
                        },
                    ],
                };
                var firstFile = fileSaveService.Save(
                    firstPath,
                    parser.GenerateBytes(first.Version, first),
                    false);
                var secondFile = fileSaveService.Save(
                    secondPath,
                    parser.GenerateBytes(second.Version, second),
                    false);
                var sceneObject = superView.SceneObjects.Single();

                sceneObjectEditor.SetMetaFile(
                    sceneObject.Data,
                    firstFile,
                    null);
                var oldInstances = sceneObject.Data.MetaDataItems.ToList();
                Assert.Multiple(() =>
                {
                    Assert.That(oldInstances, Has.Count.EqualTo(1));
                    Assert.That(
                        sceneObject.Data.Player.AnimationRules,
                        Has.Count.EqualTo(1));
                    Assert.That(
                        superView.MetaDataDiagnostics,
                        Has.Count.EqualTo(2));
                });

                sceneObjectEditor.SetMetaFile(
                    sceneObject.Data,
                    secondFile,
                    null);

                Assert.Multiple(() =>
                {
                    Assert.That(
                        sceneObject.Data.MetaDataItems,
                        Has.Count.EqualTo(1));
                    Assert.That(
                        sceneObject.Data.MetaDataItems,
                        Has.None.Matches<object>(item =>
                            oldInstances.Contains(item)));
                    Assert.That(
                        sceneObject.Data.Player.AnimationRules,
                        Is.Empty);
                    Assert.That(superView.MetaDataDiagnostics, Is.Empty);
                    Assert.That(
                        sceneObject.Data.MainNode.Children.Select(node =>
                            node.Name),
                        Has.None.EqualTo(
                            "Effect:codex_missing_lifecycle_effect"));
                    Assert.That(
                        sceneObject.Data.MainNode.Children.Select(node =>
                            node.Name),
                        Has.None.EqualTo("TRANSFORM"));
                });
            }
            finally
            {
                superView.Close();
            }
        }

        [Test]
        public void SuperView_SelectingAndEditingEffect_LeavesOtherPreviewsIntact()
        {
            const string metaPath =
                @"animations\battle\codex\effect_preview_selection.anm.meta";
            var runner = new AssetEditorTestRunner();
            runner.CreateOutputPack();
            var editorCreator =
                runner.ServiceProvider.GetRequiredService<IEditorCreator>();
            var superView = (SuperViewViewModel)editorCreator.Create(
                EditorEnums.SuperView_Editor);

            try
            {
                var parser = runner.GetRequiredServiceInCurrentEditorScope<
                    MetaDataFileParser>();
                var fileSaveService =
                    runner.GetRequiredServiceInCurrentEditorScope<
                        IFileSaveService>();
                var sceneObjectEditor =
                    runner.GetRequiredServiceInCurrentEditorScope<
                        SceneObjectEditor>();
                var metadata = new ParsedMetadataFile
                {
                    Version = 2,
                    Attributes =
                    [
                        new ImpactPosition_v10
                        {
                            Name = "IMPACT_POS",
                            Version = 10,
                            Position = new Vector3(1, 2, 3),
                        },
                        new Effect_v11
                        {
                            Name = "EFFECT",
                            Version = 11,
                            VfxName = "codex_effect",
                            Position = new Vector3(4, 5, 6),
                            Orientation = new Vector4(0, 0, 0, 1),
                            StartTime = 0,
                            EndTime = 1,
                            NodeIndex = -1,
                        },
                    ]
                };
                var bytes = parser.GenerateBytes(metadata.Version, metadata);
                var metaFile = fileSaveService.Save(metaPath, bytes, false);
                var sceneObject = superView.SceneObjects.Single();
                sceneObject.FragAndSlotSelection.MetaDataName = metaPath;
                sceneObjectEditor.SetMetaFile(sceneObject.Data, metaFile, null);
                superView.SelectedTabControllerIndex = 1;
                superView.MetaEditor.SelectedTag = superView.MetaEditor.Tags
                    .Single(tag => tag._input.Name == "IMPACT_POS");
                var impactPreview = sceneObject.Data.MetaDataItems
                    .OfType<ICombatMetaDataPreview>()
                    .Single();
                var effectPreview = sceneObject.Data.MetaDataItems
                    .OfType<DrawableMetaInstance>()
                    .Single();

                var effectTag = superView.MetaEditor.Tags
                    .Single(tag => tag._input.Name == "EFFECT");
                superView.MetaEditor.SelectedTag = effectTag;

                Assert.Multiple(() =>
                {
                    Assert.That(sceneObject.Data.MetaDataItems
                        .OfType<ICombatMetaDataPreview>()
                        .Single(), Is.SameAs(impactPreview));
                    Assert.That(sceneObject.Data.MetaDataItems
                        .OfType<DrawableMetaInstance>()
                        .Single(), Is.SameAs(effectPreview));
                    Assert.That(impactPreview.IsSelected, Is.False);
                    Assert.That(effectPreview.IsSelected, Is.True);
                    Assert.That(superView.IsEffectMetaDataSelected, Is.True);
                    Assert.That(superView.CanEditSelectedMetaData3D, Is.True);
                    Assert.That(
                        superView.CanConfigureSelectedMetaDataDisplayTime,
                        Is.True);
                    Assert.That(superView.EditEffectPosition, Is.True);
                    Assert.That(superView.EditEffectOrientation, Is.False);
                    Assert.That(
                        effectPreview.ShowForEntireAnimation,
                        Is.False);
                });

                superView.EditEffectOrientation = true;
                superView.IsCombatMetaData3dEditingEnabled = true;
                superView.ShowCombatMetaDataForEntireAnimation = true;
                Assert.Multiple(() =>
                {
                    Assert.That(superView.EditEffectPosition, Is.False);
                    Assert.That(superView.EditEffectOrientation, Is.True);
                    Assert.That(
                        effectPreview.ShowForEntireAnimation,
                        Is.True);
                    Assert.That(
                        impactPreview.ShowForEntireAnimation,
                        Is.False);
                });

                superView.MetaEditor.SelectedTag = superView.MetaEditor.Tags
                    .Single(tag => tag._input.Name == "IMPACT_POS");
                Assert.That(
                    superView.ShowCombatMetaDataForEntireAnimation,
                    Is.False);
                superView.MetaEditor.SelectedTag = effectTag;
                Assert.That(
                    superView.ShowCombatMetaDataForEntireAnimation,
                    Is.True);

                var effectPosition = effectTag.Variables
                    .OfType<VectorAttributeViewModel>()
                    .Single();
                effectPosition.Value.X.TextValue = "9";

                Assert.Multiple(() =>
                {
                    Assert.That(sceneObject.Data.MetaDataItems
                        .OfType<ICombatMetaDataPreview>()
                        .Single(), Is.SameAs(impactPreview));
                    Assert.That(sceneObject.Data.MetaDataItems
                        .OfType<DrawableMetaInstance>()
                        .Single(), Is.Not.SameAs(effectPreview));
                    Assert.That(sceneObject.Data.MetaDataItems
                        .OfType<DrawableMetaInstance>()
                        .Single().IsSelected, Is.True);
                    Assert.That(sceneObject.Data.MetaDataItems
                        .OfType<DrawableMetaInstance>()
                        .Single().ShowForEntireAnimation, Is.True);
                });
            }
            finally
            {
                superView.Close();
            }
        }

        [Test]
        public void SuperView_TimelineMarkerNavigation_PreservesOwnerIdentityAndSeeksStart()
        {
            const string animationPath =
                @"animations\battle\codex\timeline_animation.anm.meta";
            const string persistentPath =
                @"animations\battle\codex\timeline_persistent.anm.meta";
            var runner = new AssetEditorTestRunner();
            runner.CreateOutputPack();
            var editorCreator =
                runner.ServiceProvider.GetRequiredService<IEditorCreator>();
            var superView = (SuperViewViewModel)editorCreator.Create(
                EditorEnums.SuperView_Editor);

            try
            {
                var parser = runner.GetRequiredServiceInCurrentEditorScope<
                    MetaDataFileParser>();
                var fileSaveService =
                    runner.GetRequiredServiceInCurrentEditorScope<
                        IFileSaveService>();
                var sceneObjectEditor =
                    runner.GetRequiredServiceInCurrentEditorScope<
                        SceneObjectEditor>();
                var animationFile = SaveTimedMetaFile(
                    fileSaveService,
                    parser,
                    animationPath);
                var persistentFile = SaveTimedMetaFile(
                    fileSaveService,
                    parser,
                    persistentPath);
                var sceneObject = superView.SceneObjects.Single();
                sceneObject.FragAndSlotSelection.MetaDataName = animationPath;
                sceneObject.FragAndSlotSelection.MetaDataPersistName =
                    persistentPath;
                sceneObjectEditor.SetMetaFile(
                    sceneObject.Data,
                    animationFile,
                    persistentFile);
                superView.Player.SelectedAnimationMaxTime.Value = 5;
                var originalPreviews = sceneObject.Data.MetaDataItems.ToArray();

                var persistentMarker = superView.MetaDataTimeline.Markers.Single(
                    marker => marker.Item.Owner ==
                        MetaDataDocumentOwner.Persistent);
                var animationMarker = superView.MetaDataTimeline.Markers.Single(
                    marker => marker.Item.Owner ==
                        MetaDataDocumentOwner.Animation);
                superView.Player.IsEnabled.Value = true;
                Assert.That(superView.Player.IsPlaying.Value, Is.True);
                persistentMarker.SelectCommand.Execute(null);

                Assert.Multiple(() =>
                {
                    Assert.That(superView.SelectedTabControllerIndex, Is.Zero);
                    Assert.That(
                        superView.PersistentMetaEditor.SelectedAttribute,
                        Is.SameAs(persistentMarker.Item.Source));
                    Assert.That(
                        sceneObject.Data.Player.CurrentTime,
                        Is.EqualTo(TimeSpan.FromSeconds(1)));
                    Assert.That(
                        sceneObject.Data.MetaDataItems,
                        Is.EqualTo(originalPreviews));
                    Assert.That(superView.Player.IsPlaying.Value, Is.True);
                    Assert.That(
                        superView.PersistentMetaEditor.HasUnsavedChanges,
                        Is.False);
                    Assert.That(
                        superView.MetaEditor.HasUnsavedChanges,
                        Is.False);
                });

                animationMarker.SelectCommand.Execute(null);

                Assert.Multiple(() =>
                {
                    Assert.That(
                        superView.SelectedTabControllerIndex,
                        Is.EqualTo(1));
                    Assert.That(
                        superView.MetaEditor.SelectedAttribute,
                        Is.SameAs(animationMarker.Item.Source));
                    Assert.That(
                        sceneObject.Data.MetaDataItems,
                        Is.EqualTo(originalPreviews));
                });

                var persistentEndTime = superView.PersistentMetaEditor.Tags
                    .Single().Variables.Single(variable =>
                        variable.PropertyName == "EndTime");
                persistentEndTime.ValueAsString = "1.5";

                Assert.Multiple(() =>
                {
                    Assert.That(
                        superView.PersistentMetaEditor.HasUnsavedChanges,
                        Is.True);
                    Assert.That(
                        superView.MetaEditor.HasUnsavedChanges,
                        Is.False);
                    Assert.That(
                        superView.MetaDataInspectionIndex.Items.Select(item =>
                            item.Owner),
                        Is.EquivalentTo(new[]
                        {
                            MetaDataDocumentOwner.Persistent,
                            MetaDataDocumentOwner.Animation,
                        }));
                });
            }
            finally
            {
                superView.Close();
            }
        }

        [Test]
        public void SuperView_MarkerSelection_UsesCurrentIndexOwnerWithoutSeeking()
        {
            const string animationPath =
                @"animations\battle\codex\marker_animation.anm.meta";
            const string persistentPath =
                @"animations\battle\codex\marker_persistent.anm.meta";
            var runner = new AssetEditorTestRunner();
            runner.CreateOutputPack();
            var editorCreator =
                runner.ServiceProvider.GetRequiredService<IEditorCreator>();
            var superView = (SuperViewViewModel)editorCreator.Create(
                EditorEnums.SuperView_Editor);

            try
            {
                var parser = runner.GetRequiredServiceInCurrentEditorScope<
                    MetaDataFileParser>();
                var fileSaveService =
                    runner.GetRequiredServiceInCurrentEditorScope<
                        IFileSaveService>();
                var sceneObjectEditor =
                    runner.GetRequiredServiceInCurrentEditorScope<
                        SceneObjectEditor>();
                var animationFile = SaveTimedMetaFile(
                    fileSaveService,
                    parser,
                    animationPath);
                var persistentFile = SaveTimedMetaFile(
                    fileSaveService,
                    parser,
                    persistentPath);
                var sceneObject = superView.SceneObjects.Single();
                sceneObject.FragAndSlotSelection.MetaDataName = animationPath;
                sceneObject.FragAndSlotSelection.MetaDataPersistName =
                    persistentPath;
                sceneObjectEditor.SetMetaFile(
                    sceneObject.Data,
                    animationFile,
                    persistentFile);
                superView.Player.SelectedAnimationMaxTime.Value = 5;
                superView.SelectedTabControllerIndex = 1;
                foreach (var preview in sceneObject.Data.MetaDataItems)
                    preview.Update(1);
                var persistentItem = superView.MetaDataInspectionIndex.Items
                    .Single(item => item.Owner ==
                        MetaDataDocumentOwner.Persistent);

                superView.SelectMetaDataMarker(persistentItem);

                var selectedTimelineMarker = superView.MetaDataTimeline.Markers
                    .Single(marker => ReferenceEquals(
                        marker.Item.Source,
                        persistentItem.Source));
                Assert.Multiple(() =>
                {
                    Assert.That(superView.SelectedTabControllerIndex, Is.Zero);
                    Assert.That(
                        superView.PersistentMetaEditor.SelectedAttribute,
                        Is.SameAs(persistentItem.Source));
                    Assert.That(
                        sceneObject.Data.Player.CurrentTime,
                        Is.EqualTo(TimeSpan.Zero));
                    Assert.That(selectedTimelineMarker.IsSelected, Is.True);
                    Assert.That(superView.CanEditSelectedMetaData3D, Is.True);
                    Assert.That(
                        superView.GetMetaDataMarkerCandidates(),
                        Has.Count.EqualTo(2));
                    Assert.That(superView.HasUnsavedChanges, Is.False);
                });

                superView.ClearMetaDataMarkerSelection();

                Assert.Multiple(() =>
                {
                    Assert.That(
                        superView.PersistentMetaEditor.SelectedAttribute,
                        Is.Null);
                    Assert.That(
                        superView.MetaDataTimeline.Markers,
                        Has.None.Property("IsSelected").True);
                    Assert.That(
                        superView.CanEditSelectedMetaData3D,
                        Is.False);
                    Assert.That(superView.HasUnsavedChanges, Is.False);
                });
            }
            finally
            {
                superView.Close();
            }
        }

        [Test]
        public void SuperView_InvalidField_RefreshesIndexWithoutRebuildingPreview()
        {
            const string metaPath =
                @"animations\battle\codex\timeline_validation.anm.meta";
            var runner = new AssetEditorTestRunner();
            runner.CreateOutputPack();
            var editorCreator =
                runner.ServiceProvider.GetRequiredService<IEditorCreator>();
            var superView = (SuperViewViewModel)editorCreator.Create(
                EditorEnums.SuperView_Editor);

            try
            {
                var parser = runner.GetRequiredServiceInCurrentEditorScope<
                    MetaDataFileParser>();
                var fileSaveService =
                    runner.GetRequiredServiceInCurrentEditorScope<
                        IFileSaveService>();
                var sceneObjectEditor =
                    runner.GetRequiredServiceInCurrentEditorScope<
                        SceneObjectEditor>();
                var metaFile = SaveTimedMetaFile(
                    fileSaveService,
                    parser,
                    metaPath);
                var sceneObject = superView.SceneObjects.Single();
                sceneObject.FragAndSlotSelection.MetaDataName = metaPath;
                sceneObjectEditor.SetMetaFile(sceneObject.Data, metaFile, null);
                superView.Player.SelectedAnimationMaxTime.Value = 5;
                var originalPreview = sceneObject.Data.MetaDataItems.Single();
                var tag = superView.MetaEditor.Tags.Single();
                var startTime = tag.Variables.Single(variable =>
                    variable.PropertyName == "StartTime");

                startTime.ValueAsString = "invalid";

                var item = superView.MetaDataInspectionIndex.Items.Single();
                Assert.Multiple(() =>
                {
                    Assert.That(item.AreFieldsValid, Is.False);
                    Assert.That(item.TimelineMarkerKind, Is.Null);
                    Assert.That(superView.MetaDataTimeline.Markers, Is.Empty);
                    Assert.That(
                        sceneObject.Data.MetaDataItems.Single(),
                        Is.SameAs(originalPreview));
                });

                startTime.ValueAsString = "1";

                Assert.Multiple(() =>
                {
                    Assert.That(
                        superView.MetaDataInspectionIndex.Items.Single()
                            .AreFieldsValid,
                        Is.True);
                    Assert.That(
                        superView.MetaDataTimeline.Markers,
                        Has.Count.EqualTo(1));
                    Assert.That(
                        sceneObject.Data.MetaDataItems.Single(),
                        Is.SameAs(originalPreview));
                });
            }
            finally
            {
                superView.Close();
            }
        }

        [Test]
        public void SuperView_ProblemNavigation_UsesSharedIdentityWithoutBlockingWarningSave()
        {
            const string persistentPath =
                @"animations\battle\codex\problem_persistent.meta";
            var runner = new AssetEditorTestRunner();
            runner.CreateOutputPack();
            var editorCreator =
                runner.ServiceProvider.GetRequiredService<IEditorCreator>();
            var superView = (SuperViewViewModel)editorCreator.Create(
                EditorEnums.SuperView_Editor);

            try
            {
                var parser = runner.GetRequiredServiceInCurrentEditorScope<
                    MetaDataFileParser>();
                var fileSaveService =
                    runner.GetRequiredServiceInCurrentEditorScope<
                        IFileSaveService>();
                var sceneObjectEditor =
                    runner.GetRequiredServiceInCurrentEditorScope<
                        SceneObjectEditor>();
                var camera = runner.GetRequiredServiceInCurrentEditorScope<
                    ArcBallCamera>();
                var position = new Vector3(3, 4, 5);
                var metadata = new ParsedMetadataFile
                {
                    Version = 2,
                    Attributes =
                    [
                        new FirePos_v10
                        {
                            Name = "FIRE_POS",
                            Version = 10,
                            StartTime = 1,
                            EndTime = 0,
                            Position = position,
                        },
                    ],
                };
                var persistentFile = fileSaveService.Save(
                    persistentPath,
                    parser.GenerateBytes(metadata.Version, metadata),
                    false)!;
                var sceneObject = superView.SceneObjects.Single();
                sceneObject.FragAndSlotSelection.MetaDataPersistName =
                    persistentPath;
                sceneObjectEditor.SetMetaFile(
                    sceneObject.Data,
                    null,
                    persistentFile);
                superView.Player.SelectedAnimationMaxTime.Value = 5;
                superView.SelectedTabControllerIndex = 1;
                var originalPreview = sceneObject.Data.MetaDataItems.Single();
                var problem = superView.MetaDataInspectionIndex.Problems
                    .Single();
                camera.LookAt = Vector3.Zero;

                superView.NavigateToMetaDataProblem(problem);

                Assert.Multiple(() =>
                {
                    Assert.That(
                        problem.ReasonKey,
                        Is.EqualTo("SuperView.Problems.Time.Reversed"));
                    Assert.That(
                        problem.Severity,
                        Is.EqualTo(MetaDataDiagnosticSeverity.Warning));
                    Assert.That(superView.SelectedTabControllerIndex, Is.Zero);
                    Assert.That(
                        superView.PersistentMetaEditor.SelectedAttribute,
                        Is.SameAs(problem.Source));
                    Assert.That(
                        sceneObject.Data.Player.CurrentTime,
                        Is.EqualTo(TimeSpan.FromSeconds(1)));
                    Assert.That(camera.LookAt, Is.EqualTo(position));
                    Assert.That(
                        sceneObject.Data.MetaDataItems.Single(),
                        Is.SameAs(originalPreview));
                    Assert.That(
                        superView.MetaDataProblems.SelectedProblem?.Problem
                            .Source,
                        Is.SameAs(problem.Source));
                    Assert.That(
                        superView.MetaDataProblems.SelectedProblem?.Problem
                            .Owner,
                        Is.EqualTo(problem.Owner));
                    Assert.That(superView.MetaDataTimeline.Markers, Is.Empty);
                    Assert.That(superView.HasUnsavedChanges, Is.False);
                    Assert.That(superView.Save(), Is.True);
                });
            }
            finally
            {
                superView.Close();
            }
        }

        private static PackFile SaveTimedMetaFile(
            IFileSaveService fileSaveService,
            MetaDataFileParser parser,
            string path)
        {
            var metadata = new ParsedMetadataFile
            {
                Version = 2,
                Attributes =
                [
                    new FirePos_v10
                    {
                        Name = "FIRE_POS",
                        Version = 10,
                        StartTime = 1,
                        EndTime = 2,
                        Position = new Vector3(1, 2, 3),
                    },
                ],
            };
            return fileSaveService.Save(
                path,
                parser.GenerateBytes(metadata.Version, metadata),
                false)!;
        }
    }
}
