using System;
using System.Collections.Generic;
using System.Windows;
using CommonControls.BaseDialogs;
using CommonControls.BaseDialogs.ErrorListDialog;
using Shared.Core.DependencyInjection;
using Shared.Core.ErrorHandling;
using Shared.Core.ErrorHandling.Exceptions;
using Shared.Core.Events;
using Shared.Core.PackFiles;
using Shared.Core.PackFiles.Models;
using Shared.Core.Services;
using Shared.Ui.BaseDialogs.PackFileTree;
using Shared.Ui.BaseDialogs.StandardDialog.PackFile;
using Shared.Ui.Common.Exceptions;

namespace Shared.Ui.BaseDialogs.StandardDialog
{
    public class StandardDialogs : IStandardDialogs
    {
        private readonly IPackFileService _pfs;
        private readonly PackFileTreeViewFactory _packFileBrowserBuilder;
        private readonly IExceptionService _exceptionService;
        private readonly IScopeRepository _scopeRepository;
        private readonly IEventHub _eventHub;
        private readonly ScopeToken _scopeToken;

        public StandardDialogs(IPackFileService pfs, PackFileTreeViewFactory packFileBrowserBuilder, IExceptionService exceptionService, IScopeRepository scopeRepository, IEventHub eventHub, ScopeToken scopeToken)
        {
            _pfs = pfs;
            _packFileBrowserBuilder = packFileBrowserBuilder;
            _exceptionService = exceptionService;
            _scopeRepository = scopeRepository;
            _eventHub = eventHub;
            _scopeToken = scopeToken;
        }

        public SaveDialogResult DisplaySaveDialog(IPackFileService remove, List<string> extensions)
        {
            using var browser = new SavePackFileWindow(_pfs, _packFileBrowserBuilder);
            browser.ViewModel.Filter.SetExtensions(extensions);
            ApplyOwner(browser);

            if (browser.ShowDialog() == true)
                return new SaveDialogResult(true, browser.SelectedFile, browser.FilePath);

            return new SaveDialogResult(false, null, null);
        }

        public BrowseDialogResultFile DisplayBrowseDialog(List<string> extensions)
        {
            using var browser = new PackFileBrowserWindow(_packFileBrowserBuilder, extensions, showCaFiles: true, showFoldersOnly: false);
            ApplyOwner(browser);

            var saveResult = browser.ShowDialog();
            var output = new BrowseDialogResultFile(saveResult, browser.SelectedFile);
            return output;
        }

        public BrowseDialogResultFolder DisplayBrowseFolderDialog(
            PackFileContainer? container = null)
        {
            using var browser = new PackFileBrowserWindow(_packFileBrowserBuilder, null, showCaFiles: false, showFoldersOnly: true);
            if (container != null)
            {
                for (var index = browser.ViewModel.Files.Count - 1;
                     index >= 0;
                     index--)
                {
                    if (!ReferenceEquals(
                            browser.ViewModel.Files[index].FileOwner,
                            container))
                    {
                        browser.ViewModel.Files.RemoveAt(index);
                    }
                }
            }
            ApplyOwner(browser);

            var saveResult = browser.ShowDialog();
            var output = new BrowseDialogResultFolder(saveResult, browser.SelectedFolder);
            return output;
        }

        public void ShowExceptionWindow(Exception e, string userInfo = "")
        {
            var extendedException =
                _exceptionService.Create(e, userInfo);
            var errorWindow = new CustomExceptionWindow(extendedException, this, _eventHub, _scopeToken, _scopeRepository);
            ShowOwnedDialog(errorWindow);
        }

        public void ShowErrorViewDialog(string title, ErrorList errorItems, bool modal = true)
        {
            ErrorListWindow.ShowDialog(title, errorItems, modal);
        }

        public TextInputDialogResult ShowTextInputDialog(string title, string initialText = "")
        {
            var window = new TextInputWindow(title, initialText);
            var result = ShowOwnedDialog(window);

            return new TextInputDialogResult(result!.Value, window.TextValue);
        }

        public TitleDescriptionInputDialogResult ShowTitleDescriptionInputDialog(
            string title,
            string titleLabel,
            string descriptionLabel,
            string initialTitle = "",
            string initialDescription = "")
        {
            var window = new TitleDescriptionInputWindow(
                title,
                titleLabel,
                descriptionLabel,
                initialTitle,
                initialDescription);
            var result = ShowOwnedDialog(window) == true;
            return new TitleDescriptionInputDialogResult(
                result,
                window.TitleValue,
                window.DescriptionValue);
        }

        public void ShowDialogBox(string message, string title)
        {
            ShowDialogBox(message, title, UiMessageBoxIcon.None);
        }

        public void ShowDialogBox(
            string message,
            string title,
            UiMessageBoxIcon image)
        {
            var dialog = new MessageDialogWindow(
                title,
                message,
                MessageDialogButtonSet.Ok,
                image switch
                {
                    UiMessageBoxIcon.Error => MessageBoxImage.Error,
                    UiMessageBoxIcon.Warning => MessageBoxImage.Warning,
                    UiMessageBoxIcon.Question => MessageBoxImage.Question,
                    UiMessageBoxIcon.Information => MessageBoxImage.Information,
                    _ => MessageBoxImage.None,
                });
            ShowOwnedDialog(dialog);
        }

        public ShowMessageBoxResult ShowYesNoBox(string message, string title)
        {
            var dialog = new MessageDialogWindow(
                title,
                message,
                MessageDialogButtonSet.YesNo);
            if (ShowOwnedDialog(dialog) == true)
                return ShowMessageBoxResult.OK;
            return ShowMessageBoxResult.Cancel;
        }

        private static void ApplyOwner(Window window)
        {
            var owner = Application.Current?.MainWindow;
            if (owner != null &&
                owner.IsLoaded &&
                !ReferenceEquals(owner, window) &&
                window.Owner == null)
            {
                window.Owner = owner;
            }
        }

        private static bool? ShowOwnedDialog(Window window)
        {
            ApplyOwner(window);
            return window.ShowDialog();
        }


        // Show Tool?
    }
}
