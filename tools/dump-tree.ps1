param([int]$MaxItems = 400)

Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes

$p = (Get-Process Wordsbook | Where-Object { $_.MainWindowHandle -ne [IntPtr]::Zero } | Select-Object -First 1).Id
$cond = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ProcessIdProperty, $p)
$win = [System.Windows.Automation.AutomationElement]::RootElement.FindFirst([System.Windows.Automation.TreeScope]::Children, $cond)
$r = $win.Current.BoundingRectangle
Write-Output ("window pid={0} rect={1},{2} {3}x{4}" -f $p, [int]$r.X, [int]$r.Y, [int]$r.Width, [int]$r.Height)

$all = $win.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition)
Write-Output ("descendants={0}" -f $all.Count)
$i = 0
foreach ($e in $all) {
  $ct = ($e.Current.ControlType.ProgrammaticName) -replace 'ControlType\.', ''
  $b = $e.Current.BoundingRectangle
  if ($b.IsEmpty) { $bs = "EMPTY" } else { $bs = "{0},{1} {2}x{3}" -f [int]$b.X, [int]$b.Y, [int]$b.Width, [int]$b.Height }
  $n = $e.Current.Name
  if ($n.Length -gt 28) { $n = $n.Substring(0, 28) }
  Write-Output ("{0,-16} {1,-30} {2}" -f $ct, "'$n'", $bs)
  $i++
  if ($i -ge $MaxItems) { Write-Output "... truncated"; break }
}
