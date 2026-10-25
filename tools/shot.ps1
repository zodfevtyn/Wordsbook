param(
    [string]$Exe = "D:\Proj\vs26\Wordsbook\Wordsbook\bin\Debug\net9.0-windows10.0.22621.0\Wordsbook.exe",
    [string]$Out = "D:\Proj\vs26\Wordsbook\tools\shot.png",
    [int]$DelayMs = 2500,
    [switch]$Kill,
    [switch]$Screen,
    [switch]$NoPlace,
    [switch]$KeepSize
)

$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.Drawing

if (-not ("Cap" -as [type])) {
$refs = [AppDomain]::CurrentDomain.GetAssemblies() |
    Where-Object { -not $_.IsDynamic -and $_.Location } |
    ForEach-Object { $_.Location }
Add-Type -ReferencedAssemblies $refs @"
using System;
using System.Drawing;
using System.Runtime.InteropServices;

public static class Cap {
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr h, int n);
    [DllImport("user32.dll")] static extern bool MoveWindow(IntPtr h, int x, int y, int w, int ht, bool repaint);
    [DllImport("user32.dll")] static extern void SwitchToThisWindow(IntPtr h, bool alt);
    [DllImport("user32.dll")] static extern bool PrintWindow(IntPtr h, IntPtr hdc, uint flags);
    [DllImport("user32.dll")] static extern IntPtr GetWindowDC(IntPtr h);
    [DllImport("user32.dll")] static extern int ReleaseDC(IntPtr h, IntPtr dc);

    public static Rectangle RectOf(IntPtr h) { RECT r; GetWindowRect(h, out r); return Rectangle.FromLTRB(r.Left, r.Top, r.Right, r.Bottom); }
    public static void Focus(IntPtr h) { ShowWindow(h, 9); SwitchToThisWindow(h, true); SetForegroundWindow(h); }
    public static void Place(IntPtr h, int x, int y, int w, int ht) { ShowWindow(h, 9); MoveWindow(h, x, y, w, ht, true); Focus(h); }

    /// <summary>只挪位置不改尺寸，用于把窗口固定到主屏再截图。</summary>
    public static void MoveOnly(IntPtr h, int x, int y) {
        RECT r; GetWindowRect(h, out r);
        ShowWindow(h, 9);
        MoveWindow(h, x, y, r.Right - r.Left, r.Bottom - r.Top, true);
        Focus(h);
    }

    [DllImport("user32.dll")] static extern bool SetProcessDpiAwarenessContext(IntPtr ctx);

    /// <summary>PS 5.1 默认是 DPI-unaware，坐标会被虚拟化，量出来的窗口边界全是假的。</summary>
    public static void MakeAware() {
        try { SetProcessDpiAwarenessContext(new IntPtr(-4)); } catch { }
    }

    /// <summary>PrintWindow 能在窗口被遮挡时抓到内容，优先使用。</summary>
    public static Bitmap GrabWindow(IntPtr h) {
        Rectangle rc = RectOf(h);
        if (rc.Width < 20 || rc.Height < 20) return null;
        var bmp = new Bitmap(rc.Width, rc.Height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bmp)) {
            IntPtr hdc = g.GetHdc();
            bool ok = PrintWindow(h, hdc, 2);
            g.ReleaseHdc(hdc);
            if (!ok) return null;
        }
        return bmp;
    }

    public static Bitmap Grab(int x, int y, int w, int h) {
        var bmp = new Bitmap(w, h, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bmp)) g.CopyFromScreen(x, y, 0, 0, new Size(w, h));
        return bmp;
    }
}
"@
}
[Cap]::MakeAware()

if ($Kill) {
    Get-Process Wordsbook -ErrorAction SilentlyContinue | Stop-Process -Force
    Write-Output "killed"
    exit 0
}

$running = @(Get-Process Wordsbook -ErrorAction SilentlyContinue | Where-Object { $_.MainWindowHandle -ne [IntPtr]::Zero })
if ($running.Count -eq 0) {
    Start-Process -FilePath $Exe | Out-Null
    $deadline = (Get-Date).AddSeconds(25)
    while ((Get-Date) -lt $deadline) {
        Start-Sleep -Milliseconds 400
        $running = @(Get-Process Wordsbook -ErrorAction SilentlyContinue | Where-Object { $_.MainWindowHandle -ne [IntPtr]::Zero })
        if ($running.Count -gt 0) { break }
    }
}
if ($running.Count -eq 0) { throw "Wordsbook window never appeared" }

$hwnd = $running[0].MainWindowHandle
if ($KeepSize) { [Cap]::MoveOnly($hwnd, 60, 60) }
elseif ($NoPlace) { [Cap]::Focus($hwnd) }
else { [Cap]::Place($hwnd, 40, 40, 1500, 950) }
Start-Sleep -Milliseconds $DelayMs
[Cap]::Focus($hwnd)
Start-Sleep -Milliseconds 900

$rect = [Cap]::RectOf($hwnd)
if ($rect.Width -lt 50 -or $rect.Height -lt 50) { throw "unexpected rect: $rect" }

$bmp = $null
if (-not $Screen) { try { $bmp = [Cap]::GrabWindow($hwnd) } catch { $bmp = $null } }
$mode = "printwindow"
if (-not $bmp) { $bmp = [Cap]::Grab($rect.Left, $rect.Top, $rect.Width, $rect.Height); $mode = "screen" }
$bmp.Save($Out, [System.Drawing.Imaging.ImageFormat]::Png)
$bmp.Dispose()
Write-Output "OK pid=$($running[0].Id) $mode rect=$rect out=$Out"
