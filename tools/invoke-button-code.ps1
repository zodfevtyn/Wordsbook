param([Parameter(Mandatory = $true)][string]$Code)
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes

$ch = [string][char][Convert]::ToInt32($Code, 16)
$pidTarget = (Get-Process Wordsbook | Where-Object { $_.MainWindowHandle -ne [IntPtr]::Zero } | Select-Object -First 1).Id
$cond = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ProcessIdProperty, $pidTarget)
$win = [System.Windows.Automation.AutomationElement]::RootElement.FindFirst([System.Windows.Automation.TreeScope]::Children, $cond)
$all = $win.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition)
foreach ($e in $all) {
    if ($e.Current.ControlType.ProgrammaticName -ne 'ControlType.Button') { continue }
    if ($e.Current.Name -ne $ch) { continue }
    $r = $e.Current.BoundingRectangle
    ([System.Windows.Automation.InvokePattern]$e.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern)).Invoke()
    Write-Output "invoked $Code at $([int]$r.X),$([int]$r.Y)"
    exit 0
}
Write-Output "button $Code not found"
