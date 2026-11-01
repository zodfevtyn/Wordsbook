param(
    [Parameter(Mandatory = $true)][int]$X,
    [Parameter(Mandatory = $true)][int]$Y,
    [switch]$Right,
    [switch]$Double
)

# 不抢焦点、不 ShowWindow 的原生点击：shot/click.ps1 会先 SW_RESTORE，
# 那会把“最大化状态下双击”这类测试的初始状态直接改掉。
if (-not ("Raw" -as [type])) {
    Add-Type @"
using System;
using System.Runtime.InteropServices;
public static class Raw {
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L,T,R,B; }
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] public static extern void mouse_event(uint f, uint dx, uint dy, uint d, IntPtr e);
    [DllImport("user32.dll")] public static extern bool SetProcessDpiAwarenessContext(IntPtr c);
    const uint LD = 0x02, LU = 0x04, RD = 0x08, RU = 0x10;
    public static string At(IntPtr h, int x, int y, bool right, bool dbl) {
        RECT r; GetWindowRect(h, out r);
        SetCursorPos(r.L + x, r.T + y);
        System.Threading.Thread.Sleep(90);
        if (right) { mouse_event(RD,0,0,0,IntPtr.Zero); mouse_event(RU,0,0,0,IntPtr.Zero); }
        else {
            mouse_event(LD,0,0,0,IntPtr.Zero); mouse_event(LU,0,0,0,IntPtr.Zero);
            if (dbl) { System.Threading.Thread.Sleep(70); mouse_event(LD,0,0,0,IntPtr.Zero); mouse_event(LU,0,0,0,IntPtr.Zero); }
        }
        return string.Format("{0},{1} {2}x{3}", r.L, r.T, r.R - r.L, r.B - r.T);
    }
}
"@
}
[void][Raw]::SetProcessDpiAwarenessContext([IntPtr](-4))
$p = Get-Process Wordsbook | Where-Object { $_.MainWindowHandle -ne [IntPtr]::Zero } | Select-Object -First 1
$before = [Raw]::At($p.MainWindowHandle, $X, $Y, [bool]$Right, [bool]$Double)
Start-Sleep -Milliseconds 500
$r2 = New-Object Raw+RECT
[void][Raw]::GetWindowRect($p.MainWindowHandle, [ref]$r2)
Write-Output "win=$before now=$($r2.L),$($r2.T) $($r2.R-$r2.L)x$($r2.B-$r2.T) client=($X,$Y) right=$Right double=$Double"
