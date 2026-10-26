Add-Type -AssemblyName System.Drawing

$a = [System.Drawing.Bitmap]::FromFile("D:\Proj\vs26\Wordsbook\tools\icon256.png")
$b = [System.Drawing.Bitmap]::FromFile("D:\Proj\vs26\Wordsbook\tools\exe-icon-256.png")
Write-Output "sizes: $($a.Width)x$($a.Height) vs $($b.Width)x$($b.Height)"
$max = 0
$sum = 0L
$count = 0L
for ($y = 0; $y -lt $a.Height; $y++) {
    for ($x = 0; $x -lt $a.Width; $x++) {
        $pa = $a.GetPixel($x, $y)
        $pb = $b.GetPixel($x, $y)
        foreach ($d in @([Math]::Abs($pa.A - $pb.A), [Math]::Abs($pa.R - $pb.R), [Math]::Abs($pa.G - $pb.G), [Math]::Abs($pa.B - $pb.B))) {
            if ($d -gt $max) { $max = $d }
            $sum += $d
            $count++
        }
    }
}
Write-Output "max channel diff: $max"
Write-Output "mean channel diff: $([Math]::Round($sum / $count, 4))"
$a.Dispose()
$b.Dispose()
