@echo off
title FloatMate Setup
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0setup.ps1"
if errorlevel 1 (
  echo.
  echo Setup did not complete. Keep the error message above for troubleshooting.
  pause
  exit /b 1
)
echo.
pause
