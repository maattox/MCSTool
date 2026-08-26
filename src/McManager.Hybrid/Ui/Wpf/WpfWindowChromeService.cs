using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace McManager.Hybrid.Ui.Wpf;

public sealed class WpfWindowChromeService : IWindowChromeService
{
    private const int WmGetMinMaxInfo = 0x0024;
    private const int WmNcHitTest = 0x0084;
    private const int WmNcLButtonDown = 0x00A1;
    private const int HtClient = 1;
    private const int HtCaption = 2;
    private const int HtLeft = 10;
    private const int HtRight = 11;
    private const int HtTop = 12;
    private const int HtTopLeft = 13;
    private const int HtTopRight = 14;
    private const int HtBottom = 15;
    private const int HtBottomLeft = 16;
    private const int HtBottomRight = 17;
    private const int MonitorDefaultToNearest = 2;

    /// <summary>
    /// Inner resize grip in DIP. Matches <c>WindowChrome.ResizeBorderThickness</c> (10)
    /// so the cursor can start a resize ~10px in from the painted edge. WebView2's HWND
    /// swallows the default chrome hit-test when the view fills the client.
    /// </summary>
    internal const double ResizeGripDip = 10;

    private MainWindow? _window;
    private bool _hooked;

    public bool IsMaximized =>
        _window?.WindowState == WindowState.Maximized;

    public event EventHandler? Changed;

    public void Attach(MainWindow window)
    {
        ArgumentNullException.ThrowIfNull(window);
        if (_window is not null)
            throw new InvalidOperationException("Window chrome is already attached.");

        _window = window;
        window.StateChanged += OnWindowStateChanged;
        window.SourceInitialized += OnSourceInitialized;
        if (PresentationSource.FromVisual(window) is not null)
            HookWndProc(window);
    }

    public void DragMove() =>
        RunOnWindow(window =>
        {
            ReleaseCapture();
            var hwnd = new WindowInteropHelper(window).Handle;
            if (hwnd == IntPtr.Zero)
                return;
            SendMessage(hwnd, WmNcLButtonDown, (IntPtr)HtCaption, IntPtr.Zero);
        });

    public void Minimize() =>
        RunOnWindow(window => window.WindowState = WindowState.Minimized);

    public void ToggleMaximize() =>
        RunOnWindow(window =>
            window.WindowState = window.WindowState == WindowState.Maximized
                ? WindowState.Normal
                : WindowState.Maximized);

    public void Close() =>
        RunOnWindow(window => window.Close());

    private void OnWindowStateChanged(object? sender, EventArgs e) =>
        Changed?.Invoke(this, EventArgs.Empty);

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        if (_window is not null)
            HookWndProc(_window);
    }

    private void HookWndProc(MainWindow window)
    {
        if (_hooked)
            return;
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd == IntPtr.Zero)
            return;
        var source = HwndSource.FromHwnd(hwnd);
        source?.AddHook(WndProc);
        _hooked = source is not null;
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WmGetMinMaxInfo)
        {
            ApplyWorkAreaMaximize(hwnd, lParam);
            return IntPtr.Zero;
        }

        if (msg == WmNcHitTest &&
            _window is { WindowState: not WindowState.Maximized })
        {
            var hit = HitTestResizeBorder(hwnd, lParam);
            if (hit != HtClient)
            {
                handled = true;
                return (IntPtr)hit;
            }
        }

        return IntPtr.Zero;
    }

    private int HitTestResizeBorder(IntPtr hwnd, IntPtr lParam)
    {
        if (!GetWindowRect(hwnd, out var rect))
            return HtClient;

        var screenX = unchecked((short)(lParam.ToInt64() & 0xFFFF));
        var screenY = unchecked((short)((lParam.ToInt64() >> 16) & 0xFFFF));
        var grip = GetResizeGripPx();

        var onLeft = screenX - rect.Left < grip;
        var onRight = rect.Right - screenX <= grip;
        var onTop = screenY - rect.Top < grip;
        var onBottom = rect.Bottom - screenY <= grip;

        if (onTop && onLeft)
            return HtTopLeft;
        if (onTop && onRight)
            return HtTopRight;
        if (onBottom && onLeft)
            return HtBottomLeft;
        if (onBottom && onRight)
            return HtBottomRight;
        if (onLeft)
            return HtLeft;
        if (onRight)
            return HtRight;
        if (onTop)
            return HtTop;
        if (onBottom)
            return HtBottom;
        return HtClient;
    }

    private int GetResizeGripPx()
    {
        var dip = ResizeGripDip;
        if (_window is not null)
            dip *= VisualTreeHelper.GetDpi(_window).PixelsPerDip;
        return Math.Max(1, (int)Math.Ceiling(dip));
    }

    private static void ApplyWorkAreaMaximize(IntPtr hwnd, IntPtr lParam)
    {
        var monitor = MonitorFromWindow(hwnd, MonitorDefaultToNearest);
        if (monitor == IntPtr.Zero)
            return;

        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        if (!GetMonitorInfo(monitor, ref info))
            return;

        var mmi = Marshal.PtrToStructure<MinMaxInfo>(lParam);
        var work = info.Work;
        var screen = info.Monitor;
        mmi.MaxPosition.X = Math.Abs(work.Left - screen.Left);
        mmi.MaxPosition.Y = Math.Abs(work.Top - screen.Top);
        mmi.MaxSize.X = Math.Abs(work.Right - work.Left);
        mmi.MaxSize.Y = Math.Abs(work.Bottom - work.Top);
        Marshal.StructureToPtr(mmi, lParam, fDeleteOld: true);
    }

    private void RunOnWindow(Action<MainWindow> action)
    {
        var window = _window;
        if (window is null)
            return;

        if (window.Dispatcher.CheckAccess())
        {
            action(window);
            return;
        }

        window.Dispatcher.Invoke(() => action(window));
    }

    [DllImport("user32.dll")]
    private static extern bool ReleaseCapture();

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, int flags);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MonitorInfo lpmi);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hWnd, out NativeRect lpRect);

    [StructLayout(LayoutKind.Sequential)]
    private struct MinMaxInfo
    {
        public NativePoint Reserved;
        public NativePoint MaxSize;
        public NativePoint MaxPosition;
        public NativePoint MinTrackSize;
        public NativePoint MaxTrackSize;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct MonitorInfo
    {
        public int Size;
        public NativeRect Monitor;
        public NativeRect Work;
        public int Flags;
    }
}
