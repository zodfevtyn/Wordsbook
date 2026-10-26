Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes

$pidTarget = (Get-Process Wordsbook | Where-Object { $_.MainWindowHandle -ne [IntPtr]::Zero } | Select-Object -First 1).Id
$cond = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ProcessIdProperty, $pidTarget)
$win = [System.Windows.Automation.AutomationElement]::RootElement.FindFirst([System.Windows.Automation.TreeScope]::Children, $cond)
$all = $win.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition)

foreach ($e in $all) {
    if ($e.Current.ControlType.ProgrammaticName -ne 'ControlType.ScrollViewer') { continue }
    $r = $e.Current.BoundingRectangle
    Write-Output ("scrollviewer rect={0},{1} {2}x{3}" -f [int]$r.X, [int]$r.Y, [int]$r.Width, [int]$r.Height)
    if ($r.Width -gt 100 -and $r.Width -lt 500 -and $r.X -lt 500) {
        try {
            $sp = $e.GetCurrentPattern([System.Windows.Automation.ScrollPattern]::Pattern)
            Write-Output "  before: $($sp.Current.VerticalScrollPercent)% scrollable=$($sp.Current.VerticallyScrollable)"
            $sp.SetScrollPercent([System.Windows.Automation.ScrollPattern]::NoScroll, 100)
            Start-Sleep -Milliseconds 800
            Write-Output "  after: $($sp.Current.VerticalScrollPercent)%"
        } catch { Write-Output "  scroll failed: $($_.Exception.Message)" }
    }
}
