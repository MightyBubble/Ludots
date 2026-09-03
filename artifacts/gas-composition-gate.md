# GAS Composition Gate — Case E roster_sync

## 任务摘要

把 `case_e.selectable` 从 `box_begin` 按压时世界扫描，迁到 battle context `triggers[]` 挂载的 `graph.case_e.roster_sync`（`MapHeartbeat`）。

## 判断标准结论

**通过** — 无新 profile enum / preset 开关；无新 Core Manager。复用 InteractionContextTriggerGate + TriggerGraph + DispatchCollectionEvent + EventKeyedCollectionWriter。

## 复用 / 新增

| 类型 | 项 |
|------|-----|
| 复用 | context triggers 门控、MapHeartbeat、QueryAllMapEntities / FilterTeam / FilterTemplate、DispatchCollectionEvent |
| 新增 | Mod 图资产 `graph.case_e.roster_sync.json` |
| 禁止 | CaseESelectableManager、平行集合管线 |

## 已知边界（诚实）

Context 实体域挂载上的 `EntitySpawned` 只服务 scope 自身生命周期，不能听全图兵的出生。现网用 battle 存活期 `MapHeartbeat` Replace 名册；真正的 Add/Subtract 生命周期接线需后续能力，不是本切片偷建 Manager。
