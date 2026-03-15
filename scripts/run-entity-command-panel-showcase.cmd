@echo off
powershell -ExecutionPolicy Bypass -File "%~dp0run-entity-command-panel-showcase.ps1" %*
exit /b %ERRORLEVEL%
