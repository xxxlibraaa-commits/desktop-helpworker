using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace FloatMate.Controls;

public partial class PlanPage : System.Windows.Controls.UserControl
{
    private const double TimelineWidth = 326D;
    private readonly HashSet<Guid> _subscribedTasks = [];
    private readonly ObservableCollection<LongPlanTask> _activeTasks = [];
    private readonly ObservableCollection<LongPlanTask> _completedTasks = [];
    private ObservableCollection<LongPlan> _plans = [];
    private Guid? _displayedPlanId;
    private LongPlanTask? _dragTask;
    private Canvas? _dragCanvas;
    private DragMode _dragMode;
    private System.Windows.Point _dragStartPoint;
    private DateTime _dragOriginalStart;
    private DateTime _dragOriginalEnd;
    private bool _updating;

    public IReadOnlyList<string> StatusOptions { get; } = ["未开始", "进行中", "已完成", "暂停"];
    public LongPlan? SelectedPlan => _plans.FirstOrDefault(plan => plan.IsExpanded);
    public event EventHandler? ImportRequested;
    public event EventHandler<LongPlan>? ExportRequested;
    public event EventHandler? DataChanged;

    public PlanPage()
    {
        InitializeComponent();
    }

    public void BindPlans(ObservableCollection<LongPlan> plans)
    {
        _plans = plans;
        foreach (var plan in _plans) plan.IsExpanded = false;
        PlanList.ItemsSource = _plans;
        PlanTasksList.ItemsSource = _activeTasks;
        CompletedTasksList.ItemsSource = _completedTasks;
        RefreshSelectedPlan();
    }

    public void SelectPlan(LongPlan plan)
    {
        foreach (var item in _plans) item.IsExpanded = ReferenceEquals(item, plan);
        RefreshSelectedPlan();
        PlanScroll.ScrollToTop();
    }

    internal void PrepareCompletedPreview()
    {
        CompletedTasksExpander.IsExpanded = true;
        UpdateLayout();
        PlanScroll.ScrollToEnd();
    }

    private void PlanSummary_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not LongPlan plan) return;
        var shouldExpand = (sender as System.Windows.Controls.Primitives.ToggleButton)?.IsChecked == true;
        foreach (var item in _plans) item.IsExpanded = shouldExpand && ReferenceEquals(item, plan);
        RefreshSelectedPlan();
    }

    private void RefreshSelectedPlan()
    {
        var plan = SelectedPlan;
        var hasPlan = plan is not null;
        EmptyPlanPanel.Visibility = _plans.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        PlanContent.Visibility = hasPlan ? Visibility.Visible : Visibility.Collapsed;
        ExportButton.IsEnabled = hasPlan;
        if (plan is null)
        {
            _displayedPlanId = null;
            _activeTasks.Clear();
            _completedTasks.Clear();
            return;
        }

        if (_displayedPlanId != plan.Id)
        {
            CompletedTasksExpander.IsExpanded = false;
            _displayedPlanId = plan.Id;
        }

        if (plan.EndDate < plan.StartDate) plan.EndDate = plan.StartDate;
        PlanContent.DataContext = plan;
        foreach (var task in plan.Tasks)
        {
            if (_subscribedTasks.Add(task.Id)) task.PropertyChanged += Task_PropertyChanged;
            UpdateTaskGeometry(plan, task);
        }
        plan.NotifySummaryChanged();
        PlanSourceText.Text = string.IsNullOrWhiteSpace(plan.SourceFileName) ? "本地计划 · 独立保存" : $"已导入 · {plan.SourceFileName}";
        PlanProgressText.Text = plan.ProgressText;
        PlanRangeText.Text = plan.RangeText;
        PlanProgressBar.Value = plan.Progress;
        RefreshTaskLists(plan);
        PlanList.Items.Refresh();
    }

    private void RefreshTaskLists(LongPlan plan)
    {
        _activeTasks.Clear();
        _completedTasks.Clear();
        foreach (var task in plan.Tasks.OrderBy(task => task.Order))
        {
            if (task.Status == "已完成") _completedTasks.Add(task);
            else _activeTasks.Add(task);
        }

        PlanTaskCountText.Text = plan.Tasks.Count == 0 ? "—" : $"{_activeTasks.Count} 未完成 · {_completedTasks.Count} 已完成";
        EmptyTasksPanel.Visibility = plan.Tasks.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        NoActiveTasksPanel.Visibility = plan.Tasks.Count > 0 && _activeTasks.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        CompletedTasksExpander.Visibility = _completedTasks.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        CompletedTasksHeaderText.Text = $"已完成 · {_completedTasks.Count} 项";
    }

    private void Task_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_updating || sender is not LongPlanTask task || SelectedPlan is not LongPlan plan) return;
        if (e.PropertyName is nameof(LongPlanTask.StartDate) or nameof(LongPlanTask.EndDate))
        {
            NormalizeTaskDates(plan, task);
            UpdateTaskGeometry(plan, task);
        }
        if (e.PropertyName is nameof(LongPlanTask.Progress) or nameof(LongPlanTask.Status)) UpdateSummary(plan);
        if (e.PropertyName is not nameof(LongPlanTask.TimelineLeft) and not nameof(LongPlanTask.TimelineWidth)) DataChanged?.Invoke(this, EventArgs.Empty);
    }

    private void UpdateSummary(LongPlan plan)
    {
        plan.NotifySummaryChanged();
        PlanProgressText.Text = plan.ProgressText;
        PlanProgressBar.Value = plan.Progress;
        PlanList.Items.Refresh();
    }

    private static void NormalizeTaskDates(LongPlan plan, LongPlanTask task)
    {
        if (!task.StartDate.HasValue && task.EndDate.HasValue) task.StartDate = task.EndDate;
        if (task.StartDate.HasValue && !task.EndDate.HasValue) task.EndDate = task.StartDate;
        if (task.StartDate.HasValue && task.EndDate < task.StartDate) task.EndDate = task.StartDate;
        task.NotifyScheduleChanged();
    }

    private static void UpdateTaskGeometry(LongPlan plan, LongPlanTask task)
    {
        if (!task.StartDate.HasValue || !task.EndDate.HasValue)
        {
            task.TimelineLeft = 0;
            task.TimelineWidth = 0;
            task.NotifyScheduleChanged();
            return;
        }
        var totalDays = Math.Max(1, (plan.EndDate.Date - plan.StartDate.Date).Days + 1);
        var clippedStart = task.StartDate.Value.Date < plan.StartDate.Date ? plan.StartDate.Date : task.StartDate.Value.Date;
        var clippedEnd = task.EndDate.Value.Date > plan.EndDate.Date ? plan.EndDate.Date : task.EndDate.Value.Date;
        if (clippedEnd < clippedStart)
        {
            task.TimelineLeft = 0;
            task.TimelineWidth = 0;
        }
        else
        {
            task.TimelineLeft = Math.Clamp((clippedStart - plan.StartDate.Date).Days * TimelineWidth / totalDays, 0, TimelineWidth);
            task.TimelineWidth = Math.Clamp(((clippedEnd - clippedStart).Days + 1) * TimelineWidth / totalDays, 8, TimelineWidth - task.TimelineLeft);
        }
        task.NotifyScheduleChanged();
    }

    private void NewPlan_Click(object sender, RoutedEventArgs e)
    {
        var plan = new LongPlan
        {
            Name = $"{DateTime.Today:M月}工作计划",
            StartDate = DateTime.Today,
            EndDate = DateTime.Today.AddDays(39)
        };
        _plans.Add(plan);
        SelectPlan(plan);
        DataChanged?.Invoke(this, EventArgs.Empty);
    }

    private void AddTask_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedPlan is not LongPlan plan) return;
        var start = plan.StartDate.Date;
        var task = new LongPlanTask
        {
            Order = plan.Tasks.Count + 1,
            Title = "新任务",
            StartDate = start,
            EndDate = start.AddDays(Math.Min(6, Math.Max(0, (plan.EndDate - start).Days)))
        };
        plan.Tasks.Add(task);
        task.PropertyChanged += Task_PropertyChanged;
        _subscribedTasks.Add(task.Id);
        RefreshSelectedPlan();
        DataChanged?.Invoke(this, EventArgs.Empty);
        PlanScroll.ScrollToEnd();
    }

    private void Import_Click(object sender, RoutedEventArgs e) => ImportRequested?.Invoke(this, EventArgs.Empty);
    private void Export_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedPlan is LongPlan plan) ExportRequested?.Invoke(this, plan);
    }

    private void ScheduleTask_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not LongPlanTask task || SelectedPlan is not LongPlan plan) return;
        task.StartDate = plan.StartDate;
        task.EndDate = plan.StartDate.AddDays(Math.Min(6, Math.Max(0, (plan.EndDate - plan.StartDate).Days)));
        task.NotifyScheduleChanged();
        UpdateTaskGeometry(plan, task);
        DataChanged?.Invoke(this, EventArgs.Empty);
    }

    private void TaskStatus_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_updating || (sender as FrameworkElement)?.DataContext is not LongPlanTask task || SelectedPlan is not LongPlan plan) return;
        _updating = true;
        if (task.Status == "已完成") task.Progress = 100;
        else if (task.Progress >= 100) task.Progress = task.Status == "进行中" ? 75 : 0;
        _updating = false;
        UpdateSummary(plan);
        RefreshTaskLists(plan);
        DataChanged?.Invoke(this, EventArgs.Empty);
    }

    private void TaskProgress_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_updating || (sender as FrameworkElement)?.DataContext is not LongPlanTask task || SelectedPlan is not LongPlan plan) return;
        _updating = true;
        if (task.Progress >= 100) task.Status = "已完成";
        else if (task.Progress > 0 && task.Status is "未开始" or "已完成") task.Status = "进行中";
        else if (task.Progress == 0 && task.Status == "进行中") task.Status = "未开始";
        _updating = false;
        UpdateSummary(plan);
        RefreshTaskLists(plan);
        DataChanged?.Invoke(this, EventArgs.Empty);
    }

    private void PlanDate_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_updating || SelectedPlan is not LongPlan plan) return;
        if (plan.EndDate < plan.StartDate) plan.EndDate = plan.StartDate;
        RefreshSelectedPlan();
        DataChanged?.Invoke(this, EventArgs.Empty);
    }

    private void TaskDate_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_updating || (sender as FrameworkElement)?.DataContext is not LongPlanTask task || SelectedPlan is not LongPlan plan) return;
        NormalizeTaskDates(plan, task);
        UpdateTaskGeometry(plan, task);
        DataChanged?.Invoke(this, EventArgs.Empty);
    }

    private void PlanField_LostFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        PlanList.Items.Refresh();
        DataChanged?.Invoke(this, EventArgs.Empty);
    }
    private void TaskField_LostFocus(object sender, KeyboardFocusChangedEventArgs e) => DataChanged?.Invoke(this, EventArgs.Empty);

    private void TimelineBar_MouseDown(object sender, MouseButtonEventArgs e) => BeginDrag(sender, e, DragMode.Move);
    private void TimelineStart_MouseDown(object sender, MouseButtonEventArgs e) => BeginDrag(sender, e, DragMode.ResizeStart);
    private void TimelineEnd_MouseDown(object sender, MouseButtonEventArgs e) => BeginDrag(sender, e, DragMode.ResizeEnd);

    private void BeginDrag(object sender, MouseButtonEventArgs e, DragMode mode)
    {
        if ((sender as FrameworkElement)?.Tag is not LongPlanTask task || !task.StartDate.HasValue || !task.EndDate.HasValue) return;
        var canvas = FindAncestor<Canvas>((DependencyObject)sender);
        if (canvas is null) return;
        _dragTask = task;
        _dragCanvas = canvas;
        _dragMode = mode;
        _dragStartPoint = e.GetPosition(canvas);
        _dragOriginalStart = task.StartDate.Value.Date;
        _dragOriginalEnd = task.EndDate.Value.Date;
        canvas.CaptureMouse();
        e.Handled = true;
    }

    private void TimelineCanvas_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (_dragTask is null || _dragCanvas is null || SelectedPlan is not LongPlan plan || e.LeftButton != MouseButtonState.Pressed) return;
        var deltaPixels = e.GetPosition(_dragCanvas).X - _dragStartPoint.X;
        var totalDays = Math.Max(1, (plan.EndDate.Date - plan.StartDate.Date).Days + 1);
        var deltaDays = (int)Math.Round(deltaPixels * totalDays / TimelineWidth, MidpointRounding.AwayFromZero);
        _updating = true;
        if (_dragMode == DragMode.Move)
        {
            var duration = (_dragOriginalEnd - _dragOriginalStart).Days;
            var start = _dragOriginalStart.AddDays(deltaDays);
            start = start < plan.StartDate ? plan.StartDate : start;
            var latestStart = plan.EndDate.AddDays(-duration);
            start = start > latestStart ? latestStart : start;
            _dragTask.StartDate = start;
            _dragTask.EndDate = start.AddDays(duration);
        }
        else if (_dragMode == DragMode.ResizeStart)
        {
            var start = _dragOriginalStart.AddDays(deltaDays);
            _dragTask.StartDate = start < plan.StartDate ? plan.StartDate : start > _dragOriginalEnd ? _dragOriginalEnd : start;
        }
        else
        {
            var end = _dragOriginalEnd.AddDays(deltaDays);
            _dragTask.EndDate = end > plan.EndDate ? plan.EndDate : end < _dragOriginalStart ? _dragOriginalStart : end;
        }
        _updating = false;
        _dragTask.NotifyScheduleChanged();
        UpdateTaskGeometry(plan, _dragTask);
    }

    private void TimelineCanvas_MouseUp(object sender, MouseButtonEventArgs e)
    {
        if (_dragTask is null) return;
        _dragCanvas?.ReleaseMouseCapture();
        _dragTask = null;
        _dragCanvas = null;
        DataChanged?.Invoke(this, EventArgs.Empty);
        e.Handled = true;
    }

    private static T? FindAncestor<T>(DependencyObject? element) where T : DependencyObject
    {
        while (element is not null)
        {
            if (element is T match) return match;
            element = VisualTreeHelper.GetParent(element);
        }
        return null;
    }

    private enum DragMode { Move, ResizeStart, ResizeEnd }
}
