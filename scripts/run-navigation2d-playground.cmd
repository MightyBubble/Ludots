@echo off
powershell -ExecutionPolicy Bypass -File "%~dp0run-navigation2d-playground.ps1" %*
exit /b %ERRORLEVEL%
