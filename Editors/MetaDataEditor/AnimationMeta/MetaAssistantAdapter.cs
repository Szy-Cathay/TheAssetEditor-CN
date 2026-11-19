using System.Security.Cryptography;
using System.Text.Json.Nodes;
using Editors.AnimationMeta.MetaEditor.Commands;
using Editors.AnimationMeta.Presentation;
using Editors.AnimationMeta.SuperView;
using Microsoft.Xna.Framework;
using Shared.Core.ModAssistant;
using Shared.Core.Settings;
using Shared.Core.ToolCreation;
using Shared.GameFormats.AnimationMeta.Parsing;
using static Shared.Core.ModAssistant.AssistantJson;

namespace Editors.AnimationMeta;

public sealed class MetaAssistantAdapter(MetaDataFileParser parser, IMetaDataDatabase database, ApplicationSettingsService settings) : IAssistantEditorAdapter
{
    public IEnumerable<AssistantOperation> GetOperations(IEditorInterface editor, AssistantIds ids)
    {
        var documents = editor switch
        {
            SuperViewViewModel super => new[] { ("animation_meta", super.MetaEditor), ("persistent_meta", super.PersistentMetaEditor) },
            MetaDataEditorViewModel meta => [("metadata", meta)],
            _ => [],
        };
        foreach (var (role, vm) in documents)
        {
            JsonNode State() => Node(new { owner = ids.Get(editor), role, document = ids.Get(vm), file = vm.CurrentFile?.Name,
                vm.MetaDataFileVersion, vm.HasUnsavedChanges, loaded = vm.ParsedFile != null,
                sha256 = vm.ParsedFile == null ? null : Convert.ToHexString(SHA256.HashData(vm.AssistantBytes())),
                selected = vm.SelectedAttribute == null ? null : ids.Get(vm.SelectedAttribute),
                event_count = vm.Tags.Count, invalid_fields = vm.Tags.SelectMany(t => t.Variables).Count(f => !f.IsValid) });
            string? Unavailable() => vm.ParsedFile == null ? Text("TargetGone") : vm.Tags.SelectMany(t => t.Variables).Any(v => !v.IsValid) ? Text("InvalidDocument") : null;
            AssistantOperation Op(string name, string label, JsonObject schema, AssistantEffect effect, Func<JsonObject, JsonNode> run) =>
                AssistantOperation.Sync(name, ids.Get(vm), Text(label), schema, effect, Text(effect == AssistantEffect.Edit ? "GuardedUndo" : "NoDataChange"), State, run, Unavailable);
            MetaDataEntry Event(string id) => vm.Tags.SingleOrDefault(t => ids.Get(t._input) == id) ?? throw new ArgumentException(Text("TargetGone"));
            yield return Op("meta.events", "MetaEvents", Schema(("offset", Num(0, 100000, integer: true), false), ("query", Str(), false)), AssistantEffect.Query,
                args => Node(new { role, events = vm.Tags.Where(t => t.DisplayName.Contains(args["query"]?.GetValue<string>() ?? "", StringComparison.OrdinalIgnoreCase))
                    .Skip(args.Integer("offset")).Take(30).Select(t => new { id = ids.Get(t._input), t.DisplayName, t.Version, t.IsDecodedCorrectly,
                        fields = t.Variables.Select(f => new { name = f.PropertyName, f.FieldName, f.Description, f.IsReadOnly, type = f.AssistantType,
                            value = FieldValue(f), unit = f.IsStartTime || f.IsEndTime ? "seconds" : f is OrientationAttributeViewModel ? "degrees" : "native",
                            choices = (f as ChoiceAttributeViewModel)?.Choices.Select(c => new { c.ValueAsString, c.DisplayName }) }) }) }));
            yield return Op("meta.select", "Select", Schema(("event", Str(), true)), AssistantEffect.Preview,
                args => { vm.SelectedTag = Event(args.String("event")); return State(); });
            yield return Op("meta.fields.set", "MetaSet", Schema(("changes", Array(Schema(("event", Str(), true), ("field", Str(), true),
                ("text", Str(), false), ("x", Num(-1000000, 1000000), false), ("y", Num(-1000000, 1000000), false), ("z", Num(-1000000, 1000000), false)), 100), true)), AssistantEffect.Edit, args =>
            {
                var changes = ((JsonArray)args["changes"]!).Cast<JsonObject>().Select(c => (Field: Event(c.String("event")).Variables.SingleOrDefault(f => f.PropertyName == c.String("field"))
                    ?? throw new ArgumentException(Text("InvalidParameters")), Value: c)).ToArray();
                if (changes.Select(c => c.Field).Distinct().Count() != changes.Length) throw new ArgumentException(Text("InvalidParameters"));
                foreach (var (field, value) in changes)
                {
                    if (field.IsReadOnly) throw new ArgumentException(Text("ReadOnly"));
                    if (field is VectorAttributeViewModel or OrientationAttributeViewModel)
                    {
                        if (value["text"] != null || new[] { "x", "y", "z" }.Any(k => value[k] == null)) throw new ArgumentException(Text("InvalidParameters"));
                    }
                    else if (value["text"] == null || new[] { "x", "y", "z" }.Any(k => value[k] != null) || !field.AcceptsAssistantText(value.String("text")))
                        throw new ArgumentException(Text("InvalidParameters") + ": " + field.PropertyName);
                }
                vm.AssistantHistory.Apply(() =>
                {
                    foreach (var (field, value) in changes)
                    {
                        var vector = new Vector3((float)value.Number("x"), (float)value.Number("y"), (float)value.Number("z"));
                        switch (field)
                        {
                            case VectorAttributeViewModel v: v.Value.Set(vector); break;
                            case OrientationAttributeViewModel o: o.Value.Set(vector); break;
                            default: field.ValueAsString = value.String("text"); field.RefreshFromTarget(); break;
                        }
                    }
                    parser.ParseFile(vm.AssistantBytes());
                });
                return Node(new { state = State(), changed = changes.Select(c => new { c.Field.PropertyName, value = FieldValue(c.Field) }) });
            });
            yield return Op("meta.types", "MetaTypes", Schema(), AssistantEffect.Query, _ => Node(AnimationMetaTagCatalog.FilterForGame(settings.CurrentSettings.CurrentGame, database.GetSupportedTypes(), database.GetDefinition).ToArray()));
            yield return Op("meta.add", "MetaAdd", Schema(("type", Str(), true)), AssistantEffect.Edit, args =>
            {
                var type = args.String("type");
                if (!AnimationMetaTagCatalog.FilterForGame(settings.CurrentSettings.CurrentGame, database.GetSupportedTypes(), database.GetDefinition).Contains(type)) throw new ArgumentException(Text("InvalidParameters"));
                vm.AssistantHistory.Apply(() => NewEntryCommand.Add(vm, parser.CreateDefault(type)));
                return Node(new { state = State(), added_event = ids.Get(vm.SelectedAttribute!) });
            });
            yield return Op("meta.delete", "MetaDelete", Schema(("events", Array(Str(), 100), true)), AssistantEffect.Edit, args =>
            {
                var targets = ((JsonArray)args["events"]!).Select(x => Event(x!.GetValue<string>())).ToArray();
                vm.AssistantHistory.Apply(() =>
                {
                    foreach (var tag in vm.Tags) tag.IsSelected = targets.Contains(tag);
                    vm.SelectedTag = targets[0];
                    new DeleteEntryCommand().Execute(vm);
                });
                return State();
            });
            yield return Op("meta.undo", "Undo", Schema(), AssistantEffect.Edit, _ => { vm.AssistantHistory.Undo(); return State(); });
            yield return Op("meta.redo", "Redo", Schema(), AssistantEffect.Edit, _ => { vm.AssistantHistory.Redo(); return State(); });
            if (!ReferenceEquals(vm, editor))
                yield return Op("meta.save", "SaveEditor", Schema(), AssistantEffect.Disk, _ =>
                { if (!vm.Save()) throw new OperationCanceledException(); return Node(new { saved = true, state = State() }); });
        }
        if (editor is SuperViewViewModel view)
        {
            JsonNode State() => Node(new { view.AnimationMetaFilePath, view.PersistentMetaFilePath, view.ShowAllAnimationMetaPreviews,
                view.ShowAllAnimationMetaForEntireAnimation, view.EnableAllAnimationMeta3D, view.HasUnsavedChanges,
                diagnostics = view.MetaDataDiagnostics.Select(d => d.ToString()).Take(100).ToArray() });
            yield return AssistantOperation.Sync("superview.preview", ids.Get(editor), Text("MetaPreview"), Schema(("visible", Bool(), false),
                ("entire_animation", Bool(), false), ("editable_3d", Bool(), false)), AssistantEffect.Preview, Text("NoDataChange"), State, args =>
            {
                if (args["visible"] != null && view.CanToggleAnimationMetaPreviewVisibility) view.ShowAllAnimationMetaPreviews = args.Boolean("visible");
                if (args["entire_animation"] != null && view.CanToggleAnimationMetaDisplayTime) view.ShowAllAnimationMetaForEntireAnimation = args.Boolean("entire_animation");
                if (args["editable_3d"] != null && view.CanToggleAnimationMeta3D) view.EnableAllAnimationMeta3D = args.Boolean("editable_3d");
                return State();
            });
        }
    }
    private static object FieldValue(AttributeViewModel field) => field switch
    {
        VectorAttributeViewModel v => new { x = v.Value.GetAsVector3().X, y = v.Value.GetAsVector3().Y, z = v.Value.GetAsVector3().Z },
        OrientationAttributeViewModel o => new { x = o.Value.GetAsVector3().X, y = o.Value.GetAsVector3().Y, z = o.Value.GetAsVector3().Z },
        _ => field.ValueAsString,
    };
}
