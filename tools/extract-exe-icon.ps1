Add-Type -AssemblyName System.Drawing

Add-Type -TypeDefinition @"
using System;
using System.Runtime.InteropServices;
public class IconExtractor {
    [DllImport("user32.dll", CharSet=CharSet.Auto)]
    public static extern int PrivateExtractIcons(string lpszFile, int nIconIndex, int cxIcon, int cyIcon, IntPtr[] phicon, int[] piconid, int nIcons, int flags);
    [DllImport("user32.dll")]
    public static extern bool DestroyIcon(IntPtr hIcon);
}
"@

$exe = "D:\Proj\vs26\Wordsbook\Wordsbook\bin\Debug\net9.0-windows10.0.22621.0\Wordsbook.exe"
foreach ($size in 256, 48, 32, 16) {
    $h = New-Object IntPtr[] 1
    $id = New-Object int[] 1
    $n = [IconExtractor]::PrivateExtractIcons($exe, 0, $size, $size, $h, $id, 1, 0)
    if ($n -gt 0 -and $h[0] -ne [IntPtr]::Zero) {
        $icon = [System.Drawing.Icon]::FromHandle($h[0])
        $bmp = $icon.ToBitmap()
        $out = "D:\Proj\vs26\Wordsbook\tools\exe-icon-$size.png"
        $bmp.Save($out, [System.Drawing.Imaging.ImageFormat]::Png)
        Write-Output "$size -> $($bmp.Width)x$($bmp.Height) $out"
        $bmp.Dispose()
        $icon.Dispose()
        [IconExtractor]::DestroyIcon($h[0]) | Out-Null
    } else {
        Write-Output "$size -> none (count=$n)"
    }
}
