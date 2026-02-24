@echo off
setlocal
cd /d "%~dp0"
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0run-mod-launcher.ps1" %*
exit /b %errorlevel%
