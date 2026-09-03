## GAS Composition Gate — Self Review

- **Task / Issue**: #1398 Case E — 键位随实体交互上下文投影；PointerPos 进 CaseE.Controls
- **Date**: 2026-09-03
- **Agent / Author**: cloud agent

### 1. Core judgment

新变体主要交付物是（A/B/C/D）: A（既有档案字段 + 既有 IMC 绑定，纯配置）

结论: PASS

一句话理由: 不新增 schema/op；battle.inputContextId 投影 CaseE.Controls；指针轴绑进同一键位表，startup 保持空。

### 2. Layer assignment

| 步骤/能力 | Layer | 实现载体 |
|-----------|-------|----------|
| battle → CaseE.Controls | 2 | inputContextId |
| PointerPos 绑定 | 2 | CaseE.Controls bindings |
| 投影 | 0（已有） | InputContextProjectionSystem |

### 3. Reuse list

- Systems: InputContextProjectionSystem
- Registries: InteractionContextProfileRegistry
- Actions: 既有 PointerPos（Core 声明）

### 4. New Layer 0 ops

N/A

### 5. Transaction boundary

无

### 6. Config SSOT

`interaction_context_profiles.json` + `default_input.json`（CaseE.Controls）

是否新增 JSON schema: NO

### 7. Red flag scan

- [x] 未新增 profile enum
- [x] 未新建平行管线
- [x] 未添加静默 fallback
- [x] 未把 Default_Gameplay 塞回 startup 当尾巴

### 8. Next variant test

改 CaseE.Controls 绑定或档案 inputContextId，不动 Core enum
