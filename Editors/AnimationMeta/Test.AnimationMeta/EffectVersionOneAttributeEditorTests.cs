using System.Buffers.Binary;
using System.Globalization;
using Editors.AnimationMeta.Presentation;
using Shared.Core.Events;
using Shared.Core.PackFiles.Models;
using Shared.GameFormats.AnimationMeta.Definitions;
using Shared.GameFormats.AnimationMeta.Parsing;

namespace Test.AnimationMeta;

public class EffectVersionOneAttributeEditorTests
{
    [TestCase(true, 4)]
    [TestCase(false, 8)]
    public void ExistingAttributeEditor_ExposesV1SecondsAndEditsOnlyTheCorrespondingFourBytes(bool start, int offset)
    {
        var parser = new MetaDataFileParser(new MetaDataDatabase());
        var original = Source();
        var file = new ParsedMetadataFile { Version = 2, Attributes = [original] };
        var hub = new RecordingEventHub();
        var owner = new MetaDataEditorViewModel(null!, parser, hub);
        owner.LoadFile(PackFile.CreateFromBytes("diagnostic_v1.anm.meta", parser.GenerateBytes(2, file)));
        try
        {
            var source = (Effect_v1)owner.ParsedFile!.Attributes.Single();
            var recovery = source.Data;
            var bytes = parser.Serialize(source, out _)!;
            var entry = owner.Tags.Single();
            var field = entry.Variables.Single(variable => start ? variable.IsStartTime : variable.IsEndTime);
            var modern = new MetaDataEntry(new Effect_v12 { Name = "EFFECT", Version = 12 }, "", hub, true)
                .Variables.Single(variable => start ? variable.IsStartTime : variable.IsEndTime);
            Assert.Multiple(() =>
            {
                Assert.That(entry.Variables.Any(variable => variable.PropertyName is nameof(Effect_v1.UnknownIntBase_v0) or nameof(Effect_v1.UnknownIntBase_v1)), Is.False);
                Assert.That(field.PropertyName, Is.EqualTo(start ? nameof(Effect_v1.EffectStartTime) : nameof(Effect_v1.EffectEndTime)));
                Assert.That(float.Parse(field.ValueAsString, CultureInfo.CurrentCulture), Is.EqualTo(start ? .25f : 1.75f));
                Assert.That(field.IsReadOnly, Is.False);
                Assert.That(field.FieldName, Is.EqualTo(modern.FieldName));
                Assert.That(field.Description, Is.EqualTo(modern.Description));
                Assert.That(owner.HasUnsavedChanges, Is.False);
                Assert.That(hub.AttributeChanges, Is.Zero);
            });
            field.ValueAsString = 2.125f.ToString(CultureInfo.CurrentCulture);
            var expected = bytes.ToArray();
            BinaryPrimitives.WriteSingleLittleEndian(expected.AsSpan(offset), 2.125f);
            Assert.Multiple(() =>
            {
                Assert.That(field.IsValid, Is.True);
                Assert.That(field.IsModified, Is.True);
                Assert.That(owner.HasUnsavedChanges, Is.True);
                Assert.That(hub.AttributeChanges, Is.EqualTo(1));
                Assert.That(parser.Serialize(source, out _), Is.EqualTo(expected));
                Assert.That(source.Data, Is.SameAs(recovery));
            });
            owner.RefreshAttributeValues(source);
            Assert.That(hub.AttributeChanges, Is.EqualTo(1), "Refreshing a float alias must not produce another edit.");
            owner.HasUnsavedChanges = false;
            Assert.That(owner.HasUnsavedChanges, Is.False);
        }
        finally { owner.Close(); }
    }

    [Test]
    public void InvalidSeconds_LeaveBothNativeSlotsAndDirtyStateUnchanged()
    {
        var source = Source();
        var hub = new RecordingEventHub();
        var entry = new MetaDataEntry(source, "", hub, true);
        var field = entry.Variables.Single(variable => variable.IsStartTime);
        field.ValueAsString = "invalid";
        Assert.Multiple(() =>
        {
            Assert.That(field.IsValid, Is.False);
            Assert.That(field.IsModified, Is.False);
            Assert.That((source.EffectStartTime, source.EffectEndTime), Is.EqualTo((.25f, 1.75f)));
            Assert.That(hub.AttributeChanges, Is.Zero);
        });
        field.RefreshFromTarget();
        Assert.That(field.IsValid, Is.True);
        Assert.That(hub.AttributeChanges, Is.Zero);
    }

    [Test]
    public void OtherLegacyTags_KeepTheirExistingIntegerEditorsAndValues()
    {
        var source = new OtherLegacyTag { Name = "DIAG_OTHER", Version = 1, UnknownIntBase_v0 = 123, UnknownIntBase_v1 = 456 };
        var entry = new MetaDataEntry(source, "", new RecordingEventHub(), true);
        var field = entry.Variables.Single(variable => variable.PropertyName == nameof(source.UnknownIntBase_v0));
        Assert.Multiple(() =>
        {
            Assert.That(field.ValueAsString, Is.EqualTo("123"));
            Assert.That(field.IsStartTime || field.IsEndTime, Is.False);
        });
        field.ValueAsString = "789";
        Assert.That((source.UnknownIntBase_v0, source.UnknownIntBase_v1), Is.EqualTo((789, 456)));
    }

    private static Effect_v1 Source() => new()
    {
        Name = "EFFECT", Version = 1, VfxName = "diagnostic", BoolUnknown = "true", NodeIndex = -1,
        EffectStartTime = .25f, EffectEndTime = 1.75f,
    };

    private sealed class OtherLegacyTag : DecodedMetaEntryBase_v1 { }
    private sealed class RecordingEventHub : IEventHub
    {
        public int AttributeChanges { get; private set; }
        public void Publish<T>(T value) { if (value is MetaDataAttributeChangedEvent) AttributeChanges++; }
        public void PublishGlobalEvent<T>(T value) { }
        public void Register<T>(object owner, Action<T> action) { }
        public void UnRegister(object owner) { }
    }
}
