#!/usr/bin/env bash
# Git Bash 缺 ProgramFiles 系变量时 dotnet NuGet 还原会炸（path1 null）。
# 带括号的变量名 bash export 不接受，必须经 env 内联注入。
# 用法: tmp/dotnet-win.sh <dotnet args...>；若反复报 path1 null，先 tmp/dotnet-win.sh build-server shutdown
env \
  'PROGRAMFILES=C:\Program Files' \
  'PROGRAMFILES(X86)=C:\Program Files (x86)' \
  'PROGRAMW6432=C:\Program Files' \
  'ProgramFiles=C:\Program Files' \
  'ProgramFiles(x86)=C:\Program Files (x86)' \
  'ProgramW6432=C:\Program Files' \
  'NUGET_PACKAGES=C:\Users\sietg\.nuget\packages' \
  "/c/Program Files/dotnet/dotnet.exe" "$@"
