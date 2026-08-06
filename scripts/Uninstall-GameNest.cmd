@echo off
setlocal
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0Uninstall-GameNest.ps1"
if errorlevel 1 (
  echo.
  echo GameNest uninstall did not complete. Review the message above.
  pause
)
