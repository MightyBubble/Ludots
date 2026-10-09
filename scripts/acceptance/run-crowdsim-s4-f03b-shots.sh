#!/bin/bash
# F03-b 验收截图编排:一次运行抓 建造预览 + 重烘闪烁(合成输入走 AgentBridge 的
# ludots.input.raw / ludots.screenshot,不碰演示代码本身)。
set -u
cd "$(dirname "$0")/../.."

BRIDGE=http://127.0.0.1:47921
LOG=artifacts/acceptance/crowdsimulation-s1/f03b-run.log
mkdir -p artifacts/acceptance/crowdsimulation-s1

rpc() { curl -s -m 5 -X POST "$BRIDGE/rpc" -H 'Content-Type: application/json' -d "$1"; }
tickof() { rpc '{"jsonrpc":"2.0","id":1,"method":"ludots.session.info"}' | grep -o '"tick":[0-9]*' | head -1 | cut -d: -f2; }

# 1) 起游戏(无截图 env,无自动退出;跑完由本脚本杀进程)
cmd //c 'scripts\run-mod-launcher.cmd cli launch mod:CrowdSimulationS4DeployMod mod:AgentBridgeMod --adapter raylib --build never' >"$LOG" 2>&1 &
LAUNCHER=$!

# 2) 等桥起来
for i in $(seq 1 90); do
  if curl -s -m 2 "$BRIDGE/health" | grep -q '"ok":true'; then break; fi
  sleep 1
done
curl -s -m 2 "$BRIDGE/health" || { echo "BRIDGE NOT UP"; kill $LAUNCHER; exit 1; }
echo; echo "[bridge up]"

# 3) 轮询 tick,按窗口截图
rebakeA=0; rebakeB=0; build=0
for i in $(seq 1 1200); do
  T=$(tickof)
  [ -z "$T" ] && T=-1
  if [ "$rebakeA" = 0 ] && [ "$T" -ge 170 ]; then
    rebakeA=1
    rpc '{"jsonrpc":"2.0","id":2,"method":"ludots.screenshot","params":{"name":"f03b-rebake-a.png"}}' >/dev/null
    echo "[shot] rebake-a @tick $T"
  fi
  if [ "$rebakeB" = 0 ] && [ "$T" -ge 200 ]; then
    rebakeB=1
    rpc '{"jsonrpc":"2.0","id":3,"method":"ludots.screenshot","params":{"name":"f03b-rebake-b.png"}}' >/dev/null
    echo "[shot] rebake-b @tick $T"
  fi
  if [ "$build" = 0 ] && [ "$T" -ge 420 ]; then
    build=1
    rpc '{"jsonrpc":"2.0","id":4,"method":"ludots.input.raw","params":{"op":"pointerMove","x":700,"y":400}}' >/dev/null
    sleep 0.3
    rpc '{"jsonrpc":"2.0","id":5,"method":"ludots.input.raw","params":{"op":"press","key":"B"}}' >/dev/null
    sleep 1.5
    rpc '{"jsonrpc":"2.0","id":6,"method":"ludots.screenshot","params":{"name":"f03b-build.png"}}' >/dev/null
    echo "[shot] build @tick $T"
    break
  fi
  sleep 0.2
done

# 4) 杀游戏进程树(launcher 输出 pid=)
PID=$(grep -o 'pid=[0-9]*' "$LOG" | head -1 | cut -d= -f2)
sleep 2
if [ -n "$PID" ]; then taskkill //PID "$PID" //T //F >/dev/null 2>&1; fi
kill $LAUNCHER 2>/dev/null
ls -la artifacts/agent-bridge/shots/f03b-*.png 2>/dev/null || echo "NO SHOTS"
