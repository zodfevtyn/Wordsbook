Add-Type -AssemblyName System.Drawing

$codes = @(
  @('E8A9','ViewAll'), @('E7C4','E7C4'), @('E80A','E80A'), @('E8B9','E8B9'),
  @('E9E9','E9E9'), @('E8F1','E8F1'), @('E71D','AllApps'), @('E9D5','E9D5'),
  @('E7EE','E7EE'), @('E7C3','E7C3'), @('E8B7','E8B7'), @('F0E2','Grid'),
  @('E8FD','List'), @('E8A1','E8A1'), @('E8B0','E8B0'), @('EA37','EA37')
)
$bmp = New-Object System.Drawing.Bitmap(900, 420)
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.Clear([System.Drawing.Color]::FromArgb(255, 24, 24, 27))
$iconFont = New-Object System.Drawing.Font("Segoe Fluent Icons", 34)
$labelFont = New-Object System.Drawing.Font("Segoe UI", 10)
$iconBrush = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(255, 235, 235, 245))
$labelBrush = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(255, 150, 150, 160))
for ($i = 0; $i -lt $codes.Count; $i++) {
    $x = 30 + ($i % 4) * 215
    $y = 20 + [Math]::Floor($i / 4) * 100
    $ch = [string][char][Convert]::ToInt32($codes[$i][0], 16)
    $g.DrawString($ch, $iconFont, $iconBrush, $x, $y)
    $g.DrawString($codes[$i][0] + ' ' + $codes[$i][1], $labelFont, $labelBrush, $x, $y + 60)
}
$g.Dispose()
$bmp.Save("D:\Proj\vs26\Wordsbook\tools\glyph-candidates.png", [System.Drawing.Imaging.ImageFormat]::Png)
$bmp.Dispose()
Write-Output "saved"
