@echo off
setlocal
set "NUGET_PACKAGES=C:\Users\sietg\.nuget\packages"
set "HOME="
"C:\Program Files\dotnet\dotnet.exe" restore "C:\001_AI\LudotsProd\.worktrees\audit-pr660-merge\src\Core\Ludots.Core.csproj" -v minimal
echo EXIT=%ERRORLEVEL%
endlocal
