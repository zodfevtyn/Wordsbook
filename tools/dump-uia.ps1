Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes

$pidTarget = (Get-Process Wordsbook | Where-Object { $_.MainWindowHandle -ne [IntPtr]::Zero } | Select-Object -First 1).Id
$cond = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ProcessIdProperty, $pidTarget)
$win = [System.Windows.Automation.AutomationElement]::RootElement.FindFirst([System.Windows.Automation.TreeScope]::Children, $cond)
if (-not $win) { Write-Output "window not found for pid $pidTarget"; exit 1 }
Write-Output "window: $($win.Current.Name)"

$all = $win.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition)
foreach ($e in $all) {
    $name = $e.Current.Name
    if (-not $name) { continue }
    if ($name -match '20(25|26|27)|day|Start|Nederlands|Espa|Heat|Sun|Sat|Mon|Tue|Wed|Thu|Fri') {
        $ct = $e.Current.ControlType.ProgrammaticName -replace 'ControlType\.', ''
        $toggle = ''
        try {
            $tp = $e.GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern)
            $toggle = " toggle=$($tp.Current.ToggleState)"
        } catch { }
        $sel = ''
        try {
            $sp = $e.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern)
            $sel = " selected=$($sp.Current.IsSelected)"
        } catch { }
        Write-Output ("[{0}] {1}{2}{3}" -f $ct, $name, $toggle, $sel)
    }
}
