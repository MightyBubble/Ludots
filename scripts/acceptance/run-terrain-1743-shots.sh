#!/bin/bash
# 工单 #1743 地形取证 v2(夹具制,替代 v1 的 RPC 临时机位):
#   地图 terrain_1743_evidence(引用 crowd s1337 的 .height 资产,RenderProfile 同值);
#   相机 Terrain1743.Evidence:targetSource=Fixed 钉死在 clamp 误判最密窗
#   (样本 (1008,880),世界 ~(1577300,1393500)),yaw 45 / pitch 55 全程不动,
#   allowUserInput=false;近/中/远 = 同目标三档距离(60000/250000/1920000cm)。
#   每档截图前后各两张;每次截图前 camera.control get 逐字段落档 pose.jsonl,
#   供前后两跑逐字段比对(机位/距离/朝向必须全等)。
#   纯地形夹具:无 crowd 会话/单位/HUD,画面内容与引擎 tick 无关(静态场景),
#   白框类演示元素(单位选择高亮/路线标记)不存在于本夹具。
# 用法: run-terrain-1743-shots.sh before|after   → artifacts/acceptance/terrain-1743/<phase>/
# 跑完定向杀进程树(pid= 行 + 桥端口监听者)。
set -u
cd "$(dirname "$0")/../.."

PHASE="${1:?usage: $0 before|after}"
BRIDGE=http://127.0.0.1:47921
LOG=artifacts/acceptance/terrain-1743/$PHASE-run.log
OUT=artifacts/acceptance/terrain-1743/$PHASE
mkdir -p "$OUT"

rpc() { curl -s -m 8 -X POST "$BRIDGE/rpc" -H 'Content-Type: application/json' -d "$1"; }
shot() { rpc "{\"jsonrpc\":\"2.0\",\"id\":9,\"method\":\"ludots.screenshot\",\"params\":{\"name\":\"$1\"}}" >/dev/null; }
setdist() { rpc "{\"jsonrpc\":\"2.0\",\"id\":2,\"method\":\"ludots.camera.control\",\"params\":{\"action\":\"set\",\"distanceCm\":$1}}" >/dev/null; }
getpose() { rpc '{"jsonrpc":"2.0","id":3,"method":"ludots.camera.control","params":{"action":"get"}}'; }
# 机位断言:target/yaw/pitch 与夹具声明一致;distance 与档位一致;结果落 pose.jsonl
pose() { # label distanceCm
  local P
  P=$(getpose)
  echo "$P" >> "$OUT/pose.jsonl"
  POSE_JSON="$P" POSE_WANT_D="$2" POSE_LABEL="$1" python - << 'PYEOF'
import json,os,sys
want_d=float(os.environ["POSE_WANT_D"]); label=os.environ["POSE_LABEL"]
r=json.loads(os.environ["POSE_JSON"])
p=r.get("result",r)
t=p["targetCm"]; ok = abs(t["x"]-1577300)<1 and abs(t["y"]-1393500)<1 \
  and abs(p["yaw"]-45)<1e-6 and abs(p["pitch"]-55)<1e-6 and abs(p["distanceCm"]-want_d)<1
print("pose[%s] target=(%.1f,%.1f) yaw=%.4f pitch=%.4f dist=%.1f -> %s"
      % (label,t["x"],t["y"],p["yaw"],p["pitch"],p["distanceCm"],"OK" if ok else "MISMATCH"))
sys.exit(0 if ok else 1)
PYEOF
}

cmd //c 'scripts\run-mod-launcher.cmd cli launch mod:Terrain1743EvidenceMod mod:AgentBridgeMod --adapter raylib --build never' >"$LOG" 2>&1 &
LAUNCHER=$!

for i in $(seq 1 120); do
  if curl -s -m 2 "$BRIDGE/health" | grep -q '"ok":true'; then break; fi
  sleep 1
done
curl -s -m 2 "$BRIDGE/health" >/dev/null || { echo "BRIDGE NOT UP"; kill $LAUNCHER; exit 1; }
# 等地图装载 + 相机就位(get 到夹具目标即地图已激活)
UP=0
for i in $(seq 1 120); do
  P=$(getpose)
  echo "$P" | grep -q '"targetCm"' && echo "$P" | grep -q '15773' && { UP=1; break; }
  sleep 1
done
[ "$UP" = "1" ] || { echo "CAMERA/MAP NOT UP"; tail -5 "$LOG"; kill $LAUNCHER; exit 1; }
sleep 4
echo "[map+camera up]"

# near:夹具默认档(60000cm)
pose near 60000 || { echo "POSE MISMATCH near"; kill $LAUNCHER; exit 1; }
sleep 2
for k in 1 2; do shot "terrain1743-near-$PHASE-$k.png"; sleep 0.6; done
# mid:同目标 250000cm
setdist 250000; sleep 4
pose mid 250000 || { echo "POSE MISMATCH mid"; kill $LAUNCHER; exit 1; }
sleep 1
for k in 1 2; do shot "terrain1743-mid-$PHASE-$k.png"; sleep 0.6; done
# far:同目标 1920000cm(overview 路径,全图多片坑洞区)
setdist 1920000; sleep 5
pose far 1920000 || { echo "POSE MISMATCH far"; kill $LAUNCHER; exit 1; }
sleep 1
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
