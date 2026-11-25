@echo off
setlocal
cd /d "%~dp0"
WebAccess.Media.exe --console --Tray:Enabled true %*
set "launchExitCode=%errorlevel%"
if not "%launchExitCode%"=="0" pause
exit /b %launchExitCode%
