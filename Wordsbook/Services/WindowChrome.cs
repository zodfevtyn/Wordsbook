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

    const uint SWP_NOSIZE = 0x0001, SWP_NOMOVE = 0x0002, SWP_NOZORDER = 0x0004, SWP_NOACTIVATE = 0x0010, SWP_FRAMECHANGED = 0x0020;

    const int WM_NCCALCSIZE = 0x0083;
    const int WM_GETMINMAXINFO = 0x0024;
    const int WM_SIZE = 0x0005;
    const int SM_CXSIZEFRAME = 32, SM_CXPADDEDBORDER = 92;

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] static extern IntPtr GetLong(IntPtr hWnd, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] static extern IntPtr SetLong(IntPtr hWnd, int index, IntPtr value);
    [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr hWnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
    [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr hWnd, int attr, ref int value, int size);
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);
    [DllImport("user32.dll")] static extern bool GetClientRect(IntPtr hWnd, out RECT rect);
    [DllImport("user32.dll")] static extern uint GetDpiForWindow(IntPtr hWnd);
    [DllImport("user32.dll")] static extern bool ClientToScreen(IntPtr hWnd, ref POINT point);
    [DllImport("user32.dll")] static extern bool GetCursorPos(out POINT point);
    [DllImport("user32.dll")] static extern int GetSystemMetricsForDpi(int index, uint dpi);
    [DllImport("user32.dll")] static extern bool IsZoomed(IntPtr hWnd);
    [DllImport("user32.dll")] static extern IntPtr CallWindowProc(IntPtr lpPrev, IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] static extern IntPtr MonitorFromWindow(IntPtr hWnd, uint flags);
    [DllImport("user32.dll")] static extern bool GetMonitorInfo(IntPtr monitor, ref MONITORINFO info);
    [DllImport("dwmapi.dll")] static extern int DwmGetWindowAttribute(IntPtr hWnd, int attr, out RECT rect, int size);
    [DllImport("gdi32.dll")] static extern IntPtr CreateRoundRectRgn(int x1, int y1, int x2, int y2, int w, int h);
    [DllImport("gdi32.dll")] static extern IntPtr CreateRectRgn(int x1, int y1, int x2, int y2);
    [DllImport("gdi32.dll")] static extern IntPtr ExtCreateRegion(IntPtr xform, int count, IntPtr regionData);
    [DllImport("user32.dll")] static extern int SetWindowRgn(IntPtr hWnd, IntPtr hRgn, bool bRedraw);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string? cls, string? window);

    [StructLayout(LayoutKind.Sequential)] public struct RECT
    { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)] public struct POINT
    { public int X, Y; }

    [StructLayout(LayoutKind.Sequential)]
    struct NCCALCSIZE_PARAMS { public RECT rcProposed; public RECT rc1; public RECT rc2; public IntPtr windowPos; }

    [StructLayout(LayoutKind.Sequential)]
    struct MONITORINFO { public int cbSize; public RECT rcMonitor; public RECT rcWork; public uint flags; }

    [StructLayout(LayoutKind.Sequential)]
    struct RGNDATAHEADER
    {
        public int dwSize;
        public int iType;
        public int nCount;
        public int nRgnSize;
        public RECT rcBound;
    }

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
    /// XAML 岛（DesktopChildSiteBridge）默认比客户区顶边低 1px，那一行于是永远没人画：
    /// 不开 DWM 圆角时它就是一整行纯黑（实测停在 (0,1) 时顶行 (0,0,0) 占满 179/179 采样点），
    /// 开着则被 DWM 的边框线盖掉——所以之前那条边界线其实是拿来遮这个洞的。
    /// 抬回 (0,0) 铺满客户区，两个问题一起没。
    /// 只能在 WinUI 处理完尺寸/DPI 变更之后再调，见 MainWindow.RequestIslandAlign。
    /// </summary>
    public static void AlignIsland(IntPtr hWnd)
    {
        var bridge = FindWindowEx(hWnd, IntPtr.Zero, "Microsoft.UI.Content.DesktopChildSiteBridge", null);
        if (bridge == IntPtr.Zero || !GetClientRect(hWnd, out var rc) || rc.Right < 2 || rc.Bottom < 2) return;

        var origin = new POINT();
        if (ClientToScreen(hWnd, ref origin)
            && GetWindowRect(bridge, out var br)
            && br.Left == origin.X && br.Top == origin.Y
            && br.Right - br.Left == rc.Right && br.Bottom - br.Top == rc.Bottom) return;

        SetWindowPos(bridge, IntPtr.Zero, 0, 0, rc.Right, rc.Bottom, SWP_NOZORDER | SWP_NOACTIVATE);
    }

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

        // 18 圆角由 SetWindowRgn 负责，比 DWM 的 8dp 上限（实测 1=直角、2=8dp、3=4dp）
        // 更靠内，DWM 那份圆角轮不到它管，所以直接要直角。
        var corner = 1;
        DwmSetWindowAttribute(hwnd, 33, ref corner, 4);

        // 光要直角还不够：DWM 照旧在窗口顶边画一条 1px 边框线（活动窗口染强调色，
        // 实测 (47,114,170)，非活动是浅灰），而且它画在客户区外面一格，被窗口区域
        // 切成圆角后正好贴在左上角曲线尽头。只有显式要 COLOR_NONE 才拿掉。
        var border = unchecked((int)0xFFFFFFFE);
        DwmSetWindowAttribute(hwnd, 34, ref border, 4);

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

        if (msg == WM_SIZE)
        {
            var result = CallWindowProc(_prevWndProc, hWnd, msg, wParam, lParam);
            // 这里不能抬岛：WM_SIZE 里 WinUI 还在重建岛自己的合成目标，
            // 当场 SetWindowPos 把岛挪走会让整页字形被撕成半截（实测跨 DPI 搬屏必现）。
            ApplyCornerRegion(hWnd);
            return result;
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

    /// <summary>拖拽小窗用：光标在屏幕上的物理像素位置，和目标窗口坐标同一套单位。</summary>
    public static (int X, int Y) CursorPosition()
    {
        GetCursorPos(out var point);
        return (point.X, point.Y);
    }

    /// <summary>让 DWM 用系统自带的圆角（收起态要直角，否则图片角会被切掉）。</summary>
    public static void UseNativeCorners(IntPtr hWnd, bool round = true)
    {
        var preference = round ? 2 : 1; // DWMWCP_ROUND / DWMWCP_DONOTROUND
        DwmSetWindowAttribute(hWnd, 33, ref preference, 4);
    }

    /// <summary>客户区尺寸（物理像素）。窗口边框占掉的那几像素要补回给内容，否则底部会被裁。</summary>
    public static (int Width, int Height) ClientSize(IntPtr hWnd) =>
        GetClientRect(hWnd, out var rect)
            ? (rect.Right - rect.Left, rect.Bottom - rect.Top)
            : (0, 0);

    /// <summary>收起态只亮出图片所在的一块矩形，避免系统最小窗口宽度留下透明但可点的空白。</summary>
    public static void ApplyRectRegion(IntPtr hWnd, int x, int y, int width, int height)
    {
        if (width < 1 || height < 1) return;
        SetWindowRgn(hWnd, CreateRectRgn(x, y, x + width, y + height), true);
    }

    /// <summary>客户区相对窗口左上角的偏移。带系统边框的窗口，这块偏移里画的是白边，不能算进可见区域。</summary>
    public static (int X, int Y) ClientOrigin(IntPtr hWnd)
    {
        var origin = new POINT();
        if (!GetClientRect(hWnd, out _) || !ClientToScreen(hWnd, ref origin)
            || !GetWindowRect(hWnd, out var window)) return (0, 0);
        return (origin.X - window.Left, origin.Y - window.Top);
    }

    public static void ApplyRoundRectRegion(IntPtr hWnd, int x, int y, int width, int height,
        double radiusDip, double dpi)
    {
        if (width < 1 || height < 1) return;
        var diameter = Math.Max(1, (int)Math.Round(radiusDip * 2 * dpi));
        SetWindowRgn(hWnd, CreateRoundRectRgn(x, y, x + width + 1, y + height + 1, diameter, diameter), true);
    }

    /// <summary>
    /// 按像素矩形集合（窗口坐标）重建窗口区域。图片是带透明背景的 PNG，
    /// 只有把透明像素从窗口区域里挖掉，桌面才能从图片周围透出来。
    /// </summary>
    public static void ApplyRegionRects(IntPtr hWnd, IReadOnlyList<(int X, int Y, int Width, int Height)> rects)
    {
        if (rects.Count == 0) return;

        var headerSize = Marshal.SizeOf<RGNDATAHEADER>();
        var rectSize = Marshal.SizeOf<RECT>();
        var size = headerSize + rectSize * rects.Count;
        var buffer = Marshal.AllocHGlobal(size);
        try
        {
            var minX = int.MaxValue;
            var minY = int.MaxValue;
            var maxX = int.MinValue;
            var maxY = int.MinValue;
            for (var i = 0; i < rects.Count; i++)
            {
                var (x, y, width, height) = rects[i];
                minX = Math.Min(minX, x);
                minY = Math.Min(minY, y);
                maxX = Math.Max(maxX, x + width);
                maxY = Math.Max(maxY, y + height);

                var rect = new RECT { Left = x, Top = y, Right = x + width, Bottom = y + height };
                Marshal.StructureToPtr(rect, buffer + headerSize + rectSize * i, false);
            }

            var header = new RGNDATAHEADER
            {
                dwSize = headerSize,
                iType = 1, // RDH_RECTANGLES
                nCount = rects.Count,
                nRgnSize = rectSize * rects.Count,
                rcBound = new RECT { Left = minX, Top = minY, Right = maxX, Bottom = maxY },
            };
            Marshal.StructureToPtr(header, buffer, false);

            var region = ExtCreateRegion(IntPtr.Zero, size, buffer);
            if (region != IntPtr.Zero) SetWindowRgn(hWnd, region, true);
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    public static void ClearRegion(IntPtr hWnd) => SetWindowRgn(hWnd, IntPtr.Zero, true);

    /// <summary>去掉 DWM 沿窗口边缘画的那条 1px 描边（收起态的白边来源之一）。</summary>
    public static void HideWindowBorder(IntPtr hWnd, bool hide)
    {
        var color = hide ? unchecked((int)0xFFFFFFFE) : unchecked((int)0xFFFFFFFF);
        DwmSetWindowAttribute(hWnd, 34, ref color, 4);
    }

    /// <summary>
    /// 窗口四角的真实裁剪半径（物理像素）。WinUI 3 的窗口是不透明的，元素圆角只能让
    /// 贴边面板自己缩进去，被让出来的那块角不会透出桌面，只会露出黑底，所以必须用
    /// SetWindowRgn 把窗口区域本身切成圆角。半径由 MainWindow 按缩放算好后写进来。
    /// </summary>
    public static double CornerPhysical = 22;

    public static void ApplyCornerRegion(IntPtr hWnd)
    {
        if (GetClientRect(hWnd, out var rc))
        {
            var w = rc.Right - rc.Left;
            var h = rc.Bottom - rc.Top;
            if (w < 8 || h < 8) return;

            // 铺满工作区（点绿灯最大化走的是 AppWindow.MoveAndResize，IsZoomed 不会变真）
            // 时四角就是屏幕角，不该再切，否则屏幕角上会露出壁纸。
            if (CoversWorkArea(hWnd))
            {
                SetWindowRgn(hWnd, IntPtr.Zero, true);
                return;
            }

            var d = (int)Math.Round(CornerPhysical) * 2;
            // SetWindowRgn 成功后区域句柄归窗口所有，不能自己 DeleteObject。
            SetWindowRgn(hWnd, CreateRoundRectRgn(0, 0, w + 1, h + 1, d, d), true);
        }
    }

    /// <summary>窗口是否正好铺满所在显示器的工作区，即“最大化”状态。</summary>
    public static bool CoversWorkArea(IntPtr hWnd)
    {
        var info = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
        if (!GetMonitorInfo(MonitorFromWindow(hWnd, MONITOR_DEFAULTTONEAREST), ref info)
            || !GetWindowRect(hWnd, out var wr)) return false;

        var wa = info.rcWork;
        return wr.Left <= wa.Left && wr.Top <= wa.Top && wr.Right >= wa.Right && wr.Bottom >= wa.Bottom;
    }

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
