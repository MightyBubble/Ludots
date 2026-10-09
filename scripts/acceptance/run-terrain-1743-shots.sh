#!/bin/bash
# 工单 #1743 地形取证:crowd 演示(s1337 图)固定三个机位连拍——
#   near  : 哨兵坑缘 (1013490, 320626)cm,60m 距离——平顶坑壁与条纹(缺陷①近景)
#   wide  : 同目标 2.5km 距离——近景 chunk 网格全貌(缺陷①宽景)
#   far   : 地图中心 19.2km 俯瞰——overview 路径(缺陷②远景糊化 + 高原整体压平)
# 用法: run-terrain-1743-shots.sh before|after   → artifacts/acceptance/terrain-1743/<phase>/
# 机位经 AgentBridge ludots.camera.control 固定,前后两次同位可逐像素对照。
# 跑完定向杀进程树(pid= 行 + 桥端口监听者),不碰其他窗口。
set -u
cd "$(dirname "$0")/../.."

PHASE="${1:?usage: $0 before|after}"
BRIDGE=http://127.0.0.1:47921
LOG=artifacts/acceptance/terrain-1743/$PHASE-run.log
OUT=artifacts/acceptance/terrain-1743/$PHASE
mkdir -p "$OUT"

rpc() { curl -s -m 8 -X POST "$BRIDGE/rpc" -H 'Content-Type: application/json' -d "$1"; }
shot() { rpc "{\"jsonrpc\":\"2.0\",\"id\":9,\"method\":\"ludots.screenshot\",\"params\":{\"name\":\"$1\"}}" >/dev/null; }
pose() { # name targetX targetY yaw pitch distanceCm
  rpc "{\"jsonrpc\":\"2.0\",\"id\":2,\"method\":\"ludots.camera.control\",\"params\":{\"action\":\"set\",\"targetXCm\":$2,\"targetYCm\":$3,\"yaw\":$4,\"pitch\":$5,\"distanceCm\":$6}}" >/dev/null
}

cmd //c 'scripts\run-mod-launcher.cmd cli launch mod:CrowdSimulationS4DeployMod mod:AgentBridgeMod --adapter raylib --build never' >"$LOG" 2>&1 &
LAUNCHER=$!

for i in $(seq 1 120); do
  if curl -s -m 2 "$BRIDGE/health" | grep -q '"ok":true'; then break; fi
  sleep 1
done
curl -s -m 2 "$BRIDGE/health" >/dev/null || { echo "BRIDGE NOT UP"; kill $LAUNCHER; exit 1; }
for i in $(seq 1 240); do
  grep -aq "CrowdSimulation session activated" "$LOG" && break
  sleep 1
done
grep -aq "CrowdSimulation session activated" "$LOG" || { echo "SESSION NOT ACTIVATED"; tail -5 "$LOG"; kill $LAUNCHER; exit 1; }
sleep 3
echo "[session up]"

pose near  1013490 320626 45 55 60000    ; sleep 4
for k in 1 2; do shot "terrain1743-near-$PHASE-$k.png"; sleep 0.6; done
pose wide  1013490 320626 45 55 250000   ; sleep 4
for k in 1 2; do shot "terrain1743-wide-$PHASE-$k.png"; sleep 0.6; done
pose far   800000  800000  45 55 1920000 ; sleep 5
for k in 1 2; do shot "terrain1743-far-$PHASE-$k.png"; sleep 0.6; done
echo "[shots done]"

PID=$(grep -o 'pid=[0-9]*' "$LOG" | head -1 | cut -d= -f2)
sleep 2
if [ -n "$PID" ]; then taskkill //PID "$PID" //T //F >/dev/null 2>&1; fi
PORTPID=$(netstat -ano | grep ':47921' | grep -i listening | awk '{print $NF}' | awk '$1 > 4' | head -1)
if [ -n "$PORTPID" ]; then taskkill //PID "$PORTPID" //T //F >/dev/null 2>&1; fi
kill $LAUNCHER 2>/dev/null
mv artifacts/agent-bridge/shots/terrain1743-*.png "$OUT/" 2>/dev/null
ls -la "$OUT"/*.png 2>/dev/null || echo "NO SHOTS"
