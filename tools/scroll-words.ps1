param([int]$Percent = 30)
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes

$pidTarget = (Get-Process Wordsbook | Where-Object { $_.MainWindowHandle -ne [IntPtr]::Zero } | Select-Object -First 1).Id
$cond = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ProcessIdProperty, $pidTarget)
$win = [System.Windows.Automation.AutomationElement]::RootElement.FindFirst([System.Windows.Automation.TreeScope]::Children, $cond)
$all = $win.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition)
foreach ($e in $all) {
    try {
        $sp = [System.Windows.Automation.ScrollPattern]$e.GetCurrentPattern([System.Windows.Automation.ScrollPattern]::Pattern)
    } catch { continue }
    $r = $e.Current.BoundingRectangle
    if ($r.IsEmpty -or $r.X -lt 400 -or $r.Width -lt 400 -or -not $sp.Current.VerticallyScrollable) { continue }
    $sp.SetScrollPercent([System.Windows.Automation.ScrollPattern]::NoScroll, $Percent)
    Start-Sleep -Milliseconds 900
    Write-Output "scrolled word area to $($sp.Current.VerticalScrollPercent)%"
    exit 0
}
Write-Output "word-area scrollviewer not found"
