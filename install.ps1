# Claude Status Bar for Windows — one-line installer.
#
#   irm https://raw.githubusercontent.com/Phanthom-Mekat/claude-status-bar-windows/main/install.ps1 | iex
#
# Downloads the latest release exe, sets it to run at login, and launches it once
# (which installs the Claude Code hooks and registers itself). Change $Repo if you fork.
$ErrorActionPreference = 'Stop'
$Repo = 'Phanthom-Mekat/claude-status-bar-windows'

$dest = Join-Path $env:LOCALAPPDATA 'ClaudeStatusBar'
$exe  = Join-Path $dest 'ClaudeStatusBar.exe'
$url  = "https://github.com/$Repo/releases/latest/download/ClaudeStatusBar.exe"

Write-Host "Installing Claude Status Bar..." -ForegroundColor Cyan
New-Item -ItemType Directory -Force -Path $dest | Out-Null

# stop a running instance so we can overwrite the exe
Get-Process ClaudeStatusBar -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Milliseconds 500

Invoke-WebRequest -Uri $url -OutFile $exe

# run at login
$runKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
Set-ItemProperty -Path $runKey -Name 'ClaudeStatusBar' -Value "`"$exe`""

# launch once -> installs the Claude Code hooks + writes apppath.txt
Start-Process $exe

Write-Host "Done. Claude Status Bar is in your system tray." -ForegroundColor Green
Write-Host "Tip: drag its icon out of the ^ overflow (or Settings > Taskbar > Other system tray icons) to keep it always visible."
Write-Host "Start a Claude Code session and the icon will react. Uninstall: irm .../uninstall.ps1 | iex"
