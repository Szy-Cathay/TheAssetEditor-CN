using System.Text.Json.Nodes;
using Editors.Twui.Editor;
using Shared.Core.ModAssistant;
using Shared.Core.ToolCreation;
using static Shared.Core.ModAssistant.AssistantJson;

namespace Editors.Twui;

public sealed class TwuiAssistantAdapter : IAssistantEditorAdapter
{
    private static IEnumerable<TwuiComponent> Flatten(IEnumerable<TwuiComponent> roots)
    {
        foreach (var item in roots)
        {
            yield return item;
            foreach (var child in Flatten(item.Children ?? [])) yield return child;
        }
    }
    public IEnumerable<AssistantOperation> GetOperations(IEditorInterface editor, AssistantIds ids)
    {
        if (editor is not TwuiEditor vm) yield break;
        JsonNode State() => Node(new { file = vm.ParsedTwuiFile?.FileName, readonly_document = true, limitation = Text("TwuiReadOnly"),
            selected = vm.ComponentManager.SelectedComponent == null ? null : ids.Get(vm.ComponentManager.SelectedComponent),
            components = Flatten(vm.ParsedTwuiFile?.Componenets ?? []).Select(c => new { id = ids.Get(c), c.ShowInPreviewRenderer, state = c.CurrentState?.Id }) });
        yield return AssistantOperation.Sync("twui.components", ids.Get(editor), Text("TwuiComponents"), Schema(("offset", Num(0, 1000000, integer: true), false), ("query", Str(), false)),
            AssistantEffect.Query, Text("NoDataChange"), State, args => Node(Flatten(vm.ParsedTwuiFile?.Componenets ?? [])
                .Where(c => c.Name.Contains(args["query"]?.GetValue<string>() ?? "", StringComparison.OrdinalIgnoreCase)).Skip(args.Integer("offset")).Take(50)
                .Select(c => new { id = ids.Get(c), c.Name, c.Id, c.Priority, c.TemplateName, c.ShowInPreviewRenderer,
                    dimensions = new[] { c.Location.Dimensions.X, c.Location.Dimensions.Y },
                    states = c.States.Select(s => new { s.Id, s.Name, s.Width, s.Height, textures = s.ImageList.Select(i => i.Path) }) })));
        yield return AssistantOperation.Sync("twui.preview", ids.Get(editor), Text("TwuiPreview"), Schema(("component", Str(), true), ("state", Str(), false), ("visible", Bool(), false)),
            AssistantEffect.Preview, Text("NoDataChange"), State, args =>
            {
                var component = Flatten(vm.ParsedTwuiFile?.Componenets ?? []).SingleOrDefault(c => ids.Get(c) == args.String("component")) ?? throw new ArgumentException(Text("TargetGone"));
                var state = args["state"] == null ? null : component.States.SingleOrDefault(s => s.Id == args.String("state")) ?? throw new ArgumentException(Text("InvalidParameters"));
                vm.ComponentManager.SelectedComponent = component;
                if (state != null) component.CurrentState = state;
                if (args["visible"] != null && component.ShowInPreviewRenderer != args.Boolean("visible")) vm.ComponentManager.ToggleSelectedCommand.Execute(null);
                return State();
            });
    }
}
