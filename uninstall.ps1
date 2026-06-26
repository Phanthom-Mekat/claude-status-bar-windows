# Claude Status Bar for Windows — uninstaller.
#
#   irm https://raw.githubusercontent.com/Phanthom-Mekat/claude-status-bar-windows/main/uninstall.ps1 | iex
#
# Removes the run-at-login entry, removes our Claude Code hooks, and deletes the install folder.
$ErrorActionPreference = 'SilentlyContinue'
$dest = Join-Path $env:LOCALAPPDATA 'ClaudeStatusBar'
$exe  = Join-Path $dest 'ClaudeStatusBar.exe'

Write-Host "Uninstalling Claude Status Bar..." -ForegroundColor Cyan
if (Test-Path $exe) { & $exe --uninstall; Start-Sleep -Milliseconds 800 }   # strips our hooks from settings.json
Get-Process ClaudeStatusBar -ErrorAction SilentlyContinue | Stop-Process -Force
Remove-ItemProperty -Path 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run' -Name 'ClaudeStatusBar' -ErrorAction SilentlyContinue
Start-Sleep -Milliseconds 500
Remove-Item -Recurse -Force $dest -ErrorAction SilentlyContinue
Write-Host "Removed. (Your ~/.claude/statusbar/app-settings.json prefs are left in place.)" -ForegroundColor Green
