## GAS Composition Gate — Self Review

- **Task / Issue**: BT/FSM outer topology; double-click leaf/state into Func/Action Script
- **Date**: 2026-08-31
- **Agent / Author**: cloud-agent

### 1. Core judgment

新变体主要交付物是（A/B/C/D）: A

结论: PASS

一句话理由: 新增作者糖 `BtLeaf` / `FsmAction`（编译期织入已有 Script 叶子图，零新 opcode）；不新增 GraphKind / profile enum。编辑器双击只是导航到已登记函数图。

### 2. Layer assignment

| 步骤/能力 | Layer (0/1/2/3) | 实现载体 |
|-----------|-----------------|----------|
| BtLeaf / FsmAction | 1 作者糖 | GraphAuthoringSugar + BehaviorGraphLeafWeaver |
| 织入叶子 Script | 2 编译前展开 | 对偶 TriggerGraphInlineWeaver |
| 双击进子图 | 作者面 | React navigate ?graph= |

### 3. Reuse list

- Handlers: 无新 handler
- Queues / Systems: GraphBehaviorTreeHost / GraphFsmHost 仍 Require Script
- Resolvers / Registries: graphs.json 文档表；ActionLib 名可选映射到 graph id
- Existing presets / graphs: Graph.BT.Leaf.* / ActionLib bt.*

### 4. New Layer 0 ops (if any)

N/A

### 5. Transaction boundary

无新事务；织入失败关闭。

### 6. Config SSOT

行为配置落在: graphs.json（BtLeaf.functionName = 叶子 Script id）+ 既有 ActionLib 对照表

是否新增 JSON schema: NO

### 7. Red flag scan

- [x] 未新增 profile inherit/placement enum
- [x] 未新建与 spawn 平行的物化管线
- [x] 未把 placement 校验塞进 lifecycle op
- [x] 未添加「说不清的」默认 fallback
- [x] 未新增 GraphKind（BT/FSM 不是 Kind）

### 8. Next variant test

「下一个 Mod 变体」将修改: graph 连线（外层 BtLeaf 指向另一张叶子 Script）
