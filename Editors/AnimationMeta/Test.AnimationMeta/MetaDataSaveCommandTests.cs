using Editors.AnimationMeta.MetaEditor.Commands;
using Editors.AnimationMeta.Presentation;
using Moq;
using Shared.Core.Events;
using Shared.Core.Events.Scoped;
using Shared.Core.PackFiles;
using Shared.Core.PackFiles.Models;
using Shared.Core.Services;
using Shared.GameFormats.AnimationMeta.Definitions;
using Shared.GameFormats.AnimationMeta.Parsing;

namespace Test.AnimationMeta;

[TestFixture]
public class MetaDataSaveCommandTests
{
    [Test]
    public void CancelledSave_KeepsDirtyAndFileIdentityWithoutPublishingSuccess()
    {
        var context = CreateContext();
        context.Editor.HasUnsavedChanges = true;
        var original = context.Editor.CurrentFile;

        Assert.That(context.Command.Execute(context.Editor), Is.False);

        Assert.Multiple(() =>
        {
            Assert.That(context.Editor.CurrentFile, Is.SameAs(original));
            Assert.That(context.Editor.HasUnsavedChanges, Is.True);
        });
        context.Events.Verify(events => events.Publish(It.IsAny<ScopedFileSavedEvent>()), Times.Never);
    }

    [Test]
    public void UnknownTagAndPreviewWarning_SavePreservesOriginalPayloadAndAuthoredTimes()
    {
        var context = CreateContext();
        var unknown = new ParsedUnknownMetadataAttribute
        {
            Name = "UNRECOGNISED_TEST_TAG",
            Version = 77,
            Data = [77, 0, 0, 0, 3, 8, 11, 29],
        };
        context.Editor.ParsedFile!.Attributes.Add(unknown);
        context.Editor.ParsedFile.Attributes.Add(new FirePos_v10
        {
            Name = "FIRE_POS",
            Version = 10,
            StartTime = 4,
            EndTime = 2,
        });
        context.Editor.UpdateView();
        byte[]? savedBytes = null;
        context.Saves.Setup(saves => saves.Save(It.IsAny<string>(), It.IsAny<byte[]>(), false))
            .Returns((string _, byte[] bytes, bool _) =>
            {
                savedBytes = bytes;
                return PackFile.CreateFromBytes("saved.meta", bytes);
            });

        Assert.That(context.Command.Execute(context.Editor), Is.True);
        var saved = context.Parser.ParseFile(savedBytes!);
        Assert.Multiple(() =>
        {
            Assert.That(saved.Attributes, Has.Count.EqualTo(2));
            Assert.That(saved.Attributes[0], Is.TypeOf<ParsedUnknownMetadataAttribute>());
            Assert.That(saved.Attributes[0].Data, Is.EqualTo(unknown.Data));
            Assert.That(((FirePos_v10)saved.Attributes[1]).StartTime, Is.EqualTo(4));
            Assert.That(((FirePos_v10)saved.Attributes[1]).EndTime, Is.EqualTo(2));
            Assert.That(context.Editor.HasUnsavedChanges, Is.False);
            Assert.That(context.Editor.CurrentFile.Name, Is.EqualTo("saved.meta"));
        });
        context.Events.Verify(events => events.Publish(It.Is<ScopedFileSavedEvent>(saved =>
            ReferenceEquals(saved.FileOwner, context.Editor))), Times.Once);
    }

    [Test]
    public void InvalidEditedField_StillBlocksSaveAndPreservesDirty()
    {
        var context = CreateContext();
        context.Editor.ParsedFile!.Attributes.Add(new FirePos_v10 { Name = "FIRE_POS", Version = 10 });
        context.Editor.UpdateView();
        var field = context.Editor.Tags.Single().Variables.Single(variable => variable.PropertyName == "StartTime");
        field.ValueAsString = "invalid";
        context.Editor.HasUnsavedChanges = true;

        Assert.That(context.Command.Execute(context.Editor), Is.False);
        Assert.That(context.Editor.HasUnsavedChanges, Is.True);
        context.Saves.Verify(saves => saves.Save(It.IsAny<string>(), It.IsAny<byte[]>(), It.IsAny<bool>()), Times.Never);
        context.Events.Verify(events => events.Publish(It.IsAny<ScopedFileSavedEvent>()), Times.Never);
    }

    private static Context CreateContext()
    {
        _ = LocalizationManager.Instance ?? new LocalizationManager();
        var parser = new MetaDataFileParser(new MetaDataDatabase());
        var events = new Mock<IEventHub>();
        var editor = new MetaDataEditorViewModel(new Mock<IUiCommandFactory>().Object, parser, events.Object);
        editor.LoadFile(PackFile.CreateFromBytes("source.meta", [2, 0, 0, 0, 0, 0, 0, 0]));
        var packs = new Mock<IPackFileService>();
        packs.Setup(packs => packs.GetFullPath(It.IsAny<PackFile>(), null)).Returns(@"animations\test.meta");
        var saves = new Mock<IFileSaveService>();
        var command = new SaveCommand(packs.Object, events.Object, saves.Object, new Mock<IStandardDialogs>().Object, parser);
        return new Context(editor, command, parser, saves, events);
    }

    private sealed record Context(MetaDataEditorViewModel Editor, SaveCommand Command,
        MetaDataFileParser Parser, Mock<IFileSaveService> Saves, Mock<IEventHub> Events);
}
