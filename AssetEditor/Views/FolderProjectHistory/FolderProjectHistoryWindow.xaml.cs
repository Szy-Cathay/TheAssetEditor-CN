using System;
using WindowHandling;

namespace AssetEditor.Views.FolderProjectHistory;

public partial class FolderProjectHistoryWindow : AssetEditorWindow
{
    public FolderProjectHistoryWindow()
    {
        InitializeComponent();
    }

    protected override void OnClosed(EventArgs e)
    {
        Dispose();
        base.OnClosed(e);
    }
}
