@echo off
set "APP=%~dp0FloatMate\bin\Release\net8.0-windows\win-x64\publish\FloatMate.exe"
if not exist "%APP%" (
  echo FloatMate has not been built yet.
  pause
  exit /b 1
)
start "" "%APP%"
