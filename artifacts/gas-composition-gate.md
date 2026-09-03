# GAS Composition Gate — Case E 合同对齐（取值节点 + 抬起拆分）

## 任务摘要

按 Case E 最早合同改掉现网凑合：
1. 命中图用取值节点（起角 map var + 活指针 op），废除 action→attribute
2. 抬起上游只透传事件+名单；下游 handle 图听事件收框
3. ScreenRect 用 PresenterParamBinding 取值（mapVar / pointerScreen），不绑属性

## 判断标准结论

**通过（A）** — 新变体是 graph 节点 + 图连线 + presenter 取值源，不是 profile enum/开关。

## Self Review

- **Task / Issue**: #1398 Case E 合同对齐
- **Date**: 2026-09-03
- **Agent / Author**: cursor cloud

### 1. Core judgment

新变体主要交付物是（A/B/C/D）: **A**

结论: **PASS**

一句话理由: 补 `LoadPointerScreenX/Y` 取值 op；抬起拆成上游 Dispatch + 下游 TriggerGraph；presenter 增加 mapVar/pointerScreen 取值源，零新 Manager。

### 2. Layer assignment

| 步骤/能力 | Layer | 实现载体 |
|-----------|-------|----------|
| LoadPointerScreenX/Y | 0 | GraphNodeOp 477/478 |
| box_hit 读 map var + 指针 | 2 | Query 图连线 |
| selection_handle | 2 | TriggerGraph 听事件 |
| presenter mapVar/pointerScreen | 0 | ValueSourceKind 扩展 |

### 3. Reuse list

- Handlers: GasGraphOpHandlerTable、EventKeyedCollectionWriter
- Systems: InteractionContextTriggerGate、WhileActive、PointerInteractionSnapshotReader
- Registries: GraphProgramRegistry、CustomEventNameRegistry
- Existing: DispatchCollectionEvent、ReadMapVarFloat、DeactivateContext

### 4. New Layer 0 ops

| Op 名 | 单一职责 | 为何不能组合现有 op |
|-------|----------|---------------------|
| LoadPointerScreenX/Y | 读当帧屏幕指针 | 无图侧读 PointerPos；LoadEntryPayload 只覆盖边沿瞬间 |

### 5. Transaction boundary

无跨实体 rollback 需求。

### 6. Config SSOT

行为落在 Case E graphs + presenters + interaction_context_profiles。未新增 profile DSL。

### 7. Red flag scan

- [x] 未新增 profile inherit/placement enum
- [x] 未新建与 spawn 平行的物化管线
- [x] 未把 placement 校验塞进 lifecycle op
- [x] 未添加静默 fallback

### 8. Next variant test

「下一个 Mod 变体」将修改: **graph 连线**（换命中函数 / 换 handle 图）
