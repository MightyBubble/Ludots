# 图行为真图化：FSM / BT 设计冻结本

## 1. 概述

本页是 BT 真图化（BT-1 / BT-B）与 HFSM 真图化（FSM-1 / FSM-1a）的**设计冻结 SSOT**。  
进度开账只认 `gitbook/architecture/graph-capability-status.md`；分层规矩只认 `gitbook/architecture/graph-layering-flow-and-behavior.md`。本页冻结「什么叫真图、什么叫无图压测、旧宿主何时还能留」。

**FSM-1a 已收口（本轮）：**

- 作者糖 `FsmState`：编译期降为 `ReadMapVarInt` + SwitchInt 式臂链，零新 opcode。
- 宿主 `GraphFsmHost`：每 agent 私有相位 map 变量 + 每波一次 halt 分派 slice。
- 哨兵演武场 featured 走 `Graph.FSM.Sentry` + `GraphFsmHost`。
- Bridge / React 作者面投影 `FsmState`（enumType、stateVar、case 臂），保存不得丢掉 `stateVar`。
- 万人 crowd 明确标为**无图压测基线**（`HfsmWorld` + `hfsm.sentry`，LifecycleRuns==0），禁止顶真图语义。

**本轮不做：** BT Parallel、BT-2（子树复用/异步叶）、TriggerGraph 收口、文字基建、工程分层拆墙。

---

## 2. 结构

```text
L1 作者糖
  FsmState          → ReadMapVarInt + case 臂链（Script / TriggerGraph）
  BtSequence/…      → Call/Return + 状态寄存器（Script；另账，不捆本轮）

L2 真图宿主
  GraphFsmHost           → featured FSM 大脑
  GraphBehaviorTreeHost  → featured BT 大脑

旧数据路径（可留，必须显式标注）
  HfsmWorld + GraphProgramHfsmHost   → hfsm.json 生命周期绑定 / 无图压测
  BehaviorTreeWorld                  → behavior_trees.json / 无图压测
```

---

## 3. 详情

### 3.1 真图判据（FSM）

1. 相位分派写在图里（`FsmState` 或与之指令级全等的手写链），不是 C# 里再写一套状态表解释器。
2. 每 agent 相位 SSOT 是 map 变量（`stateVar`），由 `GraphFsmHost` 持有私有 `MapVariableStore`。
3. 每 think wave 每 agent 恰好一个 dispatch slice，且必须 halt（禁止 Yield / 预算挂起冒充跨波状态机）。
4. 传感器只做胶水喂数（I[0..]）；阈值与相位迁移写在图臂里。

### 3.2 Crowd / 压测策略（诚实门）

| 段 | 宿主 | 图参与 | 允许声称 |
|----|------|--------|----------|
| Featured 哨兵 | `GraphFsmHost` | `Graph.FSM.Sentry` | 真图 FSM |
| 10k crowd | `HfsmWorld(hfsm.sentry)` 无 host | LifecycleRuns==0 | **仅**无图压测基线 |

决策：真图万人每波超 CI 信封时，**不得**给 crowd 挂 Script 宿主再假装「万人真图」。注册表 summary / notes 必须写明无图基线；验收锁 `LifecycleRuns==0`（BT 对偶锁 `ScriptSlices==0`）。

### 3.3 `HfsmWorld` 退役条件

**退役的是「静默双轨 / 假真图声称」，不是立刻删 Core 类。**

| 条件 | 状态 |
|------|------|
| 凡声称 GraphFsmHost / FsmState 的旗舰演示，featured 不得再跑 `HfsmWorld` | ✅ 哨兵演武场 |
| Crowd 若仍用 `HfsmWorld`，必须标注无图，且验收锁零生命周期 Script | ✅ |
| 合波演示若仍走 `HfsmWorld`+`GraphProgramHfsmHost`，Metrics/注册表必须写 **old-path**，不得顶 FSM-1 | ✅ 整合演示 |
| 压力矩阵 M4 / `FsmRuntimeTests` 可继续用 `HfsmWorld` 测旧拓扑与 SoA | 保留 |
| Core 删除 `HfsmWorld` | **未触发**：须 featured 消费者清零、压测改挂显式无图拓扑或接受删压测、旧 hfsm.json 数据路径迁移完成 |

对偶：`BehaviorTreeWorld` 同款保留策略；图路径不得调用其遍历。

### 3.4 Bridge / 作者面

- Bridge `authoringSugars` 投影 `FsmState`：`controlOutputPorts=[default]`，无值输入（selector 烘焙为 `stateVar`），产出 Int。
- React：`enumType` + `stateVar` 字段 round-trip；case 臂 UI 与 SwitchInt 共用，FsmState 强制先绑 enum。
- 前端不得自造糖名单；Arch guard 要求 Bridge 含 `FsmState`。

---

## 4. 场景

1. 作者在编辑器选 Script，从糖列表拖出 `FsmState`，填 enum 与相位变量，挂 case 臂，Validate 通过并保存——再打开 `stateVar` 仍在。
2. 玩家进「HFSM 岗哨演武场」：前排哨兵随入侵者远近变色（idle→alert→combat）；灰点带只是万人思考压测，字幕/指标不得写「万人真图」。
3. 接手的人打开本页与 status：知道 FSM-1a 已收口，HfsmWorld 还能留在哪里，什么时候才能删 Core。

---

## 5. 边界

- 禁止平行 FSM opcode / 第二 VM。
- 禁止 crowd 静默挂 `GraphProgramHfsmHost` 顶真图。
- 禁止把整合演示的 old-path 写成 GraphFsmHost 已迁移。
- 禁止本轮捆 BT Parallel / BT-2 / TriggerGraph 收口 / 文字基建 / 工程分层。
- `FsmState` 不允许 Yield；预算内必须 halt。

---

## 6. UAT

```gherkin
Feature: 哨兵脑子是真图，万人带不假装

  Scenario: 前排哨兵跟图走相位
    Given 我走进「HFSM 岗哨演武场」
    And 前排哨兵由 GraphFsmHost 驱动 Graph.FSM.Sentry
    When 入侵者靠近再到贴身
    Then 我能看到哨兵相位从 idle 进到 alert 再到 combat
    And 指标里写的是 FSM 图波次，不是旧 HfsmWorld 叶名冒充

  Scenario: 万人灰点带不顶真图
    Given 演武场打开了万人灰点带
    When 一波思考跑完
    Then 灰点带走的是无图 HfsmWorld
    And 这一波没有任何生命周期 Script 跑起来
    And 注册表说明里写清「无图压测基线」

  Scenario: 编辑器能写出 FsmState 且存得住
    Given 我打开图编辑器并加载一张 Script
    When 我从糖列表加入 FsmState 并填写枚举与相位变量
    And 我为成员加上 case 臂后保存再打开
    Then 节点仍是 FsmState
    And 相位变量没有丢
    And Validate 仍通过

  Scenario: 旧合波不装新宿主
    Given 我打开「图行为整合演示」
    When 我看运行指标与注册表摘要
    Then 上面写的是 old-path
    And 没有人把它当成 GraphFsmHost 旗舰
```
