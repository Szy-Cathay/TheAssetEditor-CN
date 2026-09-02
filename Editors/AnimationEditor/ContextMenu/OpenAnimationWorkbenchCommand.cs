using Shared.Core.Services;
using Shared.Core.Settings;
using Shared.Core.ToolCreation;
using Shared.Ui.BaseDialogs.PackFileTree;
using Shared.Ui.BaseDialogs.PackFileTree.ContextMenu.External;

namespace Editors.AnimationVisualEditors.ContextMenu;

public sealed class OpenAnimationWorkbenchCommand(
    IEditorCreator editorCreator,
    ApplicationSettingsService settings) : IOpenAnimationWorkbenchCommand
{
    public string GetDisplayName(TreeNode node) =>
        LocalizationManager.Instance.Get(
            "ContextMenu.OpenAnimationWorkbench");

    public bool IsEnabled(TreeNode node) =>
        settings.CurrentSettings.CurrentGame == GameTypeEnum.Warhammer3 &&
        node.NodeType == NodeType.File &&
        node.Item != null &&
        node.Name.EndsWith(
            ".rigid_model_v2",
            StringComparison.OrdinalIgnoreCase);

    public void Execute(TreeNode node)
    {
        if (!IsEnabled(node))
            return;

        var model = node.Item!;
        editorCreator.Create(
            EditorEnums.AnimationKeyFrame_Editor,
            editor => ((IFileEditor)editor).LoadFile(model));
    }
}
