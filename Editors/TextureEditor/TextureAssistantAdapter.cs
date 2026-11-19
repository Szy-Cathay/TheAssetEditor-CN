using System.Collections.Generic;
using System.Text.Json.Nodes;
using Editors.TextureEditor.ViewModels;
using Shared.Core.ModAssistant;
using Shared.Core.ToolCreation;
using static Shared.Core.ModAssistant.AssistantJson;

namespace Editors.TextureEditor;

public sealed class TextureAssistantAdapter : IAssistantEditorAdapter
{
    public IEnumerable<AssistantOperation> GetOperations(IEditorInterface editor, AssistantIds ids)
    {
        if (editor is not TextureEditorViewModel vm) yield break;
        var preview = vm.ViewModel;
        JsonNode State() => Node(new { path = preview.ImagePath.Value, width = preview.Width.Value, height = preview.Height.Value,
            format = preview.Format.Value, mipmaps = preview.NumMipMaps.Value, readonly_document = true,
            channels = new { rgba = preview.FormatRgbaCheckbox, r = preview.FormatRCheckbox, g = preview.FormatGCheckbox, b = preview.FormatBCheckbox, a = preview.FormatACheckbox } });
        yield return AssistantOperation.Sync("texture.info", ids.Get(editor), Text("TextureInfo"), Schema(), AssistantEffect.Query, Text("NoDataChange"), State, _ => State());
        yield return AssistantOperation.Sync("texture.channel", ids.Get(editor), Text("TextureChannel"), Schema(("channel", Str("", "rgba", "r", "g", "b", "a"), true)),
            AssistantEffect.Preview, Text("NoDataChange"), State, args =>
            {
                var channel = args.String("channel");
                preview.FormatRgbaCheckbox = channel == "rgba"; preview.FormatRCheckbox = channel == "r";
                preview.FormatGCheckbox = channel == "g"; preview.FormatBCheckbox = channel == "b"; preview.FormatACheckbox = channel == "a";
                return State();
            });
    }
}
