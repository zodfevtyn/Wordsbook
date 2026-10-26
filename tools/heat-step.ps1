param([string]$Code = "E76B")
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes

$ch = [char][Convert]::ToInt32($Code, 16)
$pidTarget = (Get-Process Wordsbook | Where-Object { $_.MainWindowHandle -ne [IntPtr]::Zero } | Select-Object -First 1).Id
$cond = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ProcessIdProperty, $pidTarget)
$win = [System.Windows.Automation.AutomationElement]::RootElement.FindFirst([System.Windows.Automation.TreeScope]::Children, $cond)
$all = $win.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition)
foreach ($e in $all) {
    if ($e.Current.ControlType.ProgrammaticName -ne 'ControlType.Button') { continue }
    if ($e.Current.Name -ne [string]$ch) { continue }
    $r = $e.Current.BoundingRectangle
    if ($r.IsEmpty -or $r.X -lt 350 -or $r.X -gt 520 -or $r.Y -lt 560 -or $r.Y -gt 760) { continue }
    ([System.Windows.Automation.InvokePattern]$e.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern)).Invoke()
    Write-Output "invoked $Code"
    exit 0
}
Write-Output "button $Code not found"
