@echo off
setlocal
cd /d "%~dp0"
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0run-ludots-pi.ps1" %*
exit /b %errorlevel%
