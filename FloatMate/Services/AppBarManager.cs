using System.Runtime.InteropServices;
using Forms = System.Windows.Forms;

namespace FloatMate.Services;

/// <summary>Registers FloatMate as a Windows application desktop toolbar (AppBar).</summary>
public sealed class AppBarManager : IDisposable
{
    public const int CallbackMessage = 0x8001; // WM_APP + 1
    public const int AbnPosChanged = 1;
    private const uint AbmNew = 0x00000000;
    private const uint AbmRemove = 0x00000001;
    private const uint AbmQueryPos = 0x00000002;
    private const uint AbmSetPos = 0x00000003;
    private const uint AbmActivate = 0x00000006;
    private const uint AbmWindowPosChanged = 0x00000009;
    private const uint AbeLeft = 0;

    private IntPtr _windowHandle;
    private bool _registered;
    private bool _positioning;
    private int _lastWidthPixels = 420;

    public bool IsRegistered => _registered;

    public bool Register(IntPtr windowHandle)
    {
        if (_registered && _windowHandle == windowHandle) return true;
        if (_registered) Unregister();

        _windowHandle = windowHandle;
        var data = CreateData();
        data.CallbackMessage = CallbackMessage;
        _registered = SHAppBarMessage(AbmNew, ref data) != UIntPtr.Zero;
        return _registered;
    }

    public void SetLeftPosition(int widthPixels)
    {
        if (!_registered || _windowHandle == IntPtr.Zero || _positioning) return;
        _positioning = true;
        try
        {
            var screenBounds = Forms.Screen.FromHandle(_windowHandle).Bounds;
            _lastWidthPixels = Math.Clamp(widthPixels, 320, Math.Max(320, screenBounds.Width / 2));
            var data = CreateData();
            data.Edge = AbeLeft;
            data.Rect = new NativeRect
            {
                Left = screenBounds.Left,
                Top = screenBounds.Top,
                Right = screenBounds.Left + _lastWidthPixels,
                Bottom = screenBounds.Bottom
            };

            SHAppBarMessage(AbmQueryPos, ref data);
            data.Rect.Right = data.Rect.Left + _lastWidthPixels;
            SHAppBarMessage(AbmSetPos, ref data);
            MoveWindow(_windowHandle, data.Rect.Left, data.Rect.Top,
                data.Rect.Right - data.Rect.Left, data.Rect.Bottom - data.Rect.Top, true);
        }
        finally
        {
            _positioning = false;
        }
    }

    public void RefreshPosition() => SetLeftPosition(_lastWidthPixels);

    public void Activate()
    {
        if (!_registered) return;
        var data = CreateData();
        SHAppBarMessage(AbmActivate, ref data);
    }

    public void NotifyWindowPositionChanged()
    {
        if (!_registered || _positioning) return;
        var data = CreateData();
        SHAppBarMessage(AbmWindowPosChanged, ref data);
    }

    public void Unregister()
    {
        if (!_registered || _windowHandle == IntPtr.Zero) return;
        var data = CreateData();
        SHAppBarMessage(AbmRemove, ref data);
        _registered = false;
        _windowHandle = IntPtr.Zero;
    }

    public void Dispose() => Unregister();

    private AppBarData CreateData() => new()
    {
        Size = (uint)Marshal.SizeOf<AppBarData>(),
        WindowHandle = _windowHandle
    };

    [StructLayout(LayoutKind.Sequential)]
    private struct AppBarData
    {
        public uint Size;
        public IntPtr WindowHandle;
        public uint CallbackMessage;
        public uint Edge;
        public NativeRect Rect;
        public IntPtr Parameter;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [DllImport("shell32.dll", CallingConvention = CallingConvention.StdCall)]
    private static extern UIntPtr SHAppBarMessage(uint message, ref AppBarData data);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool MoveWindow(IntPtr windowHandle, int x, int y, int width, int height, bool repaint);
}
