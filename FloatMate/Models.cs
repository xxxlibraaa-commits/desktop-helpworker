using System.ComponentModel;
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

    public Guid Id { get; set; } = Guid.NewGuid();
    public DateOnly Date { get; set; } = DateOnly.FromDateTime(DateTime.Now);
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public DateTime? CompletedAt { get; set; }
    public string Title { get => _title; set => Set(ref _title, value); }
    public string Status { get => _status; set { if (Set(ref _status, value)) { OnPropertyChanged(nameof(StatusLabel)); OnPropertyChanged(nameof(IsCompleted)); OnPropertyChanged(nameof(ActionText)); OnPropertyChanged(nameof(CanStart)); } } }
    public int FocusSeconds { get => _focusSeconds; set { if (Set(ref _focusSeconds, value)) OnPropertyChanged(nameof(FocusText)); } }
    public int Progress { get => _progress; set { if (Set(ref _progress, Math.Clamp(value, 0, 100))) OnPropertyChanged(nameof(ProgressText)); } }
    public int EstimateMinutes { get => _estimateMinutes; set { if (Set(ref _estimateMinutes, Math.Max(5, value))) OnPropertyChanged(nameof(EstimateText)); } }
    [JsonIgnore] public bool IsRunning { get => _isRunning; set { if (Set(ref _isRunning, value)) OnPropertyChanged(nameof(ActionText)); } }
    [JsonIgnore] public bool IsCompleted => Status == "已完成";
    [JsonIgnore] public string FocusText => FocusSeconds < 60 ? "尚未计时" : $"专注 {FocusSeconds / 60} 分钟";
    [JsonIgnore] public string StatusLabel => IsRunning ? "进行中" : Status;
    [JsonIgnore] public string ActionText => IsCompleted ? "完成" : IsRunning ? "暂停" : "开始";
    [JsonIgnore] public bool CanStart => !IsCompleted;
    [JsonIgnore] public string ProgressText => Progress == 0 ? string.Empty : $"{Progress}%";
    [JsonIgnore] public string EstimateText => $"预计 {EstimateMinutes} 分钟";

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

public sealed class AppData
{
    public int SchemaVersion { get; set; } = 7;
    public DateOnly ActiveDate { get; set; } = DateOnly.FromDateTime(DateTime.Now);
    public List<GoalItem> Goals { get; set; } = [];
    public List<QuickRecord> Records { get; set; } = [];
    public List<ActivityEvent> Events { get; set; } = [];
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
