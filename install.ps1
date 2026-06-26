# Claude Status Bar for Windows — one-line installer.
#
#   irm https://raw.githubusercontent.com/Phanthom-Mekat/claude-status-bar-windows/main/install.ps1 | iex
#
# Runs the app through the Microsoft-signed .NET host (dotnet), so it works even under Windows
# Smart App Control — no code-signing, no "unknown publisher" warning. Change $Repo if you fork.
$ErrorActionPreference = 'Stop'
$Repo = 'Phanthom-Mekat/claude-status-bar-windows'
$dest = Join-Path $env:LOCALAPPDATA 'ClaudeStatusBar'
$dotnet = Join-Path $env:ProgramFiles 'dotnet\dotnet.exe'

Write-Host "Installing Claude Status Bar..." -ForegroundColor Cyan

# 1) Ensure the .NET 8 Desktop Runtime (the trusted host that runs our app under Smart App Control).
$haveRuntime = $false
if (Test-Path $dotnet) {
  try { if ((& $dotnet --list-runtimes) -match 'Microsoft\.WindowsDesktop\.App 8\.') { $haveRuntime = $true } } catch {}
}
if (-not $haveRuntime) {
  Write-Host "Installing the .NET 8 Desktop Runtime (one-time, via winget)..."
  winget install --id Microsoft.DotNet.DesktopRuntime.8 -e --accept-source-agreements --accept-package-agreements --silent
}
if (-not (Test-Path $dotnet)) { $c = Get-Command dotnet -ErrorAction SilentlyContinue; if ($c) { $dotnet = $c.Source } }

# 2) Stop any running instance, then download + extract the app bundle.
New-Item -ItemType Directory -Force -Path $dest | Out-Null
Get-Process ClaudeStatusBar -ErrorAction SilentlyContinue | Stop-Process -Force
Get-CimInstance Win32_Process -Filter "Name='dotnet.exe'" -ErrorAction SilentlyContinue |
  Where-Object { $_.CommandLine -like '*ClaudeStatusBar.dll*' } | ForEach-Object { Stop-Process -Id $_.ProcessId -Force }
Start-Sleep -Milliseconds 400
$zip = Join-Path $env:TEMP 'ClaudeStatusBar.zip'
Invoke-WebRequest -Uri "https://github.com/$Repo/releases/latest/download/ClaudeStatusBar.zip" -OutFile $zip
Expand-Archive -Path $zip -DestinationPath $dest -Force

# 3) Run at login + launch now, both via the trusted dotnet host.
$dll = Join-Path $dest 'ClaudeStatusBar.dll'
Set-ItemProperty -Path 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run' -Name 'ClaudeStatusBar' -Value "`"$dotnet`" `"$dll`""
Start-Process -FilePath $dotnet -ArgumentList "`"$dll`"" -WindowStyle Hidden

Write-Host "Done. Claude Status Bar is in your system tray." -ForegroundColor Green
Write-Host "Tip: drag its icon out of the ^ overflow (Settings > Personalization > Taskbar > Other system tray icons) to keep it always visible."
Write-Host "Uninstall any time:  irm https://raw.githubusercontent.com/$Repo/main/uninstall.ps1 | iex"
