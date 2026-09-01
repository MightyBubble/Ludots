## GAS Composition Gate — Self Review

- **Task / Issue**: Dedicated BT editor + FSM editor; Action/Condition are Func Graphs; double-click navigates to Graph Editor
- **Date**: 2026-09-01
- **Agent / Author**: cloud-agent

### 1. Core judgment

新变体主要交付物是（A/B/C/D）: A

结论: PASS

一句话理由: 作者面拆成三套编辑器（Func Graph / BT / FSM）；叶子仍是已有织入门户，新增 BtAction/BtCondition 糖名走同一织入路径。本刀不新增 GraphKind（BehaviorTree/Fsm Kind 下一刀）。

### 2. Layer assignment

| 步骤/能力 | Layer | 实现载体 |
|-----------|-------|----------|
| BT/FSM 独立编辑器页 | 作者面 | React routes + dialect 过滤 |
| BtAction / BtCondition | 1 作者糖 | GraphAuthoringSugar → BehaviorGraphLeafWeaver |
| Bridge 校验织入 | 装载镜像 | TryCompileGasGraph → ExpandDocuments |
| 双击进 Func Graph | 作者面 | navigate `/gas-graphs?graph=` |

### 3. Reuse list

- BehaviorGraphLeafWeaver / BtLeaf
- GasGraphEditorPage（dialect 参数）
- Bridge authoringSugars + TriggerGraphInlineWeaver 顺序

### 4. New Layer 0 ops

N/A

### 5. Transaction boundary

无

### 6. Config SSOT

graphs.json + functionName；样例图在 UiPlayerAggregateGraphMvpShowcaseMod；无新 schema

### 7. Red flag scan

- [x] 未新增 profile enum
- [x] 未平行物化管线
- [x] 未新增 GraphKind（刻意延后）

### 8. Next variant test

下一个 Mod 变体：在 BT 编辑器挂 BtAction → 另写一张 Script Func Graph；旗舰树迁纯门户另开活
