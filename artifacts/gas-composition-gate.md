## GAS Composition Gate — Self Review

- **Task / Issue**: #1296 — B1 触发轨道：TriggerGraph 专用 op `OfferActivity` + provider effect `activity.offer`
- **Date**: 2026-08-28
- **Agent / Author**: ZCode (GLM-5.3)

### 1. Core judgment

新变体主要交付物是（A/B/C/D）: **A**（新 graph 节点；provider effect 对齐 `task.create` 既有模式）

结论: **PASS**

一句话理由: `OfferActivity` 是单一职责 Layer 0 桥接节点（graph → ActivityRuntimeService.OfferOrActivate），无 enum、无 preset 开关、无平行管线；下一个内容变体改 graph 连线即可换活动。

### 2. Layer assignment

| 步骤/能力 | Layer (0/1/2/3) | 实现载体 |
|---|---|---|
| OfferActivity graph op | 0 | `GasGraphOpHandlerTable` + `GraphOpDescriptorTable.Data`（TriggerGraph-only 掩码） |
| `activity.offer` provider effect | 0（provider 合同，非 GAS effect preset） | `ActivityBridgeProviders.cs`，镜像 `TaskBridgeProviders.cs` |
| showcase 触发编排（事件→graph→offer） | 2 | mod 的 `Events/custom_events.json` + `GAS/graphs.json` 连线 |

### 3. Reuse list

- Handlers: `ActivityRuntimeService.OfferOrActivateChecked`（含全部准入/拒绝语义，不重写）
- Queues / Systems: `RegionTriggerSystem` / `TriggerManager`（事件轨道）、`SystemGroup.ClearPresentationFlags`（排水相位）
- Resolvers / Registries: `ProviderServices.Effects`（`activity.offer` 注册）、`ActivityDefinitionRegistry`（定义校验）
- Existing presets / graphs: night raid TriggerGraph 事件/过滤语法；`task.create` effect 形状

### 4. New Layer 0 ops (if any)

| Op 名 | 单一职责 | 为何不能组合现有 op |
|---|---|---|
| `OfferActivity` | 以 graph 上下文实体为 scope 派发一次活动 | 现有 op 集没有任何节点能到达 `ProviderServices` 或 `ActivityRuntimeService`（ApplyEffect* 只走 GAS EffectTemplate；DispatchMapEvent 只进 TriggerManager） |

### 5. Transaction boundary

无需原子 rollback：派发是幂等准入（repeat_policy 去重）+ 实例物化由 ECS world 承担；拒绝路径走 `ActivityAdmissionResult` 只读返回，无半提交状态。

### 6. Config SSOT

行为配置落在: graph（`GAS/graphs.json` 连线）+ `Activities/activities.json`（既有 loader/registry）

是否新增 JSON schema: **NO**（op 参数复用标准 op config 形状 `{op, activity_id}`；活动定义走既有 schema）

### 7. Red flag scan

- [x] 未新增 profile inherit/placement enum
- [x] 未新建与 spawn 平行的物化管线
- [x] 未把 placement 校验塞进 lifecycle op
- [x] 未添加「说不清的」默认 fallback

### 8. Next variant test

「下一个 Mod 变体」将修改: **graph 连线**（换 activity_id、换事件、换过滤）

若选了 Core enum → FAIL — 未选。
