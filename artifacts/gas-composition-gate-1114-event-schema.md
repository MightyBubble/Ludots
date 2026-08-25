# GAS Composition Gate — #1114 事件参数 Schema 数据化 SSOT

- **Task / Issue**: TriggerGraph：事件参数 Schema 数据化 SSOT（mod 可扩展 payload + 作用域）— https://github.com/MightyBubble/Ludots/issues/1114
- **Date**: 2026-08-24
- **Agent / Author**: Codex（TriggerGraph 作者面对齐 · 第一批）
- **承载方式**: HITL 已拍板方案 b——集中内建 schema 注册表 + 与 `GameEvents` / `MapTriggerEventPayloadKeys` 交叉校验。

## GAS Composition Gate — Self Review

### 1. Core judgment

新变体主要交付物是: **A（现有能力组合：数据契约 + 校验）**

结论: PASS

一句话理由: 把"事件 ↔ payload 键 ↔ 类型"从注释提升为一张机器可读注册表，复用 `GameEvents` 反射词汇表、`MapTriggerEventPayloadKeys` 常量、`CustomEventCatalog` 装载管线与 `TriggerManager.FireMapEvent` 单一派发口；不新增 handler、profile enum、preset 开关或平行管线。

### 2. Layer assignment

| 步骤/能力 | Layer (0/1/2/3) | 实现载体 |
|-----------|-----------------|----------|
| schema 类型 + 内建表 + 交叉校验 | 0 | `src/Core/Scripting/EventSchema*.cs`（与 GameEvents/键表同层） |
| custom_events.json schema 扩展解析（严格 fail closed） | 0 | `CustomEventCatalog.cs` 装载器扩展 |
| fire 期 payload 契约校验 | 0 | `TriggerManager.FireMapEvent/Async` 入口 + `EventSchemaRegistry.ValidateFirePayload` |
| 引擎接线 | 0 | `GameEngine` 装载后 SetService + 绑定 TriggerManager |

### 3. Reuse list

- Handlers: 无新增；不触碰 `BuiltinHandlerId` / `EffectPresetType`。
- Queues / Systems: 不新增 system；校验挂在既有 `TriggerManager` 派发口。
- Resolvers / Registries: 复用 `CustomEventNameRegistry`（词汇表裁决）、`CustomEventCatalogLoader`（ArrayById 合并装载）、`GameEvents` 反射（`BuildEngineKnownSet` 同款机制枚举键表常量）。
- Existing presets / graphs: 现有图零改动；`NightRaid.KillTool.Used` 无参事件零回归。

### 4. New Layer 0 ops (if any)

N/A——本票不新增 graph op（针脚下沉是 #1106）。

### 5. Transaction boundary

无 gameplay 事务。装载期解析失败 = 引擎初始化失败（fail closed）；fire 期校验失败抛错进 `TriggerManager` 错误路径，不留半装载状态。

### 6. Config SSOT

行为配置落在: `Events/custom_events.json`（ArrayById 合并，扩展 `scope`/`params` 可选字段）+ 代码内建表（引用 `MapTriggerEventPayloadKeys` 常量，不复制字符串）。

是否新增 JSON schema: **YES（扩展现有 catalog 字段，非新文件）**——扩展的就是本票的交付物：事件参数契约本身无法由现有字段组合表达；解析严格（未知字段/类型越界/前缀冲突 fail closed）。

### 7. Red flag scan

- [x] 未新增 profile inherit/placement enum
- [x] 未新建与 spawn 平行的物化管线
- [x] 未把 placement 校验塞进 lifecycle op
- [x] 未添加「说不清的」默认 fallback（无 schema 的事件不校验——这是"尚未声明契约"的显式状态，不是兜底；PhaseChanged 等 #1113 收编后入表）

### 8. Next variant test

下一个事件参数变体将修改: catalog JSON 的 `params[]` 或代码内建表——不触碰 Core enum、不新增 op。

## 复用 / 新增清单（与 ai-assisted-development §4.2 合并）

| 类型 | 项 |
|------|-----|
| 复用 | `GameEvents` 反射词汇表、`MapTriggerEventPayloadKeys` 19 键、`CustomEventNameRegistry.IsKnownEntryEvent`、`CustomEventCatalogLoader` ArrayById 装载、`TriggerManager.FireMapEvent` 单口、`ScriptContext` 字符串键袋 |
| 新增 Layer 0 | `EventSchemaRegistry`（唯一真相：内建表 + custom 汇入 + 交叉校验 + fire 期校验）|
| 新增 Layer 1 | 无 |
| 新增 Layer 2 | 无 |
| 禁止项自查 | 不造平行事件字典；不复制 payload 键字符串；无 schema 不做静默放行校验 |

## 已拍板与遗留 HITL 记录

- 承载方式 a/b：**b**（本轮会话 HITL 拍板）。
- fire 期多余 `MapTrigger.*` 键：**fail closed**（内建与自定义一致；键只能来自 schema 声明，漂移即错）。
- mod payload 键前缀：一期强制"必须点号命名空间 + 首段不得为 `MapTrigger`"；"首段 == mod id"的严格校验因 `MergedConfigEntry` 不携带来源 mod（ArrayById 合并后来源丢失）暂不可验证，记为后续（需给合并管线补来源追踪，随 #1106 编辑器字典一并评估）。
- `MapTriggerEventPayloadKeys` 无孤儿断言：`schema 引用 ∪ 动态桥键（Gas.Event.*/Moment 桥）∪ 显式待收编清单（VarName/Phase/VarValueInt→#1113，VarValueFloat→float phase 边界）` 三集并集必须穷尽 19 键，缺一即测试失败。
