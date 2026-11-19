using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Shared.Core.ModAssistant;
using Shared.Core.ToolCreation;
using static Shared.Core.ModAssistant.AssistantJson;

namespace Shared.Ui.Editors.TextEditor;

public interface IAssistantTextDocument
{
    string Text { get; }
    bool AssistantHistoryAvailable { get; }
    bool CanUndo { get; }
    bool CanRedo { get; }
    void Replace(int offset, int length, string replacement);
    void ValidateText(string text);
    void Undo();
    void Redo();
}

public sealed class TextAssistantAdapter : IAssistantEditorAdapter
{
    public IEnumerable<AssistantOperation> GetOperations(IEditorInterface editor, AssistantIds ids)
    {
        if (editor is not IAssistantTextDocument document) yield break;
        JsonNode State() => Node(new { length = document.Text.Length, sha256 = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(document.Text))),
            dirty = (editor as ISaveableEditor)?.HasUnsavedChanges, can_undo = document.CanUndo, can_redo = document.CanRedo, source = "unsaved_editor" });
        AssistantOperation Op(string name, string description, JsonObject schema, AssistantEffect effect, Func<JsonObject, JsonNode> execute, Func<string?>? unavailable = null) =>
            AssistantOperation.Sync(name, ids.Get(editor), Text(description), schema, effect, Text("EditorRecovery"), State, execute, unavailable);
        yield return Op("text.read", "TextRead", Schema(("offset", Num(0, int.MaxValue, integer: true), false), ("length", Num(1, 16000, integer: true), false)), AssistantEffect.Query, args =>
        {
            var offset = Math.Min(document.Text.Length, args.Integer("offset"));
            var length = Math.Min(document.Text.Length - offset, args.Integer("length", 8000));
            return Node(new { text = document.Text.Substring(offset, length), offset, length, total = document.Text.Length, source = "unsaved_editor" });
        });
        yield return Op("text.validate", "TextValidate", Schema(), AssistantEffect.Query, _ => { document.ValidateText(document.Text); return Node(new { valid = true }); });
        yield return Op("text.replace", "TextReplace", Schema(("old_text", Str(), true), ("new_text", Str(), true)), AssistantEffect.Edit, args =>
        {
            var old = args.String("old_text");
            var replacement = args.String("new_text");
            var offset = document.Text.IndexOf(old, StringComparison.Ordinal);
            if (old.Length == 0 || offset < 0 || document.Text.IndexOf(old, offset + old.Length, StringComparison.Ordinal) >= 0)
                throw new ArgumentException(Text("AmbiguousText"));
            document.Replace(offset, old.Length, replacement);
            return Node(new { before = old, after = replacement, offset, saved = false });
        }, () => document.AssistantHistoryAvailable ? null : Text("ViewNotReady"));
        yield return Op("text.undo", "Undo", Schema(), AssistantEffect.Edit, _ => { document.Undo(); return State(); }, () => document.CanUndo ? null : Text("NoUndo"));
        yield return Op("text.redo", "Redo", Schema(), AssistantEffect.Edit, _ => { document.Redo(); return State(); }, () => document.CanRedo ? null : Text("NoRedo"));
    }
}
