Add-Type -AssemblyName System.Drawing

$src = [System.Drawing.Bitmap]::FromFile("D:\Proj\vs26\Wordsbook\tools\heat-sep.png")
$rect = New-Object System.Drawing.Rectangle(330, 595, 140, 60)
$crop = $src.Clone($rect, $src.PixelFormat)
$big = New-Object System.Drawing.Bitmap(560, 240)
$g = [System.Drawing.Graphics]::FromImage($big)
$g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::NearestNeighbor
$g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::Half
$g.DrawImage($crop, 0, 0, 560, 240)
$g.Dispose()
$big.Save("D:\Proj\vs26\Wordsbook\tools\heat-arrows-zoom.png", [System.Drawing.Imaging.ImageFormat]::Png)
$src.Dispose()
$crop.Dispose()
$big.Dispose()
Write-Output "saved"
