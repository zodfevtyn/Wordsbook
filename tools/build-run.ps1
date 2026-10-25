param([string]$Out = "D:\Proj\vs26\Wordsbook\tools\shot.png", [switch]$SkipBuild)

$ErrorActionPreference = "Continue"
Get-Process Wordsbook -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Milliseconds 600

if (-not $SkipBuild) {
    Push-Location "D:\Proj\vs26\Wordsbook"
    $log = dotnet build Wordsbook/Wordsbook.csproj -c Debug -v q --nologo 2>&1 | Out-String
    Pop-Location
    if ($log -notmatch "Build succeeded") {
        ($log -split "`n" | Where-Object { $_ -match "error" } | Select-Object -Unique -First 20) -join "`n" | Write-Output
        throw "build failed"
    }
}

& "D:\Proj\vs26\Wordsbook\tools\shot.ps1" -Out $Out -DelayMs 2600
