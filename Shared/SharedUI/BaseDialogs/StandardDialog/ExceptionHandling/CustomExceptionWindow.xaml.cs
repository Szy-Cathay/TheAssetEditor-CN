using System;
using System.Linq;
using CommonControls;
using System.Text;
using System.Text.Json;
using System.Windows;
using Shared.Core.DependencyInjection;
using Shared.Core.ErrorHandling.Exceptions;
using Shared.Core.Events;
using Shared.Core.Events.Global;
using Shared.Core.Services;

namespace Shared.Ui.Common.Exceptions
{
    public partial class CustomExceptionWindow : Window
    {
        private readonly ExceptionInformation _extendedExceptionInformation;
        private readonly IStandardDialogs _standardDialogs;
        private readonly IEventHub _eventHub;
        private readonly ScopeToken _scopeToken;
        private readonly IScopeRepository _scopeRepository;

        public CustomExceptionWindow(ExceptionInformation extendedExceptionInformation, IStandardDialogs standardDialogs, IEventHub eventHub, ScopeToken scopeToken, IScopeRepository scopeRepository)
        {
            InitializeComponent();
            DarkTitleBarHelper.Enable(this);
            _extendedExceptionInformation = extendedExceptionInformation;
            _standardDialogs = standardDialogs;
            _eventHub = eventHub;
            _scopeToken = scopeToken;
            _scopeRepository = scopeRepository;
            var allMessages = extendedExceptionInformation.ExceptionInfo.Select(x => x.Message).ToList();

            ErrorTextHandle.Text = string.Empty; 
            if (string.IsNullOrWhiteSpace(extendedExceptionInformation.UserMessage) == false)
            {
                ErrorTextHandle.Text += GetText(
                    "Shared.CustomException.UserInformationLabel") + "\n";
                ErrorTextHandle.Text += extendedExceptionInformation.UserMessage + "\n\n";
            }

            ErrorTextHandle.Text += string.Join("\n", allMessages);

            var lastStackFrame = extendedExceptionInformation.ExceptionInfo.LastOrDefault();
            if (lastStackFrame != null && lastStackFrame.StackTrace.Length != 0)
            {
                ErrorTextHandle.Text += "\n\n" + GetText(
                    "Shared.CustomException.StackTraceLabel") + "\n";
                ErrorTextHandle.Text += string.Join("\n", lastStackFrame.StackTrace);
            }

            var editorName = "";
            if (string.IsNullOrWhiteSpace(extendedExceptionInformation.CurrentEditorName) == false)
            {
                editorName = extendedExceptionInformation.CurrentEditorName + " : ";
                if (editorName.Contains("ViewModel", StringComparison.InvariantCultureIgnoreCase))
                    editorName = editorName.Replace("ViewModel", "", StringComparison.InvariantCultureIgnoreCase);
            }
            Title = string.Format(
                GetText("Shared.CustomException.WindowTitleFormat"),
                editorName,
                extendedExceptionInformation.AssetEditorVersion,
                extendedExceptionInformation.CurrentGame);

            var extraInfo = new StringBuilder();
            extraInfo.AppendLine(GetText(
                "Shared.CustomException.PackedFilesLabel"));
            foreach (var item in extendedExceptionInformation.ActivePackFiles)
                extraInfo.AppendLine($"\t'{item.Name}' @ '{item.SystemPath}' IsCa:{item.IsCa} IsMain:{item.IsMainEditable}");

            extraInfo.AppendLine(string.Format(
                GetText("Shared.CustomException.RuntimeFormat"),
                extendedExceptionInformation.RunTimeInSeconds));
            extraInfo.AppendLine(string.Format(
                GetText("Shared.CustomException.OsVersionFormat"),
                extendedExceptionInformation.OSVersion));
            extraInfo.AppendLine(string.Format(
                GetText("Shared.CustomException.CultureFormat"),
                extendedExceptionInformation.Culture));
            extraInfo.AppendLine(string.Format(
                GetText("Shared.CustomException.OpenEditorsFormat"),
                extendedExceptionInformation.NumberOfOpenEditors));
            extraInfo.AppendLine(string.Format(
                GetText("Shared.CustomException.CreatedEditorsFormat"),
                extendedExceptionInformation.NumberOfOpenedEditors));

            ExtraInfoHandle.Text = extraInfo.ToString();
        }

        private void CopyButtonPressed(object sender, RoutedEventArgs e)
        {
            var options = new JsonSerializerOptions()
            {
                WriteIndented = true,
            };
            var text = JsonSerializer.Serialize(_extendedExceptionInformation, options);
            Clipboard.SetText(text);
            _standardDialogs.ShowDialogBox(
                LocalizationManager.Instance.Get(
                    "Msg.ErrorCopiedToClipboard"),
                GetText("Shared.CustomException.Title"));
        }

        private void CloseButtonPressed(object sender, RoutedEventArgs e) => Close();

        private void ForceCloseButtonPressed(object sender, RoutedEventArgs e)
        {
            var result = _standardDialogs.ShowYesNoBox(
                GetText("Shared.CustomException.ForceCloseConfirm"),
                GetText("Shared.CustomException.ForceCloseTitle"));
            if (result == ShowMessageBoxResult.Cancel)
                return;

            var editorHandle = _scopeRepository.GetEditorFromToken(_scopeToken);
            if (editorHandle == null)
            {
                _standardDialogs.ShowDialogBox(
                    GetText("Shared.CustomException.ForceCloseFailed"),
                    GetText("Shared.CustomException.Title"));
                return;
            }

            _eventHub.PublishGlobalEvent(new ForceShutdownEvent(editorHandle));
        }

        private static string GetText(string key) =>
            LocalizationManager.Instance.Get(key);
    }
}
