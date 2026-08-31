# Graph 编辑器与 Live Debug 上手

面向作者与调试者：如何打开蓝图编辑器、用运行时 descriptor 连控制流，以及怎样用 Agent Bridge 的 `ludots.graph.debug` 在运行中的 TriggerGraph 上看节点与引脚变化。

进度与边界只认 [图能力唯一入口](graph-capability-status.md)；本页是操作手册，不另开进度账。

---

## 1. 概述

打开编辑器后，节点联想和控制输出端口只来自 Bridge 投影的运行时 descriptor / 作者糖，不在前端自造 op。游戏进程加载 `AgentBridgeMod` 后，编辑器右侧 Live Debug 能列出已挂载的 TriggerGraph 条目，开启固定容量 trace，并按 sequence 增量拉取变化。

当前收口：控制流端口、作者糖（含 `FsmState` / `SelectByEnum` / `InlineGraph` / `FormatText`，以及 Script-only 的 `BtSequence` / `BtSelector` / `BtDecorator` 与动态 `child:{n}` 臂）、删节点清悬挂边、source map 缺失失败关闭。正式文字合同已齐：`ConstText` / `ConcatText` / `IntToText` / `FloatToText` / `SinkPresentationText` 与 `FormatText` 花括号自动引脚进入 descriptor / 糖名册（见 [图正式文字](graph-formal-text.md)）。地图变量面板只暴露 Integer / Float；不再列出引擎还不认的 Array / Map。

Codegen 产品化合同见 [图 Codegen 产品化](graph-codegen-productization.md)：右侧 **Codegen** 面板可预览生成 C#、看资格红绿灯、一键对拍；Bridge 提供 `codegen/preview`、`codegen/parity`、`GET /api/graph/codegen/coverage`。Live Debug 后端徽章可显示当前执行后端标签（面板已预留 `backend` 展示位）。

---

## 2. 结构

```text
React /gas-graphs  ──/api──▶ Editor.Bridge :5299  ──descriptor──▶ GraphOpDescriptorTable
                 ──/api──▶ codegen/preview|parity（产品化后）
                 ──/agent-bridge──▶ AgentBridge :47921 ──ludots.graph.debug──▶ TriggerGraphMountTrace
```

---

## 3. 详情

### 3.1 启动编辑器

```bash
dotnet run --project src/Tools/Ludots.Editor.Bridge -c Release
cd src/Tools/Ludots.Editor.React && npm ci && npm run dev
```

打开 <http://localhost:5173/gas-graphs>。

| 字段 | 示例 |
|------|------|
| modId | `MapTriggerNightRaidMod` |
| graphId | `Graph.NightRaid.Flow` |

Load 后画布显示控制边（蓝）与值边。左侧节点表里的作者糖只来自 Bridge `authoringSugars`（含 `BranchBool`、`SwitchInt`、`SelectByEnum`、`FsmState`、`Wait`、`While`、`Until`、`Break`；Script 另有 `BtSequence` / `BtSelector` / `BtDecorator`；TriggerGraph 另有 `InlineGraph`）。普通节点的 `Jump.target`、`Call.call/next` 等端口来自 Bridge，不是前端硬编码。`FsmState` 必须绑 `enumType` + `stateVar`，case 臂用枚举成员名。BT 组合糖用 `child:{n}` 臂（Decorator 固定 `child:0` + `decoratorKind`）。

Validate 走 `GraphProgramAuthoringFrontDoor`；缺控制边 / 未知 op 失败关闭。Save 只在 Validate 通过后写 `assets/GAS/graphs.json`；布局写 `graph_editor.json`，不进运行时合同。

### 3.2 启动可调试的运行时

用带 `$agent_bridge` 的 preset，例如：

```bash
scripts/run-mod-launcher.ps1 cli launch --preset map_trigger_night_raid_raylib
```

判活：

```bash
curl -s http://127.0.0.1:47921/health
curl -s http://127.0.0.1:47921/tools | jq '.[].name'   # 或 .tools[].name
```

目录里必须出现 `ludots.graph.debug`。没有该工具时 Live Debug 无法工作——属于注册缺口，不得用编辑器假状态掩盖。

### 3.3 Live Debug 操作

1. 编辑器加载与游戏同一 `graphId`（如 `Graph.NightRaid.Flow`）。
2. 右侧 Live Debug → Refresh / 选择 mounted entry → Watch。
3. 工具动作：`list` → `configure { mode: nodeAndPins }` → `drain { since }`。
4. 画布高亮最近执行节点；事件流显示 NodeEnter / Suspended / Halted / pin / blackboard 变化。不声称 `NodeExit` 完整生命周期。
5. 嵌套 `InvokeScript` 的记录带 `graphId`；source map 缺失时 AgentBridge fail-closed，错误含 graph id 与 pc。

---

## 4. 场景

1. 新人打开 `/gas-graphs`，加载夜袭 Flow 图，看见控制端口与 Bridge 投影的作者糖，Validate 通过。
2. 故意加未连线的 Until，Validate 点名缺 `body`/`next`/`condition`。
3. 夜袭 + AgentBridge 运行中，Watch 后看到 heartbeat / MapLoaded 触发的节点高亮与 pin 变化。
4. 在 Script 图里加入 `FsmState`，填写枚举与相位变量并挂 case 臂，保存后再打开字段仍在。
5. 在 Script 图里加入 `BtSequence`，用 child 臂挂子节点；`BtDecorator` 选 `decoratorKind` 后连 `child:0`，保存后再打开仍在。
6. 变量面板类型只见 Integer / Float，没有 Array / Map。

---

## 5. 边界

- 正式文字节点与 FormatText 糖只来自运行时 descriptor / 糖名册；未登记前不得展示可保存假节点。
- Live Debug 依赖真实挂载与 source map；无游戏进程时面板应明示 Bridge 错误，不得假装有事件。
- 本页不替代 [Agent 调试桥](agent-debug-bridge.md) 的全工具手册。
- 文字值容量与表现出口细则见 [图正式文字](graph-formal-text.md)。

---

## 6. UAT

```gherkin
Feature: 蓝图编辑器与 live debug 可教可验

  Scenario: 作者从运行时端口连线
    Given 编辑器已打开且 Bridge 健康
    When 我加载 MapTriggerNightRaidMod 的 Graph.NightRaid.Flow
    Then 画布显示控制流节点与蓝端口
    And 左侧能找到六种作者糖

  Scenario: 校验失败关闭
    Given 图上有一个未接线的 Until
    When 我点 Validate
    Then 编译失败并点名缺 body、next、condition

  Scenario: Live debug 看见执行
    Given 夜袭 showcase 与 AgentBridge 正在跑
    And tools 目录含 ludots.graph.debug
    When 我在编辑器对 Graph.NightRaid.Flow 打开 Watch
    Then 事件流出现节点归因或挂起/停机记录
    And 缺 source map 时请求失败并点名 graph 与 pc
```
