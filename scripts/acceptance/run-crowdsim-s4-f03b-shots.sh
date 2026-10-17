#!/bin/bash
# F03-b 验收截图编排:同一次运行抓「脚本建筑 op 的重烘(HUD 常态前段) → HUD 常态
# (回放完成) → 建造预览」三组,每组连拍多张,入库按画面内容挑选并以画面 HUD 自己
# 显示的 tick 命名——截图相对指令有 0.5~3s 呈现管线滞后,且脏 tile 闪烁窗口是重烘
# 报告后的真实时间 2.5s,单张定点抓不可靠。
# crowd tick 读 HUD 缓存页 NavTick(「tick / 指令日志」的前半)。引擎 tick 与会话 tick
# 无固定比值,桥的 session.info 不能当会话时钟。默认 Info 日志不打印
# "CrowdSimulation tick N"(那行要 Engine 通道 Debug,且只在 FixedHz 的整数倍出现)。
# 重烘帧抓脚本建筑 op(tick 120→123)的重烘与改道,不经合成输入;建造帧用语义动作
# CrowdSimulation.S4.ToggleBuild 切换 + 指针预览,不提交现场指令(回放结论保持逐位一致)。
# press/release 跨帧按住:单次 press 点按时长短于一个呈现帧,演示的边沿采样会整拍漏掉。
# 启动和结束按操作系统分叉,拍摄步骤只有这一份。
set -u
cd "$(dirname "$0")/../.."

BRIDGE=http://127.0.0.1:47921
LOG=artifacts/acceptance/crowdsimulation-s1/f03b-run.log
TOGGLE_BUILD_ACTION=CrowdSimulation.S4.ToggleBuild
# 建造预览指针(屏幕像素,1280x720):屏幕左中已探明为可放点(足迹避开脚本道路条带,
# 地面 y≈5641~5671、x≈4968~5218),且位于流场走廊上;右中与下中均压条带为不可放
SHOT_X=320
SHOT_Y=360
mkdir -p artifacts/acceptance/crowdsimulation-s1
LAUNCHER=

rpc() { curl -s -m 5 -X POST "$BRIDGE/rpc" -H 'Content-Type: application/json' -d "$1"; }
crowdtick() {
  python3 - <<'PY'
import json, urllib.request
body = json.dumps({
    "jsonrpc": "2.0", "id": 1, "method": "ludots.ui.query",
    "params": {"selector": "[ui-text=NavTick]", "limit": 1},
}).encode()
req = urllib.request.Request(
    "http://127.0.0.1:47921/rpc", data=body, headers={"Content-Type": "application/json"})
try:
    with urllib.request.urlopen(req, timeout=5) as resp:
        payload = json.loads(resp.read().decode())
except Exception:
    print("")
    raise SystemExit(0)
nodes = (payload.get("result") or {}).get("nodes") or []
text = ((nodes[0].get("text") if nodes else "") or "").strip()
head = text.split("/")[0].strip()
print(head if head.isdigit() else "")
PY
}
shot() {
  local base="$1"
  local tick
  tick=$(crowdtick)
  [ -n "$tick" ] || tick=unknown
  rpc "{\"jsonrpc\":\"2.0\",\"id\":9,\"method\":\"ludots.screenshot\",\"params\":{\"name\":\"${base}-t${tick}.png\"}}" >/dev/null
}
inject() { rpc "{\"jsonrpc\":\"2.0\",\"id\":$2,\"method\":\"ludots.input.inject\",\"params\":{\"actionId\":\"$1\",\"mode\":\"$3\"}}" >/dev/null; }
waittick() { # 等 HUD 上的会话 tick ≥ $1
  for i in $(seq 1 400); do
    T=$(crowdtick)
    [ -n "$T" ] && [ "$T" -ge "$1" ] && return 0
    sleep 0.25
  done
  return 1
}
launch_host() {
  if [ "$(uname -s)" = "Linux" ]; then
    export DISPLAY="${DISPLAY:-:1}"
    export LIBGL_ALWAYS_SOFTWARE=1
    export GALLIUM_DRIVER=llvmpipe
    export DOTNET_CLI_TELEMETRY_OPTOUT=1
    dotnet run --project src/Tools/Ludots.Launcher.Cli/Ludots.Launcher.Cli.csproj -c Release -- \
      launch mod:CrowdSimulationS4DeployMod mod:AgentBridgeMod --adapter raylib --build auto >"$LOG" 2>&1 &
    LAUNCHER=$!
  else
    cmd //c 'scripts\run-mod-launcher.cmd cli launch mod:CrowdSimulationS4DeployMod mod:AgentBridgeMod --adapter raylib --build never' >"$LOG" 2>&1 &
    LAUNCHER=$!
  fi
}
kill_host() {
  local PID PORTPID
  PID=$(grep -o 'pid=[0-9]*' "$LOG" | head -1 | cut -d= -f2)
  sleep 2
  if [ "$(uname -s)" = "Linux" ]; then
    if [ -n "${PID}" ]; then
      kill "${PID}" 2>/dev/null || true
      sleep 1
      kill -9 "${PID}" 2>/dev/null || true
    fi
    PORTPID=$(ss -ltnp 2>/dev/null | sed -n 's/.*:47921 .*pid=\([0-9]*\).*/\1/p' | head -1)
    if [ -n "${PORTPID}" ]; then
      kill "${PORTPID}" 2>/dev/null || true
      sleep 1
      kill -9 "${PORTPID}" 2>/dev/null || true
    fi
  else
    if [ -n "${PID}" ]; then taskkill //PID "$PID" //T //F >/dev/null 2>&1; fi
    PORTPID=$(netstat -ano | grep ':47921' | grep -i listening | awk '{print $NF}' | awk '$1 > 4' | head -1)
    if [ -n "${PORTPID}" ]; then taskkill //PID "$PORTPID" //T //F >/dev/null 2>&1; fi
  fi
  if [ -n "${LAUNCHER}" ]; then
    kill "${LAUNCHER}" 2>/dev/null || true
  fi
}

launch_host

for i in $(seq 1 180); do
  if curl -s -m 2 "$BRIDGE/health" | grep -q '"ok":true'; then break; fi
  sleep 1
done
curl -s -m 2 "$BRIDGE/health" || { echo "BRIDGE NOT UP"; kill_host; exit 1; }
echo; echo "[bridge up]"
for i in $(seq 1 60); do
  rpc '{"jsonrpc":"2.0","id":1,"method":"ludots.ui.click","params":{"elementId":"crowd-dv-tab-cache"}}' >/dev/null
  sleep 0.8
  if [ -n "$(crowdtick)" ]; then break; fi
done
echo "[crowd tick] $(crowdtick)"

# 重烘帧:等脚本建筑 op(tick 120)落地,高频连发截图(每张 RPC 含 PNG 落盘,
# 实际采样间隔由桥的泵串行化决定,约 0.2~0.5s/张);闪窗=报告(→123)后真实
# 时间 2.5s,风暴期帧慢,连发 20 张保证窗内有多帧命中
waittick 120 || echo "[warn] crowd tick did not reach 120"
SHOTPIDS=()
for k in $(seq 1 20); do
  shot "f03b-rebake-b$k" &
  SHOTPIDS+=($!)
  sleep 0.2
done
wait "${SHOTPIDS[@]}"
echo "[shots] rebake x20 @crowd $(crowdtick)"

# HUD 常态帧:等自动回放完成且逐位一致(日志 replay 行 status=1;兜底 crowd≥400)
for i in $(seq 1 600); do
  if grep -aq "CrowdSimulation replay at tick .*status=1" "$LOG"; then break; fi
  T=$(crowdtick)
  [ -n "$T" ] && [ "$T" -ge 400 ] && break
  sleep 0.5
done
grep -a "CrowdSimulation replay" "$LOG" | tail -1 || true
sleep 1
for k in 1 2 3; do shot "f03b-hud-b$k"; sleep 0.4; done
echo "[shots] hud x3 @crowd $(crowdtick)"

# 建造帧:ToggleBuild 按住跨帧切建造模式,指针落在可放点,预览稳定后连拍
inject "$TOGGLE_BUILD_ACTION" 2 press
sleep 0.4
inject "$TOGGLE_BUILD_ACTION" 3 release
rpc "{\"jsonrpc\":\"2.0\",\"id\":4,\"method\":\"ludots.input.raw\",\"params\":{\"op\":\"pointerMove\",\"x\":$SHOT_X,\"y\":$SHOT_Y}}" >/dev/null
sleep 1.5
for k in 1 2 3; do shot "f03b-build-b$k"; sleep 0.4; done
echo "[shots] build x3 @crowd $(crowdtick)"

kill_host
ls -la artifacts/agent-bridge/shots/f03b-*-t*.png 2>/dev/null || echo "NO SHOTS"
