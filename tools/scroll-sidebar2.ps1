Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes

$pidTarget = (Get-Process Wordsbook | Where-Object { $_.MainWindowHandle -ne [IntPtr]::Zero } | Select-Object -First 1).Id
$cond = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ProcessIdProperty, $pidTarget)
$win = [System.Windows.Automation.AutomationElement]::RootElement.FindFirst([System.Windows.Automation.TreeScope]::Children, $cond)
$all = $win.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition)
$found = 0
foreach ($e in $all) {
    try {
        $sp = [System.Windows.Automation.ScrollPattern]$e.GetCurrentPattern([System.Windows.Automation.ScrollPattern]::Pattern)
    } catch { continue }
    $r = $e.Current.BoundingRectangle
    $found++
    Write-Output ("scrollable: type={0} rect={1},{2} {3}x{4} vScroll={5} percent={6}" -f ($e.Current.ControlType.ProgrammaticName -replace 'ControlType\.',''), [int]$r.X, [int]$r.Y, [int]$r.Width, [int]$r.Height, $sp.Current.VerticallyScrollable, $sp.Current.VerticalScrollPercent)
    if ($r.X -lt 500 -and $r.Width -gt 100 -and $r.Width -lt 500 -and $sp.Current.VerticallyScrollable) {
        $sp.SetScrollPercent([System.Windows.Automation.ScrollPattern]::NoScroll, 100)
        Start-Sleep -Milliseconds 900
        Write-Output "  -> scrolled to $($sp.Current.VerticalScrollPercent)%"
    }
}
if ($found -eq 0) { Write-Output "no scrollable elements found" }
