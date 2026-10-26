Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes

function Get-Win {
    $pidTarget = (Get-Process Wordsbook | Where-Object { $_.MainWindowHandle -ne [IntPtr]::Zero } | Select-Object -First 1).Id
    $cond = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ProcessIdProperty, $pidTarget)
    return [System.Windows.Automation.AutomationElement]::RootElement.FindFirst([System.Windows.Automation.TreeScope]::Children, $cond)
}

function Get-Label($win) {
    $all = $win.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition)
    foreach ($e in $all) {
        if ($e.Current.ControlType.ProgrammaticName -ne 'ControlType.Text') { continue }
        $r = $e.Current.BoundingRectangle
        if ($r.IsEmpty -or $r.X -lt 20 -or $r.X -gt 350 -or $r.Y -lt 560 -or $r.Y -gt 760) { continue }
        if ($e.Current.Name -match '^\w{3} \d{4}$') { return $e.Current.Name }
    }
    return '(label not found)'
}

function Invoke-HeatArrow($win, $code) {
    $all = $win.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition)
    foreach ($e in $all) {
        if ($e.Current.ControlType.ProgrammaticName -ne 'ControlType.Button') { continue }
        if ($e.Current.Name -ne $code) { continue }
        $r = $e.Current.BoundingRectangle
        if ($r.IsEmpty -or $r.X -lt 350 -or $r.X -gt 520 -or $r.Y -lt 560 -or $r.Y -gt 760) { continue }
        $p = $e.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern)
        $p.Invoke()
        return "invoked $code at $([int]$r.X),$([int]$r.Y)"
    }
    return "button $code not found"
}

$win = Get-Win
Write-Output "before: $(Get-Label $win)"
Write-Output (Invoke-HeatArrow $win ([string][char]0xE76B))
Start-Sleep -Milliseconds 700
$win = Get-Win
Write-Output "after E76B: $(Get-Label $win)"
Write-Output (Invoke-HeatArrow $win ([string][char]0xE76C))
Start-Sleep -Milliseconds 700
$win = Get-Win
Write-Output "after E76C: $(Get-Label $win)"
