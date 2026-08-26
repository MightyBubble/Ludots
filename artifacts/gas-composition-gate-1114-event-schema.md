# GAS Composition Gate — #1114 EventSchemaRegistry

## 任务摘要

为 TriggerGraph / Dialogue / GAS bridge 建立事件参数 Schema 的机器可读 SSOT（`EventSchemaRegistry`），扩展既有 `Events/custom_events.json` 与唯一 `TriggerManager` 派发路径。不新增第二事件总线、不新增平行 Registry 管线。

## 判断标准结论

**通过（基建扩展，不是 profile enum / preset 开关）。**

- 新变体形态：扩展已有 custom event 配置形状（`scope` + `params[]`）+ Core 侧 schema 合同，不是新 `BuiltinHandlerId` / `EffectPresetType` / morph profile DSL。
- 派发仍走 `TriggerManager.FireMapEvent`；编辑器命名针脚（#1106）与 `DispatchMapEvent`（#1115）消费本 registry，本票不造第二作者面。

## 自审清单

| 项 | 结论 |
|----|------|
| 是否新增 profile enum / preset 开关？ | 否 |
| 是否可复用现有 Registry / Pipeline？ | 是：`CustomEventNameRegistry` + ConfigPipeline `ArrayById` + `TriggerManager` |
| 是否新建第二事件总线？ | 否 |
| fail-closed？ | 是：未知字段、保留 key、缺参、类型错、registry 未绑定均抛错 |
| 热路径结构变更？ | 否 |

## 复用 / 新增

| 类型 | 项 |
|------|-----|
| 复用 | `TriggerManager`、`CustomEventNameRegistry`、`MapTriggerEventPayloadKeys`、`GameEvents`、`ScriptContext` |
| 新增 | `EventSchema` / `EventParamSchema` / `EventSchemaRegistry`；catalog loader 解析 `scope`/`params` |
| 禁止 | 第二 TriggerManager、手写第二份参数表、`EventSchemas?.` 静默跳过 |

## 架构审计（合入前）

1. 废弃提交的 `EventSchemas?.ValidateFirePayload` 改为 **强制 RequireEventSchemas**。
2. 无 `params` 的 custom event **登记空 schema**，不再是 unbound name。
3. 未登记 schema 却携带 `MapTrigger.*` key → fail-closed；`Gas.Event.*` / `Ability.*` / `Effect.*` 仅允许动态桥 key 集。
4. 补 `ModLoaded` 与现行 `PhaseChanged` 内建 schema（#1113 再演进为 MapVariableChanged）。
