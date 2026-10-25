param(
    [string]$Font = "Segoe Fluent Icons",
    [string]$Codes = "E700,E800,E794,F0E2,E8A0,EA37,F58C,EE7A,E716,E717",
    [string]$Out = "D:\Proj\vs26\Wordsbook\tools\glyphs.png"
)
$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.Drawing

$list = $Codes -split ','
$cell = 70
$bmp = New-Object System.Drawing.Bitmap ([int]($cell * $list.Count)), 110
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.Clear([System.Drawing.Color]::White)
$g.TextRenderingHint = [System.Drawing.Text.TextRenderingHint]::SingleBitPerPixelGridFit

$fam = New-Object System.Drawing.FontFamily $Font
$glyph = New-Object System.Drawing.Font $fam, 34
$label = New-Object System.Drawing.Font "Consolas", 10
$black = [System.Drawing.Brushes]::Black

for ($i = 0; $i -lt $list.Count; $i++) {
    $cp = [Convert]::ToInt32($list[$i], 16)
    $ch = [char]::ConvertFromUtf32($cp)
    $x = $i * $cell
    $g.DrawString($ch, $glyph, $black, [single]($x + 14), [single]18)
    $g.DrawString($list[$i], $label, $black, [single]($x + 14), [single]80)
}
$g.Dispose()
$bmp.Save($Out, [System.Drawing.Imaging.ImageFormat]::Png)
$bmp.Dispose()
Write-Output "rendered -> $Out"
