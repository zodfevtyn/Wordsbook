Add-Type -AssemblyName System.Drawing

Add-Type -TypeDefinition @"
using System;
using System.Runtime.InteropServices;
public static class WinIcon {
    [DllImport("user32.dll", CharSet=CharSet.Auto)]
    public static extern IntPtr SendMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll", EntryPoint="GetClassLongPtr", CharSet=CharSet.Auto)]
    public static extern IntPtr GetClassLongPtr(IntPtr hWnd, int nIndex);
}
"@

$p = Get-Process Wordsbook | Select-Object -First 1
if (-not $p) { Write-Output "no Wordsbook process"; exit 1 }
$h = $p.MainWindowHandle
Write-Output "pid=$($p.Id) hwnd=$h title=$($p.MainWindowTitle)"

$WM_GETICON = 0x007F
$map = @{ "big" = 1; "small" = 0; "small2" = 2 }
foreach ($k in $map.Keys) {
    $hi = [WinIcon]::SendMessage($h, $WM_GETICON, [IntPtr]$map[$k], [IntPtr]::Zero)
    Write-Output "WM_GETICON $k -> $hi"
    if ($hi -ne [IntPtr]::Zero) {
        $icon = [System.Drawing.Icon]::FromHandle($hi)
        $bmp = $icon.ToBitmap()
        $out = "D:\Proj\vs26\Wordsbook\tools\window-icon-$k.png"
        $bmp.Save($out, [System.Drawing.Imaging.ImageFormat]::Png)
        Write-Output "  saved $($bmp.Width)x$($bmp.Height) $out"
        $bmp.Dispose()
        $icon.Dispose()
    }
}

$classBig = [WinIcon]::GetClassLongPtr($h, -14)
Write-Output "GCLP_HICON -> $classBig"
if ($classBig -ne [IntPtr]::Zero) {
    $icon = [System.Drawing.Icon]::FromHandle($classBig)
    $bmp = $icon.ToBitmap()
    $out = "D:\Proj\vs26\Wordsbook\tools\window-icon-class.png"
    $bmp.Save($out, [System.Drawing.Imaging.ImageFormat]::Png)
    Write-Output "  saved $($bmp.Width)x$($bmp.Height) $out"
    $bmp.Dispose()
    $icon.Dispose()
}
