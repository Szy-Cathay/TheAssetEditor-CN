using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Input;
using Shared.Core.Services;
using WindowHandling;
using System.Windows;
using System.Windows.Input;
using Shared.Ui.BaseDialogs.PackFileTree;
using Shared.Ui.BaseDialogs.PackFileTree.ContextMenu;

namespace Shared.Ui.BaseDialogs.StandardDialog.PackFile
{
    public partial class PackFileBrowserWindow : AssetEditorWindow, IDisposable
    {
        public Core.PackFiles.Models.PackFile SelectedFile { get; set; }
        public string SelectedFolder { get; set; }

        public PackFileBrowserViewModel ViewModel { get; set; }
        public Task FilterTask { get; private set; } = Task.CompletedTask;
        private readonly bool _showFoldersOnly;
        private readonly BrowseDialogFilter? _fileFilter;
        private readonly IReadOnlyList<KeyValuePair<string, Core.PackFiles.Models.PackFile>> _filterCandidates;
        private readonly CancellationTokenSource _filterCancellation = new();
        private HashSet<Core.PackFiles.Models.PackFile>? _matchingFiles;
        private bool _isFiltering;
        private bool _disposed;
        private bool CanConfirmSelection => !_isFiltering &&
            (_showFoldersOnly
                ? ViewModel.SelectedItem?.NodeType == NodeType.Directory
                : ViewModel.SelectedItem?.NodeType == NodeType.File &&
                  ViewModel.SelectedItem.Item != null && ViewModel.SelectedItem.IsVisible &&
                  MatchesFileFilter(ViewModel.SelectedItem.Item));

        public PackFileBrowserWindow(PackFileTreeViewFactory packFileBrowserBuilder, List<string>? extensions,
            bool showCaFiles, bool showFoldersOnly, BrowseDialogFilter? fileFilter = null,
            IReadOnlyList<KeyValuePair<string, Core.PackFiles.Models.PackFile>>? filterCandidates = null)
        {
            _showFoldersOnly = showFoldersOnly;
            _fileFilter = fileFilter;
            _filterCandidates = filterCandidates ?? [];
            Create(packFileBrowserBuilder, showCaFiles, showFoldersOnly);

            if (extensions != null)
                ViewModel.Filter.SetExtensions(extensions);

            if (fileFilter != null)
            {
                FileFilterToggle.Content = fileFilter.Description;
                FileFilterToggle.ToolTip = fileFilter.ToolTip;
                FileFilterToggle.IsChecked = true;
                FileFilterToggle.Visibility = Visibility.Visible;
                FileFilterStatus.Visibility = Visibility.Visible;
                FilterProgress.CancelCommand = new RelayCommand(Close);
                _isFiltering = true;
                ViewModel.Filter.SetFileFilter(_ => false);
                FileFilterToggle.IsEnabled = false;
                Loaded += OnFilterLoaded;
                Closed += OnFilterClosed;
            }
        }

        private void OnFilterLoaded(object sender, RoutedEventArgs e)
        {
            Loaded -= OnFilterLoaded;
            FilterTask = BuildFileFilterAsync();
        }

        private void OnFilterClosed(object? sender, EventArgs e) => _filterCancellation.Cancel();

        private async Task BuildFileFilterAsync()
        {
            var token = _filterCancellation.Token;
            FilterProgress.ProgressMaximum = Math.Max(1, _filterCandidates.Count);
            FilterProgress.IsProgressIndeterminate = false;
            FilterProgress.IsOperationActive = true;
            var progress = new Progress<(int Count, string Path)>(value =>
            {
                if (_disposed || token.IsCancellationRequested)
                    return;
                FilterProgress.ProgressValue = value.Count;
                FilterProgress.CurrentDetailText = value.Path;
            });

            try
            {
                _matchingFiles = await Task.Run(() =>
                {
                    var matches = new HashSet<Core.PackFiles.Models.PackFile>();
                    var timer = Stopwatch.StartNew();
                    for (var index = 0; index < _filterCandidates.Count; index++)
                    {
                        token.ThrowIfCancellationRequested();
                        var entry = _filterCandidates[index];
                        if (_fileFilter!.Matches(entry.Value))
                            matches.Add(entry.Value);
                        if (timer.ElapsedMilliseconds >= 100 || index + 1 == _filterCandidates.Count)
                        {
                            ((IProgress<(int Count, string Path)>)progress).Report((index + 1, entry.Key));
                            timer.Restart();
                        }
                    }
                    return matches;
                }, token);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception exception)
            {
                if (_disposed || token.IsCancellationRequested)
                    return;
                Shared.Core.ErrorHandling.Logging.Create<PackFileBrowserWindow>().Error(exception, "File filter failed.");
                FileFilterToggle.IsChecked = false;
                FileFilterStatus.Text = LocalizationManager.Instance.Get("Shared.PackFileBrowserWindow.FilterFailed");
            }
            finally
            {
                if (!_disposed && !token.IsCancellationRequested)
                {
                    await FilterProgress.CompleteAsync();
                    if (!_disposed && !token.IsCancellationRequested)
                    {
                        _isFiltering = false;
                        FileFilterToggle.IsEnabled = _matchingFiles != null;
                        ApplyFileFilter();
                    }
                }
            }
        }

        private bool MatchesFileFilter(Core.PackFiles.Models.PackFile file) =>
            _fileFilter == null || FileFilterToggle.IsChecked != true || _matchingFiles?.Contains(file) == true;

        private void FileFilterChanged(object sender, RoutedEventArgs e)
        {
            if (!_isFiltering && _matchingFiles != null)
                ApplyFileFilter();
        }

        private void ApplyFileFilter()
        {
            ViewModel.Filter.SetFileFilter(FileFilterToggle.IsChecked == true && _matchingFiles != null
                ? _matchingFiles.Contains : null);
            if (ViewModel.SelectedItem?.IsVisible == false)
                ViewModel.SelectedItem = null;
            ConfirmButton.IsEnabled = CanConfirmSelection;
            if (_matchingFiles != null)
                FileFilterStatus.Text = FileFilterToggle.IsChecked != true
                    ? LocalizationManager.Instance.Get("Shared.PackFileBrowserWindow.ShowingAllFiles")
                    : _matchingFiles.Count == 0
                        ? LocalizationManager.Instance.Get("Shared.PackFileBrowserWindow.NoFilterMatches")
                        : string.Format(LocalizationManager.Instance.Get("Shared.PackFileBrowserWindow.FilterMatchCount"),
                            _matchingFiles.Count);
        }

        void Create(PackFileTreeViewFactory packFileBrowserBuilder, bool showCaFiles, bool showFoldersOnly)
        {
            ViewModel = packFileBrowserBuilder.Create(ContextMenuType.None, showCaFiles, showFoldersOnly);
            ViewModel.FileOpen += ViewModel_FileOpen;

            InitializeComponent();
            SelectionHint.Text = LocalizationManager.Instance.Get(_showFoldersOnly
                ? "Shared.PackFileBrowserWindow.SelectFolderHint" : "Shared.PackFileBrowserWindow.SelectFileHint");
            ConfirmButton.IsEnabled = CanConfirmSelection;
            ViewModel.PropertyChanged += OnSelectionChanged;
            ViewModel.Filter.Changed += OnFilterChanged;
            DataContext = this;
            PreviewKeyDown += HandleEsc;
        }

        public new bool ShowDialog() => (this as Window).ShowDialog() == true && (SelectedFile != null || SelectedFolder != null);

        private void OnSelectionChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(ViewModel.SelectedItem))
                ConfirmButton.IsEnabled = CanConfirmSelection;
        }

        private void OnFilterChanged(object? sender, EventArgs e)
        {
            if (ViewModel.SelectedItem?.IsVisible == false)
                ViewModel.SelectedItem = null;
            ConfirmButton.IsEnabled = CanConfirmSelection;
        }

        private void HandleEsc(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
                Close();
        }

        private void ViewModel_FileOpen(Core.PackFiles.Models.PackFile file)
        {
            if (_isFiltering || !MatchesFileFilter(file))
                return;
            SelectedFile = file;
            if (DialogResult != true)
                DialogResult = true;
            Close();
        }

        private void Button_Click(object sender, RoutedEventArgs e)
        {
            if (!CanConfirmSelection)
                return;
            SelectedFile = ViewModel.SelectedItem?.Item;

            if (ViewModel.SelectedItem?.NodeType == NodeType.Directory)
                SelectedFolder = GetFolderPath(ViewModel.SelectedItem, ViewModel.SelectedItem?.Name);

            DialogResult = true;
            Close();
        }

        private static string GetFolderPath(TreeNode node, string folderPath)
        {
            if (node.Parent?.NodeType == NodeType.Root)
                return folderPath;
            else
            {
                folderPath = $"{node.Parent.Name}\\{folderPath}";
                return GetFolderPath(node.Parent, folderPath);
            }
        }

        public new void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;
            _filterCancellation.Cancel();
            _filterCancellation.Dispose();
            Loaded -= OnFilterLoaded;
            Closed -= OnFilterClosed;
            base.Dispose();
            ViewModel.PropertyChanged -= OnSelectionChanged;
            ViewModel.Filter.Changed -= OnFilterChanged;
            PreviewKeyDown -= HandleEsc;
            ViewModel.FileOpen -= ViewModel_FileOpen;
            ViewModel.Dispose();
            ViewModel = null;
            DataContext = null;
        }
    }
}
