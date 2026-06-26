# Claude Status Bar for Windows — uninstaller.
#
#   irm https://raw.githubusercontent.com/Phanthom-Mekat/claude-status-bar-windows/main/uninstall.ps1 | iex
#
# Removes our Claude Code hooks, the run-at-login entry, and the install folder.
$ErrorActionPreference = 'SilentlyContinue'
$dest = Join-Path $env:LOCALAPPDATA 'ClaudeStatusBar'
$dll = Join-Path $dest 'ClaudeStatusBar.dll'
$dotnet = Join-Path $env:ProgramFiles 'dotnet\dotnet.exe'
if (-not (Test-Path $dotnet)) { $c = Get-Command dotnet -ErrorAction SilentlyContinue; if ($c) { $dotnet = $c.Source } }

Write-Host "Uninstalling Claude Status Bar..." -ForegroundColor Cyan
if ((Test-Path $dll) -and (Test-Path $dotnet)) { & $dotnet "$dll" --uninstall; Start-Sleep -Milliseconds 800 } # strips our hooks from settings.json

Get-Process ClaudeStatusBar -ErrorAction SilentlyContinue | Stop-Process -Force
Get-CimInstance Win32_Process -Filter "Name='dotnet.exe'" -ErrorAction SilentlyContinue |
  Where-Object { $_.CommandLine -like '*ClaudeStatusBar.dll*' } | ForEach-Object { Stop-Process -Id $_.ProcessId -Force }
Remove-ItemProperty -Path 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run' -Name 'ClaudeStatusBar' -ErrorAction SilentlyContinue
Start-Sleep -Milliseconds 500
Remove-Item -Recurse -Force $dest -ErrorAction SilentlyContinue
Write-Host "Removed. (Your ~/.claude/statusbar/app-settings.json prefs are left in place.)" -ForegroundColor Green
