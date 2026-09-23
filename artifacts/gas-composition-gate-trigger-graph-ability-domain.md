# GAS Composition Gate — TriggerGraph 技能域挂载（#1031 S3）

> 本片自审正本：ability 域挂载。前置正本 `artifacts/gas-composition-gate-trigger-graph-domains.md` 管 S1/S2（方言更名/挂载域模型/实体域/GAS 桥），本片在其上放开 D2 预留的 `domain:"ability"`，不改其结论。

## 任务摘要

- 目标：ability 定义 JSON 增加 `TriggerGraphs` 挂载（scope=施法者）；技能时刻/GAS 事件按 caster 路由到技能域挂载；打开 `domain:"ability"`（现为预留拒绝）。
- 交付物：`TriggerGraphMountDomain.Ability` 放开、`AbilityDefinition.TriggerGraphs`（复用挂载域模型）、按施法者路由的挂载生命周期（CastStarted 创建/终态时刻拆除）、加载期图注册校验、全链测试。
- 涉及 JSON schema：`abilities.json` 新增 `TriggerGraphs` 数组（graph id 字符串列表，与 entity template 同构）。**因此本 gate 完整执行。**

## 1. Core judgment

新变体主要交付物是（A/B/C/D）: **A** —— 已有 TriggerGraph 图挂载到新挂载域（ability）。变体全部由"挂载域扩展 + 既有图程序组合"表达，没有新增 gameplay profile enum / preset 开关 / 平行管线。

结论: **PASS**

一句话理由: 技能反应逻辑是"把同一张图挂到 ability 生命周期上"，复用 S1/S2 的挂载、分发、续跑、GAS 桥全部基建；`TriggerGraphMountDomain` 的 Ability 值是 D2 决策已预留的挂载域枚举扩展，不是玩法变体枚举。

## 2. Layer assignment

| 步骤/能力 | Layer (0/1/2/3) | 实现载体 |
|-----------|-----------------|----------|
| ability 定义声明图名 | Layer 2 组合 | `abilities.json` `TriggerGraphs`（复用 EntityTemplate 同构字段，非新 DSL） |
| 挂载创建/拆除（cast 生命周期） | Layer 1 薄壳 | `AbilityTriggerGraphMounts`（注册/注销触发器，无业务开关） |
| 图执行/续跑/载荷种子 | Layer 2 | 复用 `TriggerGraphMountTrigger`/`TriggerGraphResumeTrigger`/`GraphExecutor` |
| 技能时刻/事件路由 | Layer 2 | 复用 `GasPresentationEventBuffer` 单缓冲 + `TriggerManager` 地图总线 |

## 3. Reuse list

- Handlers: 无新 handler；复用既有挂载触发器执行路径
- Queues / Systems: `AbilityExecSystem` 既有时刻写入、`TriggerGraphMomentBridgeSystem` 既有桥、`TriggerManager` 地图总线、`GasPresentationEventBuffer`
- Resolvers / Registries: `AbilityDefinitionRegistry`、`GraphProgramRegistry`、`GraphIdRegistry`、`AbilitySlotResolver`（cast 实例键）
- Existing presets / graphs: 现有 TriggerGraph 图程序原样可挂 ability 域（作者面不变）

## 4. New Layer 0 ops (if any)

N/A —— 本片零新 op；纯挂载域扩展。

## 5. Transaction boundary

必须原子 rollback 的步骤: 无结构写事务。挂载创建失败（图未注册/kind 不符）在**ability 加载期**失败关闭（graphs 先于 abilities 加载，可校验），不留半挂载状态；cast 期挂载创建/拆除同步完成，异常即抛。

## 6. Config SSOT

行为配置落在: `abilities.json`（挂载声明）+ graph 资产（`graphs.json`，既有）。新增 JSON schema 字段 `TriggerGraphs`（graph id 字符串数组）——**为何不通过组合表达**：这不是行为开关，而是"把既有图程序挂到哪个生命周期"的引用声明，与 entity template 的 `TriggerGraphs` 完全同构，是挂载域模型的既有形态。

## 7. Red flag scan

- [x] 未新增 profile inherit/placement enum
- [x] 未新建与 spawn 平行的物化管线
- [x] 未把 placement 校验塞进 lifecycle op
- [x] 未添加「说不清的」默认 fallback（caster 无图/无地图 = 可读丢弃计数，非静默 fallback）

## 8. Next variant test

「下一个 Mod 变体」将修改: **graph 连线** —— 技能域新反应逻辑 = 新图程序或新连线，挂载引用复用；Core 仅新增挂载域值（D2 已预留）。未选 Core enum 表达玩法变体。

## 复用/新增清单（合并 §4.2）

| 类型 | 项 |
|------|-----|
| 复用 | TriggerGraphMountTrigger / TriggerGraphResumeTrigger / GraphExecutor / TriggerManager / GasPresentationEventBuffer / TriggerGraphMomentBridgeSystem 桥点 / TriggerDecoratorRegistry |
| 新增 | `AbilityDefinition.TriggerGraphs` 字段 + 加载解析；`AbilityTriggerGraphMounts` 按 cast 生命周期管理；`AbilityTriggerGraphMountSystem`（AbilityActivation 槽）；`TriggerGraphMountDomain.Ability`（D2 预留值放开） |
| 禁止 | 新 profile DSL / 第二事件总线 / 平行执行器 |

## 与 S4/S5 的边界

- S4 成文 §D6 时序合同（本片只实现，不展开文档化）。
- S5 双域 showcase（实体「受击计数怪」+ 技能「火球叠层」）在本片之后，复用本片挂载链。
