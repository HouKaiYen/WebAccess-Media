@echo off
cd /d "%~dp0"
WebAccess.Media.exe --urls http://127.0.0.1:3008 --Tray:Enabled true %*
if errorlevel 1 pause
