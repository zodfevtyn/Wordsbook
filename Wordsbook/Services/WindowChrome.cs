using System.IO;
using System.Runtime.InteropServices;

namespace Wordsbook.Services;

/// <summary>
/// 自绘标题栏需要的 Win32 调整。这个 Windows App SDK 版本的 AppWindowTitleBar
/// 没有 ButtonVisibility（运行时反射确认），只能直接改窗口样式。
/// </summary>
public static class WindowChrome
{
    const int GWL_STYLE = -16;
    const long WS_CAPTION = 0x00C0_0000;
    const long WS_THICKFRAME = 0x0004_0000;
    const long WS_SYSMENU = 0x0008_0000;

    const uint SWP_NOSIZE = 0x0001, SWP_NOMOVE = 0x0002, SWP_NOZORDER = 0x0004, SWP_FRAMECHANGED = 0x0020;

    const int WM_NCCALCSIZE = 0x0083;
    const int WM_GETMINMAXINFO = 0x0024;
    const int SM_CXSIZEFRAME = 32, SM_CXPADDEDBORDER = 92;

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] static extern IntPtr GetLong(IntPtr hWnd, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] static extern IntPtr SetLong(IntPtr hWnd, int index, IntPtr value);
    [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr hWnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
    [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr hWnd, int attr, ref int value, int size);
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);
    [DllImport("user32.dll")] static extern bool GetClientRect(IntPtr hWnd, out RECT rect);
    [DllImport("user32.dll")] static extern uint GetDpiForWindow(IntPtr hWnd);
    [DllImport("user32.dll")] static extern bool ClientToScreen(IntPtr hWnd, ref POINT point);
    [DllImport("user32.dll")] static extern int GetSystemMetricsForDpi(int index, uint dpi);
    [DllImport("user32.dll")] static extern bool IsZoomed(IntPtr hWnd);
    [DllImport("user32.dll")] static extern IntPtr CallWindowProc(IntPtr lpPrev, IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] static extern IntPtr MonitorFromWindow(IntPtr hWnd, uint flags);
    [DllImport("user32.dll")] static extern bool GetMonitorInfo(IntPtr monitor, ref MONITORINFO info);
    [DllImport("dwmapi.dll")] static extern int DwmGetWindowAttribute(IntPtr hWnd, int attr, out RECT rect, int size);

    [StructLayout(LayoutKind.Sequential)] public struct RECT
    { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)] public struct POINT
    { public int X, Y; }

    [StructLayout(LayoutKind.Sequential)]
    struct NCCALCSIZE_PARAMS { public RECT rcProposed; public RECT rc1; public RECT rc2; public IntPtr windowPos; }

    [StructLayout(LayoutKind.Sequential)]
    struct MONITORINFO { public int cbSize; public RECT rcMonitor; public RECT rcWork; public uint flags; }

    [StructLayout(LayoutKind.Sequential)]
    struct MINMAXINFO
    {
        public POINT ptReserved;
        public POINT ptMaxSize;
        public POINT ptMaxPosition;
        public POINT ptMinTrackSize;
        public POINT ptMaxTrackSize;
    }

    const uint MONITOR_DEFAULTTONEAREST = 2;

    delegate IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    static IntPtr _prevWndProc;
    static WndProc? _proc; // 委托必须被静态字段持有，否则 GC 之后回调指针就是野的

    /// <summary>
    /// 窗口圆角：实测这台 Win11 只认 1=直角、2=8dp 圆角、3=4dp 小圆角，
    /// 传 4 以上要么按 2 处理要么直接变直角，所以 2 已经是系统给的上限。
    /// </summary>
    const int CornerRound = 2;

    /// <summary>
    /// 去掉系统标题栏。清掉 WS_CAPTION 后 DWM 会在客户区上方留一条约 8px 的边框带，
    /// 所以还得接管 WM_NCCALCSIZE 把客户区铺满整个窗口，并自己补上边缘命中标记，
    /// 否则四边拖拽缩放会一起失效（实测过）。
    /// </summary>
    public static void MakeBorderless(IntPtr hwnd)
    {
        var style = GetLong(hwnd, GWL_STYLE).ToInt64();
        style = (style & ~WS_CAPTION) | WS_THICKFRAME | WS_SYSMENU;
        SetLong(hwnd, GWL_STYLE, new IntPtr(style));

        _proc = Hook;
        _prevWndProc = SetLong(hwnd, -4, Marshal.GetFunctionPointerForDelegate(_proc));

        var corner = CornerRound;
        DwmSetWindowAttribute(hwnd, 33, ref corner, 4);

        SetWindowPos(hwnd, IntPtr.Zero, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOZORDER | SWP_FRAMECHANGED);
    }

    static IntPtr Hook(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        if (msg == WM_GETMINMAXINFO)
        {
            // 无标题栏窗口默认会连任务栏一起最大化，导致客户区按工作区收缩后
            // 底边空出一条白带。这里把最大化的目标框定在显示器工作区。
            var mi = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
            if (GetMonitorInfo(MonitorFromWindow(hWnd, MONITOR_DEFAULTTONEAREST), ref mi))
            {
                var info = Marshal.PtrToStructure<MINMAXINFO>(lParam);
                info.ptMaxPosition.X = mi.rcWork.Left - mi.rcMonitor.Left;
                info.ptMaxPosition.Y = mi.rcWork.Top - mi.rcMonitor.Top;
                info.ptMaxSize.X = mi.rcWork.Right - mi.rcWork.Left;
                info.ptMaxSize.Y = mi.rcWork.Bottom - mi.rcWork.Top;
                Marshal.StructureToPtr(info, lParam, false);
            }

            return CallWindowProc(_prevWndProc, hWnd, msg, wParam, lParam);
        }

        if (msg == WM_NCCALCSIZE && wParam != IntPtr.Zero)
        {
            if (!IsZoomed(hWnd)) return IntPtr.Zero;

            // 最大化时把客户区直接对齐到显示器工作区。沿用窗口边框内缩会在四周
            // 留下 1px 非客户区，DWM 会把它画成一条白线；直接铺满则没有缝。
            var info = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
            if (GetMonitorInfo(MonitorFromWindow(hWnd, MONITOR_DEFAULTTONEAREST), ref info))
            {
                var p = Marshal.PtrToStructure<NCCALCSIZE_PARAMS>(lParam);
                p.rcProposed = info.rcWork;
                Marshal.StructureToPtr(p, lParam, false);
            }

            return IntPtr.Zero;
        }

        return CallWindowProc(_prevWndProc, hWnd, msg, wParam, lParam);
    }

    static int Border(IntPtr hWnd)
    {
        var dpi = GetDpiForWindow(hWnd);
        return GetSystemMetricsForDpi(SM_CXSIZEFRAME, dpi) + GetSystemMetricsForDpi(SM_CXPADDEDBORDER, dpi);
    }

    /// <summary>AppWindow.Resize 收的是物理像素，先按窗口 DPI 换算再乘。</summary>
    public static double DpiScale(IntPtr hWnd) => GetDpiForWindow(hWnd) / 96.0;

    /// <summary>
    /// 客户区铺满整个窗口后 DWM 不再给边缘留缩放热区，得自己按这个宽度告诉 WinUI。
    /// </summary>
    public static int ResizeBorder(IntPtr hWnd) => Border(hWnd);

    /// <summary>
    /// 把窗口真实几何写进诊断文件。PowerShell 跨进程量会被 DPI 虚拟化带偏，
    /// 只有进程自己读到的数字可信；顶部残留到底多宽就靠这个定。
    /// </summary>
    public static void DumpGeometry(IntPtr hwnd, string path)
    {
        GetWindowRect(hwnd, out var wr);
        GetClientRect(hwnd, out var cr);
        var clientOrigin = new POINT();
        ClientToScreen(hwnd, ref clientOrigin);
        DwmGetWindowAttribute(hwnd, 9, out RECT frame, Marshal.SizeOf<RECT>());

        var dpi = GetDpiForWindow(hwnd);
        var text = string.Join('\n',
            $"dpi={dpi} scale={dpi / 96.0:0.##}",
            $"windowRect=({wr.Left},{wr.Top})-({wr.Right},{wr.Bottom}) {wr.Right - wr.Left}x{wr.Bottom - wr.Top}",
            $"clientRect={cr.Right}x{cr.Bottom}",
            $"clientOrigin=({clientOrigin.X},{clientOrigin.Y})",
            $"dwmFrame=({frame.Left},{frame.Top})-({frame.Right},{frame.Bottom}) {frame.Right - frame.Left}x{frame.Bottom - frame.Top}",
            $"insetFromDwmFrame: left={clientOrigin.X - frame.Left} top={clientOrigin.Y - frame.Top} right={frame.Right - clientOrigin.X - cr.Right} bottom={frame.Bottom - clientOrigin.Y - cr.Bottom}",
            $"resizeBorder={Border(hwnd)}");

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, text);
        }
        catch { }
    }
}
