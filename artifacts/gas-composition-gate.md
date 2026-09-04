## GAS Composition Gate — Self Review

- **Task / Issue**: #1398 Case E 纠偏——退役档案空壳键；起角落操作者 rep；ScreenRect 按 audience；删四张 Score 适配器
- **Date**: 2026-09-03
- **Agent / Author**: cloud agent

### 1. Core judgment

新变体主要交付物是（A/B/C/D）: A（放宽既有 op 图种白名单 + 既有 ParamBinding/档案字段可选 + 组合既有黑板/指针/audience 读面）

结论: PASS

一句话理由: 不新增 profile enum/平行管线；Write/ReadBlackboardFloat 扩到 TriggerGraph/Query；ParamBinding 补 ownerBlackboardFloat + pointerScreen；档案空壳键改为可选。

### 2. Layer assignment

| 步骤/能力 | Layer | 实现载体 |
|-----------|-------|----------|
| 档案 collection/view 键可选 | 0 小补丁 | InteractionContextProfileConfigLoader / Registry |
| 起角写/读 rep 黑板 | 0 白名单 + 2 图连线 | Write/ReadBlackboardFloat + box_begin/box_hit |
| ScreenRect 角点绑定 | 0 ParamBinding 源 | ownerBlackboardFloat / pointerScreen* |
| 框可见性 | 0 小补丁 | PresenterScreenRectSystem × PresenterRelationContext.Viewer × sole local viewer |
| 删 Score 适配器图 | 2 | Case E 资产 |

### 3. Reuse list

- Handlers: WriteBlackboardFloat / ReadBlackboardFloat / LoadPointerScreen*
- Systems: PresenterScreenRectSystem、InputContextProjectionSystem
- Resolvers: KnowledgeProjectionConsumer.TryResolveSoleLocalSeatViewer（仅作本机 audience 身份，不当迷雾）
- Registries: InteractionContextProfileRegistry、ConfigKeyRegistry（blackboardKey）
- Graphs: box_begin / box_hit / box_commit / selection_handle

### 4. New Layer 0 ops

N/A（不新增 opcode；只扩既有 op 的 authorableKinds）

### 5. Transaction boundary

无新事务壳；ActivateContext / DeactivateContext 既有生命周期不变

### 6. Config SSOT

- `interaction_context_profiles.json`（去掉空壳键）
- `Entities/templates.json`（BlackboardFloatBuffer）
- `GAS/graphs/graph.case_e.*`、`Presentation/presenters.json`
- 地图 Variables 去掉 press 角

是否新增 JSON schema: NO（档案字段改为可省略；ParamBinding 新增合法 source 字符串，非新 profile DSL）

### 7. Red flag scan

- [x] 未新增 profile inherit/placement enum
- [x] 未新建与 spawn 平行的物化管线
- [x] 未把 placement 校验塞进 lifecycle op
- [x] 未添加静默 fallback（缺黑板/缺指针 fail-fast；缺 Viewer 匹配则不画框）
- [x] 不用 Knowledge/Fog 管交互 UI 框

### 8. Next variant test

「下一个 Mod 变体」将修改: graph 连线 / ParamBinding sourceId（黑板键名），不动 Core enum
