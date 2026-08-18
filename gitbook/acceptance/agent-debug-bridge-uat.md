# Agent 调试桥 UAT（18 工具逐一实证）

对应架构文档：[Agent 调试桥](../architecture/agent-debug-bridge.md)；RFC：`docs/rfcs/RFC-0066-agent-debug-bridge.md`。

## 验收环境

- 分支 `feat/agent-debug-bridge`（PR #1001），Release 构建。
- 目标实例：`SkiaUiRaylibHost` 跑 `champion_skill_sandbox` 图，launch graph 的 mods 列表含 `AgentBridgeMod`，label=`demo`，端口 47922。
- 另有一个通过 `LUDOTS_AGENT_BRIDGE=1` 环境变量一键注入的对照实例（label=`injected`），用于验证多实例注册表与选择器路由。
- 所有证据 JSON 存于 `artifacts/agent-bridge/showcase/NN-工具名.json`（artifacts 不入库）；截图与录屏帧在 `artifacts/agent-bridge/shots/` 与 `artifacts/agent-bridge/recordings/`。

## 一键启动与多实例定位（本轮新增能力）

| 能力 | 实证 |
| --- | --- |
| 环境变量注入 | 不设 `LUDOTS_AGENT_BRIDGE` 时桥不激活（对照实验）；设 `=1` 后 mods 列表自动出现 `AgentBridgeMod`，桥监听自动端口。`LUDOTS_EXTRA_MODS=a,b` 可同法注入任意 mod，找不到显式报错。 |
| 实例注册表 | 每实例写 `artifacts/agent-bridge/sessions/<pid>.json`（pid/port/host/label/capabilities/mods/mapId/启动时间）；启动时清扫死 pid 文件，退出时删除自身。 |
| 选择器路由 | MCP 适配器 `--instance label:demo` 命中 47922；`--instance host:raylib` 在两实例同 host 时显式报歧义并列出候选；`--instance latest` 选最晚启动；目标被杀后报"no alive match"并列出死实例。 |

## 工具逐项实证

### 1. ludots.session.info

会话锚点：tick、本地玩家、mods、相机、分辨率、mapId，以及实例身份块。

证据（`01-session-info.json`）：`instance:{pid:89844, port:47922, host:"raylib", label:"demo", capabilities:["frameCapture","syntheticInput"]}`，`mapId:"champion_skill_sandbox"`（mod entry 在 GameStart 后补写），`toolCount:18`。

### 2. ludots.instances.list

枚举注册表内全部实例并做活探测（`/health` 比对 pid）。

证据（`02-instances-list.json`）：`aliveCount:1`，自身 `alive:true, self:true`；已死的 50888 文件被过滤。

### 3. ludots.time.get

读时间状态。证据（`03-time-get.json`）：`paused:false, pacemaker:"RealtimePacemaker", tick:9754`。

### 4. ludots.time.control

暂停 / 步进 / 恢复。证据（`04a~04d`）：pause 后切 `TurnBasedPacemaker`；`step 3` 返回 `targetTick:10770`；1 秒后 `time.get` 确认 `tick:10770` 精确到位；resume 回到 `RealtimePacemaker`。

### 5. ludots.entities.query

镜头内实体结构化输出：世界坐标、屏幕矩形、屏占比、onScreen。证据（`05b-entities-all.json`）：`totalMatched:16`，`Ezreal Alpha entityId:9 worldCm:(1180,720)`；`onScreenOnly:true` 时当前视角 `totalMatched:0`（相机看向空区，行为正确）。

### 6. ludots.ui.tree

UI DOM 树（Markup/Composite/Web/Skia 四路径统一抽象）。证据（`06-ui-tree.json`）：根 `ui-surface-host-root`（1280×720），子树含 `ui-surface-EntityCommandPanel-Host`，带 `sceneVersion/truncated/visited` 截断诊断。

### 7. ludots.ui.query

CSS 风格选择器查节点。证据（`07-ui-query.json`）：`selector:"button"` 命中 13 个，含 `RTS rect:(365,119,68,51)`。

### 8. ludots.ui.click

按坐标/元素点击 UI。证据（`08-ui-click.json`）：`handled:true`，命中 `RTS` 按钮，`pseudoState` 转为 `Hover, Focus`；截图目验 RTS 按钮呈激活绿色，HUD 相机切换为 `Target=(1850,980)cm Pitch=54 Dist=3900cm`。

### 9. ludots.gas.entity

单实体 GAS 状态。证据（`09-gas-entity.json`）：entity 9 = Ezreal Alpha，属性 `Health 160/160、Armor 4、MoveSpeed 355`，技能槽 `Q/W/E/R = abilityId 3/2/1/4`。

### 10. ludots.gas.diagnostics

帧级 GAS 诊断事件缓冲。证据（`10-gas-diagnostics.json`）：`frameIndex:11784`，含 `OrderAdmission` 的 Backlog/HighWatermark 两个 metric。

### 11. ludots.orders.issue

向实体下发指令。证据（`11-orders-issue.json` / `11b`）：`orderTypeId:101, result:"Queued", accepted:true`。

### 12. ludots.orders.inspect

指令准入/在途/终态三路检查。证据（`12b-orders-inspect-paused.json`）：暂停下抓到 `orderId:2 stage:"GlobalIntake" result:"Queued"`；`12c-orders-terminal.json` 显示该指令进入实体 OrderBuffer `active` 态、目标 `(2500,1000)` 正确。注：沙盒实体未实际位移（无移动执行链路），桥侧链路（下发→准入→入 buffer）已验证完整。

### 13. ludots.input.state

输入快照：阻塞标志、UI 捕获、合成设备状态。证据（`13-input-state.json`）：`inputBlocked:false, uiCaptured:false, synthetic:{pointerOverride:false, buttonsDown:[], keysDown:[]}`。

### 14. ludots.input.inject

逻辑层注入（走游戏绑定同一管线）。证据（`14-input-inject.json`）：`PointerPos set (800,400) injected:true`。

### 15. ludots.input.raw

窗口层注入（模拟物理鼠标键盘，宿主轮询点生效）。证据（`15-input-raw.json`）：`op:"click" (719,144) left queued:true`，下一帧在宿主输入轮询点应用。

### 16. ludots.screenshot

经 `IHostFrameCapture` 端口抓帧（宿主无关）。证据（`16-screenshot.json` + `shots/showcase.png`）：1280×720 PNG，493 KB，目验 HUD/实体面板/RTS 激活态完整。

### 17. ludots.recording.start

定时抓帧开录。证据（`17-recording-start.json`）：`state:"started"`，目录 `recordings/20260818-034915`，`intervalMs:300, maxFrames:6`。

### 18. ludots.recording.stop

停录并出 manifest。证据（`18-recording-stop.json`）：`framesWritten:6`，目录内 `frame-000001.png ~ frame-000006.png` + `manifest.json` 齐全。
