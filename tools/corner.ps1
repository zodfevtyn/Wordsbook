param(
    [string]$Path = "D:\Proj\vs26\Wordsbook\tools\final1.png",
    [int]$Rows = 26,
    [string]$Near = "179,179,181",
    [int]$Tol = 7
)
$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.Drawing
$bmp = New-Object System.Drawing.Bitmap $Path
$t = $Near -split ','
$tr = [int]$t[0]; $tg = [int]$t[1]; $tb = [int]$t[2]

function Inside([int]$x, [int]$y) {
    $c = $bmp.GetPixel($x, $y)
    return ([Math]::Abs($c.R - $tr) -le $Tol) -and ([Math]::Abs($c.G - $tg) -le $Tol) -and ([Math]::Abs($c.B - $tb) -le $Tol)
}

Write-Output "size=$($bmp.Width)x$($bmp.Height) near=$Near"
for ($y = 0; $y -lt $Rows; $y++) {
    $found = -1
    for ($x = 0; $x -lt 60; $x++) {
        $ok = $true
        for ($k = 0; $k -lt 8; $k++) { if (-not (Inside ($x + $k) $y)) { $ok = $false; break } }
        if ($ok) { $found = $x; break }
    }
    Write-Output ("row {0,2}: left={1}" -f $y, $found)
}
$bmp.Dispose()
