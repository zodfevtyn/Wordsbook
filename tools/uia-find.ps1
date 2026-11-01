param([string]$NamePattern = '.')

Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes

$proc = Get-Process Wordsbook | Where-Object { $_.MainWindowHandle -ne [IntPtr]::Zero } | Select-Object -First 1
$cond = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ProcessIdProperty, $proc.Id)
$win = [System.Windows.Automation.AutomationElement]::RootElement.FindFirst([System.Windows.Automation.TreeScope]::Children, $cond)
$all = $win.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition)

foreach ($e in $all) {
    $ct = $e.Current.ControlType.ProgrammaticName -replace 'ControlType\.', ''
    if ($ct -notmatch 'Button|ListItem|Edit|Text|Hyperlink') { continue }
    $n = $e.Current.Name
    if ($n -notmatch $NamePattern) { continue }
    $r = $e.Current.BoundingRectangle
    if ($r.IsEmpty) { continue }
    Write-Output ("{0} id='{1}' name='{2}' rect={3},{4} {5}x{6}" -f $ct, $e.Current.AutomationId, $n, [int]$r.X, [int]$r.Y, [int]$r.Width, [int]$r.Height)
}
