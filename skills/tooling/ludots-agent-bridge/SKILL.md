---
name: ludots-agent-bridge
description: 驱动运行中的 Ludots 游戏进程做调试、验收与取证。当你在 Ludots 仓库工作且需要：验证 showcase/游戏行为、查看运行时状态（实体/GAS/UI/日志）、模拟输入或下订单、截图录屏取证、触发事件看反应、或用户要求"跑起来看看/玩玩/验证一下"时，使用本 skill。前提：游戏进程带 AgentBridgeMod 启动（环回 HTTP JSON-RPC，127.0.0.1:47921）。
---

# Ludots Agent Bridge 实操

20 个自描述工具经 `POST http://127.0.0.1:47921/rpc`（JSON-RPC 2.0，method=工具名）调用；`GET /tools` 拿全部 schema；`GET /health` 判活。完整文档：仓库 `gitbook/agent-bridge.md`。

## 第 0 步：判活

```bash
curl -s http://127.0.0.1:47921/health
```

`ok:true` 且两次调用间 `pumpCount` 在涨 → 游戏主循环活着。`pumpCount` 不涨 = 卡死或长暂停，后续结果不可信。没起游戏时先启动（launch graph 需含 `AgentBridgeMod`，参考 `raylib.agent-demo.launch.graph.json`；端口可用 `LUDOTS_AGENT_BRIDGE_PORT` 覆盖，实际端口看 `artifacts/agent-bridge/session.json`）。

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
| 查属性/技能槽 | `gas.entity {entityId}`（attributes 只列非零） |
| 下订单 | `orders.issue {entityId, orderType, targetEntityId? \| worldXCm/worldYCm?}` |
| 按键 | `input.inject {actionId, mode: press\|release\|set}` |
| 真实鼠标键盘 | `input.raw {op: pointerMove\|click\|press\|type…, x?, y?, key?}`（下一帧生效） |
| 输入去向排查 | `input.state`（`uiCaptured`？） |
| 点 UI | `ui.click {elementId}` 或 `{x,y}`；找节点用 `ui.query {selector}` / `ui.tree` |
| 镜头 | `camera.control {action: get\|set\|follow\|unfollow, …}` |
| 冻结逐步看 | `time.control {action: pause}` → `step {steps:N}` → `resume` |
| 截图/录像 | `screenshot {name?}` / `recording.start` + `recording.stop`（PNG 序列+manifest） |
| 日志 | `logs.tail {count?, minLevel?, channel?, contains?}` |
| 触发事件 | `events.fire {event}`（配 logs.tail 看 handler 反应） |
| 订单管线/诊断 | `orders.inspect {entityId?, recent?}` / `gas.diagnostics` |

## 踩坑（实测验证过，别再踩）

1. `orderType` 合法键在 `mods/LudotsCoreMod/assets/GAS/order_types.json`（`castAbility`/`moveTo`/`attackTarget`/`stop`…），支持字符串键或数字 id；inspect 工具里**没有**键清单，报错信息会误导。
2. `ui.query` 选择器按 tag/`#id`/`.class` 匹配：本仓按钮多数只有 tag，用 `selector:"button"`，别用 `.button`。
3. `ui.click` 返回 `handled:false` 说明命中了无处理器的容器——用 `ui.query` 拿真按钮的 `elementId` 重试，不是工具坏了。
4. `input.inject` 的 `press` 是按住语义，**必须配对 `release`**。
5. `logs.tail` 只覆盖桥激活之后的日志；启动期日志看进程输出文件。
6. `screenshot` 在 pause 状态下依然可用（帧末履行）；产物固定落仓库 `artifacts/agent-bridge/shots/`。
7. 错误协议：`-32602` 参数错（先读 `data.code` 与 message，通常自带下一步指引）；`-32000` 域错误（`entity.not_found` / `ui.node_not_found` / `bridge.timeout` / `capability.unavailable`）。

## 常用配方

**配方 A · 验收一个 showcase**：`health` → `session.info` → `entities.query` → `camera.follow` → `time.pause` → `screenshot` → 读图确认视觉 → `time.resume`。多场景就换 preset 重复。

**配方 B · 验证技能生效**：`gas.entity {id}` 记基线 → `orders.issue {castAbility}` 或 `input.inject {SkillQ press+release}` → 等 2s（`time.get` 看 tick 推进）→ 再 `gas.entity` 对比属性 → `logs.tail {contains:"…"}` 看引擎说什么。

**配方 C · 排查 UI 不响应**：`ui.tree` 确认节点在 → `ui.query` 拿 elementId → `ui.click` → `handled:false` 就查 `input.state` 的 `uiCaptured` → 仍不通换 `input.raw {op:"click"}` 走窗口层验证命中测试。

**配方 D · 事件链取证**：`events.fire {event}` → 响应看 `triggerErrors` → `logs.tail {contains:"<handler名>"}`。triggerErrors>0 时逐条翻日志定位失败的 handler。

## MCP 接入（可选）

不想手写 HTTP 时，把 stdio 适配器注册为 MCP server：`dotnet exec <仓库>/src/Tools/Ludots.AgentBridge.Mcp/bin/Release/net9.0/Ludots.AgentBridge.Mcp.dll`（地址解析：argv > `LUDOTS_AGENT_BRIDGE_URL` > 发现文件 > 47921）。`tools/list` 与 HTTP 目录一致。
