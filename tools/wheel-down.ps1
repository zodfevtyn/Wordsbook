param([int]$X = 800, [int]$Y = 600, [int]$Notches = -3)
Add-Type -AssemblyName System.Windows.Forms
Add-Type -TypeDefinition @"
using System;
using System.Runtime.InteropServices;
public static class WheelDown {
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] public static extern void mouse_event(uint f, uint dx, uint dy, uint d, IntPtr e);
    [DllImport("user32.dll")] public static extern bool SetProcessDpiAwarenessContext(IntPtr ctx);
    public static void Go(IntPtr hwnd, int x, int y, int notches) {
        try { SetProcessDpiAwarenessContext(new IntPtr(-4)); } catch { }
        RECT r; GetWindowRect(hwnd, out r);
        SetForegroundWindow(hwnd);
        SetCursorPos(r.Left + x, r.Top + y);
        System.Threading.Thread.Sleep(120);
        mouse_event(0x0800, 0, 0, (uint)(notches * 120), IntPtr.Zero);
    }
}
"@
$proc = Get-Process Wordsbook -ErrorAction SilentlyContinue | Where-Object { $_.MainWindowHandle -ne [IntPtr]::Zero } | Select-Object -First 1
if (-not $proc) { Write-Error "no Wordsbook window"; exit 1 }
[WheelDown]::Go($proc.MainWindowHandle, $X, $Y, $Notches)
Write-Output "wheel $Notches at $X,$Y"
