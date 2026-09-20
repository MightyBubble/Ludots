# GAS Composition Gate — #1106 按实例订阅 + 事件节点命名针脚

- **Task / Issue**: TriggerGraph：按实例订阅事件 + 事件节点命名针脚 — https://github.com/MightyBubble/Ludots/issues/1106
- **Date**: 2026-08-24
- **Agent / Author**: Codex（TriggerGraph 作者面对齐 · 第一批）
- **数据源**: #1114 `EventSchemaRegistry`（针脚字典唯一来源，编辑器/编译器零手写平行表）。

## GAS Composition Gate — Self Review

### 1. Core judgment

新变体主要交付物是: **A（现有能力组合 + 3 个 Layer 0 读取 op）**

结论: PASS

一句话理由: 针脚= schema 投影，实例订阅= `MapLoadEntityIndex` 反查，Tag 接线= 既有 TagId payload；新增的只有三个单一职责读取 op（entry 捕获表 → 寄存器），无 profile enum、无 preset 开关、无平行管线。

### 2. Layer assignment

| 步骤/能力 | Layer | 实现载体 |
|-----------|-------|----------|
| `LoadEntryPayloadEntity/Int/Float` op | 0 | GraphOps 413-415 + 零分配 `GraphEntryPayloadTable` + 执行器/patcher/描述符 |
| 入口捕获（schema 参数 → 表） | 0 | `TriggerGraphMountTrigger.CaptureEntryPayload` |
| InstanceId 订阅 | 0 | `TriggerGraphEntryFilters.instanceId` + 挂载期校验 + `MapLoadEntityIndex.TryGetInstanceId` 反查 |
| Tag 过滤接线 | 0 | 挂载期 `TagRegistry.GetId` 解析进 filters.TagId，evaluator 只比 int（GraphRuntime 禁引 GAS 的守卫保持） |
| 编辑器针脚/下拉 | 2 | Bridge 三端点（schemas / payloadKeys / instances）+ React（GasNode 针脚、Inspector 下拉、针脚拖线降级 op） |

### 3. Reuse list

- Handlers: 无新增 BuiltinHandler；复用 LoadExplicitTarget/LoadCaster 作 owner/caster 帧种子。
- Queues / Systems: 无新 system；派发复用 `TriggerManager.FireMapEvent` 单口。
- Resolvers / Registries: `EventSchemaRegistry`（#1114）、`MapLoadEntityIndex`（加反查，不另起表）、`ConfigKeyRegistry` 符号通道（SymbolPatcher 复用 map var 的 Register 分支）、`TagRegistry`。
- Existing presets / graphs: 夜袭全链零迁移（无 slot 使用）；override 夹具 mod 新增探针图承载引擎级验收。

### 4. New Layer 0 ops

| Op 名 | 单一职责 | 为何不能组合现有 op |
|-------|----------|---------------------|
| LoadEntryPayloadEntity (413) | 入口捕获表 → E 寄存器 | Entity 类 payload 此前无任何读取路径（issue 点名的真缺口） |
| LoadEntryPayloadInt (414) | 入口捕获表 → I 寄存器 | 411/412 读的是 presenter 事件槽（`GraphEventPayload`），与 TriggerGraph 入口 payload 是两条数据通道，不可复用 |
| LoadEntryPayloadFloat (415) | 入口捕获表 → F 寄存器 | 同上 |

### 5. Transaction boundary

无 gameplay 事务；捕获表 Clear→Set 原子于 StartRun 内同步完成。

### 6. Config SSOT

行为配置: graphs.json 的 `payloadKey` 字段 + entry filters `instanceId`；键合法性 = `MapTriggerEventPayloadKeys.IsKnownKey`（编译期 fail closed），键是否属于入口事件 = 运行时捕获表 fail closed（未携带即抛）。

是否新增 JSON schema: YES（节点字段 `payloadKey` + filters 字段 `instanceId`，均严格解析、未知值 fail closed）。

### 7. Red flag scan

- [x] 未新增 profile inherit/placement enum
- [x] 未新建与 spawn 平行的物化管线
- [x] 未把 placement 校验塞进 lifecycle op
- [x] 未添加「说不清的」默认 fallback（slot op 未删——见下方"设计偏离记录"）

### 8. Next variant test

下一个事件参数读取变体只改 schema 表或新增类型 op；不碰 Core enum、不加开关。

## 设计偏离记录（按 v2 纪律上报，未静默处理）

**「slot 形态删干净」不可全量执行**：`LoadEventPayloadInt/Float`（411/412）不是 TriggerGraph 的死路径，而是 presenter 条件图的活跃合同——`PresenterRuleSystem` 塞 `GraphEventPayload` 槽，`PresenterTopologyConditionGraphTests` 手写槽位指令验收。TriggerGraph 入口 payload 走的是挂载种子寄存器（E[0..1]/I[0..2]/F[0..1]），与槽位机制本就互不相干。处置：411/412 保留为 presenter 通道合同（注释已注明归属），TriggerGraph 通道新增 413-415 具名键 op；夜袭与全部 mod 图零 slot 使用（grep 证实），迁移集为空。若后续要把 presenter 条件也 schema 化，属 presentation 时序合同票。

**`LoadEntryPayloadString` 不交付**：运行时无字符串寄存器/文本值合同（graph-capability-status 明文），编辑器不渲染 String 针脚（防"能画不能跑"假针脚）。

## 验收证据

- GasTests 全量 529/529（新增 `TriggerGraphEntryPayloadTests` 6 用例 + InterModOverride 扩展断言：`probe_last_count==3` 具名键读取、`probe_hero_entered==1` 实例订阅命中）
- `npm run build` 通过（事件卡针脚/Instance 下拉/payloadKey 下拉/拖线降级）
- Bridge 构建零错误（三新端点）
