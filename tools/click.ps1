param(
    [Parameter(Mandatory = $true)][int]$X,
    [Parameter(Mandatory = $true)][int]$Y,
    [string]$Type = "",
    [switch]$Right,
    [switch]$Wheel,
    [string]$ProcessName = "Wordsbook"
)

Add-Type -AssemblyName System.Windows.Forms

if (-not ("Clicker" -as [type])) {
    $refs = [AppDomain]::CurrentDomain.GetAssemblies() |
        Where-Object { -not $_.IsDynamic -and $_.Location } |
        ForEach-Object { $_.Location }
    Add-Type -ReferencedAssemblies $refs @"
using System;
using System.Runtime.InteropServices;
public static class Clicker {
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr h, int n);
    [DllImport("user32.dll")] static extern void SwitchToThisWindow(IntPtr h, bool a);
    [DllImport("user32.dll")] static extern void mouse_event(uint f, uint dx, uint dy, uint d, IntPtr e);
    [DllImport("user32.dll")] static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] static extern bool SetProcessDpiAwarenessContext(IntPtr ctx);
    const uint LEFTDOWN = 0x02, LEFTUP = 0x04, RIGHTDOWN = 0x08, RIGHTUP = 0x10;

    public static void MakeAware() { try { SetProcessDpiAwarenessContext(new IntPtr(-4)); } catch { } }

    public static void Click(IntPtr hwnd, int clientX, int clientY, bool right) {
        RECT r; GetWindowRect(hwnd, out r);
        ShowWindow(hwnd, 9); SwitchToThisWindow(hwnd, true); SetForegroundWindow(hwnd);
        System.Threading.Thread.Sleep(250);
        SetCursorPos(r.Left + clientX, r.Top + clientY);
        System.Threading.Thread.Sleep(90);
        if (right) {
            mouse_event(RIGHTDOWN, 0, 0, 0, IntPtr.Zero);
            mouse_event(RIGHTUP, 0, 0, 0, IntPtr.Zero);
        } else {
            mouse_event(LEFTDOWN, 0, 0, 0, IntPtr.Zero);
            mouse_event(LEFTUP, 0, 0, 0, IntPtr.Zero);
        }
    }

    /// <summary>滚轮：WinUI 的 ScrollViewer 只认鼠标消息，键盘和拖拽条都不好使。</summary>
    public static void Wheel(IntPtr hwnd, int clientX, int clientY, int notches) {
        RECT r; GetWindowRect(hwnd, out r);
        ShowWindow(hwnd, 9); SwitchToThisWindow(hwnd, true); SetForegroundWindow(hwnd);
        System.Threading.Thread.Sleep(250);
        SetCursorPos(r.Left + clientX, r.Top + clientY);
        System.Threading.Thread.Sleep(120);
        mouse_event(0x0800, 0, 0, (uint)(notches * 120), IntPtr.Zero);
    }
}
"@
}
[Clicker]::MakeAware()

$proc = Get-Process -Name $ProcessName -ErrorAction SilentlyContinue | Where-Object { $_.MainWindowHandle -ne [IntPtr]::Zero } | Select-Object -First 1
if (-not $proc) { Write-Error "no $ProcessName window"; exit 1 }

if ($Wheel) { [Clicker]::Wheel($proc.MainWindowHandle, $X, $Y, 3) }
else { [Clicker]::Click($proc.MainWindowHandle, $X, $Y, [bool]$Right) }
Start-Sleep -Milliseconds 260

if ($Type -ne "") {
    foreach ($ch in $Type.ToCharArray()) {
        [System.Windows.Forms.SendKeys]::SendWait([string]$ch)
        Start-Sleep -Milliseconds 25
    }
}
Write-Output "clicked client ($X,$Y) right=$Right wheel=$Wheel type='$Type'"
