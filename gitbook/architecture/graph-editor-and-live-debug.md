# Graph 编辑器与 Live Debug 上手

面向作者与调试者：如何打开蓝图编辑器、用运行时 descriptor 连控制流，以及怎样用 Agent Bridge 的 `ludots.graph.debug` 在运行中的 TriggerGraph 上看节点与引脚变化。

进度与边界只认 [图能力唯一入口](graph-capability-status.md)；本页是操作手册，不另开进度账。

---

## 1. 概述

打开编辑器后，节点联想和控制输出端口只来自 Bridge 投影的运行时 descriptor / 作者糖，不在前端自造 op。游戏进程加载 `AgentBridgeMod` 后，编辑器右侧 Live Debug 能列出已挂载的 TriggerGraph 条目，开启固定容量 trace，并按 sequence 增量拉取变化。

当前收口：控制流端口、作者糖（含 Script/TriggerGraph 可用的流程糖如 `FsmState` / `SelectByEnum` / `InlineGraph` / `FormatText`，以及 Script-only 的 `BtSequence` / `BtSelector` / `BtDecorator` 与动态 `child:{n}` 臂——**这些是流程图组合糖，不是角色 AI 的 L2 正统**；L2 见 [图怎么分层](graph-layering-flow-and-behavior.md)）、删节点清悬挂边、source map 缺失失败关闭。正式文字合同已齐：`ConstText` / `ConcatText` / `IntToText` / `FloatToText` / `SinkPresentationText` 与 `FormatText` 花括号自动引脚进入 descriptor / 糖名册（见 [图正式文字](graph-formal-text.md)）。地图变量面板只暴露 Integer / Float；不再列出引擎还不认的 Array / Map。

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

Load 后画布显示控制边（蓝）与值边。左侧节点表里的作者糖只来自 Bridge `authoringSugars`（含 `BranchBool`、`SwitchInt`、`SelectByEnum`、`FsmState`、`Wait`、`While`、`Until`、`Break`；Script 另有 `BtSequence` / `BtSelector` / `BtDecorator`；TriggerGraph 另有 `InlineGraph`）。普通节点的 `Jump.target`、`Call.call/next` 等端口来自 Bridge，不是前端硬编码。`FsmState` 必须绑 `enumType` + `stateVar`，case 臂用枚举成员名。BT 组合糖用 `child:{n}` 臂（Decorator 固定 `child:0` + `decoratorKind`）。**角色 AI 行为树 / 状态机的正统作者面是 L2 JSON 拓扑，不在本页 `/gas-graphs` 用整树糖代替**（见 [图能力唯一入口](graph-capability-status.md) §3.3.0）。

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
5. **一拍跑完的链是齐亮齐灭，不是逐步流动。** `GraphDebugTraceRecord` 只有序号和步数，没有时间或帧号；编辑器按 `drain` 收到的时刻盖戳。所以纯读写比较、没有 `Wait` / `AwaitCallback` 的链在同一拍跑完，整批事件同时到达、同时衰减。跨拍挂起的链才看得到先后。不许把单拍链的齐闪说成流动光点。
6. 不声称完整 `NodeExit` 生命周期。嵌套 `InvokeScript` 记录带 `graphId`；source map 缺失时 AgentBridge 失败关闭，错误含 graph id 与 pc。

### 3.4 TriggerGraph 事件入口

一条入口只有一个起因：等一个游戏事件，或者等一个输入动作。检查器把这件事做成一次选择，两边不会同时填上——运行时要求 `event` / `action` 恰有其一，编辑器不许存出运行时挂不上的入口。

等事件时，事件名优先从 `/api/graph/event-schemas/{modId}` 下拉选择；选中后自动建议 `on_<Event>` 标签，并在检查器展示 Schema 载荷针。仍可手填未登记事件名，但会标明载荷针未类型化。

等输入动作时，动作 id 从 `/api/graph/input-actions/{modId}` 下拉选择（各 mod `Input/default_input.json` 的合并目录），不手打。这类入口不进事件总线，载荷针走共用的 `InputAction` schema；「事件载荷里带的动作」那个过滤器对它不适用，检查器直接说明而不是留个能填坏的空框。

### 3.5 作者标注：底栏用人话讲这一趟

底栏那几句人话是 **mod 自己的数据**，不在编辑器源码里。写在该 mod 的 `mods/showcases/map_trigger_night_raid/MapTriggerNightRaidMod/assets/GAS/graph_editor.json`：

- `annotations.groups`：给节点分组，每组一句人话。**每张图只声明一次**，共用同一段链的入口自然复用，不重抄节点表。一个节点只能属于一组。
- `annotations.entries`：按入口标签写 `title` / `summary`，作为底栏抬头。

Watch 之后，底栏按执行到达的顺序列出这一趟走过的每一组，最后一组标成落点；衰减用的是和画布热度同一个 TTL，热度灭了这几句一起撤，不会留一句「正在走」在黑屏上。没写标注的图照常能 live debug，底栏只是不讲人话。

分组里的节点名和入口标签都要在 `graphs.json` 里真实存在：Bridge 读写 sidecar 时都对着图核对，改了节点名就点名报错，不会悄悄不讲了。`npm run assert` 在 CI 里对仓库内所有 mod 做同样的核对。

---

## 4. 场景

1. 新人打开 `/gas-graphs`，加载夜袭 Flow 图，看见控制端口与 Bridge 投影的作者糖，Validate 通过。
2. 故意加未连线的 Until，Validate 点名缺 `body`/`next`/`condition`。
3. 夜袭 + AgentBridge 运行中，Watch 杀敌那条入口：右边只剩这条入口够得到的短链，底栏写出这条链是干什么的。游戏里砍倒一个敌人，链路上的节点和暖黄控制边一起亮起，底栏按顺序列出这一趟走过的几步、最后停在哪一步；两秒后画面和文字一起安静下来。
4. 同一张图上，凑满门槛那一刀走完整条链，底栏多出刷 Boss 那一步，游戏里 Boss 出现、提醒面板弹出。
5. 一条入口改成等输入动作：起因切到「an input action」，动作 id 从下拉里选，事件名那栏收起，载荷针换成 `InputAction`。把动作 id 留空按保存，编辑器点名这条入口拒绝保存。
6. TriggerGraph 事件入口从 Schema 下拉选事件，检查器列出载荷针。
7. 在 Script 图里加入 `FsmState`（流程糖），填写枚举与相位变量并挂 case 臂，保存后再打开字段仍在——这不表示角色 AI 正统已迁到糖图。
8. 在 Script 图里加入 `BtSequence`（流程糖），用 child 臂挂子节点；`BtDecorator` 选 `decoratorKind` 后连 `child:0`，保存后再打开仍在——同上，L2 行为树正统仍是 `behavior_trees.json`。
9. 变量面板类型只见 Integer / Float，没有 Array / Map。

---

## 5. 边界

- 正式文字节点与 FormatText 糖只来自运行时 descriptor / 糖名册；未登记前不得展示可保存假节点。
- Live Debug 依赖真实挂载与 source map；无游戏进程时面板应明示 Bridge 错误，不得假装有事件。
- 底栏人话只来自 mod 的 `graph_editor.json`。编辑器源码里不得出现任何具体图 id、mod id 或节点 id（`DEFAULT_*_ID` 落地默认值除外），`ReactEditor_MustNotNameShowcaseGraphsOrMods` 守着这条。
- 标注点到的节点或入口在图里不存在时，读写 sidecar 都失败关闭并点名；不得退回「不讲人话」的静默降级。
- 单拍跑完的链只能说齐亮齐灭。要演逐步流动，先给 trace 记录补时间或帧号，别在文档里先许诺。
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

  Scenario: 砍一刀，右边用人话讲清刚才发生了什么
    Given 夜袭 showcase 与 AgentBridge 正在跑
    And 编辑器左边是战场，右边是这张图
    When 我在入口列表里挑「杀敌刷 Boss」并按 Watch
    Then 底栏抬头写出这条链叫什么、干什么
    And 画布只留下这条链，别的链看不见
    And 底栏说在等游戏里发生这件事
    When 我在战场上砍倒一个敌人
    Then 这条链上的节点和暖黄的线一起亮起来
    And 底栏按先后列出这一趟走过的几步
    And 最后那一步被标出来，就是这一刀停在哪儿
    When 我等两秒不动
    Then 线和节点暗下去
    And 底栏那几步也一起撤掉，不会留一句「正在走」

  Scenario: 凑满门槛那一刀，能看到 Boss 被刷出来
    Given 我正在 Watch「杀敌刷 Boss」
    And 我已经砍倒了差一个就够门槛的敌人数
    When 我砍倒凑满门槛的那一个
    Then 底栏多出「营地刷出 Boss」这一步，并停在那儿
    And 战场上出现 Boss
    And 提醒面板弹出来

  Scenario: 作者改了节点名，编辑器当场点名，而不是不吭声
    Given 某一组人话说明里点到了一个节点
    When 我把图里那个节点改名，再打开这张图
    Then 编辑器报错并写出是哪一组、点到了哪个不存在的节点
    And 底栏不会假装什么都好、只是不讲话

  Scenario: 一条入口只能有一个起因
    Given 我选中一张入口卡
    When 我把起因切成「等一个输入动作」
    Then 事件名那栏收起来
    And 动作 id 只能从已登记的动作里挑
    And 「事件载荷里带的动作」那个过滤器写明对它不适用
    When 我把动作 id 清空再按保存
    Then 编辑器点名这条入口，拒绝保存

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
