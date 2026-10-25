param(
    [string]$Path = "D:\Proj\vs26\Wordsbook\tools\i0.png",
    [int]$Left = 340, [int]$Top = 12, [int]$Wid = 200, [int]$Hei = 28,
    [int]$Scale = 6
)
$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.Drawing
$bmp = New-Object System.Drawing.Bitmap $Path

$neutral = 0; $subpixel = 0; $mid = 0; $maxSpread = 0
for ($py = $Top; $py -lt ($Top + $Hei); $py++) {
    for ($px = $Left; $px -lt ($Left + $Wid); $px++) {
        $c = $bmp.GetPixel($px, $py)
        $rg = [Math]::Abs([int]$c.R - [int]$c.G)
        $gb = [Math]::Abs([int]$c.G - [int]$c.B)
        $rb = [Math]::Abs([int]$c.R - [int]$c.B)
        $spread = [Math]::Max($rg, [Math]::Max($gb, $rb))
        if ($spread -gt $maxSpread) { $maxSpread = $spread }
        if ($spread -le 6) { $neutral++ }
        elseif ($spread -ge 26) { $subpixel++ }
        else { $mid++ }
    }
}
Write-Output "region=${Wid}x${Hei} at ($Left,$Top) file=$Path"
Write-Output "neutral=$neutral  fringe(>=26)=$subpixel  mid=$mid  maxChannelSpread=$maxSpread"

$big = New-Object System.Drawing.Bitmap ([int]($Wid * $Scale)), ([int]($Hei * $Scale))
$g = [System.Drawing.Graphics]::FromImage($big)
$g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::NearestNeighbor
$src = New-Object System.Drawing.Rectangle($Left, $Top, $Wid, $Hei)
$dst = New-Object System.Drawing.Rectangle(0, 0, ($Wid * $Scale), ($Hei * $Scale))
$g.DrawImage($bmp, $dst, $src, [System.Drawing.GraphicsUnit]::Pixel)
$g.Dispose()
$big.Save("D:\Proj\vs26\Wordsbook\tools\text-zoom.png", [System.Drawing.Imaging.ImageFormat]::Png)
$big.Dispose(); $bmp.Dispose()
Write-Output "zoom -> tools/text-zoom.png"
