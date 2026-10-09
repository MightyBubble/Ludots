#!/bin/bash
# F03-b 验收截图编排:同一次运行抓「脚本建筑 op 的重烘(HUD 常态前段) → HUD 常态
# (回放完成) → 建造预览」三组,每组连拍多张,入库按画面内容挑选并以画面 HUD 自己
# 显示的 tick 命名——截图相对指令有 0.5~3s 呈现管线滞后,且脏 tile 闪烁窗口是重烘
# 报告后的真实时间 2.5s,单张定点抓不可靠。
# crowd tick 真值取自运行日志 "CrowdSimulation tick N" 行:引擎 tick 与会话 tick 无
# 固定比值(规划停摆让会话持续落后引擎),桥的 session.info tick 不能当会话时钟。
# 重烘帧抓脚本建筑 op(tick 120→123)的重烘与改道,不经合成输入;建造帧只做 B 模式
# 切换 + 指针预览,不提交现场指令(回放结论保持逐位一致)。B 键必须 keyDown/停/keyUp
# 跨帧按住:press 的点按时长短于一个呈现帧,演示的边沿采样会整拍漏掉。
set -u
cd "$(dirname "$0")/../.."

BRIDGE=http://127.0.0.1:47921
LOG=artifacts/acceptance/crowdsimulation-s1/f03b-run.log
# 建造预览指针(屏幕像素,1280x720):屏幕左中已探明为可放点(足迹避开脚本道路条带,
# 地面 y≈5641~5671、x≈4968~5218),且位于流场走廊上;右中与下中均压条带为不可放
SHOT_X=320
SHOT_Y=360
mkdir -p artifacts/acceptance/crowdsimulation-s1

rpc() { curl -s -m 5 -X POST "$BRIDGE/rpc" -H 'Content-Type: application/json' -d "$1"; }
crowdtick() { grep -a "CrowdSimulation tick " "$LOG" | tail -1 | sed 's/.*CrowdSimulation tick \([0-9]*\).*/\1/'; }
shot() { rpc "{\"jsonrpc\":\"2.0\",\"id\":9,\"method\":\"ludots.screenshot\",\"params\":{\"name\":\"$1\"}}" >/dev/null; }
waittick() { # 等日志 crowd tick ≥ $1
  for i in $(seq 1 400); do
    T=$(crowdtick)
    [ -n "$T" ] && [ "$T" -ge "$1" ] && return 0
    sleep 0.25
  done
  return 1
}

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

# 3) 重烘帧:等脚本建筑 op(tick 120)落地,高频连发截图(每张 RPC 含 PNG 落盘,
#    实际采样间隔由桥的泵串行化决定,约 0.2~0.5s/张);闪窗=报告(→123)后真实
#    时间 2.5s,风暴期帧慢,连发 20 张保证窗内有多帧命中
waittick 120
SHOTPIDS=()
for k in $(seq 1 20); do
  shot "f03b-rebake-b$k.png" &
  SHOTPIDS+=($!)
  sleep 0.2
done
wait "${SHOTPIDS[@]}"
echo "[shots] rebake x20 @crowd $(crowdtick)"

# 4) HUD 常态帧:等自动回放完成且逐位一致(日志 replay 行 status=1;兜底 crowd≥400)
for i in $(seq 1 600); do
  if grep -aq "CrowdSimulation replay at tick .*status=1" "$LOG"; then break; fi
  T=$(crowdtick)
  [ -n "$T" ] && [ "$T" -ge 400 ] && break
  sleep 0.5
done
grep -a "CrowdSimulation replay" "$LOG" | tail -1 || true
sleep 1
for k in 1 2 3; do shot "f03b-hud-b$k.png"; sleep 0.4; done
echo "[shots] hud x3 @crowd $(crowdtick)"

# 5) 建造帧:B 按住跨帧切建造模式,指针落在可放点,预览稳定后连拍
rpc '{"jsonrpc":"2.0","id":2,"method":"ludots.input.raw","params":{"op":"keyDown","key":"B"}}' >/dev/null
sleep 0.4
rpc '{"jsonrpc":"2.0","id":3,"method":"ludots.input.raw","params":{"op":"keyUp","key":"B"}}' >/dev/null
rpc "{\"jsonrpc\":\"2.0\",\"id\":4,\"method\":\"ludots.input.raw\",\"params\":{\"op\":\"pointerMove\",\"x\":$SHOT_X,\"y\":$SHOT_Y}}" >/dev/null
sleep 1.5
for k in 1 2 3; do shot "f03b-build-b$k.png"; sleep 0.4; done
echo "[shots] build x3 @crowd $(crowdtick)"

# 6) 杀游戏进程树:先按 launcher 输出的 pid=,再按桥端口(47921)的监听者兜底——
#    日志若被上一轮残留进程交叉写入会拿错 pid,漏杀会留僵尸占端口污染下一轮
PID=$(grep -o 'pid=[0-9]*' "$LOG" | head -1 | cut -d= -f2)
sleep 2
if [ -n "$PID" ]; then taskkill //PID "$PID" //T //F >/dev/null 2>&1; fi
PORTPID=$(netstat -ano | grep ':47921' | grep -i listening | awk '{print $NF}' | awk '$1 > 4' | head -1)
if [ -n "$PORTPID" ]; then taskkill //PID "$PORTPID" //T //F >/dev/null 2>&1; fi
kill $LAUNCHER 2>/dev/null
ls -la artifacts/agent-bridge/shots/f03b-*.png 2>/dev/null || echo "NO SHOTS"
