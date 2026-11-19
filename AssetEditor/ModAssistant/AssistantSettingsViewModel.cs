using System;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Shared.Core.ModAssistant;

namespace AssetEditor.ModAssistant;

public partial class AssistantSettingsViewModel(IAssistantSecretStore secrets) : ObservableObject
{
    [ObservableProperty] private bool _hasKey = secrets.HasKey;
    [ObservableProperty] private string _status = secrets.HasKey ? AssistantJson.Text("KeyConfigured") : AssistantJson.Text("MissingKey");
    public void SaveKey(string key)
    {
        try { secrets.Save(key); HasKey = true; Status = AssistantJson.Text("KeyConfigured"); }
        catch (Exception) { Status = AssistantJson.Text("KeyStoreError"); }
    }
    [RelayCommand]
    private void ClearKey()
    {
        try { secrets.Clear(); HasKey = false; Status = AssistantJson.Text("MissingKey"); }
        catch (Exception) { Status = AssistantJson.Text("KeyStoreError"); }
    }
}
