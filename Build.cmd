@echo off
where pwsh.exe >nul 2>nul
if errorlevel 1 (
  echo PowerShell 7 is required for the developer build.
  exit /b 1
)
pwsh.exe -NoProfile -File "%~dp0scripts\build.ps1" -Test -Installer
exit /b %ERRORLEVEL%

