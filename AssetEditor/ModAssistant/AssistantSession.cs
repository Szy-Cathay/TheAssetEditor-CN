using System.Collections.ObjectModel;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using Shared.Core.ModAssistant;

namespace AssetEditor.ModAssistant;

public partial class AssistantSession : ObservableObject
{
    internal AssistantConversation Conversation { get; }
    public ObservableCollection<AssistantMessage> Messages { get; } = [];
    [ObservableProperty] private string _title = AssistantJson.Text("NewConversation");
    internal string Draft { get; set; } = "";
    internal string? ImageData { get; set; }
    internal BitmapSource? Attachment { get; set; }
    internal string AttachmentName { get; set; } = "";
    internal string Usage { get; set; } = "";
    internal string Status { get; set; } = AssistantJson.Text("Ready");

    internal AssistantSession(IAssistantModelClient client, IAssistantWorkspace workspace)
    {
        Conversation = new AssistantConversation(client, workspace);
    }
}

public partial class AssistantTextPart : ObservableObject
{
    [ObservableProperty] private string _text = "";
}

public partial class AssistantReasoningPart : ObservableObject
{
    [ObservableProperty] private string _text = "";
}
