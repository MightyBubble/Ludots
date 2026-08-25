@echo off
setlocal
cd /d "%~dp0.."
dotnet run --project tools/FieldEditor/FieldEditor.csproj -- %*
exit /b %errorlevel%
