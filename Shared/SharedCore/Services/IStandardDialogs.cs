using Shared.Core.ErrorHandling;
using Shared.Core.PackFiles;
using Shared.Core.PackFiles.Models;

namespace Shared.Core.Services
{
    public interface IStandardDialogs
    {
        SaveDialogResult DisplaySaveDialog(IPackFileService pfs, List<string> extensions);
        BrowseDialogResultFile DisplayBrowseDialog(List<string> extensions);
        BrowseDialogResultFolder DisplayBrowseFolderDialog(
            PackFileContainer? container = null);

        void ShowExceptionWindow(Exception e, string userInfo = "");
        void ShowErrorViewDialog(string title, ErrorList errorItems, bool modal = true);

        TextInputDialogResult ShowTextInputDialog(string title, string initialText = "");
        TitleDescriptionInputDialogResult ShowTitleDescriptionInputDialog(
            string title,
            string titleLabel,
            string descriptionLabel,
            string initialTitle = "",
            string initialDescription = "");
        void ShowDialogBox(string message, string title = "Error");
        void ShowDialogBox(
            string message,
            string title,
            UiMessageBoxIcon image);
        ShowMessageBoxResult ShowYesNoBox(string message, string title);
    }

    public record SaveDialogResult(bool Result, PackFile? SelectedPackFile, string? SelectedFilePath);
    public record BrowseDialogResultFile(bool Result, PackFile File);
    public record BrowseDialogResultFolder(bool Result, string Folder);
    public record TextInputDialogResult(bool Result, string Text);
    public record TitleDescriptionInputDialogResult(
        bool Result,
        string Title,
        string Description);

    public enum ShowMessageBoxResult
    {
        OK,
        Cancel,
    }
}
