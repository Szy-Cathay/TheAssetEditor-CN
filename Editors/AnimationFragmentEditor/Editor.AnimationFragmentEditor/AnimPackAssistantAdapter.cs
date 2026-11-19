using System.Runtime.CompilerServices;
using System.Text.Json.Nodes;
using CommonControls.Editors.AnimationPack;
using Shared.Core.ModAssistant;
using Shared.Core.ToolCreation;
using Shared.Ui.Editors.TextEditor;
using static Shared.Core.ModAssistant.AssistantJson;

namespace Editors.AnimationFragmentEditor;

public sealed class AnimPackAssistantAdapter : IAssistantEditorAdapter
{
    private readonly ConditionalWeakTable<SimpleTextEditorViewModel, AssistantUndoJournal<string>> _history = new();
    public IEnumerable<AssistantOperation> GetOperations(IEditorInterface editor, AssistantIds ids)
    {
        if (editor is not AnimPackViewModel vm) yield break;
        JsonNode State() => Node(new { vm.HasUnsavedChanges, vm.HasEditConflict, vm.IsStandaloneCampaign, vm.EditStatusMessage,
            selected = vm.AnimationPackItems.SelectedItem?.FileName, child = vm.SelectedItemViewModel == null ? null : ids.Get(vm.SelectedItemViewModel),
            content_revision = Revision(Node(vm.AssistantCurrentText)), files = vm.AnimationPackItems.PossibleValues.Select(x => new { x.FileName, x.IsUnknownFile, changed = x.IsChanged.Value }),
            table_pending = vm.AssistantHasTableEdits, text_pending = vm.SelectedItemViewModel?.HasUnsavedChanges() });
        string? Editable() => vm.IsSelectedItemUnsupported || vm.AnimationPackItems.SelectedItem == null ? Text("ReadOnly") :
            vm.HasEditConflict ? Text("EditConflict") : vm.AssistantHasTableEdits ? Text("ApplyChildFirst") : null;
        AssistantOperation Op(string name, string label, JsonObject schema, AssistantEffect effect, Func<JsonObject, JsonNode> run, Func<string?>? unavailable = null) =>
            AssistantOperation.Sync(name, ids.Get(editor), Text(label), schema, effect, Text("AnimPackRecovery"), State, run, unavailable);
        yield return Op("animpack.files", "AnimPackFiles", Schema(("offset", Num(0, 1000000, integer: true), false)), AssistantEffect.Query,
            args => Node(vm.AnimationPackItems.PossibleValues.Skip(args.Integer("offset")).Take(100).Select(x => new { x.FileName, x.IsUnknownFile, changed = x.IsChanged.Value })));
        yield return Op("animpack.select", "Select", Schema(("path", Str(), true)), AssistantEffect.Preview, args =>
        {
            var selected = vm.AnimationPackItems.PossibleValues.Single(x => x.FileName == args.String("path"));
            vm.AnimationPackItems.SelectedItem = selected;
            if (!ReferenceEquals(vm.AnimationPackItems.SelectedItem, selected)) throw new OperationCanceledException();
            return State();
        });
        yield return Op("animpack.read", "TextRead", Schema(("offset", Num(0, 10000000, integer: true), false)), AssistantEffect.Query, args =>
        {
            var text = vm.AssistantCurrentText; var offset = Math.Min(args.Integer("offset"), text.Length);
            return Node(new { length = text.Length, offset, text = text.Substring(offset, Math.Min(16000, text.Length - offset)), source = "unsaved_editor" });
        });
        yield return Op("animpack.replace", "TextReplace", Schema(("find", Str(), true), ("replace", Str(), true)), AssistantEffect.Edit, args =>
        {
            var child = vm.SelectedItemViewModel;
            var find = args.String("find"); var replacement = args.String("replace"); var text = child.Text;
            var index = find.Length == 0 ? -1 : text.IndexOf(find, StringComparison.Ordinal);
            if (index < 0 || text.IndexOf(find, index + find.Length, StringComparison.Ordinal) >= 0) throw new ArgumentException(Text("UniqueMatchRequired"));
            var next = text.Remove(index, find.Length).Insert(index, replacement);
            vm.ValidateAssistantText(next);
            var journal = _history.GetValue(child, c => new(() => c.Text, value => c.Text = value, (a, b) => a == b));
            journal.Apply(() => child.Text = next);
            vm.IsTableView = false;
            return State();
        }, Editable);
        yield return Op("animpack.validate", "TextValidate", Schema(), AssistantEffect.Query, _ => { vm.ValidateAssistantText(vm.AssistantCurrentText); return Node(new { valid = true }); },
            () => vm.IsSelectedItemUnsupported ? Text("ReadOnly") : null);
        yield return Op("animpack.apply_child", "ApplyChild", Schema(), AssistantEffect.Edit, _ =>
        { if (!vm.SaveActiveFile()) throw new InvalidOperationException(Text("NotCompleted")); return Node(new { applied_to_memory = true, saved_to_disk = false, state = State() }); },
            () => vm.HasEditConflict ? Text("EditConflict") : null);
        foreach (var redo in new[] { false, true })
            yield return Op(redo ? "animpack.redo" : "animpack.undo", redo ? "Redo" : "Undo", Schema(), AssistantEffect.Edit, _ =>
            {
                if (vm.IsTableView)
                {
                    var command = vm.CampaignEditorVM != null ? redo ? vm.CampaignEditorVM.RedoCommand : vm.CampaignEditorVM.UndoCommand
                        : redo ? vm.TableEditorVM?.RedoCommand : vm.TableEditorVM?.UndoCommand;
                    if (command?.CanExecute(null) != true) throw new InvalidOperationException(Text("NoUndo"));
                    command.Execute(null);
                }
                else
                {
                    if (!_history.TryGetValue(vm.SelectedItemViewModel, out var journal)) throw new InvalidOperationException(Text("NoUndo"));
                    if (redo) journal.Redo(); else journal.Undo();
                }
                return State();
            }, () => vm.HasEditConflict ? Text("EditConflict") : null);
    }
}
