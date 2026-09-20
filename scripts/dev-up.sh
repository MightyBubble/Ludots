#!/usr/bin/env bash
# 作者弱网一键入口（Linux / macOS / Git Bash）：不访问 nuget.org，只用仓库内 external/nuget。
# 依赖：已安装 .NET 9 SDK（与 global.json 一致）。不需要 Node（Raylib CLI 路径）。
# 用法：
#   ./scripts/dev-up.sh                              # 构建并启动默认 ExampleMod
#   ./scripts/dev-up.sh launch mod:X --adapter raylib
#   ./scripts/dev-up.sh resolve mod:X --adapter raylib
#   ./scripts/dev-up.sh build-only
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$ROOT"

if [[ ! -f nuget.config ]] || [[ ! -d external/nuget ]]; then
  echo "error: offline nuget layer missing (nuget.config + external/nuget/). Refusing to fall back to nuget.org." >&2
  exit 1
fi

if ! command -v dotnet >/dev/null 2>&1; then
  echo "error: dotnet SDK not found on PATH. Install .NET 9 SDK first." >&2
  exit 1
fi

SDK_LINE="$(dotnet --list-sdks | awk '/^9\./ {print; exit}')"
if [[ -z "${SDK_LINE}" ]]; then
  echo "error: .NET 9 SDK required (global.json). Installed SDKs:" >&2
  dotnet --list-sdks >&2 || true
  exit 1
fi

TFM=net9.0
CLI_PROJ="$ROOT/src/Tools/Ludots.Launcher.Cli/Ludots.Launcher.Cli.csproj"
CLI_DLL="$ROOT/src/Tools/Ludots.Launcher.Cli/bin/Release/${TFM}/Ludots.Launcher.Cli.dll"
APP_PROJ="$ROOT/src/Apps/Raylib/Ludots.App.Raylib/Ludots.App.Raylib.csproj"
DEFAULT_SELECTOR=(mod:LudotsCoreMod mod:ExampleMod)

# 断网友好：强制只用本地源（与 nuget.config <clear/> 对齐）；勿设置会指向外网的 fallback
export DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1
export DOTNET_NOLOGO=1
export DOTNET_CLI_TELEMETRY_OPTOUT=1

echo "== Ludots dev-up (offline nuget) =="
echo "repo: $ROOT"
echo "sdk:  ${SDK_LINE}"

build_stack() {
  echo "restore+build: Launcher.Cli"
  dotnet build "$CLI_PROJ" -c Release -v q
  echo "restore+build: App.Raylib"
  dotnet build "$APP_PROJ" -c Release -v q
  echo "restore+build: LudotsCoreMod + ExampleMod"
  dotnet build "$ROOT/mods/LudotsCoreMod/LudotsCoreMod.csproj" -c Release -v q
  dotnet build "$ROOT/mods/ExampleMod/ExampleMod.csproj" -c Release -v q
  if [[ ! -f "$CLI_DLL" ]]; then
    echo "error: launcher DLL missing after build: $CLI_DLL" >&2
    exit 1
  fi
}

ACTION="${1:-launch}"
if [[ "$ACTION" == "build-only" ]]; then
  build_stack
  echo "done: build-only"
  exit 0
fi

build_stack

if [[ "$ACTION" == "launch" || "$ACTION" == "resolve" || "$ACTION" == "build" ]]; then
  shift || true
  if [[ $# -eq 0 ]]; then
    if [[ "$ACTION" == "launch" ]]; then
      set -- "${DEFAULT_SELECTOR[@]}" --adapter raylib
    else
      set -- "${DEFAULT_SELECTOR[@]}" --adapter raylib
    fi
  fi
  echo "dotnet $CLI_DLL $ACTION $*"
  exec dotnet "$CLI_DLL" "$ACTION" "$@"
fi

# 透传任意 launcher 子命令
echo "dotnet $CLI_DLL $*"
exec dotnet "$CLI_DLL" "$@"
