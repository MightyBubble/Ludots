## GAS Composition Gate — Self Review

- **Task / Issue**: #1398 Case E — 键位随实体交互上下文投影，去掉 startup/scheme 硬推
- **Date**: 2026-09-03
- **Agent / Author**: cloud agent

### 1. Core judgment

新变体主要交付物是（A/B/C/D）: A（既有档案字段 `inputContextId` + 既有投影系统，纯配置对齐）

结论: PASS

一句话理由: 不新增 schema/enum/op；只填档案已有字段，让 InputContextProjectionSystem 从实体挂载派生 IMC。Default_Gameplay 仍留在 startup——它承载 PointerPos，不是 CaseE 键位表。

### 2. Layer assignment

| 步骤/能力 | Layer (0/1/2/3) | 实现载体 |
|-----------|-----------------|----------|
| battle → CaseE.Controls | 2（配置组合） | interaction_context_profiles.inputContextId |
| 投影 push/pop | 0（已有） | InputContextProjectionSystem |

### 3. Reuse list

- Handlers: N/A
- Queues / Systems: InputContextProjectionSystem、ControlSchemeRuntime
- Resolvers / Registries: InteractionContextProfileRegistry.InputContextIdRegistry
- Existing presets / graphs: 不变

### 4. New Layer 0 ops (if any)

N/A

### 5. Transaction boundary

无新事务边界

### 6. Config SSOT

行为配置落在: `mods/showcases/case_e_selection/.../Input/interaction_context_profiles.json`

是否新增 JSON schema: NO

### 7. Red flag scan

- [x] 未新增 profile inherit/placement enum
- [x] 未新建与 spawn 平行的物化管线
- [x] 未把 placement 校验塞进 lifecycle op
- [x] 未添加「说不清的」默认 fallback

### 8. Next variant test

「下一个 Mod 变体」将修改: graph 连线 / effect 步骤（换档案 inputContextId 或键位表，不动 Core enum）
