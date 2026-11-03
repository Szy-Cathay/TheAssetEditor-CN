using System.Buffers.Binary;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using Editors.AnimationMeta.Presentation;
using Editors.AnimationMeta.Presentation.View;
using NUnit.Framework;
using Shared.Core.Events;
using Shared.Core.PackFiles.Models;
using Shared.Core.Settings;
using Shared.GameFormats.AnimationMeta.Definitions;
using Shared.GameFormats.AnimationMeta.Parsing;
using Assert = NUnit.Framework.Assert;

namespace AssetEditorTests;

[NonParallelizable]
public class EffectVersionOneAttributeControlTests
{
    public static IEnumerable<TestCaseData> Cases =>
        from theme in new[] { ThemeType.DarkTheme, ThemeType.LightTheme, ThemeType.HighContrastDark, ThemeType.HighContrastLight }
        from start in new[] { true, false }
        select new TestCaseData(theme, start);

    [TestCaseSource(nameof(Cases))]
    public void NativeV1Seconds_UseTheExistingTextBoxBindingAndLocalUndoWithoutAWindow(ThemeType theme, bool start)
    {
        SuperViewWorkspaceLayoutTests.WithTheme(theme, () =>
        {
            var parser = new MetaDataFileParser(new MetaDataDatabase());
            var authored = new Effect_v1 { Name = "EFFECT", Version = 1, VfxName = "diagnostic", BoolUnknown = "true", NodeIndex = -1,
                EffectStartTime = .25f, EffectEndTime = 1.75f };
            var bytes = parser.GenerateBytes(2, new ParsedMetadataFile { Version = 2, Attributes = [authored] });
            var hub = new RecordingEventHub();
            var owner = new MetaDataEditorViewModel(null!, parser, hub);
            owner.LoadFile(PackFile.CreateFromBytes("diagnostic_v1.anm.meta", bytes));
            owner.SelectedTag = owner.Tags.Single();
            var view = new MetaDataAttributeView { DataContext = owner };
            try
            {
                SuperViewWorkspaceLayoutTests.Layout(view, 600, 850);
                var source = (Effect_v1)owner.ParsedFile!.Attributes.Single();
                var original = parser.Serialize(source, out _)!;
                var backup = source.Data;
                var field = owner.SelectedTag.Variables.Single(variable => start ? variable.IsStartTime : variable.IsEndTime);
                var input = SuperViewWorkspaceLayoutTests.Descendants<TextBox>(view).Single(control => ReferenceEquals(control.DataContext, field));
                var label = SuperViewWorkspaceLayoutTests.Descendants<Label>(view).Single(control => ReferenceEquals(control.DataContext, field));
                Assert.Multiple(() =>
                {
                    Assert.That(Window.GetWindow(view), Is.Null);
                    Assert.That(label.Content, Is.EqualTo(start ? "开始时间" : "结束时间"));
                    Assert.That(input.ToolTip, Is.EqualTo(start ? "开始时间（秒）" : "结束时间（秒）"));
                    Assert.That(input.IsReadOnly, Is.False);
                    Assert.That(float.Parse(input.Text, CultureInfo.CurrentCulture), Is.EqualTo(start ? .25f : 1.75f));
                    Assert.That(owner.HasUnsavedChanges, Is.False);
                    Assert.That(hub.AttributeChanges, Is.Zero);
                });
                input.IsUndoEnabled = false;
                input.IsUndoEnabled = true;
                input.BeginChange();
                input.SelectAll();
                input.SelectedText = .75f.ToString(CultureInfo.CurrentCulture);
                input.EndChange();
                input.GetBindingExpression(TextBox.TextProperty)!.UpdateSource();
                var changed = original.ToArray();
                BinaryPrimitives.WriteSingleLittleEndian(changed.AsSpan(start ? 4 : 8), .75f);
                Assert.Multiple(() =>
                {
                    Assert.That(input.CanUndo, Is.True);
                    Assert.That(field.IsModified && owner.HasUnsavedChanges, Is.True);
                    Assert.That(parser.Serialize(source, out _), Is.EqualTo(changed));
                    Assert.That(source.Data, Is.SameAs(backup));
                    Assert.That(hub.AttributeChanges, Is.EqualTo(1));
                });
                input.Undo();
                input.GetBindingExpression(TextBox.TextProperty)!.UpdateSource();
                Assert.Multiple(() =>
                {
                    Assert.That(parser.Serialize(source, out _), Is.EqualTo(original));
                    Assert.That(hub.AttributeChanges, Is.EqualTo(2));
                    Assert.That(owner.HasUnsavedChanges, Is.True, "Keep the existing field editor's dirty-until-save contract.");
                });
                input.Redo();
                input.GetBindingExpression(TextBox.TextProperty)!.UpdateSource();
                Assert.That(parser.Serialize(source, out _), Is.EqualTo(changed));
                Assert.That(hub.AttributeChanges, Is.EqualTo(3));
                owner.HasUnsavedChanges = false;
                Assert.That(owner.HasUnsavedChanges, Is.False);
                SuperViewWorkspaceLayoutTests.Snapshot(view, $"effect-v1-seconds-{theme}-{(start ? "start" : "end")}.png", 96);
            }
            finally { view.DataContext = null; owner.Close(); }
        });
    }

    private sealed class RecordingEventHub : IEventHub
    {
        public int AttributeChanges { get; private set; }
        public void Publish<T>(T value) { if (value is MetaDataAttributeChangedEvent) AttributeChanges++; }
        public void PublishGlobalEvent<T>(T value) { }
        public void Register<T>(object owner, Action<T> action) { }
        public void UnRegister(object owner) { }
    }
}
