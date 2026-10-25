param(
    [Parameter(Mandatory = $true)][int]$X,
    [Parameter(Mandatory = $true)][int]$Y,
    [int]$DX = 0,
    [int]$DY = 0,
    [int]$Steps = 20,
    [string]$ProcessName = "Wordsbook"
)
$ErrorActionPreference = "Stop"

if (-not ("Dragger" -as [type])) {
    $refs = [AppDomain]::CurrentDomain.GetAssemblies() |
        Where-Object { -not $_.IsDynamic -and $_.Location } | ForEach-Object { $_.Location }
    Add-Type -ReferencedAssemblies $refs @"
using System;
using System.Runtime.InteropServices;
using System.Threading;

public static class Dragger {
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] public struct MOUSEINPUT { public int dx, dy; public uint mouseData, dwFlags, time; public IntPtr extra; }
    [StructLayout(LayoutKind.Sequential)] public struct INPUT { public uint type; public MOUSEINPUT mi; }

    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr h, int n);
    [DllImport("user32.dll")] static extern void SwitchToThisWindow(IntPtr h, bool a);
    [DllImport("user32.dll")] static extern int GetSystemMetrics(int i);
    [DllImport("user32.dll")] static extern uint SendInput(uint n, INPUT[] inputs, int size);
    [DllImport("user32.dll")] static extern bool SetProcessDpiAwarenessContext(IntPtr c);

    const uint MOVE = 0x0001, ABS = 0x8000, DOWN = 0x0002, UP = 0x0004;

    public static void Aware() { try { SetProcessDpiAwarenessContext(new IntPtr(-4)); } catch { } }

    static void Send(uint flags, int x, int y) {
        // WinUI 的指针栈吃 raw input，SetCursorPos 跳位置它看不到，必须走 SendInput。
        int w = GetSystemMetrics(0), h = GetSystemMetrics(1);
        var inp = new INPUT {
            type = 0,
            mi = new MOUSEINPUT {
                dx = (int)Math.Round(x * 65535.0 / (w - 1)),
                dy = (int)Math.Round(y * 65535.0 / (h - 1)),
                dwFlags = flags | ABS
            }
        };
        SendInput(1, new[] { inp }, Marshal.SizeOf(typeof(INPUT)));
    }

    public static void Grab(IntPtr hwnd, int x, int y, int dx, int dy, int steps) {
        RECT r; GetWindowRect(hwnd, out r);
        ShowWindow(hwnd, 9); SwitchToThisWindow(hwnd, true); SetForegroundWindow(hwnd);
        Thread.Sleep(300);
        int sx = r.Left + x, sy = r.Top + y;

        Send(MOVE, sx, sy);
        Thread.Sleep(250);
        Send(DOWN, sx, sy);
        Thread.Sleep(160);
        for (int i = 1; i <= steps; i++) {
            Send(MOVE, sx + dx * i / steps, sy + dy * i / steps);
            Thread.Sleep(35);
        }
        Thread.Sleep(200);
        Send(UP, sx + dx, sy + dy);
        Thread.Sleep(300);
    }
}
"@
}
[Dragger]::Aware()

$p = Get-Process -Name $ProcessName -ErrorAction SilentlyContinue |
    Where-Object { $_.MainWindowHandle -ne [IntPtr]::Zero } | Select-Object -First 1
if (-not $p) { Write-Error "no $ProcessName window"; exit 1 }

[Dragger]::Grab($p.MainWindowHandle, $X, $Y, $DX, $DY, $Steps)
Write-Output "dragged ($X,$Y) by ($DX,$DY) in $Steps steps"
