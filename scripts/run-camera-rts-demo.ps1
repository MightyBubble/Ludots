Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

# 说明：原 rts-demo 玩法视角相机 preset 已不存在（launcher.presets.json 中无 arpg/moba/rts-demo）。
# 当前临时转发到现存合法 preset camera_acceptance_raylib，待后续补齐差异化玩法视角相机 preset。
& "$PSScriptRoot\run-mod-launcher.ps1" run --preset camera_acceptance_raylib @args
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
