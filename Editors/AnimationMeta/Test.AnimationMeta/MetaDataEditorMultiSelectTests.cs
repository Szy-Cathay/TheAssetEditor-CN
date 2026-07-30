using System.IO;
using System.Xml.Linq;
using Editors.AnimationMeta.Presentation;
using Shared.Core.Events.Global;
using Shared.GameFormats.AnimationMeta.Parsing;
using Test.TestingUtility.Shared;
using Test.TestingUtility.TestUtility;

namespace Test.AnimationMeta
{
    [TestFixture]
    public class MetaDataEditorMultiSelectTests
    {
        private const string MetadataPath =
            @"animations/battle/humanoid17/throt_whip_catcher/attacks/hu17_whip_catcher_attack_05.anm.meta";

        [Test]
        public void MetadataList_UsesExtendedSelectionMode()
        {
            var document = XDocument.Load(GetRepositoryFilePath(
                "Editors",
                "MetaDataEditor",
                "AnimationMeta",
                "MetaEditor",
                "View",
                "MetaDataEntryView.xaml"));
            XNamespace presentation =
                "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
            var listView = document
                .Descendants(presentation + "ListView")
                .Single();

            Assert.That(
                (string?)listView.Attribute("SelectionMode"),
                Is.EqualTo("Extended"));
        }

        [Test]
        public void DeleteAction_MultiSelectedTags_RemovesAllSelectedAndPersists()
        {
            var (runner, editor, outputPackFile) = OpenEditor();
            var original = editor.ParsedFile!.Attributes.ToList();
            var firstRemoved = editor.Tags[1];
            var secondRemoved = editor.Tags[3];
            firstRemoved.IsSelected = true;
            secondRemoved.IsSelected = true;
            editor.SelectedTag = secondRemoved;

            editor.DeleteActionCommand.Execute(null);

            var expected = original
                .Where(item =>
                    item != firstRemoved._input &&
                    item != secondRemoved._input)
                .ToList();
            Assert.Multiple(() =>
            {
                Assert.That(editor.ParsedFile.Attributes, Is.EqualTo(expected));
                Assert.That(editor.HasUnsavedChanges, Is.True);
                Assert.That(editor.SelectedTag?._input, Is.SameAs(expected[0]));
            });

            editor.SaveActionCommand.Execute(null);
            var savedFile = runner.PackFileService.FindFile(
                MetadataPath,
                outputPackFile);
            Assert.That(savedFile, Is.Not.Null);
            var parser =
                runner.GetRequiredServiceInCurrentEditorScope<MetaDataFileParser>();
            var parsedFile = parser.ParseFile(savedFile!);
            Assert.That(parsedFile, Is.Not.Null);

            Assert.Multiple(() =>
            {
                Assert.That(parsedFile!.Attributes.Count, Is.EqualTo(expected.Count));
                Assert.That(editor.HasUnsavedChanges, Is.False);
            });
        }

        [Test]
        public void MoveUpAction_NonAdjacentSelection_MovesEachGroupAndRestoresSelection()
        {
            var (_, editor, _) = OpenEditor();
            var original = editor.ParsedFile!.Attributes.ToList();
            var firstMoved = editor.Tags[2];
            var secondMoved = editor.Tags[4];
            firstMoved.IsSelected = true;
            secondMoved.IsSelected = true;
            editor.SelectedTag = secondMoved;

            editor.MoveUpActionCommand.Execute(null);

            var expected = original.ToList();
            (expected[1], expected[2]) = (expected[2], expected[1]);
            (expected[3], expected[4]) = (expected[4], expected[3]);
            Assert.Multiple(() =>
            {
                Assert.That(editor.ParsedFile.Attributes, Is.EqualTo(expected));
                Assert.That(GetSelectedInputs(editor), Is.EquivalentTo(
                    new[] { firstMoved._input, secondMoved._input }));
                Assert.That(editor.SelectedTag?._input, Is.SameAs(secondMoved._input));
                Assert.That(editor.HasUnsavedChanges, Is.True);
            });
        }

        [Test]
        public void MoveDownAction_NonAdjacentSelection_MovesEachGroupAndRestoresSelection()
        {
            var (_, editor, _) = OpenEditor();
            var original = editor.ParsedFile!.Attributes.ToList();
            var firstMoved = editor.Tags[1];
            var secondMoved = editor.Tags[3];
            firstMoved.IsSelected = true;
            secondMoved.IsSelected = true;
            editor.SelectedTag = firstMoved;

            editor.MoveDownActionCommand.Execute(null);

            var expected = original.ToList();
            (expected[1], expected[2]) = (expected[2], expected[1]);
            (expected[3], expected[4]) = (expected[4], expected[3]);
            Assert.Multiple(() =>
            {
                Assert.That(editor.ParsedFile.Attributes, Is.EqualTo(expected));
                Assert.That(GetSelectedInputs(editor), Is.EquivalentTo(
                    new[] { firstMoved._input, secondMoved._input }));
                Assert.That(editor.SelectedTag?._input, Is.SameAs(firstMoved._input));
                Assert.That(editor.HasUnsavedChanges, Is.True);
            });
        }

        [Test]
        public void MoveActions_ContiguousSelection_MovesAsStableBlock()
        {
            var (_, editor, _) = OpenEditor();
            var original = editor.ParsedFile!.Attributes.ToList();
            var movedTags = editor.Tags.Skip(2).Take(3).ToList();
            foreach (var tag in movedTags)
                tag.IsSelected = true;
            editor.SelectedTag = movedTags[1];

            editor.MoveDownActionCommand.Execute(null);

            var movedDown = original.ToList();
            var itemBelow = movedDown[5];
            movedDown.RemoveAt(5);
            movedDown.Insert(2, itemBelow);
            Assert.Multiple(() =>
            {
                Assert.That(editor.ParsedFile.Attributes, Is.EqualTo(movedDown));
                Assert.That(
                    GetSelectedInputs(editor),
                    Is.EqualTo(movedTags.Select(tag => tag._input)));
                Assert.That(editor.SelectedTag, Is.SameAs(movedTags[1]));
            });

            editor.MoveUpActionCommand.Execute(null);

            Assert.Multiple(() =>
            {
                Assert.That(editor.ParsedFile.Attributes, Is.EqualTo(original));
                Assert.That(
                    GetSelectedInputs(editor),
                    Is.EqualTo(movedTags.Select(tag => tag._input)));
                Assert.That(editor.SelectedTag, Is.SameAs(movedTags[1]));
            });
        }

        [Test]
        public void MoveAction_SelectionAtBoundary_DoesNotMarkEditorDirty()
        {
            var (_, editor, _) = OpenEditor();
            var original = editor.ParsedFile!.Attributes.ToList();
            editor.Tags[0].IsSelected = true;
            editor.SelectedTag = editor.Tags[0];

            editor.MoveUpActionCommand.Execute(null);

            Assert.Multiple(() =>
            {
                Assert.That(editor.ParsedFile.Attributes, Is.EqualTo(original));
                Assert.That(editor.HasUnsavedChanges, Is.False);
                Assert.That(editor.SelectedTag?._input, Is.SameAs(original[0]));
            });
        }

        [Test]
        public void MoveAction_PreservesInvalidInputOnUnmovedTag()
        {
            const string invalidValue = "CODEX_INVALID_NUMBER";
            var (_, editor, _) = OpenEditor();
            var untouchedTag = editor.Tags[0];
            var invalidVariable = untouchedTag.Variables.First(variable =>
                variable.IsReadOnly == false &&
                variable is not VectorAttributeViewModel &&
                variable is not OrientationAttributeViewModel);
            invalidVariable.ValueAsString = invalidValue;
            Assert.That(invalidVariable.IsValid, Is.False);
            editor.Tags[2].IsSelected = true;
            editor.Tags[4].IsSelected = true;
            editor.SelectedTag = editor.Tags[4];

            editor.MoveUpActionCommand.Execute(null);

            Assert.Multiple(() =>
            {
                Assert.That(editor.Tags, Does.Contain(untouchedTag));
                Assert.That(invalidVariable.ValueAsString, Is.EqualTo(invalidValue));
                Assert.That(invalidVariable.IsValid, Is.False);
            });
        }

        [Test]
        public void DeleteAction_PreservesInvalidInputOnRemainingTag()
        {
            const string invalidValue = "CODEX_INVALID_NUMBER";
            var (_, editor, _) = OpenEditor();
            var untouchedTag = editor.Tags[0];
            var invalidVariable = untouchedTag.Variables.First(variable =>
                variable.IsReadOnly == false &&
                variable is not VectorAttributeViewModel &&
                variable is not OrientationAttributeViewModel);
            invalidVariable.ValueAsString = invalidValue;
            Assert.That(invalidVariable.IsValid, Is.False);
            editor.Tags[2].IsSelected = true;
            editor.Tags[4].IsSelected = true;
            editor.SelectedTag = editor.Tags[4];

            editor.DeleteActionCommand.Execute(null);

            Assert.Multiple(() =>
            {
                Assert.That(editor.Tags, Does.Contain(untouchedTag));
                Assert.That(invalidVariable.ValueAsString, Is.EqualTo(invalidValue));
                Assert.That(invalidVariable.IsValid, Is.False);
            });
        }

        [Test]
        public void DeleteAction_SelectedTagFallback_PreservesExistingSingleSelectionBehavior()
        {
            var (_, editor, _) = OpenEditor();
            var original = editor.ParsedFile!.Attributes.ToList();
            var removed = editor.Tags[2];
            editor.SelectedTag = removed;

            editor.DeleteActionCommand.Execute(null);

            Assert.That(editor.ParsedFile.Attributes, Does.Not.Contain(removed._input));
            Assert.That(editor.ParsedFile.Attributes.Count, Is.EqualTo(original.Count - 1));
            Assert.That(editor.SelectedTag, Is.SameAs(editor.Tags[0]));
            Assert.That(editor.SelectedTag!.IsSelected, Is.True);
        }

        [Test]
        public void MoveAction_SelectedTagFallback_NormalizesSelectionState()
        {
            var (_, editor, _) = OpenEditor();
            var moved = editor.Tags[2];
            editor.SelectedTag = moved;

            editor.MoveDownActionCommand.Execute(null);

            Assert.Multiple(() =>
            {
                Assert.That(
                    editor.ParsedFile!.Attributes[3],
                    Is.SameAs(moved._input));
                Assert.That(editor.SelectedTag, Is.SameAs(moved));
                Assert.That(moved.IsSelected, Is.True);
            });
        }

        private static List<ParsedMetadataAttribute> GetSelectedInputs(
            MetaDataEditorViewModel editor)
        {
            return editor.Tags
                .Where(tag => tag.IsSelected)
                .Select(tag => tag._input)
                .ToList();
        }

        private static (
            AssetEditorTestRunner Runner,
            MetaDataEditorViewModel Editor,
            Shared.Core.PackFiles.Models.PackFileContainer OutputPackFile)
            OpenEditor()
        {
            var runner = new AssetEditorTestRunner();
            runner.CreateCaContainer();
            var outputPackFile = runner.LoadPackFile(
                PathHelper.GetDataFile("Throt.pack"),
                true);
            Assert.That(outputPackFile, Is.Not.Null);
            var metaPackFile = runner.PackFileService.FindFile(MetadataPath);
            var editor = runner.CommandFactory
                .Create<OpenEditorCommand>()
                .Execute<MetaDataEditorViewModel>(
                    metaPackFile!,
                    Shared.Core.ToolCreation.EditorEnums.Meta_Editor);

            Assert.That(editor.ParsedFile, Is.Not.Null);
            Assert.That(editor.Tags.Count, Is.GreaterThan(4));
            return (runner, editor, outputPackFile!);
        }

        private static string GetRepositoryFilePath(params string[] pathParts)
        {
            var directory = new DirectoryInfo(
                TestContext.CurrentContext.TestDirectory);
            while (directory != null)
            {
                if (File.Exists(Path.Combine(
                    directory.FullName,
                    "AssetEditor.CN.sln")))
                {
                    return Path.Combine(
                        [directory.FullName, .. pathParts]);
                }

                directory = directory.Parent;
            }

            throw new DirectoryNotFoundException(
                "Unable to locate the AssetEditor.CN repository root.");
        }
    }
}
