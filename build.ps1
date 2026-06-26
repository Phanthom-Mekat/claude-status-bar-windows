# Build portable ClaudeStatusBar.exe (self-contained default + slim framework-dependent).
# Usage: ./build.ps1            # win-x64
#        ./build.ps1 -Rid win-arm64
param([string]$Rid = "win-x64")
$ErrorActionPreference = "Stop"
Set-Location $PSScriptRoot
if (Test-Path 'C:\Program Files\dotnet') { $env:Path += ';C:\Program Files\dotnet' }

Write-Host "Extracting artwork from the macOS sources..."
node tools/extract-assets.mjs

Write-Host "Publishing self-contained ($Rid)..."
dotnet publish src/ClaudeStatusBar -c Release -r $Rid --self-contained `
  -p:PublishSingleFile=true -o "build/$Rid-selfcontained"

Write-Host "Publishing framework-dependent ($Rid)..."
dotnet publish src/ClaudeStatusBar -c Release -r $Rid --self-contained:$false `
  -p:PublishSingleFile=true -o "build/$Rid-framework"

Write-Host ""
Write-Host "Built:"
Write-Host "  build/$Rid-selfcontained/ClaudeStatusBar.exe   (portable, no runtime needed)"
Write-Host "  build/$Rid-framework/ClaudeStatusBar.exe        (needs .NET 8 Desktop Runtime)"
