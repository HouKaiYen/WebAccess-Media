@echo off
cd /d "%~dp0"
WebAccess.Media.exe --Tray:Enabled true %*
if errorlevel 1 pause
