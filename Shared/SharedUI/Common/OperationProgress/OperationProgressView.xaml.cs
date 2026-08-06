using System;
using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

using Shared.Core.Services;

namespace Shared.Ui.Common.OperationProgress;

public partial class OperationProgressView : UserControl
{
    private const int MaxUpdatesPerBatch = 250;
    private readonly ConcurrentQueue<OperationProgressUpdate> _pending = [];
    private readonly ObservableCollection<string> _detailHistory = [];
    private readonly ReadOnlyObservableCollection<string> _readOnlyHistory;
    private readonly Stopwatch _stopwatch = new();
    private readonly DispatcherTimer _elapsedTimer;
    private readonly OperationProgressVisibilityController
        _visibilityController;
    private bool _isBatchApplying;
    private int _flushScheduled;

    public static readonly DependencyProperty StatusTextProperty =
        DependencyProperty.Register(
            nameof(StatusText),
            typeof(string),
            typeof(OperationProgressView),
            new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty CurrentDetailTextProperty =
        DependencyProperty.Register(
            nameof(CurrentDetailText),
            typeof(string),
            typeof(OperationProgressView),
            new PropertyMetadata(
                string.Empty,
                OnCurrentDetailTextChanged));

    public static readonly DependencyProperty IsOperationActiveProperty =
        DependencyProperty.Register(
            nameof(IsOperationActive),
            typeof(bool),
            typeof(OperationProgressView),
            new PropertyMetadata(false, OnIsOperationActiveChanged));

    public static readonly DependencyProperty ProgressValueProperty =
        DependencyProperty.Register(
            nameof(ProgressValue),
            typeof(double),
            typeof(OperationProgressView),
            new PropertyMetadata(0d, OnProgressMetricChanged));

    public static readonly DependencyProperty ProgressMaximumProperty =
        DependencyProperty.Register(
            nameof(ProgressMaximum),
            typeof(double),
            typeof(OperationProgressView),
            new PropertyMetadata(1d, OnProgressMetricChanged));

    public static readonly DependencyProperty IsProgressIndeterminateProperty =
        DependencyProperty.Register(
            nameof(IsProgressIndeterminate),
            typeof(bool),
            typeof(OperationProgressView),
            new PropertyMetadata(true, OnProgressMetricChanged));

    public static readonly DependencyProperty ProgressSummaryTextProperty =
        DependencyProperty.Register(
            nameof(ProgressSummaryText),
            typeof(string),
            typeof(OperationProgressView),
            new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty DetailsHeaderTextProperty =
        DependencyProperty.Register(
            nameof(DetailsHeaderText),
            typeof(string),
            typeof(OperationProgressView),
            new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty UseDeferredVisibilityProperty =
        DependencyProperty.Register(
            nameof(UseDeferredVisibility),
            typeof(bool),
            typeof(OperationProgressView),
            new PropertyMetadata(true, OnUseDeferredVisibilityChanged));

    public OperationProgressView()
    {
        _readOnlyHistory = new ReadOnlyObservableCollection<string>(
            _detailHistory);
        InitializeComponent();
        _visibilityController = new OperationProgressVisibilityController(
            Dispatcher,
            isVisible => SetCurrentValue(
                VisibilityProperty,
                isVisible ? Visibility.Visible : Visibility.Collapsed));
        _elapsedTimer = new DispatcherTimer(
            TimeSpan.FromSeconds(1),
            DispatcherPriority.Background,
            OnElapsedTimerTick,
            Dispatcher);
        DetailsHeaderText = GetText(
            "OperationProgress.ShowDetails",
            "查看详情");
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    public string StatusText
    {
        get => (string)GetValue(StatusTextProperty);
        set => SetValue(StatusTextProperty, value);
    }

    public string CurrentDetailText
    {
        get => (string)GetValue(CurrentDetailTextProperty);
        set => SetValue(CurrentDetailTextProperty, value);
    }

    public bool IsOperationActive
    {
        get => (bool)GetValue(IsOperationActiveProperty);
        set => SetValue(IsOperationActiveProperty, value);
    }

    public double ProgressValue
    {
        get => (double)GetValue(ProgressValueProperty);
        set => SetValue(ProgressValueProperty, value);
    }

    public double ProgressMaximum
    {
        get => (double)GetValue(ProgressMaximumProperty);
        set => SetValue(ProgressMaximumProperty, value);
    }

    public bool IsProgressIndeterminate
    {
        get => (bool)GetValue(IsProgressIndeterminateProperty);
        set => SetValue(IsProgressIndeterminateProperty, value);
    }

    public string ProgressSummaryText
    {
        get => (string)GetValue(ProgressSummaryTextProperty);
        set => SetValue(ProgressSummaryTextProperty, value);
    }

    public string DetailsHeaderText
    {
        get => (string)GetValue(DetailsHeaderTextProperty);
        set => SetValue(DetailsHeaderTextProperty, value);
    }

    public bool UseDeferredVisibility
    {
        get => (bool)GetValue(UseDeferredVisibilityProperty);
        set => SetValue(UseDeferredVisibilityProperty, value);
    }

    public ReadOnlyObservableCollection<string> DetailHistory =>
        _readOnlyHistory;

    public bool IsDetailsExpanded
    {
        get => DetailsExpander.IsExpanded;
        set => DetailsExpander.IsExpanded = value;
    }

    public void Report(OperationProgressUpdate update)
    {
        ArgumentNullException.ThrowIfNull(update);

        if (Dispatcher.CheckAccess())
        {
            ApplyUpdate(update);
            return;
        }

        _pending.Enqueue(update);
        ScheduleFlush();
    }

    private void ScheduleFlush()
    {
        if (Interlocked.Exchange(ref _flushScheduled, 1) != 0)
            return;

        Dispatcher.BeginInvoke(
            DispatcherPriority.Background,
            FlushPending);
    }

    private void FlushPending()
    {
        Interlocked.Exchange(ref _flushScheduled, 0);
        var processed = 0;
        _isBatchApplying = true;
        try
        {
            while (processed < MaxUpdatesPerBatch &&
                   _pending.TryDequeue(out var update))
            {
                ApplyUpdate(update);
                processed++;
            }
        }
        finally
        {
            _isBatchApplying = false;
        }

        if (!_pending.IsEmpty)
            ScheduleFlush();

        if (DetailsExpander.IsExpanded && _detailHistory.Count > 0)
            DetailsList.ScrollIntoView(_detailHistory[^1]);
    }

    private void ApplyUpdate(OperationProgressUpdate update)
    {
        if (!_stopwatch.IsRunning)
        {
            _stopwatch.Start();
            _elapsedTimer.Start();
        }

        StatusText = update.Status;
        CurrentDetailText = update.Detail ?? string.Empty;
        IsProgressIndeterminate = update.Total <= 0;
        ProgressMaximum = Math.Max(1, update.Total);
        ProgressValue = Math.Clamp(update.Completed, 0, ProgressMaximum);
        UpdateProgressSummary(update.Completed, update.Total);
    }

    private void UpdateProgressSummary(long completed, long total)
    {
        var elapsed = _stopwatch.Elapsed;
        var elapsedText = elapsed.TotalHours >= 1
            ? elapsed.ToString(@"hh\:mm\:ss")
            : elapsed.ToString(@"mm\:ss");
        ProgressSummaryText = total > 0
            ? $"{completed:N0} / {total:N0}  ·  {elapsedText}"
            : elapsedText;
    }

    private void OnElapsedTimerTick(object? sender, EventArgs e)
    {
        var total = IsProgressIndeterminate
            ? 0
            : (long)ProgressMaximum;
        UpdateProgressSummary((long)ProgressValue, total);
    }

    private void OnDetailsExpansionChanged(
        object sender,
        RoutedEventArgs e)
    {
        DetailsHeaderText = DetailsExpander.IsExpanded
            ? GetText("OperationProgress.HideDetails", "收起详情")
            : GetText("OperationProgress.ShowDetails", "查看详情");
        if (DetailsExpander.IsExpanded && _detailHistory.Count > 0)
            DetailsList.ScrollIntoView(_detailHistory[^1]);
    }

    private void OnLoaded(object sender, RoutedEventArgs eventArgs)
    {
        if (!UseDeferredVisibility)
            _visibilityController.RevealImmediately();
        else if (IsOperationActive)
            _visibilityController.Begin();
    }

    private void OnUnloaded(object sender, RoutedEventArgs eventArgs)
    {
        _elapsedTimer.Stop();
        _visibilityController.ForceHide();
    }

    private static string GetText(string key, string fallback)
    {
        return LocalizationManager.Instance?.Get(key) ?? fallback;
    }

    private static void OnCurrentDetailTextChanged(
        DependencyObject dependencyObject,
        DependencyPropertyChangedEventArgs eventArgs)
    {
        var view = (OperationProgressView)dependencyObject;
        if (eventArgs.NewValue is not string detail ||
            string.IsNullOrWhiteSpace(detail) ||
            view._detailHistory.Count > 0 &&
            view._detailHistory[^1] == detail)
        {
            return;
        }

        view._detailHistory.Add(detail);
        if (view.DetailsExpander.IsExpanded &&
            !view._isBatchApplying)
        {
            view.DetailsList.ScrollIntoView(detail);
        }
    }

    private static void OnProgressMetricChanged(
        DependencyObject dependencyObject,
        DependencyPropertyChangedEventArgs eventArgs)
    {
        var view = (OperationProgressView)dependencyObject;
        var total = view.IsProgressIndeterminate
            ? 0
            : (long)view.ProgressMaximum;
        view.UpdateProgressSummary(
            (long)view.ProgressValue,
            total);
    }

    private static void OnIsOperationActiveChanged(
        DependencyObject dependencyObject,
        DependencyPropertyChangedEventArgs eventArgs)
    {
        var view = (OperationProgressView)dependencyObject;
        if (eventArgs.NewValue is true)
        {
            view._detailHistory.Clear();
            if (!string.IsNullOrWhiteSpace(view.CurrentDetailText))
                view._detailHistory.Add(view.CurrentDetailText);
            view._stopwatch.Restart();
            view._elapsedTimer.Start();
            view.UpdateProgressSummary(0, 0);
            if (view.UseDeferredVisibility)
                view._visibilityController.Begin();
            else
                view._visibilityController.RevealImmediately();
        }
        else
        {
            view._elapsedTimer.Stop();
            view._stopwatch.Stop();
            if (view.UseDeferredVisibility)
                _ = view._visibilityController.EndAsync();
        }
    }

    private static void OnUseDeferredVisibilityChanged(
        DependencyObject dependencyObject,
        DependencyPropertyChangedEventArgs eventArgs)
    {
        var view = (OperationProgressView)dependencyObject;
        if (eventArgs.NewValue is false)
        {
            view._visibilityController.RevealImmediately();
        }
        else if (view.IsOperationActive)
        {
            view._visibilityController.Begin();
        }
        else
        {
            view._visibilityController.ForceHide();
        }
    }
}
