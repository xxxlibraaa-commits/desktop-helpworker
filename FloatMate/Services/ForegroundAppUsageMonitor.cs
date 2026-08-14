using System.Diagnostics;
using System.Runtime.InteropServices;

namespace FloatMate.Services;

public sealed class ForegroundAppUsageMonitor
{
    private static readonly Dictionary<string, string> KnownNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["chrome"] = "Google Chrome",
        ["msedge"] = "Microsoft Edge",
        ["firefox"] = "Mozilla Firefox",
        ["Code"] = "Visual Studio Code",
        ["devenv"] = "Visual Studio",
        ["WindowsTerminal"] = "Windows Terminal",
        ["powershell"] = "Windows PowerShell",
        ["pwsh"] = "PowerShell",
        ["EXCEL"] = "Microsoft Excel",
        ["WINWORD"] = "Microsoft Word",
        ["POWERPNT"] = "Microsoft PowerPoint",
        ["OUTLOOK"] = "Microsoft Outlook",
        ["ms-teams"] = "Microsoft Teams",
        ["Teams"] = "Microsoft Teams",
        ["explorer"] = "文件资源管理器",
        ["notepad"] = "记事本",
        ["Spotify"] = "Spotify",
        ["WeChat"] = "微信",
        ["QQ"] = "QQ",
        ["Discord"] = "Discord",
        ["slack"] = "Slack"
    };

    private static readonly HashSet<string> ExcludedProcesses = new(StringComparer.OrdinalIgnoreCase)
    {
        "FloatMate", "LockApp", "LogonUI", "dwm", "ShellExperienceHost", "StartMenuExperienceHost",
        "SearchHost", "TextInputHost", "ApplicationFrameHost"
    };

    private readonly Dictionary<string, string> _displayNameCache = new(StringComparer.OrdinalIgnoreCase);
    private DateTime _lastSampleAt = DateTime.Now;

    public string StatusText { get; private set; } = "正在本机统计";

    public ForegroundAppSample? Capture()
    {
        var now = DateTime.Now;
        var elapsed = now - _lastSampleAt;
        _lastSampleAt = now;
        if (elapsed <= TimeSpan.Zero || elapsed > TimeSpan.FromSeconds(12)) return null;

        if (GetIdleTime() >= TimeSpan.FromMinutes(5))
        {
            StatusText = "当前空闲，不计入使用时间";
            return null;
        }

        var window = GetForegroundWindow();
        if (window == IntPtr.Zero)
        {
            StatusText = "等待前台应用";
            return null;
        }

        GetWindowThreadProcessId(window, out var processId);
        if (processId == 0) return null;
        processId = ResolveHostedAppProcess(window, processId);

        try
        {
            using var process = Process.GetProcessById((int)processId);
            var processName = process.ProcessName;
            if (ExcludedProcesses.Contains(processName))
            {
                StatusText = processName.Equals("FloatMate", StringComparison.OrdinalIgnoreCase)
                    ? "查看助手时暂停统计"
                    : "系统界面不计入使用时间";
                return null;
            }

            var displayName = ResolveDisplayName(process, processName);
            StatusText = $"正在统计 · {displayName}";
            return new ForegroundAppSample(processName, displayName, Math.Max(1, (int)Math.Round(elapsed.TotalSeconds)), now);
        }
        catch
        {
            StatusText = "等待前台应用";
            return null;
        }
    }

    public void ResetClock() => _lastSampleAt = DateTime.Now;

    private string ResolveDisplayName(Process process, string processName)
    {
        if (KnownNames.TryGetValue(processName, out var knownName)) return knownName;
        if (_displayNameCache.TryGetValue(processName, out var cachedName)) return cachedName;

        var displayName = processName;
        try
        {
            var description = process.MainModule?.FileVersionInfo.FileDescription?.Trim();
            if (!string.IsNullOrWhiteSpace(description)) displayName = description;
        }
        catch
        {
            // Protected and packaged processes may not expose executable metadata.
        }

        displayName = displayName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? displayName[..^4] : displayName;
        _displayNameCache[processName] = displayName;
        return displayName;
    }

    private static uint ResolveHostedAppProcess(IntPtr window, uint hostProcessId)
    {
        try
        {
            using var host = Process.GetProcessById((int)hostProcessId);
            if (!host.ProcessName.Equals("ApplicationFrameHost", StringComparison.OrdinalIgnoreCase)) return hostProcessId;
        }
        catch
        {
            return hostProcessId;
        }

        var childProcessId = hostProcessId;
        EnumChildWindows(window, (child, _) =>
        {
            GetWindowThreadProcessId(child, out var candidate);
            if (candidate != 0 && candidate != hostProcessId)
            {
                childProcessId = candidate;
                return false;
            }
            return true;
        }, IntPtr.Zero);
        return childProcessId;
    }

    private static TimeSpan GetIdleTime()
    {
        var input = new LastInputInfo { Size = (uint)Marshal.SizeOf<LastInputInfo>() };
        if (!GetLastInputInfo(ref input)) return TimeSpan.Zero;
        var elapsedMilliseconds = unchecked((uint)Environment.TickCount - input.Time);
        return TimeSpan.FromMilliseconds(elapsedMilliseconds);
    }

    private delegate bool EnumWindowsProc(IntPtr window, IntPtr parameter);

    [StructLayout(LayoutKind.Sequential)]
    private struct LastInputInfo
    {
        public uint Size;
        public uint Time;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);

    [DllImport("user32.dll")]
    private static extern bool EnumChildWindows(IntPtr parent, EnumWindowsProc callback, IntPtr parameter);

    [DllImport("user32.dll")]
    private static extern bool GetLastInputInfo(ref LastInputInfo input);
}

public sealed record ForegroundAppSample(string ProcessName, string DisplayName, int ActiveSeconds, DateTime CapturedAt);
