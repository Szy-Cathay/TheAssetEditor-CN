using System;
using System.Globalization;
using System.Windows.Controls;
using System.Windows.Data;
using Shared.Core.PackFiles.Models;
using Shared.Core.Services;

namespace AssetEditor.Views.FolderProjectHistory;

public partial class FolderProjectHistoryView : UserControl
{
    public FolderProjectHistoryView()
    {
        InitializeComponent();
    }
}

public sealed class HistoryChangeKindConverter : IValueConverter
{
    public object Convert(
        object value,
        Type targetType,
        object parameter,
        CultureInfo culture)
    {
        var key = value switch
        {
            FolderProjectUnrecordedChangeKind kind =>
                GetUnrecordedChangeKey(kind),
            FolderProjectRestorePointChangeKind kind => kind.ToString(),
            _ => "Modified",
        };
        return LocalizationManager.Instance.Get(
            $"FolderProject.History.ChangeKind.{key}");
    }

    public object ConvertBack(
        object value,
        Type targetType,
        object parameter,
        CultureInfo culture) =>
        Binding.DoNothing;

    private static string GetUnrecordedChangeKey(
        FolderProjectUnrecordedChangeKind kind)
    {
        if (kind.HasFlag(FolderProjectUnrecordedChangeKind.Unreadable))
            return nameof(FolderProjectUnrecordedChangeKind.Unreadable);
        if (kind.HasFlag(FolderProjectUnrecordedChangeKind.Conflicted))
            return nameof(FolderProjectUnrecordedChangeKind.Conflicted);
        if (kind.HasFlag(FolderProjectUnrecordedChangeKind.Renamed))
            return nameof(FolderProjectUnrecordedChangeKind.Renamed);
        if (kind.HasFlag(FolderProjectUnrecordedChangeKind.Deleted))
            return nameof(FolderProjectUnrecordedChangeKind.Deleted);
        if (kind.HasFlag(FolderProjectUnrecordedChangeKind.Added))
            return nameof(FolderProjectUnrecordedChangeKind.Added);
        if (kind.HasFlag(FolderProjectUnrecordedChangeKind.TypeChanged))
            return nameof(FolderProjectUnrecordedChangeKind.TypeChanged);
        return nameof(FolderProjectUnrecordedChangeKind.Modified);
    }
}

public sealed class RestorePointSummaryConverter : IValueConverter
{
    public object Convert(
        object value,
        Type targetType,
        object parameter,
        CultureInfo culture)
    {
        if (value is not FolderProjectRestorePoint restorePoint ||
            restorePoint.ChangeSummary == null)
        {
            return LocalizationManager.Instance.Get(
                "FolderProject.History.ChangeSummaryPending");
        }

        return LocalizationManager.Instance.GetFormat(
            "FolderProject.History.ChangeSummary",
            restorePoint.ChangeSummary.Total);
    }

    public object ConvertBack(
        object value,
        Type targetType,
        object parameter,
        CultureInfo culture) =>
        Binding.DoNothing;
}
