@echo off
set "APP=%~dp0dist\FloatMate\FloatMate.exe"
if not exist "%APP%" set "APP=%~dp0dist\FloatMate-framework-dependent\FloatMate.exe"
if not exist "%APP%" call "%~dp0install.cmd"
if errorlevel 1 exit /b %errorlevel%
set "APP=%~dp0dist\FloatMate\FloatMate.exe"
if not exist "%APP%" set "APP=%~dp0dist\FloatMate-framework-dependent\FloatMate.exe"
if not exist "%APP%" (
  echo FloatMate.exe was not found. Run install.cmd again.
  pause
  exit /b 1
)
start "" "%APP%"
