param(
    [Parameter(Mandatory = $true)][string]$Source,
    [Parameter(Mandatory = $true)][string]$OutIco
)

Add-Type -AssemblyName System.Drawing

$sizes = @(256, 128, 64, 48, 32, 16)
$pngs = @()

$src = [System.Drawing.Image]::FromFile((Resolve-Path $Source).Path)

foreach ($s in $sizes) {
    $bmp = New-Object System.Drawing.Bitmap($s, $s, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $g.DrawImage($src, (New-Object System.Drawing.Rectangle(0, 0, $s, $s)))
    $g.Dispose()

    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    $pngs += ,@($s, $ms.ToArray())
    $ms.Dispose()
}
$src.Dispose()

$out = New-Object System.IO.MemoryStream
$w = New-Object System.IO.BinaryWriter($out)

$w.Write([uint16]0)          # reserved
$w.Write([uint16]1)          # type: icon
$w.Write([uint16]$pngs.Count)

$offset = 6 + (16 * $pngs.Count)
foreach ($entry in $pngs) {
    $dim = if ($entry[0] -ge 256) { 0 } else { $entry[0] }
    $w.Write([byte]$dim)                 # width
    $w.Write([byte]$dim)                 # height
    $w.Write([byte]0)                    # palette colours
    $w.Write([byte]0)                    # reserved
    $w.Write([uint16]1)                  # planes
    $w.Write([uint16]32)                 # bpp
    $w.Write([uint32]$entry[1].Length)   # data size
    $w.Write([uint32]$offset)            # data offset
    $offset += $entry[1].Length
}
foreach ($entry in $pngs) { $w.Write($entry[1]) }
$w.Flush()

New-Item -ItemType Directory -Force -Path (Split-Path $OutIco) | Out-Null
[System.IO.File]::WriteAllBytes($OutIco, $out.ToArray())
$w.Dispose(); $out.Dispose()

Write-Output "wrote $OutIco ($($pngs.Count) sizes)"
