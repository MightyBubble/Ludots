---
name: ludots-agent-bridge
description: 驱动运行中的 Ludots 游戏进程做调试、验收与取证。当你在 Ludots 仓库工作且需要：验证 showcase/游戏行为、查看运行时状态（实体/GAS/UI/日志）、模拟输入或下订单、截图录屏取证、触发事件看反应、或用户要求"跑起来看看/玩玩/验证一下"时，使用本 skill。前提：游戏进程带 AgentBridgeMod 启动（环回 HTTP JSON-RPC，127.0.0.1:47921）。
---

# Ludots Agent Bridge 实操

内置工具经 `POST http://127.0.0.1:47921/rpc`（JSON-RPC 2.0，method=工具名）调用；**工具清单以运行时 `GET /tools` 为准**（与 `BuiltinAgentTools`、MCP `tools/list`、Inspector 侧栏同一注册表，禁止手写平行清单）。`GET /health` 判活。完整文档：`gitbook/agent-bridge.md`（任务视角）与 `gitbook/architecture/agent-debug-bridge.md`（架构正本）；计划 SSOT：epic #1056。

优先用 CLI（与 MCP 方法名/参数完全相同）：

```bash
dotnet run --project src/Tools/Ludots.AgentBridge.Cli -- health
dotnet run --project src/Tools/Ludots.AgentBridge.Cli -- tools --names
dotnet run --project src/Tools/Ludots.AgentBridge.Cli -- call ludots.session.info
```

## 第 0 步：判活

```bash
curl -s http://127.0.0.1:47921/health
# 或
dotnet run --project src/Tools/Ludots.AgentBridge.Cli -- health
```

`ok:true` 且两次调用间 `pumpCount` 在涨 → 游戏主循环活着。`pumpCount` 不涨 = 卡死或长暂停，后续结果不可信。没起游戏时先启动（launch graph 需含 `AgentBridgeMod`，参考 `raylib.agent-demo.launch.graph.json`；端口可用 `LUDOTS_AGENT_BRIDGE_PORT` 覆盖，实际端口看 `artifacts/agent-bridge/sessions/<pid>.json`）。

## 工作循环：观察 → 驱动 → 验证

任何调试任务都走这个闭环，不要跳步：

1. **观察**：`ludots.session.info`（tick/mods）→ `ludots.entities.query`（`nameFilter` 找目标，`screenCoverage` 判断在不在镜头里、看得清吗）
2. **驱动**：`ludots.orders.issue`（下订单）/ `ludots.input.inject`（语义按键）/ `ludots.ui.click`（点按钮）/ `ludots.camera.control`（`follow {entityId}` 对准目标）/ `ludots.events.fire`（触发事件）
3. **验证**：`ludots.gas.entity`（属性变了？）/ `ludots.ui.tree`（面板状态？）/ `ludots.screenshot`（画面对了？）/ `ludots.logs.tail`（日志怎么说？）

每步驱动后必须有对应验证——没有验证的驱动等于没做。

## 工具速查（按任务）

| 任务 | 工具与参数 |
|------|-----------|
| 找实体 | `entities.query {nameFilter?, onScreenOnly?, limit?}` |
| 点选/空间探针 | `entities.pick` / `spatial.query` |
| 寻路 | `nav.project` / `nav.findPath` |
| 查属性/技能槽 | `gas.entity {entityId}`（attributes 只列非零） |
| 下订单 | `orders.issue {entityId, orderType, targetEntityId? \| worldXCm/worldYCm?}` |
| 按键 | `input.inject {actionId, mode: press\|release\|set}` |
| 真实鼠标键盘 | `input.raw {op: pointerMove\|click\|press\|type…, x?, y?, key?}`（下一帧生效） |
| 输入去向排查 | `input.state`（`uiCaptured`？） |
| 点 UI | `ui.click {elementId}` 或 `{x,y}`；找节点用 `ui.query {selector}` / `ui.tree` |
| 镜头 | `camera.control {action: get\|set\|follow\|unfollow, …}` |
| 冻结逐步看 | `time.control {action: pause}` → `step {steps:N}` → `resume` |
| 截图/录像 | `screenshot {name?}` / `recording.start` + `recording.stop` |
| 日志 | `logs.tail {count?, minLevel?, channel?, contains?}` |
| 触发事件 | `events.fire {event}`（配 logs.tail 看 handler 反应） |
| 订单管线/诊断 | `orders.inspect {entityId?, recent?}` / `gas.diagnostics` |
| 图调试 / Presenter | `graph.debug` / `presenters.query|desync|screen` |

不确定有哪些工具时先 `tools --names` 或 `GET /tools`，不要凭记忆数个数。

## 踩坑（实测验证过，别再踩）

1. `orderType` 合法键在 `mods/LudotsCoreMod/assets/GAS/order_types.json`（`castAbility`/`moveTo`/`attackTarget`/`stop`…），支持字符串键或数字 id；`orders.inspect` 响应也带键清单。
2. `ui.query` 选择器按 tag/`#id`/`.class` 匹配：本仓按钮多数只有 tag，用 `selector:"button"`，别用 `.button`。
3. `ui.click` 返回 `handled:false` 说明命中了无处理器的容器——用 `ui.query` 拿真按钮的 `elementId` 重试，不是工具坏了。
4. `input.inject` 的 `press` 是按住语义，**必须配对 `release`**。
5. `logs.tail` 只覆盖桥激活之后的日志；启动期日志看进程输出文件。
6. `screenshot` 在 pause 状态下依然可用（帧末履行）；产物固定落仓库 `artifacts/agent-bridge/shots/`。
7. 错误协议：`-32602` 参数错（先读 `data.code` 与 message）；`-32000` 域错误（`entity.not_found` / `ui.node_not_found` / `bridge.timeout` / `service.unavailable`）。

## 常用配方

**配方 A · 验收一个 showcase**：`health` → `session.info` → `entities.query` → `camera.follow` → `time.pause` → `screenshot` → 读图确认视觉 → `time.resume`。

**配方 B · 验证技能生效**：`gas.entity {id}` 记基线 → `orders.issue {castAbility}` 或 `input.inject {SkillQ press+release}` → 等 2s → 再 `gas.entity` 对比 → `logs.tail`。

**配方 C · 排查 UI 不响应**：`ui.tree` → `ui.query` → `ui.click` → `handled:false` 就查 `input.state` → 仍不通换 `input.raw {op:"click"}`。

**配方 D · 事件链取证**：`events.fire {event}` → `logs.tail {contains:"…"}`。

## MCP / 人前端（可选）

```bash
dotnet build src/Tools/Ludots.AgentBridge.Mcp/Ludots.AgentBridge.Mcp.csproj -c Release
dotnet exec src/Tools/Ludots.AgentBridge.Mcp/bin/Release/net8.0/Ludots.AgentBridge.Mcp.dll
```

人用 Inspector：`src/Tools/Ludots.Inspector.React`（`npm run dev`，默认连 `47921`，每个工具一张 schema 表单，调用同一 `/rpc`）。
