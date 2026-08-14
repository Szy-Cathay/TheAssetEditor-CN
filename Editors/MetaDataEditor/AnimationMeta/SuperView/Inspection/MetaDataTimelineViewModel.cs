using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Editors.AnimationMeta.SuperView.Visualisation;
using Editors.Shared.Core.Common.BaseControl;
using Shared.Core.Services;
using Shared.GameFormats.AnimationMeta.Parsing;

namespace Editors.AnimationMeta.SuperView.Inspection
{
    public sealed partial class MetaDataTimelineMarkerViewModel :
        ObservableObject
    {
        [ObservableProperty] private bool _isSelected;

        public MetaDataInspectionItem Item { get; }
        public double StartFraction { get; }
        public double EndFraction { get; }
        public string ToolTipText { get; }
        public string AutomationName { get; }
        public IRelayCommand SelectCommand { get; }

        public MetaDataTimelineMarkerViewModel(
            MetaDataInspectionItem item,
            float clipDurationSeconds,
            Action<MetaDataInspectionItem> select)
        {
            Item = item;
            if (item.TimelineMarkerKind ==
                MetaDataTimelineMarkerKind.WholeAnimation)
            {
                StartFraction = 0;
                EndFraction = 1;
            }
            else if (item.AuthoredTimeRange is MetaDataTimeRange timeRange)
            {
                StartFraction = timeRange.StartTime / clipDurationSeconds;
                EndFraction = timeRange.EndTime / clipDurationSeconds;
            }

            var typeName = GetTypeName(item);
            var ownerName = LocalizationManager.Instance.Get(
                item.Owner == MetaDataDocumentOwner.Persistent
                    ? "SuperView.Timeline.Owner.Persistent"
                    : "SuperView.Timeline.Owner.Animation");
            var timeText = GetTimeText(item);
            ToolTipText = LocalizationManager.Instance.GetFormat(
                "SuperView.Timeline.MarkerToolTip",
                typeName,
                ownerName,
                timeText);
            AutomationName = LocalizationManager.Instance.GetFormat(
                "SuperView.Timeline.AutomationName",
                typeName,
                ownerName,
                timeText);
            SelectCommand = new RelayCommand(() => select(Item));
        }

        private static string GetTypeName(MetaDataInspectionItem item) =>
            item.PreviewCategory switch
            {
                CombatMetaDataPreviewCategory.Impact =>
                    LocalizationManager.Instance.Get("SuperView.ShowImpactPos"),
                CombatMetaDataPreviewCategory.Target =>
                    LocalizationManager.Instance.Get("SuperView.ShowTargetPos"),
                CombatMetaDataPreviewCategory.Fire =>
                    LocalizationManager.Instance.Get("SuperView.ShowFirePos"),
                CombatMetaDataPreviewCategory.Splash =>
                    LocalizationManager.Instance.Get(
                        "SuperView.ShowSplashAttack"),
                _ => LocalizationManager.Instance.GetFormat(
                    "SuperView.Timeline.Type.Other",
                    item.Source.DisplayName),
            };

        private static string GetTimeText(MetaDataInspectionItem item)
        {
            var timeRange = item.AuthoredTimeRange!.Value;
            return item.TimelineMarkerKind switch
            {
                MetaDataTimelineMarkerKind.WholeAnimation =>
                    LocalizationManager.Instance.Get(
                        "SuperView.Timeline.Time.WholeAnimation"),
                MetaDataTimelineMarkerKind.Instant =>
                    LocalizationManager.Instance.GetFormat(
                        "SuperView.Timeline.Time.Instant",
                        timeRange.StartTime),
                _ => LocalizationManager.Instance.GetFormat(
                    "SuperView.Timeline.Time.Range",
                    timeRange.StartTime,
                    timeRange.EndTime),
            };
        }
    }

    public sealed class MetaDataTimelineViewModel :
        ObservableObject,
        IEditorViewModelTypeProvider
    {
        private readonly Action<MetaDataInspectionItem> _select;

        public ObservableCollection<MetaDataTimelineMarkerViewModel> Markers
        {
            get;
        } = [];
        public bool HasMarkers => Markers.Count != 0;
        public string EmptyStateText => LocalizationManager.Instance.Get(
            "SuperView.Timeline.Empty");
        public Type EditorViewModelType => typeof(MetaDataTimelineView);

        public MetaDataTimelineViewModel(Action<MetaDataInspectionItem> select)
        {
            _select = select;
        }

        public void Update(
            MetaDataInspectionIndex index,
            float clipDurationSeconds)
        {
            Markers.Clear();
            foreach (var item in index.Items.Where(item =>
                item.TimelineMarkerKind.HasValue))
            {
                Markers.Add(new MetaDataTimelineMarkerViewModel(
                    item,
                    clipDurationSeconds,
                    _select));
            }

            OnPropertyChanged(nameof(HasMarkers));
        }

        public void UpdateSelection(
            MetaDataDocumentOwner owner,
            ParsedMetadataAttribute? source)
        {
            foreach (var marker in Markers)
            {
                marker.IsSelected = marker.Item.Owner == owner &&
                    ReferenceEquals(marker.Item.Source, source);
            }
        }
    }
}
