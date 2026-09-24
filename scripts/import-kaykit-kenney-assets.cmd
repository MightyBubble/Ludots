@echo off
setlocal
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0import-kaykit-kenney-assets.ps1" %*
endlocal
