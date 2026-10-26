param([Parameter(Mandatory = $true)][string]$Name)
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes

$pidTarget = (Get-Process Wordsbook | Where-Object { $_.MainWindowHandle -ne [IntPtr]::Zero } | Select-Object -First 1).Id
$cond = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ProcessIdProperty, $pidTarget)
$win = [System.Windows.Automation.AutomationElement]::RootElement.FindFirst([System.Windows.Automation.TreeScope]::Children, $cond)
$all = $win.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition)
$walker = [System.Windows.Automation.TreeWalker]::ControlViewWalker

foreach ($e in $all) {
    if ($e.Current.ControlType.ProgrammaticName -ne 'ControlType.Text') { continue }
    if ($e.Current.Name -ne $Name) { continue }
    $p = $walker.GetParent($e)
    while ($p -and $p.Current.ControlType.ProgrammaticName -ne 'ControlType.Button') { $p = $walker.GetParent($p) }
    if (-not $p) { continue }
    $r = $p.Current.BoundingRectangle
    if ($r.IsEmpty -or $r.X -gt 500) { continue }
    ([System.Windows.Automation.InvokePattern]$p.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern)).Invoke()
    Write-Output "invoked row '$Name' at $([int]$r.X),$([int]$r.Y)"
    exit 0
}
Write-Output "row '$Name' not found"
