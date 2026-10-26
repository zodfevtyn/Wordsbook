Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes

$pidTarget = (Get-Process Wordsbook | Where-Object { $_.MainWindowHandle -ne [IntPtr]::Zero } | Select-Object -First 1).Id
$cond = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ProcessIdProperty, $pidTarget)
$win = [System.Windows.Automation.AutomationElement]::RootElement.FindFirst([System.Windows.Automation.TreeScope]::Children, $cond)
$all = $win.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition)

Write-Output "--- interactive elements in sidebar (x < 450) ---"
foreach ($e in $all) {
    $ct = $e.Current.ControlType.ProgrammaticName
    if ($ct -notmatch 'Button|ScrollViewer|List|Edit') { continue }
    $r = $e.Current.BoundingRectangle
    if ($r.IsEmpty -or $r.X -gt 450 -or $r.Y -lt 40) { continue }
    if ($r.Y -gt 1150) { continue }
    Write-Output ("[{0}] name='{1}' rect={2},{3} {4}x{5}" -f ($ct -replace 'ControlType\.', ''), $e.Current.Name, [int]$r.X, [int]$r.Y, [int]$r.Width, [int]$r.Height)
}

Write-Output "--- all texts y>700 ---"
foreach ($e in $all) {
    if ($e.Current.ControlType.ProgrammaticName -ne 'ControlType.Text') { continue }
    $r = $e.Current.BoundingRectangle
    if ($r.IsEmpty -or $r.X -gt 450 -or $r.Y -lt 700) { continue }
    Write-Output ("text='{0}' rect={1},{2} {3}x{4}" -f $e.Current.Name, [int]$r.X, [int]$r.Y, [int]$r.Width, [int]$r.Height)
}
