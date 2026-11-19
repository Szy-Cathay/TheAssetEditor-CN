using Shared.Ui.BaseDialogs.PackFileTree;

namespace AssetEditor.ModAssistant;

// The main resource tree outlives individual tool scopes.
public sealed class AssistantSelectionContext
{
    public PackFileBrowserViewModel? Browser { get; set; }
}
