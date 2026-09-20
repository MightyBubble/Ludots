# ed-03 reference · 编辑器缺口与路线图

> 现状参考。第一性需求见 [ed-03 PRD](../prd/ed-03-gap-roadmap.md)；配置说明见 [ed-03 配置说明](../config/ed-03-gap-roadmap.md)。

## 1. 现状快照

- UnavailableActions 现行清单（BuildUnavailableActionsUnlocked）：恒有 undo/redo（会话撤销/重做栈未接入）；未绑定 LiveGasEditPipeline → precheck/applyNextCast；未绑定 AI 生成器 → aiDraft；未配保存服务或保存根 → saveMod。
- 文档投影源现状：接口 ILiveSkillWorkbenchDocumentSource 在 mod contracts；唯一实现是测试桩（FixedDocumentSource）；ModEntry 启动可选注入，未注入仅日志——UI 目录树默认空（治理项 R5）。
- UI 诊断码 LSWUI0001-0009：ApplyNotSupported/PrecheckNotSupported/UndoNotSupported/RedoNotSupported/FieldReadOnly/ValueBelowMin/ValueAboveMax/PipelineMissing/PrecheckRequired。
- 前端 Inspector"尚未接入"区渲染（WebApp 资产内）；路线图四步（投影源→撤销重做→图编辑器→冷编辑流）为本手册口径，引擎无对应清单。
- 图编辑器与冷编辑流无任何实现；冷编辑当前唯一完整链路是离线改文件+重启。

## 2. 代码锚点

| 机制 | 位置 |
|---|---|
| 不可用清单生成 | mods/capabilities/live_skill_workbench/LiveSkillWorkbenchMod/Runtime/LiveSkillWorkbenchRuntime.cs:806-833 |
| 文档投影源接口 | mods/capabilities/live_skill_workbench/LiveSkillWorkbenchMod/Contracts/LiveSkillWorkbenchDtos.cs:92 |
| 唯一实现（测试桩，R5 证据） | src/Tests/WebUiDataPlaneTests/LiveSkillWorkbenchDataPlaneTests.cs:641 |
| 可选注入点 | mods/capabilities/live_skill_workbench/LiveSkillWorkbenchMod/LiveSkillWorkbenchModEntry.cs:105-112 |
| LoadFromSource | mods/capabilities/live_skill_workbench/LiveSkillWorkbenchMod/Runtime/LiveSkillWorkbenchRuntime.cs:384 |
| LSWUI 码 | mods/capabilities/live_skill_workbench/LiveSkillWorkbenchMod/LiveSkillWorkbenchIds.cs:42-56 |
| 前端"尚未接入"区 | mods/capabilities/live_skill_workbench/LiveSkillWorkbenchMod/Assets/live-skill-workbench-app（WebApp 构建产物） |

**相关文档**：[ed-03 PRD](../prd/ed-03-gap-roadmap.md) · [ed-01 reference](ed-01-workbench-base.md) · [ed-02 reference](ed-02-hot-apply.md)
