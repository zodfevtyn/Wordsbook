param(
    [Parameter(Mandatory = $true)][int]$X,
    [Parameter(Mandatory = $true)][int]$Y,
    [string]$ProcessName = "Wordsbook"
)
$ErrorActionPreference = "Stop"

if (-not ("Mover" -as [type])) {
    $refs = [AppDomain]::CurrentDomain.GetAssemblies() |
        Where-Object { -not $_.IsDynamic -and $_.Location } | ForEach-Object { $_.Location }
    Add-Type -ReferencedAssemblies $refs @"
using System;
using System.Runtime.InteropServices;
using System.Threading;

public static class Mover {
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] public struct MOUSEINPUT { public int dx, dy; public uint mouseData, dwFlags, time; public IntPtr extra; }
    [StructLayout(LayoutKind.Sequential)] public struct INPUT { public uint type; public MOUSEINPUT mi; }

    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("user32.dll")] static extern void SwitchToThisWindow(IntPtr h, bool a);
    [DllImport("user32.dll")] static extern int GetSystemMetrics(int i);
    [DllImport("user32.dll")] static extern uint SendInput(uint n, INPUT[] inputs, int size);
    [DllImport("user32.dll")] static extern bool SetProcessDpiAwarenessContext(IntPtr c);

    public static void Aware() { try { SetProcessDpiAwarenessContext(new IntPtr(-4)); } catch { } }

    public static void MoveTo(IntPtr hwnd, int x, int y) {
        RECT r; GetWindowRect(hwnd, out r);
        SwitchToThisWindow(hwnd, true); SetForegroundWindow(hwnd);
        int w = GetSystemMetrics(0), h = GetSystemMetrics(1);
        var inp = new INPUT {
            type = 0,
            mi = new MOUSEINPUT {
                dx = (int)Math.Round((r.Left + x) * 65535.0 / (w - 1)),
                dy = (int)Math.Round((r.Top + y) * 65535.0 / (h - 1)),
                dwFlags = 0x0001 | 0x8000
            }
        };
        SendInput(1, new[] { inp }, Marshal.SizeOf(typeof(INPUT)));
        Thread.Sleep(250);
    }
}
"@
}
[Mover]::Aware()

$p = Get-Process -Name $ProcessName -ErrorAction SilentlyContinue |
    Where-Object { $_.MainWindowHandle -ne [IntPtr]::Zero } | Select-Object -First 1
if (-not $p) { Write-Error "no $ProcessName window"; exit 1 }
[Mover]::MoveTo($p.MainWindowHandle, $X, $Y)
Write-Output "moved to client ($X,$Y)"
