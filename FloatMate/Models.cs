using System.ComponentModel;
using System.Collections.ObjectModel;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;

namespace FloatMate;

public sealed class GoalItem : INotifyPropertyChanged
{
    private string _title = string.Empty;
    private string _status = "未开始";
    private int _focusSeconds;
    private int _progress;
    private int _estimateMinutes = 60;
    private bool _isRunning;
    private string _details = string.Empty;
    private string _editingDetails = string.Empty;
    private bool _isEditingDetails;

    public Guid Id { get; set; } = Guid.NewGuid();
    public DateOnly Date { get; set; } = DateOnly.FromDateTime(DateTime.Now);
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public DateTime? CompletedAt { get; set; }
    public string Title { get => _title; set => Set(ref _title, value); }
    public string Status { get => _status; set { if (Set(ref _status, value)) { OnPropertyChanged(nameof(StatusLabel)); OnPropertyChanged(nameof(IsCompleted)); OnPropertyChanged(nameof(ActionText)); OnPropertyChanged(nameof(CanStart)); } } }
    public int FocusSeconds { get => _focusSeconds; set { if (Set(ref _focusSeconds, value)) OnPropertyChanged(nameof(FocusText)); } }
    public int Progress { get => _progress; set { if (Set(ref _progress, Math.Clamp(value, 0, 100))) OnPropertyChanged(nameof(ProgressText)); } }
    public int EstimateMinutes { get => _estimateMinutes; set { if (Set(ref _estimateMinutes, Math.Max(5, value))) OnPropertyChanged(nameof(EstimateText)); } }
    public string Details
    {
        get => _details;
        set
        {
            if (!Set(ref _details, value ?? string.Empty)) return;
            OnPropertyChanged(nameof(HasDetails));
            OnPropertyChanged(nameof(DetailsPreview));
            OnPropertyChanged(nameof(DetailsActionText));
        }
    }
    [JsonIgnore] public bool IsRunning { get => _isRunning; set { if (Set(ref _isRunning, value)) OnPropertyChanged(nameof(ActionText)); } }
    [JsonIgnore] public string EditingDetails { get => _editingDetails; set => Set(ref _editingDetails, value ?? string.Empty); }
    [JsonIgnore] public bool IsEditingDetails { get => _isEditingDetails; set => Set(ref _isEditingDetails, value); }
    [JsonIgnore] public bool IsCompleted => Status == "已完成";
    [JsonIgnore] public string FocusText => FocusSeconds < 60 ? "尚未计时" : $"专注 {FocusSeconds / 60} 分钟";
    [JsonIgnore] public string StatusLabel => IsRunning ? "进行中" : Status;
    [JsonIgnore] public string ActionText => IsCompleted ? "完成" : IsRunning ? "暂停" : "开始";
    [JsonIgnore] public bool CanStart => !IsCompleted;
    [JsonIgnore] public string ProgressText => Progress == 0 ? string.Empty : $"{Progress}%";
    [JsonIgnore] public string EstimateText => $"预计 {EstimateMinutes} 分钟";
    [JsonIgnore] public bool HasDetails => !string.IsNullOrWhiteSpace(Details);
    [JsonIgnore] public string DetailsActionText => HasDetails ? "编辑内容" : "添加内容";
    [JsonIgnore] public string DetailsPreview
    {
        get
        {
            var preview = string.Join(" · ", Details.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
                .Select(line => line.Trim()).Where(line => line.Length > 0));
            return preview.Length <= 160 ? preview : $"{preview[..157]}…";
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(name);
        return true;
    }
    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public sealed class QuickRecord
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Type { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; } = DateTime.Now;
    public double? Amount { get; set; }
    public string? Unit { get; set; }
}

public sealed class ActivityEvent
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public DateTime Timestamp { get; set; } = DateTime.Now;
    public string Category { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Detail { get; set; } = string.Empty;
    public Guid? GoalId { get; set; }
}

public sealed class ReminderSettings
{
    public bool WaterEnabled { get; set; } = true;
    public int WaterMinutes { get; set; } = 60;
    public bool StandEnabled { get; set; } = true;
    public int StandMinutes { get; set; } = 50;
    public bool EyeEnabled { get; set; } = true;
    public int EyeMinutes { get; set; } = 25;
}

public sealed class TimelineRow
{
    public DateTime Timestamp { get; set; }
    public string TimeText => Timestamp.ToString("HH:mm");
    public string Icon { get; set; } = "·";
    public string Title { get; set; } = string.Empty;
    public string Detail { get; set; } = string.Empty;
}

public sealed class AppUsageEntry
{
    public DateOnly Date { get; set; } = DateOnly.FromDateTime(DateTime.Now);
    public string ProcessName { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public int ActiveSeconds { get; set; }
    public DateTime LastSeenAt { get; set; } = DateTime.Now;
}

public sealed class AppUsageRow
{
    public string DisplayName { get; init; } = string.Empty;
    public string ProcessText { get; init; } = string.Empty;
    public string DurationText { get; init; } = string.Empty;
    public string Initial { get; init; } = "·";
    public double Share { get; init; }
}

public sealed class LongPlan : INotifyPropertyChanged
{
    private string _name = "长期工作计划";
    private DateTime _startDate = DateTime.Today;
    private DateTime _endDate = DateTime.Today.AddDays(39);
    private string _sourceFileName = string.Empty;
    private bool _isExpanded;

    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get => _name; set => Set(ref _name, string.IsNullOrWhiteSpace(value) ? "长期工作计划" : value.Trim()); }
    public DateTime StartDate { get => _startDate.Date; set { if (Set(ref _startDate, value.Date)) OnPropertyChanged(nameof(RangeText)); } }
    public DateTime EndDate { get => _endDate.Date; set { if (Set(ref _endDate, value.Date)) OnPropertyChanged(nameof(RangeText)); } }
    public string SourceFileName { get => _sourceFileName; set => Set(ref _sourceFileName, value ?? string.Empty); }
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public ObservableCollection<LongPlanTask> Tasks { get; set; } = [];

    [JsonIgnore] public bool IsExpanded { get => _isExpanded; set => Set(ref _isExpanded, value); }
    [JsonIgnore] public string RangeText => $"{StartDate:M月d日} — {EndDate:M月d日} · {Math.Max(1, (EndDate - StartDate).Days + 1)} 天";
    [JsonIgnore] public int Progress => Tasks.Count == 0 ? 0 : (int)Math.Round(Tasks.Average(task => task.Progress));
    [JsonIgnore] public string ProgressText => Tasks.Count == 0 ? "尚未添加任务" : $"{Tasks.Count(task => task.Status == "已完成")}/{Tasks.Count} 已完成 · {Progress}%";

    public override string ToString() => Name;

    public event PropertyChangedEventHandler? PropertyChanged;
    public void NotifySummaryChanged()
    {
        OnPropertyChanged(nameof(Progress));
        OnPropertyChanged(nameof(ProgressText));
        OnPropertyChanged(nameof(RangeText));
    }
    private bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(name);
        return true;
    }
    private void OnPropertyChanged([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public sealed class LongPlanTask : INotifyPropertyChanged
{
    private string _category = "未分类";
    private string _title = "新任务";
    private string _owner = string.Empty;
    private string _status = "未开始";
    private int _progress;
    private DateTime? _startDate;
    private DateTime? _endDate;
    private string _notes = string.Empty;
    private string _milestone = string.Empty;
    private double _timelineLeft;
    private double _timelineWidth;

    public Guid Id { get; set; } = Guid.NewGuid();
    public int Order { get; set; }
    public string Category { get => _category; set => Set(ref _category, string.IsNullOrWhiteSpace(value) ? "未分类" : value.Trim()); }
    public string Title { get => _title; set => Set(ref _title, string.IsNullOrWhiteSpace(value) ? "新任务" : value.Trim()); }
    public string Owner { get => _owner; set { if (Set(ref _owner, value?.Trim() ?? string.Empty)) OnPropertyChanged(nameof(MetaText)); } }
    public string Status { get => _status; set => Set(ref _status, string.IsNullOrWhiteSpace(value) ? "未开始" : value); }
    public int Progress { get => _progress; set => Set(ref _progress, Math.Clamp(value, 0, 100)); }
    public DateTime? StartDate { get => _startDate?.Date; set { if (Set(ref _startDate, value?.Date)) OnPropertyChanged(nameof(DateText)); } }
    public DateTime? EndDate { get => _endDate?.Date; set { if (Set(ref _endDate, value?.Date)) OnPropertyChanged(nameof(DateText)); } }
    public string Notes { get => _notes; set => Set(ref _notes, value ?? string.Empty); }
    public string Milestone { get => _milestone; set { if (Set(ref _milestone, value ?? string.Empty)) OnPropertyChanged(nameof(MetaText)); } }

    [JsonIgnore] public bool IsScheduled => StartDate.HasValue && EndDate.HasValue;
    [JsonIgnore] public string DateText => IsScheduled ? $"{StartDate:M月d日} — {EndDate:M月d日}" : "尚未安排时间";
    [JsonIgnore] public string MetaText
    {
        get
        {
            var parts = new[] { Category, Owner, Milestone }.Where(value => !string.IsNullOrWhiteSpace(value));
            return string.Join(" · ", parts);
        }
    }
    [JsonIgnore] public double TimelineLeft { get => _timelineLeft; set => Set(ref _timelineLeft, value); }
    [JsonIgnore] public double TimelineWidth { get => _timelineWidth; set => Set(ref _timelineWidth, value); }

    public event PropertyChangedEventHandler? PropertyChanged;
    public void NotifyScheduleChanged()
    {
        OnPropertyChanged(nameof(IsScheduled));
        OnPropertyChanged(nameof(DateText));
    }
    private bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(name);
        return true;
    }
    private void OnPropertyChanged([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public sealed class AppData
{
    public int SchemaVersion { get; set; } = 10;
    public DateOnly ActiveDate { get; set; } = DateOnly.FromDateTime(DateTime.Now);
    public List<GoalItem> Goals { get; set; } = [];
    public List<QuickRecord> Records { get; set; } = [];
    public List<ActivityEvent> Events { get; set; } = [];
    public List<AppUsageEntry> AppUsage { get; set; } = [];
    public List<LongPlan> Plans { get; set; } = [];
    public ReminderSettings Reminders { get; set; } = new();
    public double WindowLeft { get; set; } = double.NaN;
    public double WindowTop { get; set; } = double.NaN;
    public bool IsExpanded { get; set; }
    public double Opacity { get; set; } = 0.94;
    public bool AutoCollapse { get; set; } = true;
    public bool DesktopWidgetMode { get; set; } = true;
    public bool DockedRightMode { get; set; } = true;
}

public sealed record HistoryDateOption(DateOnly Date, string Label)
{
    public override string ToString() => Label;
}

public sealed record SystemSnapshot(
    double CpuPercent,
    double MemoryPercent,
    double MemoryUsedGb,
    double MemoryTotalGb,
    double DiskPercent,
    double DiskFreeGb,
    double DownloadKb,
    double UploadKb);
