param([string]$Version = "0.1.1")
$ErrorActionPreference = "Stop"

# 打包 Wordsbook 桌面版（未打包 WinUI 3，非 MSIX）。
# 必须带 EnableMsixTooling=true：关掉它时自包含发布不会接上 Windows App Runtime 的本地引导，
# 启动即在 Microsoft.UI.Xaml.dll 里以 0xC000027B 崩溃。
$proj = Join-Path $PSScriptRoot "..\Wordsbook\Wordsbook.csproj"
$out = Join-Path $PSScriptRoot "..\Wordsbook\bin\publish\Wordsbook-$Version-win-x64"

dotnet publish $proj -c Release -r win-x64 --self-contained true `
    -p:WindowsAppSDKSelfContained=true -p:PublishReadyToRun=false -p:EnableMsixTooling=true `
    -o $out --nologo

if (-not (Test-Path (Join-Path $out "Wordsbook.exe"))) { throw "publish produced no exe" }

$zip = Join-Path $env:TEMP "wordsbook-release\Wordsbook-$Version-win-x64-selfcontained.zip"
New-Item -ItemType Directory -Force -Path (Split-Path $zip) | Out-Null
if (Test-Path $zip) { Remove-Item $zip -Force }
Add-Type -AssemblyName System.IO.Compression.FileSystem
[System.IO.Compression.ZipFile]::CreateFromDirectory($out, $zip,
    [System.IO.Compression.CompressionLevel]::Optimal, $false)
Write-Output ("zip=" + $zip)
Write-Output ("mb=" + [Math]::Round((Get-Item $zip).Length / 1MB, 1))
