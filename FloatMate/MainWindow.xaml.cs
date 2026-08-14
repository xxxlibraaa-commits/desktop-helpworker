using FloatMate.Services;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using System.IO;
using Forms = System.Windows.Forms;

namespace FloatMate;

public partial class MainWindow : Window
{
    private const int HotkeyId = 0x1205;
    private readonly LocalStore _store = new();
    private readonly SystemMonitor _monitor = new();
    private readonly AppBarManager _appBar = new();
    private readonly XlsmExportService _xlsmExporter = new();
    private readonly WorkDocxExportService _workDocxExporter = new();
    private readonly HealthWorkbookService _healthWorkbookExporter = new();
    private readonly PlanWorkbookService _planWorkbook = new();
    private readonly ForegroundAppUsageMonitor _usageMonitor = new();
    private readonly DispatcherTimer _systemTimer = new() { Interval = TimeSpan.FromSeconds(3) };
    private readonly DispatcherTimer _focusTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly DispatcherTimer _saveTimer = new() { Interval = TimeSpan.FromMilliseconds(350) };
    private readonly DispatcherTimer _toastTimer = new() { Interval = TimeSpan.FromSeconds(3) };
    private readonly DispatcherTimer _reminderTimer = new() { Interval = TimeSpan.FromSeconds(30) };
    private readonly DispatcherTimer _usageTimer = new() { Interval = TimeSpan.FromSeconds(5) };
    private readonly DispatcherTimer _collapseTimer = new() { Interval = TimeSpan.FromMilliseconds(900) };
    private readonly ObservableCollection<GoalItem> _goals = [];
    private readonly ObservableCollection<LongPlan> _plans = [];
    private readonly List<GoalItem> _allGoals = [];
    private readonly List<QuickRecord> _records = [];
    private readonly List<ActivityEvent> _events = [];
    private readonly List<AppUsageEntry> _appUsage = [];
    private readonly Dictionary<string, DateTime> _lastReminderShown = [];
    private readonly DateTime _sessionStartedAt = DateTime.Now;
    private AppData _data = new();
    private Forms.NotifyIcon? _trayIcon;
    private GoalItem? _runningGoal;
    private bool _isExpanded;
    private bool _allowExit;
    private bool _initializing = true;
    private bool _previewMode;
    private int _warningStreak;
    private GoalItem? _lastDeletedGoal;
    private int _lastDeletedTodayIndex = -1;
    private int _lastDeletedAllIndex = -1;
    private double _floatingLeft = double.NaN;
    private double _floatingTop = double.NaN;
    private DateTime _lastUsageSaveAt = DateTime.MinValue;

    public MainWindow()
    {
        InitializeComponent();
        GoalsList.ItemsSource = _goals;
        PlanView.DataChanged += (_, _) => QueueSave();
        PlanView.ImportRequested += (_, _) => ImportPlanWorkbook();
        PlanView.ExportRequested += (_, plan) => ExportPlanWorkbook(plan);
        _systemTimer.Tick += (_, _) => RefreshSystemStatus();
        _focusTimer.Tick += (_, _) => TickFocus();
        _saveTimer.Tick += (_, _) => { _saveTimer.Stop(); SaveNow(); };
        _toastTimer.Tick += (_, _) => HideToast();
        _reminderTimer.Tick += (_, _) => CheckReminders();
        _usageTimer.Tick += (_, _) => TrackForegroundApp();
        _collapseTimer.Tick += (_, _) => { _collapseTimer.Stop(); if (_isExpanded && AutoCollapseCheck.IsChecked == true) SetExpanded(false); };
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        _data = _store.Load();
        _allGoals.AddRange(_data.Goals);
        _records.AddRange(_data.Records);
        _events.AddRange(_data.Events);
        _appUsage.AddRange(_data.AppUsage);
        foreach (var plan in _data.Plans) _plans.Add(plan);
        PlanView.BindPlans(_plans);
        var today = DateOnly.FromDateTime(DateTime.Now);
        foreach (var goal in _allGoals.Where(g => g.Date == today)) _goals.Add(goal);

        TodayText.Text = $"{DateTime.Now:M月d日 dddd} · 本地独立记录";
        DataLocationText.Text = $"本地数据：{_store.DataLocation}";
        InitializeSettings();

        var initialWidth = _data.IsExpanded ? 420 : 336;
        if (!double.IsNaN(_data.WindowLeft) && !double.IsNaN(_data.WindowTop))
        {
            Left = Math.Clamp(_data.WindowLeft, SystemParameters.VirtualScreenLeft,
                SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth - initialWidth);
            Top = Math.Clamp(_data.WindowTop, SystemParameters.VirtualScreenTop,
                SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight - 80);
        }
        else
        {
            Left = SystemParameters.WorkArea.Right - initialWidth - 12;
            Top = SystemParameters.WorkArea.Top + 18;
        }
        _floatingLeft = Left;
        _floatingTop = Top;

        CreateTrayIcon();
        var backgroundStart = Environment.GetCommandLineArgs().Any(a => a.Equals("--background", StringComparison.OrdinalIgnoreCase));
        SetExpanded(_data.DockedRightMode || !(backgroundStart || _data.DesktopWidgetMode) && _data.IsExpanded);
        ShowTodayView();
        UpdateSummaries();
        RefreshHistoryDates();
        RefreshSystemStatus();
        RefreshUsageView();
        _systemTimer.Start();
        _focusTimer.Start();
        _reminderTimer.Start();
        _usageMonitor.ResetClock();
        _usageTimer.Start();
        _initializing = false;
        QueueSave();

        var handle = new WindowInteropHelper(this).Handle;
        HwndSource.FromHwnd(handle)?.AddHook(WndProc);
        RegisterHotKey(handle, HotkeyId, 0x0002 | 0x0004, 0x20);
        ApplyWindowMode();
    }

    private void InitializeSettings()
    {
        WaterReminderCheck.IsChecked = _data.Reminders.WaterEnabled;
        StandReminderCheck.IsChecked = _data.Reminders.StandEnabled;
        EyeReminderCheck.IsChecked = _data.Reminders.EyeEnabled;
        SelectInterval(WaterIntervalCombo, _data.Reminders.WaterMinutes);
        SelectInterval(StandIntervalCombo, _data.Reminders.StandMinutes);
        SelectInterval(EyeIntervalCombo, _data.Reminders.EyeMinutes);
        StartupCheck.IsChecked = StartupManager.IsEnabled();
        DesktopWidgetModeCheck.IsChecked = _data.DesktopWidgetMode;
        DockedRightModeCheck.IsChecked = _data.DockedRightMode;
        AutoCollapseCheck.IsChecked = _data.AutoCollapse;
        OpacitySlider.Value = Math.Clamp(_data.Opacity, 0.86, 1.0);
        ApplySurfaceOpacity(false);
    }

    private static void SelectInterval(System.Windows.Controls.ComboBox combo, int minutes)
    {
        combo.SelectedItem = combo.Items.OfType<ComboBoxItem>()
            .FirstOrDefault(item => item.Tag?.ToString() == minutes.ToString()) ?? combo.Items[0];
    }

    private void CreateTrayIcon()
    {
        _trayIcon = new Forms.NotifyIcon
        {
            Icon = System.Drawing.SystemIcons.Information,
            Text = "浮岛 FloatMate",
            Visible = true
        };
        _trayIcon.MouseClick += (_, e) =>
        {
            if (e.Button == Forms.MouseButtons.Left) Dispatcher.Invoke(ToggleWindowVisibility);
        };
        _trayIcon.BalloonTipClicked += (_, _) => Dispatcher.Invoke(ShowAndActivate);
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("打开浮岛", null, (_, _) => Dispatcher.Invoke(ShowAndActivate));
        menu.Items.Add("记录喝水 250 ml", null, (_, _) => Dispatcher.Invoke(() => AddRecord("喝水", 250, "ml")));
        menu.Items.Add("记录如厕", null, (_, _) => Dispatcher.Invoke(() => AddRecord("如厕")));
        menu.Items.Add("记录起身", null, (_, _) => Dispatcher.Invoke(() => AddRecord("起身")));
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("退出", null, (_, _) => Dispatcher.Invoke(ExitApplication));
        _trayIcon.ContextMenuStrip = menu;
    }

    private void RefreshSystemStatus()
    {
        var snapshot = _monitor.Read();
        CpuText.Text = $"{snapshot.CpuPercent:0}%";
        MemoryText.Text = $"{snapshot.MemoryPercent:0}%";
        MiniCpuText.Text = $"{snapshot.CpuPercent:0}%";
        MiniMemoryText.Text = $"{snapshot.MemoryPercent:0}%";
        DiskText.Text = $"{snapshot.DiskPercent:0}%";
        NetworkText.Text = snapshot.DownloadKb < 1 ? "—" : snapshot.DownloadKb >= 1024 ? $"{snapshot.DownloadKb / 1024:0.0} M" : $"{snapshot.DownloadKb:0} K";
        var uploadText = snapshot.UploadKb < 1 ? "—" : $"{snapshot.UploadKb:0} KB/s";
        SystemDetailText.Text = $"内存 {snapshot.MemoryUsedGb:0.0} / {snapshot.MemoryTotalGb:0.0} GB  ·  磁盘剩余 {snapshot.DiskFreeGb:0.0} GB  ·  上传 {uploadText}";

        var severe = snapshot.MemoryPercent >= 94 || snapshot.CpuPercent >= 96 || snapshot.DiskFreeGb < 3;
        var warning = snapshot.MemoryPercent >= 85 || snapshot.CpuPercent >= 85 || snapshot.DiskFreeGb < 10;
        _warningStreak = warning ? _warningStreak + 1 : 0;
        if (severe && _warningStreak >= 2) SetHealth("负载较高", "#3A3A3C", "#EEEEF2");
        else if (warning && _warningStreak >= 3) SetHealth("负载偏高", "#5A5A60", "#F2F2F7");
        else SetHealth("运行平稳", "#3A3A3C", "#F2F2F7");
        UpdateMiniView();
    }

    private void SetHealth(string text, string color, string softColor)
    {
        var brush = new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(color));
        HealthText.Text = text;
        HealthText.Foreground = brush;
        HealthDot.Background = brush;
        HealthBadge.Background = new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(softColor));
    }

    private void TickFocus()
    {
        if (_runningGoal is null) return;
        _runningGoal.FocusSeconds++;
        if (_runningGoal.FocusSeconds % 15 == 0) QueueSave();
        UpdateMiniView();
        if (_runningGoal.FocusSeconds % 60 == 0) UpdateSummaries();
    }

    private void ShowAddGoal_Click(object sender, RoutedEventArgs e)
    {
        AddGoalPanel.Visibility = AddGoalPanel.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible;
        if (AddGoalPanel.Visibility == Visibility.Visible) { GoalInput.Focus(); Keyboard.Focus(GoalInput); }
    }

    private void GoalInput_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.Enter && Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            AddGoal();
            e.Handled = true;
        }
        else if (e.Key == Key.Enter)
        {
            GoalDetailsInput.Focus();
            Keyboard.Focus(GoalDetailsInput);
            e.Handled = true;
        }
        else if (e.Key == Key.Escape) AddGoalPanel.Visibility = Visibility.Collapsed;
    }

    private void GoalDetailsInput_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.Enter && Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            AddGoal();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape) AddGoalPanel.Visibility = Visibility.Collapsed;
    }

    private void AddGoal_Click(object sender, RoutedEventArgs e) => AddGoal();

    private void AddGoal()
    {
        var title = GoalInput.Text.Trim();
        if (string.IsNullOrWhiteSpace(title)) return;
        var estimate = GoalEstimateCombo.SelectedItem is ComboBoxItem estimateItem && int.TryParse(estimateItem.Tag?.ToString(), out var parsed)
            ? parsed : 60;
        var goal = new GoalItem
        {
            Title = title,
            Details = GoalDetailsInput.Text.Trim(),
            Date = DateOnly.FromDateTime(DateTime.Now),
            EstimateMinutes = estimate
        };
        _goals.Add(goal);
        _allGoals.Add(goal);
        AddActivity("目标", "新增目标", BuildGoalActivityDetail(goal), goal.Id);
        GoalInput.Clear();
        GoalDetailsInput.Clear();
        AddGoalPanel.Visibility = Visibility.Collapsed;
        ShowToast("目标已添加");
        UpdateSummaries();
        QueueSave();
    }

    private void GoalDetailsEdit_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not GoalItem goal) return;
        foreach (var item in _goals.Where(item => item != goal)) item.IsEditingDetails = false;
        goal.EditingDetails = goal.Details;
        goal.IsEditingDetails = true;
    }

    private void GoalDetailsSave_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is GoalItem goal) SaveGoalDetails(goal);
    }

    private void GoalDetailsCancel_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not GoalItem goal) return;
        goal.EditingDetails = goal.Details;
        goal.IsEditingDetails = false;
    }

    private void GoalDetailsEditor_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not GoalItem goal) return;
        if (e.Key == Key.Enter && Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            SaveGoalDetails(goal);
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            goal.EditingDetails = goal.Details;
            goal.IsEditingDetails = false;
            e.Handled = true;
        }
    }

    private void SaveGoalDetails(GoalItem goal)
    {
        var details = goal.EditingDetails.Trim();
        var changed = !string.Equals(details, goal.Details, StringComparison.Ordinal);
        goal.Details = details;
        goal.IsEditingDetails = false;
        if (!changed) return;
        AddActivity("目标", details.Length == 0 ? "清除工作内容" : "更新工作内容", BuildGoalActivityDetail(goal), goal.Id);
        ShowToast("工作内容已保存");
        RefreshHistoryDates();
        QueueSave();
    }

    private static string BuildGoalActivityDetail(GoalItem goal) => goal.HasDetails
        ? $"{goal.Title}\n{goal.Details}"
        : goal.Title;

    private void GoalStart_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not GoalItem goal || goal.IsCompleted) return;
        if (_runningGoal == goal)
        {
            goal.IsRunning = false;
            goal.Status = "暂停";
            _runningGoal = null;
            AddActivity("目标", "暂停目标", goal.Title, goal.Id);
            ShowToast("计时已暂停");
        }
        else
        {
            if (_runningGoal is not null)
            {
                _runningGoal.IsRunning = false;
                _runningGoal.Status = "暂停";
                AddActivity("目标", "暂停目标", _runningGoal.Title, _runningGoal.Id);
            }
            _runningGoal = goal;
            goal.IsRunning = true;
            goal.Status = "进行中";
            AddActivity("目标", "开始专注", goal.Title, goal.Id);
            ShowToast("计时已开始");
        }
        UpdateSummaries();
        QueueSave();
    }

    private void GoalProgress_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not GoalItem goal || goal.IsCompleted) return;
        goal.Progress = Math.Min(100, goal.Progress + 10);
        AddActivity("目标", "更新进度", $"{goal.Title} · {goal.Progress}%", goal.Id);
        if (goal.Progress >= 100) CompleteGoal(goal);
        else ShowToast("进度已更新");
        UpdateSummaries();
        QueueSave();
    }

    private void GoalProgressDecrease_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not GoalItem goal || goal.IsCompleted) return;
        goal.Progress = Math.Max(0, goal.Progress - 10);
        AddActivity("目标", "调整进度", $"{goal.Title} · {goal.Progress}%", goal.Id);
        ShowToast("进度已更新");
        UpdateSummaries();
        QueueSave();
    }

    private void GoalComplete_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not GoalItem goal) return;
        if (goal.IsCompleted)
        {
            goal.Status = "未开始";
            goal.Progress = Math.Min(goal.Progress, 90);
            goal.CompletedAt = null;
            AddActivity("目标", "恢复目标", goal.Title, goal.Id);
            ShowToast("目标已恢复");
        }
        else CompleteGoal(goal);
        UpdateSummaries();
        QueueSave();
    }

    private void CompleteGoal(GoalItem goal)
    {
        if (_runningGoal == goal) _runningGoal = null;
        goal.IsRunning = false;
        goal.Status = "已完成";
        goal.Progress = 100;
        goal.CompletedAt = DateTime.Now;
        AddActivity("目标", "完成目标", goal.Title, goal.Id);
        ShowToast("目标已完成");
    }

    private void GoalDelete_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not GoalItem goal) return;
        if (_runningGoal == goal) _runningGoal = null;
        _lastDeletedGoal = goal;
        _lastDeletedTodayIndex = _goals.IndexOf(goal);
        _lastDeletedAllIndex = _allGoals.IndexOf(goal);
        _goals.Remove(goal);
        _allGoals.Remove(goal);
        AddActivity("目标", "删除目标", goal.Title, goal.Id);
        ShowToast("目标已删除", true);
        UpdateSummaries();
        QueueSave();
    }

    private void UndoDelete_Click(object sender, RoutedEventArgs e)
    {
        if (_lastDeletedGoal is null) return;
        var goal = _lastDeletedGoal;
        _allGoals.Insert(Math.Clamp(_lastDeletedAllIndex, 0, _allGoals.Count), goal);
        if (goal.Date == DateOnly.FromDateTime(DateTime.Now))
            _goals.Insert(Math.Clamp(_lastDeletedTodayIndex, 0, _goals.Count), goal);
        AddActivity("目标", "撤销删除", goal.Title, goal.Id);
        _lastDeletedGoal = null;
        ShowToast("目标已恢复");
        UpdateSummaries();
        QueueSave();
    }

    private void QuickWater_Click(object sender, RoutedEventArgs e) => AddRecord("喝水", 250, "ml");

    private void QuickRecord_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is string type) AddRecord(type);
    }

    private void AddRecord(string type, double? amount = null, string? unit = null)
    {
        _records.Add(new QuickRecord { Type = type, Amount = amount, Unit = unit, Timestamp = DateTime.Now });
        ShowToast("已记录");
        UpdateSummaries();
        RefreshHistoryDates();
        QueueSave();
    }

    private void AddActivity(string category, string title, string detail, Guid? goalId = null)
    {
        _events.Add(new ActivityEvent { Category = category, Title = title, Detail = detail, GoalId = goalId });
    }

    private void ExportWorkXlsm_Click(object sender, RoutedEventArgs e)
    {
        var today = DateOnly.FromDateTime(DateTime.Now);
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = "导出今日工作 XLSM",
            Filter = "Excel 宏启用工作簿 (*.xlsm)|*.xlsm",
            DefaultExt = ".xlsm",
            AddExtension = true,
            OverwritePrompt = true,
            FileName = $"FloatMate-工作-{today:yyyy-MM-dd}.xlsm"
        };

        if (dialog.ShowDialog(this) != true) return;

        try
        {
            ExportWorkWorkbook(dialog.FileName);
            ShowToast("工作 XLSM 已导出");
        }
        catch (Exception exception)
        {
            System.Windows.MessageBox.Show(this, $"无法导出文件。\n\n{exception.Message}", "导出 XLSM",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    private void ExportWorkDocx_Click(object sender, RoutedEventArgs e)
    {
        var today = DateOnly.FromDateTime(DateTime.Now);
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = "导出今日工作 DOCX",
            Filter = "Word 文档 (*.docx)|*.docx",
            DefaultExt = ".docx",
            AddExtension = true,
            OverwritePrompt = true,
            FileName = $"FloatMate-工作日报-{today:yyyy-MM-dd}.docx"
        };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            ExportWorkDocument(dialog.FileName);
            ShowToast("工作 DOCX 已导出");
        }
        catch (Exception exception)
        {
            System.Windows.MessageBox.Show(this, $"无法导出文件。\n\n{exception.Message}", "导出 DOCX",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    private void ExportHealthXlsx_Click(object sender, RoutedEventArgs e)
    {
        var today = DateOnly.FromDateTime(DateTime.Now);
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = "导出今日健康记录",
            Filter = "Excel 工作簿 (*.xlsx)|*.xlsx",
            DefaultExt = ".xlsx",
            AddExtension = true,
            OverwritePrompt = true,
            FileName = $"FloatMate-健康记录-{today:yyyy-MM-dd}.xlsx"
        };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            ExportHealthWorkbook(dialog.FileName);
            ShowToast("健康表格已导出");
        }
        catch (Exception exception)
        {
            System.Windows.MessageBox.Show(this, $"无法导出文件。\n\n{exception.Message}", "导出健康表格",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    public void ExportWorkWorkbook(string outputPath)
    {
        var today = DateOnly.FromDateTime(DateTime.Now);
        var goals = _allGoals.Where(goal => goal.Date == today).ToList();
        _xlsmExporter.ExportWorkToday(outputPath, today, goals);
    }

    public void ExportWorkDocument(string outputPath)
    {
        var today = DateOnly.FromDateTime(DateTime.Now);
        var goals = _allGoals.Where(goal => goal.Date == today).ToList();
        _workDocxExporter.ExportToday(outputPath, today, goals);
    }

    public void ExportHealthWorkbook(string outputPath)
    {
        var today = DateOnly.FromDateTime(DateTime.Now);
        var records = _records.Where(record => DateOnly.FromDateTime(record.Timestamp) == today).ToList();
        _healthWorkbookExporter.ExportToday(outputPath, today, records);
    }

    private void UpdateSummaries()
    {
        var today = DateOnly.FromDateTime(DateTime.Now);
        var todayRecords = _records.Where(r => DateOnly.FromDateTime(r.Timestamp) == today).ToList();
        var complete = _goals.Count(g => g.IsCompleted);
        GoalCountText.Text = _goals.Count == 0 ? "—" : complete == 0 ? $"{_goals.Count} 条" : $"{complete} / {_goals.Count}";
        EmptyGoalsPanel.Visibility = _goals.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        RecordCountText.Text = todayRecords.Count == 0 ? "暂无记录" : $"{todayRecords.Count} 条记录";
        var water = todayRecords.Where(r => r.Type == "喝水").Sum(r => r.Amount ?? 0);
        var toilet = todayRecords.Count(r => r.Type == "如厕");
        var stand = todayRecords.Count(r => r.Type == "起身");
        var eyes = todayRecords.Count(r => r.Type == "护眼");
        var focusMinutes = _goals.Sum(g => g.FocusSeconds) / 60;
        WaterStatText.Text = water > 0 ? $"喝水 {water:0} ml" : "喝水 —";
        ToiletStatText.Text = toilet > 0 ? $"如厕 {toilet} 次" : "如厕 —";
        StandStatText.Text = stand > 0 ? $"起身 {stand} 次" : "起身 —";
        EyeStatText.Text = eyes > 0 ? $"护眼 {eyes} 次" : "护眼 —";
        MiniGoalText.Text = _goals.Count == 0 ? "暂无目标" : complete == 0 ? $"{_goals.Count} 个目标" : $"完成 {complete}/{_goals.Count}";
        MiniWaterText.Text = water > 0 ? $"{water:0} ml 水" : "喝水 —";
        var workParts = new List<string>();
        if (complete > 0) workParts.Add($"完成 {complete}/{_goals.Count}");
        if (focusMinutes > 0) workParts.Add($"专注 {focusMinutes} 分钟");
        var healthParts = new List<string>();
        if (water > 0) healthParts.Add($"喝水 {water:0} ml");
        if (toilet > 0) healthParts.Add($"如厕 {toilet} 次");
        if (stand + eyes > 0) healthParts.Add($"起身/护眼 {stand + eyes} 次");
        SummaryText.Text = workParts.Count + healthParts.Count == 0
            ? "今天的记录会在这里汇总。"
            : string.Join("  ·  ", workParts) + (workParts.Count > 0 && healthParts.Count > 0 ? "\n" : string.Empty) + string.Join("  ·  ", healthParts);
        UpdateMiniView();
    }

    private void UpdateMiniView()
    {
        MiniTitle.Text = _runningGoal is null ? "今天，从一条轨道开始" : _runningGoal.Title;
        MiniSubtitle.Text = _runningGoal is null
            ? _goals.Count == 0 ? HealthText.Text : $"{HealthText.Text} · {_goals.Count} 个目标"
            : $"专注 {_runningGoal.FocusSeconds / 60:00}:{_runningGoal.FocusSeconds % 60:00} · {_runningGoal.Progress}%";
        MiniProgressBar.Value = _runningGoal?.Progress ?? (_goals.Count == 0 ? 0 : _goals.Average(g => g.Progress));
    }

    private void TrackForegroundApp()
    {
        var sample = _usageMonitor.Capture();
        var usageChanged = false;
        if (sample is not null)
        {
            var date = DateOnly.FromDateTime(sample.CapturedAt);
            var entry = _appUsage.FirstOrDefault(item => item.Date == date &&
                item.ProcessName.Equals(sample.ProcessName, StringComparison.OrdinalIgnoreCase));
            if (entry is null)
            {
                entry = new AppUsageEntry
                {
                    Date = date,
                    ProcessName = sample.ProcessName,
                    DisplayName = sample.DisplayName
                };
                _appUsage.Add(entry);
            }

            entry.DisplayName = sample.DisplayName;
            entry.ActiveSeconds += sample.ActiveSeconds;
            entry.LastSeenAt = sample.CapturedAt;
            usageChanged = true;
        }

        if (UsageScroll.Visibility == Visibility.Visible) RefreshUsageView();
        if (usageChanged && DateTime.Now - _lastUsageSaveAt >= TimeSpan.FromMinutes(1))
        {
            _lastUsageSaveAt = DateTime.Now;
            QueueSave();
        }
    }

    private void RefreshUsageView()
    {
        var today = DateOnly.FromDateTime(DateTime.Now);
        var entries = _appUsage.Where(item => item.Date == today && item.ActiveSeconds > 0)
            .OrderByDescending(item => item.ActiveSeconds)
            .ToList();
        var totalSeconds = entries.Sum(item => item.ActiveSeconds);
        var maxSeconds = entries.FirstOrDefault()?.ActiveSeconds ?? 1;

        UsageTotalText.Text = FormatUsageDuration(totalSeconds);
        UsageCountText.Text = entries.Count == 0 ? "暂无数据" : $"{entries.Count} 个应用";
        UsageTopAppText.Text = entries.Count == 0
            ? "从现在开始累计今天的前台活跃时间"
            : $"使用最多 · {entries[0].DisplayName}  {FormatUsageDuration(entries[0].ActiveSeconds)}";
        UsageTrackingText.Text = _usageMonitor.StatusText;
        UsageList.ItemsSource = entries.Select(item => new AppUsageRow
        {
            DisplayName = item.DisplayName,
            ProcessText = $"{item.ProcessName}.exe  ·  最近 {item.LastSeenAt:HH:mm}",
            DurationText = FormatUsageDuration(item.ActiveSeconds),
            Initial = string.IsNullOrWhiteSpace(item.DisplayName) ? "·" : item.DisplayName[..1].ToUpperInvariant(),
            Share = item.ActiveSeconds * 100D / maxSeconds
        }).ToList();
        EmptyUsagePanel.Visibility = entries.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private static string FormatUsageDuration(int seconds)
    {
        if (seconds < 60) return seconds <= 0 ? "—" : "少于 1 分钟";
        var duration = TimeSpan.FromSeconds(seconds);
        return duration.TotalHours >= 1
            ? $"{(int)duration.TotalHours} 小时 {duration.Minutes} 分钟"
            : $"{duration.Minutes} 分钟";
    }

    private void ShowToday_Click(object sender, RoutedEventArgs e) => ShowTodayView();
    private void ShowPlan_Click(object sender, RoutedEventArgs e) => ShowView(PlanView, PlanNavButton);
    private void ShowUsage_Click(object sender, RoutedEventArgs e)
    {
        RefreshUsageView();
        ShowView(UsageScroll, UsageNavButton);
    }
    private void ShowHistory_Click(object sender, RoutedEventArgs e)
    {
        ShowView(HistoryScroll, HistoryNavButton);
        RefreshHistoryDates();
        if (HistoryDateCombo.SelectedItem is HistoryDateOption option) RefreshHistory(option.Date);
    }
    private void ShowSettings_Click(object sender, RoutedEventArgs e) => ShowView(SettingsScroll, SettingsNavButton);
    private void ShowTodayView() => ShowView(TodayScroll, TodayNavButton);

    private void ShowView(UIElement view, System.Windows.Controls.Button activeButton)
    {
        TodayScroll.Visibility = Visibility.Collapsed;
        PlanView.Visibility = Visibility.Collapsed;
        UsageScroll.Visibility = Visibility.Collapsed;
        HistoryScroll.Visibility = Visibility.Collapsed;
        SettingsScroll.Visibility = Visibility.Collapsed;
        view.Visibility = Visibility.Visible;
        FadeIn(view);
        TodayNavButton.Style = (Style)FindResource("IconButton");
        PlanNavButton.Style = (Style)FindResource("IconButton");
        UsageNavButton.Style = (Style)FindResource("IconButton");
        HistoryNavButton.Style = (Style)FindResource("IconButton");
        SettingsNavButton.Style = (Style)FindResource("IconButton");
        activeButton.Style = (Style)FindResource("SegmentedActiveButton");
    }

    private void ImportPlanWorkbook()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "导入长期工作计划",
            Filter = "Excel 工作簿 (*.xlsx)|*.xlsx|Excel 工作簿 (*.xlsx;*.xlsm)|*.xlsx;*.xlsm",
            DefaultExt = ".xlsx",
            Multiselect = false,
            CheckFileExists = true
        };
        if (dialog.ShowDialog(this) != true) return;

        try
        {
            var plan = ImportPlanWorkbook(dialog.FileName);
            _plans.Add(plan);
            PlanView.SelectPlan(plan);
            ShowView(PlanView, PlanNavButton);
            AddActivity("计划", "导入长期计划", $"{plan.Name} · {plan.Tasks.Count} 项任务");
            ShowToast($"已导入 {plan.Tasks.Count} 项任务");
            RefreshHistoryDates();
            QueueSave();
        }
        catch (Exception exception)
        {
            System.Windows.MessageBox.Show(this, $"无法导入这个工作簿。\n\n{exception.Message}", "导入 XLSX",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    private void ExportPlanWorkbook(LongPlan plan)
    {
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = "导出长期工作计划",
            Filter = "Excel 工作簿 (*.xlsx)|*.xlsx",
            DefaultExt = ".xlsx",
            AddExtension = true,
            OverwritePrompt = true,
            FileName = $"{SanitizeFileName(plan.Name)}-{DateTime.Today:yyyy-MM-dd}.xlsx"
        };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            ExportPlanWorkbook(dialog.FileName, plan);
            ShowToast("长期计划已导出");
        }
        catch (Exception exception)
        {
            System.Windows.MessageBox.Show(this, $"无法导出文件。\n\n{exception.Message}", "导出 XLSX",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    public LongPlan ImportPlanWorkbook(string inputPath) => _planWorkbook.Import(inputPath);
    public void ExportPlanWorkbook(string outputPath, LongPlan plan) => _planWorkbook.Export(outputPath, plan);

    private static string SanitizeFileName(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var cleaned = new string(value.Select(character => invalid.Contains(character) ? '-' : character).ToArray()).Trim();
        return string.IsNullOrWhiteSpace(cleaned) ? "FloatMate-长期计划" : cleaned;
    }

    private void RefreshHistoryDates()
    {
        var selected = (HistoryDateCombo.SelectedItem as HistoryDateOption)?.Date ?? DateOnly.FromDateTime(DateTime.Now);
        var dates = _records.Select(r => DateOnly.FromDateTime(r.Timestamp))
            .Concat(_events.Select(e => DateOnly.FromDateTime(e.Timestamp)))
            .Concat(_allGoals.Select(g => g.Date))
            .Append(DateOnly.FromDateTime(DateTime.Now))
            .Distinct().OrderByDescending(d => d).ToList();
        HistoryDateCombo.ItemsSource = dates.Select(d => new HistoryDateOption(d,
            d == DateOnly.FromDateTime(DateTime.Now) ? $"今天 · {d:M月d日}" : d.ToString("yyyy年M月d日"))).ToList();
        HistoryDateCombo.SelectedItem = HistoryDateCombo.Items.OfType<HistoryDateOption>().FirstOrDefault(x => x.Date == selected)
            ?? HistoryDateCombo.Items.OfType<HistoryDateOption>().FirstOrDefault();
    }

    private void HistoryDateCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (HistoryDateCombo.SelectedItem is HistoryDateOption option) RefreshHistory(option.Date);
    }

    private void RefreshHistory(DateOnly date)
    {
        var records = _records.Where(r => DateOnly.FromDateTime(r.Timestamp) == date).ToList();
        var goals = _allGoals.Where(g => g.Date == date).ToList();
        var rows = records.Select(r => new TimelineRow
        {
            Timestamp = r.Timestamp,
            Icon = r.Type switch { "喝水" => "水", "如厕" => "厕", "起身" => "起", "护眼" => "眼", _ => "·" },
            Title = r.Type,
            Detail = r.Amount.HasValue ? $"独立记录 · {r.Amount:0} {r.Unit}" : "独立健康记录"
        }).Concat(_events.Where(e => DateOnly.FromDateTime(e.Timestamp) == date).Select(e => new TimelineRow
        {
            Timestamp = e.Timestamp,
            Icon = "轨",
            Title = e.Title,
            Detail = e.Detail
        })).OrderByDescending(row => row.Timestamp).ToList();
        HistoryList.ItemsSource = rows;
        EmptyHistoryText.Visibility = rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        var water = records.Where(r => r.Type == "喝水").Sum(r => r.Amount ?? 0);
        var healthCount = records.Count;
        var focus = goals.Sum(g => g.FocusSeconds) / 60;
        var complete = goals.Count(g => g.IsCompleted);
        HistorySummaryText.Text = $"目标完成 {complete}/{goals.Count}  ·  专注 {focus} 分钟\n喝水 {water:0} ml  ·  健康记录 {healthCount} 条  ·  轨道事件 {rows.Count - healthCount} 条";
    }

    private void ReminderSetting_Changed(object sender, RoutedEventArgs e)
    {
        if (_initializing) return;
        _data.Reminders.WaterEnabled = WaterReminderCheck.IsChecked == true;
        _data.Reminders.StandEnabled = StandReminderCheck.IsChecked == true;
        _data.Reminders.EyeEnabled = EyeReminderCheck.IsChecked == true;
        _data.Reminders.WaterMinutes = ReadInterval(WaterIntervalCombo, 60);
        _data.Reminders.StandMinutes = ReadInterval(StandIntervalCombo, 50);
        _data.Reminders.EyeMinutes = ReadInterval(EyeIntervalCombo, 25);
        QueueSave();
    }

    private static int ReadInterval(System.Windows.Controls.ComboBox combo, int fallback) =>
        combo.SelectedItem is ComboBoxItem item && int.TryParse(item.Tag?.ToString(), out var value) ? value : fallback;

    private void CheckReminders()
    {
        CheckReminder("喝水", _data.Reminders.WaterEnabled, _data.Reminders.WaterMinutes,
            "喝水记录", "距上次喝水已过去 {0} 分钟。", "喝水");
        CheckReminder("起身", _data.Reminders.StandEnabled, _data.Reminders.StandMinutes,
            "起身记录", "距上次起身已过去 {0} 分钟。", "起身");
        CheckReminder("护眼", _data.Reminders.EyeEnabled, _data.Reminders.EyeMinutes,
            "护眼记录", "距上次护眼已过去 {0} 分钟。", "护眼");
    }

    private void CheckReminder(string key, bool enabled, int minutes, string title, string message, string recordType)
    {
        if (!enabled || _trayIcon is null) return;
        var lastRecord = _records.Where(r => r.Type == recordType).Select(r => r.Timestamp).DefaultIfEmpty(_sessionStartedAt).Max();
        var elapsed = DateTime.Now - lastRecord;
        if (elapsed < TimeSpan.FromMinutes(minutes)) return;
        if (_lastReminderShown.TryGetValue(key, out var shown) && DateTime.Now - shown < TimeSpan.FromMinutes(10)) return;
        _lastReminderShown[key] = DateTime.Now;
        _trayIcon.BalloonTipTitle = title;
        _trayIcon.BalloonTipText = string.Format(message, Math.Max(1, (int)elapsed.TotalMinutes));
        _trayIcon.BalloonTipIcon = Forms.ToolTipIcon.Info;
        _trayIcon.ShowBalloonTip(5000);
    }

    private void StartupCheck_Click(object sender, RoutedEventArgs e)
    {
        if (_initializing) return;
        try
        {
            StartupManager.SetEnabled(StartupCheck.IsChecked == true);
            ShowToast(StartupCheck.IsChecked == true ? "已开启开机启动" : "已关闭开机启动");
        }
        catch (Exception ex)
        {
            StartupCheck.IsChecked = StartupManager.IsEnabled();
            ShowToast($"开机启动设置失败：{ex.Message}");
        }
    }

    private void DesktopSetting_Changed(object sender, RoutedEventArgs e)
    {
        if (_initializing) return;
        _initializing = true;
        if (sender == DockedRightModeCheck && DockedRightModeCheck.IsChecked == true)
        {
            DesktopWidgetModeCheck.IsChecked = false;
            AutoCollapseCheck.IsChecked = false;
        }
        else if (sender == DesktopWidgetModeCheck && DesktopWidgetModeCheck.IsChecked == true)
        {
            DockedRightModeCheck.IsChecked = false;
        }
        else if (sender == AutoCollapseCheck && AutoCollapseCheck.IsChecked == true)
        {
            DockedRightModeCheck.IsChecked = false;
        }
        _initializing = false;
        _data.DockedRightMode = DockedRightModeCheck.IsChecked == true;
        _data.DesktopWidgetMode = DesktopWidgetModeCheck.IsChecked == true;
        _data.AutoCollapse = AutoCollapseCheck.IsChecked == true;
        ApplyWindowMode();
        QueueSave();
    }

    private void ApplyWindowMode()
    {
        var dockedMode = DockedRightModeCheck.IsChecked == true;
        var desktopMode = DesktopWidgetModeCheck.IsChecked == true;
        if (dockedMode)
        {
            if (!_appBar.IsRegistered)
            {
                _floatingLeft = Left;
                _floatingTop = Top;
            }
            var handle = new WindowInteropHelper(this).Handle;
            if (!_appBar.Register(handle))
            {
                _initializing = true;
                DockedRightModeCheck.IsChecked = false;
                _initializing = false;
                _data.DockedRightMode = false;
                ShowToast("左侧工作区注册失败，已恢复为置顶浮窗");
                Topmost = true;
                return;
            }

            Topmost = true;
            AutoCollapseCheck.IsChecked = false;
            AutoCollapseCheck.IsEnabled = false;
            OpacitySlider.IsEnabled = false;
            FullCollapseButton.Visibility = Visibility.Collapsed;
            FullHideButton.Visibility = Visibility.Collapsed;
            ApplyDockedVisualShell(true);
            SetExpanded(true);
            var dpi = VisualTreeHelper.GetDpi(this).DpiScaleX;
            _appBar.SetLeftPosition((int)Math.Round(420 * dpi));
            WidgetModeText.Text = " · 左侧停靠";
        }
        else
        {
            var wasDocked = _appBar.IsRegistered;
            _appBar.Unregister();
            AutoCollapseCheck.IsEnabled = true;
            OpacitySlider.IsEnabled = true;
            FullCollapseButton.Visibility = Visibility.Visible;
            FullHideButton.Visibility = Visibility.Visible;
            ApplyDockedVisualShell(false);
            Topmost = !desktopMode;
            WidgetModeText.Text = desktopMode ? " · 桌面组件" : " · 置顶浮窗";
            if (wasDocked)
            {
                Width = 420;
                Height = 760;
                Left = double.IsNaN(_floatingLeft) ? SystemParameters.WorkArea.Right - Width - 12 : _floatingLeft;
                Top = double.IsNaN(_floatingTop) ? SystemParameters.WorkArea.Top + 18 : _floatingTop;
            }
        }
        // 非置顶窗口会在其他软件获得焦点时自然留在其后方，桌面仍可见时则保持显示。
    }

    private void ApplyDockedVisualShell(bool docked)
    {
        if (docked)
        {
            WindowFrame.CornerRadius = new CornerRadius(0);
            WindowFrame.BorderThickness = new Thickness(0, 0, 1, 0);
            WindowFrame.Padding = new Thickness(0);
            WindowFrame.BorderBrush = new SolidColorBrush(System.Windows.Media.Color.FromRgb(199, 199, 204));
            WindowFrame.Background = System.Windows.Media.Brushes.White;
            WindowFrame.Effect = null;
            AppTitleText.Text = "本地助手";
            LocalBadgeText.Text = "ON DEVICE";
            Title = "FloatMate 本地助手";
            System.Windows.Automation.AutomationProperties.SetName(this, "FloatMate 左侧本地助手");
        }
        else
        {
            WindowFrame.CornerRadius = new CornerRadius(26);
            WindowFrame.BorderThickness = new Thickness(1);
            WindowFrame.Padding = new Thickness(1);
            WindowFrame.Effect = new System.Windows.Media.Effects.DropShadowEffect
            {
                BlurRadius = 28,
                ShadowDepth = 7,
                Opacity = 0.20,
                Color = System.Windows.Media.Color.FromRgb(88, 88, 96)
            };
            AppTitleText.Text = "浮岛";
            LocalBadgeText.Text = "LOCAL";
            Title = "浮岛 FloatMate";
            System.Windows.Automation.AutomationProperties.SetName(this, "浮岛桌面助手");
            ApplySurfaceOpacity(false);
        }
    }

    private void ApplySurfaceOpacity(bool highlighted)
    {
        if (_appBar.IsRegistered)
        {
            WindowFrame.Background = System.Windows.Media.Brushes.White;
            WindowFrame.BorderBrush = new SolidColorBrush(System.Windows.Media.Color.FromRgb(199, 199, 204));
            return;
        }
        var surfaceOpacity = Math.Clamp(_data.Opacity, 0.86, 1.0);
        if (highlighted) surfaceOpacity = Math.Min(1.0, surfaceOpacity + 0.04);
        var alpha = (byte)Math.Round(surfaceOpacity * 255);
        WindowFrame.Background = new SolidColorBrush(System.Windows.Media.Color.FromArgb(alpha, 255, 255, 255));
        WindowFrame.BorderBrush = new SolidColorBrush(System.Windows.Media.Color.FromArgb(
            highlighted ? (byte)205 : (byte)175, 199, 199, 204));
    }

    private void OpacitySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!IsLoaded) return;
        _data.Opacity = e.NewValue;
        ApplySurfaceOpacity(IsMouseOver);
        QueueSave();
    }

    private void Window_Deactivated(object? sender, EventArgs e)
    {
        if (!_appBar.IsRegistered && _isExpanded && AutoCollapseCheck.IsChecked == true)
        {
            _collapseTimer.Stop();
            _collapseTimer.Start();
        }
    }

    private void Window_MouseEnter(object sender, System.Windows.Input.MouseEventArgs e)
    {
        _collapseTimer.Stop();
        ApplySurfaceOpacity(true);
    }

    private void Window_MouseLeave(object sender, System.Windows.Input.MouseEventArgs e)
    {
        ApplySurfaceOpacity(false);
        if (!_appBar.IsRegistered && _isExpanded && !IsActive && AutoCollapseCheck.IsChecked == true) _collapseTimer.Start();
    }

    private void ShowToast(string message, bool showUndo = false)
    {
        ToastText.Text = message;
        ToastPanel.Visibility = Visibility.Visible;
        UndoDeleteButton.Visibility = showUndo ? Visibility.Visible : Visibility.Collapsed;
        _toastTimer.Interval = TimeSpan.FromSeconds(showUndo ? 5 : 3);
        _toastTimer.Stop();
        _toastTimer.Start();
        FadeIn(ToastPanel);
    }

    private void HideToast()
    {
        _toastTimer.Stop();
        ToastPanel.Visibility = Visibility.Collapsed;
        UndoDeleteButton.Visibility = Visibility.Collapsed;
        _lastDeletedGoal = null;
        _lastDeletedTodayIndex = -1;
        _lastDeletedAllIndex = -1;
        _toastTimer.Interval = TimeSpan.FromSeconds(3);
    }

    private static void FadeIn(UIElement element)
    {
        element.BeginAnimation(OpacityProperty, null);
        element.Opacity = 1;
        if (!SystemParameters.ClientAreaAnimation) return;
        var animation = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(180))
        {
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
        };
        element.BeginAnimation(OpacityProperty, animation);
    }

    private void ToggleView_Click(object sender, RoutedEventArgs e) => SetExpanded(!_isExpanded);

    private void SetExpanded(bool expanded)
    {
        if (_appBar.IsRegistered && !expanded) return;
        var rightEdge = double.IsNaN(Left) ? SystemParameters.WorkArea.Right - 12 : Left + ActualWidth;
        _isExpanded = expanded;
        Width = expanded ? 420 : 336;
        if (expanded)
        {
            MiniView.Visibility = Visibility.Collapsed;
            FullView.Visibility = Visibility.Visible;
            Height = 760;
            MinHeight = 300;
        }
        else
        {
            FullView.Visibility = Visibility.Collapsed;
            MiniView.Visibility = Visibility.Visible;
            MinHeight = 190;
            Height = 190;
        }
        FadeIn(expanded ? FullView : MiniView);
        if (rightEdge > 0) Left = Math.Max(SystemParameters.VirtualScreenLeft, rightEdge - Width);
        WindowFrame.CornerRadius = _appBar.IsRegistered ? new CornerRadius(0) : new CornerRadius(26);
        QueueSave();
    }

    private void Hide_Click(object sender, RoutedEventArgs e) => Hide();

    private void ToggleWindowVisibility()
    {
        if (_appBar.IsRegistered) ShowAndActivate();
        else if (IsVisible && IsActive) Hide(); else ShowAndActivate();
    }

    private void ShowAndActivate()
    {
        Show();
        WindowState = WindowState.Normal;
        SetExpanded(true);
        if (_appBar.IsRegistered) _appBar.RefreshPosition();
        if (DesktopWidgetModeCheck.IsChecked == true) Topmost = false;
        Activate();
    }

    private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (!_appBar.IsRegistered && e.ButtonState == MouseButtonState.Pressed &&
            e.OriginalSource is not System.Windows.Controls.Button && e.OriginalSource is not System.Windows.Controls.TextBox &&
            e.OriginalSource is not Slider && e.OriginalSource is not System.Windows.Controls.CheckBox)
        {
            try { DragMove(); } catch { }
        }
    }

    private void Window_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_appBar.IsRegistered) return;
        const double snap = 18;
        var area = SystemParameters.WorkArea;
        if (Math.Abs(Left - area.Left) < snap) Left = area.Left;
        if (Math.Abs((Left + Width) - area.Right) < snap) Left = area.Right - Width;
        if (Math.Abs(Top - area.Top) < snap) Top = area.Top;
        if (Math.Abs((Top + Height) - area.Bottom) < snap) Top = area.Bottom - Height;
        QueueSave();
    }

    private void QueueSave()
    {
        if (_initializing) return;
        _saveTimer.Stop();
        _saveTimer.Start();
    }

    private void SaveNow()
    {
        if (_previewMode) return;
        _data.ActiveDate = DateOnly.FromDateTime(DateTime.Now);
        _data.Goals = _allGoals.ToList();
        _data.Records = _records.ToList();
        _data.Events = _events.ToList();
        _data.AppUsage = _appUsage.ToList();
        _data.Plans = _plans.ToList();
        if (!_appBar.IsRegistered)
        {
            _data.WindowLeft = Left;
            _data.WindowTop = Top;
        }
        _data.IsExpanded = _isExpanded;
        _data.Opacity = OpacitySlider.Value;
        _data.AutoCollapse = AutoCollapseCheck.IsChecked == true;
        _data.DesktopWidgetMode = DesktopWidgetModeCheck.IsChecked == true;
        _data.DockedRightMode = DockedRightModeCheck.IsChecked == true;
        _store.Save(_data);
    }

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (!_allowExit)
        {
            e.Cancel = true;
            if (_appBar.IsRegistered) ShowAndActivate(); else Hide();
            return;
        }
        SaveNow();
        _appBar.Dispose();
        _trayIcon?.Dispose();
        UnregisterHotKey(new WindowInteropHelper(this).Handle, HotkeyId);
    }

    private void ExitApplication()
    {
        _allowExit = true;
        Close();
        System.Windows.Application.Current.Shutdown();
    }

    public void CapturePreview(string outputPath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath) ?? Environment.CurrentDirectory);
        var width = Math.Max(1, (int)Math.Ceiling(ActualWidth));
        var height = Math.Max(1, (int)Math.Ceiling(ActualHeight));
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(this);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(outputPath);
        encoder.Save(stream);
    }

    public void EnablePreviewMode() => _previewMode = true;

    public void PreparePreview(bool expanded, string page)
    {
        _previewMode = true;
        _collapseTimer.Stop();
        AutoCollapseCheck.IsChecked = false;
        if (_goals.Count == 0)
        {
            var sample = new GoalItem
            {
                Title = "优化桌面助手交互",
                Status = "进行中",
                Progress = 70,
                FocusSeconds = 42 * 60,
                EstimateMinutes = 90,
                Details = "整理目标详情录入流程\n验证本地保存、历史复盘和 XLSM 导出",
                IsRunning = true
            };
            _goals.Add(sample);
            _runningGoal = sample;
        }
        if (page.StartsWith("details", StringComparison.OrdinalIgnoreCase))
        {
            var detailsSample = new GoalItem
            {
                Title = "整理发布前检查清单",
                Status = "进行中",
                Progress = 40,
                FocusSeconds = 28 * 60,
                EstimateMinutes = 60,
                Details = "核对安装包与版本号\n完成主流程回归测试\n整理发布说明和已知限制"
            };
            if (page.Equals("details-edit", StringComparison.OrdinalIgnoreCase))
            {
                detailsSample.EditingDetails = detailsSample.Details;
                detailsSample.IsEditingDetails = true;
            }
            _goals.Insert(0, detailsSample);
        }
        if (page.Equals("usage", StringComparison.OrdinalIgnoreCase))
        {
            var today = DateOnly.FromDateTime(DateTime.Now);
            _appUsage.Clear();
            _appUsage.AddRange(
            [
                new AppUsageEntry { Date = today, ProcessName = "Code", DisplayName = "Visual Studio Code", ActiveSeconds = 2 * 3600 + 18 * 60, LastSeenAt = DateTime.Now.AddMinutes(-2) },
                new AppUsageEntry { Date = today, ProcessName = "msedge", DisplayName = "Microsoft Edge", ActiveSeconds = 74 * 60, LastSeenAt = DateTime.Now.AddMinutes(-8) },
                new AppUsageEntry { Date = today, ProcessName = "WindowsTerminal", DisplayName = "Windows Terminal", ActiveSeconds = 39 * 60, LastSeenAt = DateTime.Now.AddMinutes(-18) },
                new AppUsageEntry { Date = today, ProcessName = "explorer", DisplayName = "文件资源管理器", ActiveSeconds = 12 * 60, LastSeenAt = DateTime.Now.AddMinutes(-31) }
            ]);
        }
        if (page.StartsWith("plan", StringComparison.OrdinalIgnoreCase) && _plans.Count == 0)
        {
            var plan = new LongPlan
            {
                Name = "产品交付 · 40 天计划",
                StartDate = DateTime.Today,
                EndDate = DateTime.Today.AddDays(39),
                SourceFileName = "台州计划.xlsx",
                Tasks =
                [
                    new LongPlanTask { Order = 1, Category = "环境", Title = "场地规划与准备", Owner = "孙杰", Status = "已完成", Progress = 100, StartDate = DateTime.Today, EndDate = DateTime.Today.AddDays(5) },
                    new LongPlanTask { Order = 2, Category = "设备", Title = "设备采购与进场", Owner = "任霄原", Status = "进行中", Progress = 55, StartDate = DateTime.Today.AddDays(4), EndDate = DateTime.Today.AddDays(21), Milestone = "设备进场" },
                    new LongPlanTask { Order = 3, Category = "物料", Title = "零部件开发和验证", Owner = "研发团队", Status = "未开始", Progress = 0, StartDate = DateTime.Today.AddDays(14), EndDate = DateTime.Today.AddDays(33) }
                ]
            };
            _plans.Add(plan);
            _plans.Add(new LongPlan
            {
                Name = "EVA 功能测试排期",
                StartDate = DateTime.Today.AddDays(-7),
                EndDate = DateTime.Today.AddDays(48),
                Tasks =
                [
                    new LongPlanTask { Order = 1, Category = "范围", Title = "确认任务与验收清单", Status = "已完成", Progress = 100, StartDate = DateTime.Today.AddDays(-7), EndDate = DateTime.Today.AddDays(-5) },
                    new LongPlanTask { Order = 2, Category = "联调", Title = "模拟车机控制闭环", Status = "进行中", Progress = 35, StartDate = DateTime.Today, EndDate = DateTime.Today.AddDays(8) }
                ]
            });
            PlanView.BindPlans(_plans);
        }
        if ((page.Equals("plan-expanded", StringComparison.OrdinalIgnoreCase) || page.Equals("plan-completed", StringComparison.OrdinalIgnoreCase)) && _plans.Count > 0)
            PlanView.SelectPlan(_plans[0]);
        SetExpanded(expanded);
        if (page.Equals("settings", StringComparison.OrdinalIgnoreCase)) ShowView(SettingsScroll, SettingsNavButton);
        else if (page.StartsWith("plan", StringComparison.OrdinalIgnoreCase)) ShowView(PlanView, PlanNavButton);
        else if (page.Equals("usage", StringComparison.OrdinalIgnoreCase))
        {
            RefreshUsageView();
            ShowView(UsageScroll, UsageNavButton);
        }
        else if (page.Equals("history", StringComparison.OrdinalIgnoreCase))
        {
            ShowView(HistoryScroll, HistoryNavButton);
            RefreshHistoryDates();
            if (HistoryDateCombo.SelectedItem is HistoryDateOption option) RefreshHistory(option.Date);
        }
        else ShowTodayView();
        if (page.Equals("add-goal", StringComparison.OrdinalIgnoreCase))
        {
            AddGoalPanel.Visibility = Visibility.Visible;
            GoalInput.Text = "整理本周开发交付";
            GoalDetailsInput.Text = "完成核心流程自测\n补充发布说明与待办事项";
        }
        UpdateSummaries();
        UpdateLayout();
        if (page.Equals("plan-completed", StringComparison.OrdinalIgnoreCase)) PlanView.PrepareCompletedPreview();
        if (page.StartsWith("details", StringComparison.OrdinalIgnoreCase) || page.Equals("add-goal", StringComparison.OrdinalIgnoreCase))
            TodayScroll.ScrollToTop();
        if (page.Equals("today-bottom", StringComparison.OrdinalIgnoreCase)) TodayScroll.ScrollToEnd();
    }

    public void ExitAfterPreview()
    {
        _allowExit = true;
        Close();
        System.Windows.Application.Current.Shutdown();
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == AppBarManager.CallbackMessage && wParam.ToInt32() == AppBarManager.AbnPosChanged)
        {
            _appBar.RefreshPosition();
            handled = true;
        }
        else if (msg == 0x0006 && _appBar.IsRegistered)
        {
            _appBar.Activate();
        }
        else if (msg == 0x0047 && _appBar.IsRegistered)
        {
            _appBar.NotifyWindowPositionChanged();
        }
        if (msg == 0x0312 && wParam.ToInt32() == HotkeyId)
        {
            ToggleWindowVisibility();
            handled = true;
        }
        return IntPtr.Zero;
    }

    [DllImport("user32.dll")]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll")]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

}
