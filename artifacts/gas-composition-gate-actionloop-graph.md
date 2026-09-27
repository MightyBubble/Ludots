# GAS Composition Gate — ActionLoop 图化（issue #1536 切 A）

## GAS Composition Gate — Self Review

- **Task / Issue**: #1536 肃清 Core ActionLoops：行为循环迁 FSM/BT 图基建，订单引用语义键化
- **Date**: 2026-09-15
- **Agent / Author**: Codex（分支 codex/issue-1536-actionloop-graph-brains，worktree tmp/wt-1536，基于 origin/main@901d05e7c8）

### 1. Core judgment

新变体主要交付物是（A/B/C/D）: **A**（新 graph 节点：5 个 Layer 0 纯计算原语 + 2 个 Layer 0 订单动作 op；行为逻辑本身是 Layer 2 的 mod 图数据）

结论: **PASS**

一句话理由: 接敌/运矿行为从 Core C# 系统改写为 mod 侧 Script 图连线；Core 侧只补齐图 VM 缺的原子原语（实体位置读、int↔float、开方）和订单管线桥（下单/终态），全部单一职责，下一个玩法变体只改图不动 Core。

### 2. Layer assignment

| 步骤/能力 | Layer (0/1/2/3) | 实现载体 |
|-----------|-----------------|----------|
| LoadEntityPosX/Y(500/501) | 0 | GraphOps + handler table |
| IntToFloat(502) / FloatToInt(503) / SqrtFloat(504) | 0 | GraphOps + handler table |
| SubmitAssignedOrder(505) / CompleteActiveOrder(506) | 0 | GraphOps + handler table（委托 OrderSubmitter/OrderQueue/OrderTerminalResultBuffer） |
| GraphActionBrain 宿主系统 | 基建（非行为层） | 复用 GraphExecutor/常驻帧/MapVariableStore，实体查询驱动 |
| 接敌/运矿行为 | 2 | 前线 mod `assets/GAS/graphs.json`（BtSequence/FsmState 糖连线） |
| 订单类型/规则 | 既有配置 | `GAS/order_types.json`（ConfigPipeline 语义键，不动） |

### 3. Reuse list

- Handlers: `ApplyEffectTemplate`(200) 原样承担伤害发布；黑板读写 op(300-305) 承担订单载荷与行为计数
- Queues / Systems: `OrderQueue`、`OrderSubmitter`（TryEnqueueAssigned / NotifyOrderComplete）、`OrderBufferSystem`、`EffectRequestQueue`、`MoveToWorldCmOrderSystem`
- Resolvers / Registries: `OrderTypeRegistry`（语义键→id）、`OrderTypeConfigLoader`、`ComponentRegistry`/`AuthoringRegistry`（组件 authoring 既有通道）、`GraphProgramRegistry`、`OrderTerminalResultBuffer`
- Existing presets / graphs: `bt.patrolChaseAttack`（BT 糖先例）、`Graph.FSM.Sentry`（FsmState 先例）、前线 `Effect.Rts.Frontline.InfantryDamage`

### 4. New Layer 0 ops (if any)

| Op 名 | 单一职责 | 为何不能组合现有 op |
|-------|----------|---------------------|
| LoadEntityPosX/Y | 读任意实体世界坐标厘米 → Int 寄存器 | op 集只有帧级 TargetPosCm 读(402/403)与写(448)，无实体位置读 |
| IntToFloat | Int→Float 寄存器转换 | 不存在；位置为 int cm，float 算术（Sub/Mul/Div/Clamp）无入口 |
| FloatToInt | Float→Int（round-half-away-from-zero，对齐厘米约定） | 不存在；提交移动令需要 int cm |
| SqrtFloat | Float 开方 | 不存在；DivFloat(23) 在，环槽方向归一化需要模长 |
| SubmitAssignedOrder | 行为体向 OrderQueue 下 assigned order（语义键 orderType，坐标取寄存器） | 图侧无任何触碰 OrderQueue 的 op；输入侧 op（graph-input-order-chain 线）走 §12 意图缓冲，合同不同不共用 |
| CompleteActiveOrder | 为自身激活订单发布终态（OrderTerminalResultBuffer 既有通道） | 图侧无终态发布入口；订单完成权目前只在 C# executor（MoveToWorldCmOrderSystem 等）手里 |

### 5. Transaction boundary

必须原子 rollback 的步骤: **N/A**——订单提交失败即 fail-closed 抛错（对齐 DirectAttackSystem 现行 "OrderQueue is full" 合同），不引入静默重试；效果结算事务仍在 EffectRequestQueue 既有管线，本任务不碰。

### 6. Config SSOT

行为配置落在: 前线 mod `assets/GAS/graphs.json`（行为图）+ `assets/GAS/order_types.json`（订单语义键，既有）+ `assets/Entities/templates.json`（GraphActionBrain 组件绑定）

是否新增 JSON schema: **NO**——GraphActionBrain 走 ComponentRegistry/AuthoringRegistry 既有组件 authoring 通道（与 DirectAttackProfile 同机制），不新增文件格式、不建平行加载器。

### 7. Red flag scan

- [x] 未新增 profile inherit/placement enum
- [x] 未新建与 spawn 平行的物化管线（宿主复用 GraphExecutor 唯一 VM，不造第二台）
- [x] 未把 placement 校验塞进 lifecycle op
- [x] 未添加「说不清的」默认 fallback（未注册语义键、图缺失、队列满全部 fail-closed 点名）

### 8. Next variant test

「下一个 Mod 变体」（例如：远程放风筝单位、不同运矿节奏）将修改: **graph 连线**（BtSequence/FsmState 臂、常量、SubmitAssignedOrder 参数）——不改 Core enum。

---

## 复用 / 新增总表（与 ai-assisted-development.md §4.2 合并）

| 类型 | 项 |
|------|-----|
| 复用 | OrderQueue/OrderSubmitter/OrderBufferSystem/OrderTerminalResultBuffer、EffectRequestQueue+ApplyEffectTemplate(200)、GraphExecutor/GraphProgramRegistry、BtSequence/BtSelector/BtDecorator/FsmState 糖、黑板 op(300-305)、MapVariableStore、ComponentRegistry/AuthoringRegistry、ConfigPipeline/order_types.json |
| 新增 Layer 0 op | LoadEntityPosX(500)、LoadEntityPosY(501)、IntToFloat(502)、FloatToInt(503)、SqrtFloat(504)、SubmitAssignedOrder(505)、CompleteActiveOrder(506) |
| 新增 Layer 1 | N/A |
| 新增 Layer 2 | bt.frontlineAttack、fsm.resourceTransport（切 B，前线 mod 数据） |
| 禁止 | 新 profile DSL、平行加载器、第二 VM、SubmitCommandIntent 合并、数字订单 id |
