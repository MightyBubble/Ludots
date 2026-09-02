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
2. 右侧 Live Debug → 选择已挂载入口 → Watch。
3. 工具动作：`list` → `configure { mode: nodeAndPins }` → `drain { since }`；drain 事件带 `nodeId` / `op` / `controlPort` / pin 值。
4. 画布做 Flow Canvas 式可视化：**控制边**走暖黄粗线 + 流动光点（只亮当前走过的路径）；**数值边**走细实线，当前值标在线上或引脚旁（不用虚线）；节点本身只做轻量高亮，热度约 2 秒内衰减。配色参考 agent 前端套件（zinc 深色画布、n8n 节点壳、暖黄执行 / 冷蓝数据）。Watch 某一入口时只留下从该入口可达的短链（含其间的数值边），其它链隐藏；同时自动收起左右侧栏，把 Live Debug 收到画布底栏，方便单屏「左游戏右编辑器」。右侧日志只是辅助轨迹，不再是唯一反馈。
5. 不声称完整 `NodeExit` 生命周期。嵌套 `InvokeScript` 记录带 `graphId`；source map 缺失时 AgentBridge 失败关闭，错误含 graph id 与 pc。

### 3.4 TriggerGraph 事件入口

事件名优先从 `/api/graph/event-schemas/{modId}` 下拉选择；选中后自动建议 `on_<Event>` 标签，并在检查器展示 Schema 载荷针。仍可手填未登记事件名，但会标明载荷针未类型化。

---

## 4. 场景

1. 新人打开 `/gas-graphs`，加载夜袭 Flow 图，看见控制端口与 Bridge 投影的作者糖，Validate 通过。
2. 故意加未连线的 Until，Validate 点名缺 `body`/`next`/`condition`。
3. 夜袭 + AgentBridge 运行中，Watch 后看到节点/连线亮起与 pin 芯片，而不只是右侧日志滚动。
4. TriggerGraph 事件入口从 Schema 下拉选事件，检查器列出载荷针。
5. 在 Script 图里加入 `FsmState`，填写枚举与相位变量并挂 case 臂，保存后再打开字段仍在。
6. 在 Script 图里加入 `BtSequence`，用 child 臂挂子节点；`BtDecorator` 选 `decoratorKind` 后连 `child:0`，保存后再打开仍在。
7. 变量面板类型只见 Integer / Float，没有 Array / Map。

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
    Then 画布只留下该入口可达的短链，其它链不可见
    And 画布上当前节点亮起并带 LIVE 标记
    And 最近走过的控制边加粗动画
    And 有 pin 变化时节点上出现数值芯片
    And 右侧事件流仍可见节点归因或挂起/停机记录
    And 缺 source map 时请求失败并点名 graph 与 pc

  Scenario: 左键瞬移看得到落点
    Given 夜袭 showcase 与 AgentBridge 正在跑
    And 我 Watch 了 on_teleport
    When 我在战场空白处点左键
    Then 英雄跳到点击对应的地面落点
    And 短链按 tp_px → tp_ground → tp_move 顺序亮起

  Scenario: TriggerGraph 事件入口选 Schema
    Given 编辑器已打开 MapTriggerNightRaidMod 的 TriggerGraph
    When 我选中一张 Event 卡并打开检查器
    Then 我能从事件 Schema 下拉里选登记过的事件
    And 载荷针列表与 Schema 参数一致
```
