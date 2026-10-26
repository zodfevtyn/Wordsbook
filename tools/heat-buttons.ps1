Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes

$pidTarget = (Get-Process Wordsbook | Where-Object { $_.MainWindowHandle -ne [IntPtr]::Zero } | Select-Object -First 1).Id
$cond = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ProcessIdProperty, $pidTarget)
$win = [System.Windows.Automation.AutomationElement]::RootElement.FindFirst([System.Windows.Automation.TreeScope]::Children, $cond)
$all = $win.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition)

Write-Output "--- heatmap area buttons (screen y 560-760, x 350-520) ---"
foreach ($e in $all) {
    if ($e.Current.ControlType.ProgrammaticName -ne 'ControlType.Button') { continue }
    $r = $e.Current.BoundingRectangle
    if ($r.IsEmpty -or $r.X -lt 350 -or $r.X -gt 520 -or $r.Y -lt 560 -or $r.Y -gt 760) { continue }
    $name = $e.Current.Name
    $codes = ($name.ToCharArray() | ForEach-Object { 'U+{0:X4}' -f [int]$_ }) -join ' '
    Write-Output ("button name='{0}' codes={1} rect={2},{3} {4}x{5}" -f $name, $codes, [int]$r.X, [int]$r.Y, [int]$r.Width, [int]$r.Height)
}

Write-Output "--- heat label texts (screen y 560-660, x 20-350) ---"
foreach ($e in $all) {
    if ($e.Current.ControlType.ProgrammaticName -ne 'ControlType.Text') { continue }
    $r = $e.Current.BoundingRectangle
    if ($r.IsEmpty -or $r.X -lt 20 -or $r.X -gt 350 -or $r.Y -lt 560 -or $r.Y -gt 660) { continue }
    Write-Output ("text='{0}' rect={1},{2} {3}x{4}" -f $e.Current.Name, [int]$r.X, [int]$r.Y, [int]$r.Width, [int]$r.Height)
}
