using Shared.Core.Services;
using Shared.Core.ToolCreation;
using Shared.Ui.BaseDialogs.PackFileTree;
using Shared.Ui.BaseDialogs.PackFileTree.ContextMenu.External;

namespace Editors.AnimationVisualEditors.ContextMenu;

public sealed class OpenAnimationWorkbenchCommand(
    IEditorCreator editorCreator) : IOpenAnimationWorkbenchCommand
{
    public string GetDisplayName(TreeNode node) =>
        LocalizationManager.Instance.Get(
            "ContextMenu.OpenAnimationWorkbench");

    public bool IsEnabled(TreeNode node) =>
        node.NodeType == NodeType.File &&
        node.Item != null &&
        node.Name.EndsWith(".anim", StringComparison.OrdinalIgnoreCase);

    public void Execute(TreeNode node)
    {
        if (!IsEnabled(node))
            return;

        var animation = node.Item!;
        editorCreator.Create(
            EditorEnums.AnimationKeyFrame_Editor,
            editor => ((IFileEditor)editor).LoadFile(animation));
    }
}
