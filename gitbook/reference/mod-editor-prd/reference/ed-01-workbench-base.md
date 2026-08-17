# ed-01 reference · 实时技能工作台编辑基座

> 现状参考。第一性需求见 [ed-01 PRD](../prd/ed-01-workbench-base.md)；配置说明见 [ed-01 配置说明](../config/ed-01-workbench-base.md)。

## 1. 现状快照

- LiveEditSession：TryStage 先校验后入补丁、Revision++；Discard 清空且++；来源三值 ManualWorkbench/FileChange/AiGeneratedDraft。
- LiveGasEditPipeline：Stage→Classify→Commit（类注释"Never Clear+Register-all，非 ReloadConfigs 分支"）；BeginSafeFrame/EndSafeFrame；七操作（SkillEffectNumeric/SelectedActorAttribute/GraphBodyReplace/TagRuleBodyReplace/AttrConstraintNumeric/EffectTemplateRef/EffectGrantedTag）分派七候选表；CommitImmediate 走 ILiveAttributeCommandSink；CommitNextCastSafeFrame 要求安全帧，按 Graph→EffectNumeric→TagRule→AttrConstraint→EffectRef→GrantedTag 顺序提交、失败逆序回滚。
- LiveApplyMode 四级：ImmediateCommand/NextCastLiveApply/MapReloadRequired/EngineRestartRequired。
- LiveEditModSaveService：Preview 产保存计划（立即属性命令默认排除并显式列出）；Save 按计划 upsert 回 mod 文件（graphs/effects 等）。
- LiveEffectChainTracer：环形缓冲默认 256、七相位（Cast/Effect/Attribute/Tag/Graph/Response/Dropped）、溢出计 Dropped 显式事件（"no silent loss"）。
- LiveAttributeCommandExecutor：fail-closed——未选中/实体死/无缓冲/未知属性抛错。
- LiveAiSkillDraft：草稿=结构化补丁操作；UnconfiguredAiSkillDraftGenerator 默认抛错。
- 诊断码 LSW0001-0021（LiveEditDiagnosticCodes）；DataPlane：主题 ludots.capability.liveSkillWorkbench.session + 11 命令（lsw.stageEdit/discardEdits/selectCatalogItem/precheck/applyNextCast/applyImmediateAttribute/generateAiDraft/bindAiDraft/previewSave/saveToMod/refreshEffectChain），快照 LatestWins。
- 不可用动作清单（undo/redo 恒有；未绑定管线/AI/保存根时 precheck/applyNextCast、aiDraft、saveMod）见 ed-03。

## 2. 代码锚点

| 机制 | 位置 |
|---|---|
| 会话 | src/Core/Gameplay/GAS/LiveSkillWorkbench/LiveEditSession.cs:15-80 |
| 来源枚举 | src/Core/Gameplay/GAS/LiveSkillWorkbench/LiveEditSource.cs:7-12 |
| 三段流水线与铁律 | src/Core/Gameplay/GAS/LiveSkillWorkbench/LiveGasEditPipeline.cs:16-19 |
| 安全帧开关 | src/Core/Gameplay/GAS/LiveSkillWorkbench/LiveGasEditPipeline.cs:52-54 |
| Classify 分派 | src/Core/Gameplay/GAS/LiveSkillWorkbench/LiveGasEditPipeline.cs:59-155 |
| 提交顺序与逆序回滚 | src/Core/Gameplay/GAS/LiveSkillWorkbench/LiveGasEditPipeline.cs:179-290 |
| 四级分级 | src/Core/Gameplay/GAS/LiveSkillWorkbench/LiveApplyMode.cs:9-15 |
| 七操作枚举 | src/Core/Gameplay/GAS/LiveSkillWorkbench/LiveDebugPatch.cs:7-15 |
| 保存计划与 upsert | src/Core/Gameplay/GAS/LiveSkillWorkbench/LiveEditModSaveService.cs:24-190 |
| 效果链追踪 | src/Core/Gameplay/GAS/LiveSkillWorkbench/LiveEffectChainTracer.cs:10-18、54-77 |
| 立即命令执行器 | src/Core/Gameplay/GAS/LiveSkillWorkbench/LiveAttributeCommandExecutor.cs:10-106 |
| AI 草稿 | src/Core/Gameplay/GAS/LiveSkillWorkbench/LiveAiSkillDraft.cs:41-46 |
| 诊断码 | src/Core/Gameplay/GAS/LiveSkillWorkbench/LiveEditDiagnostics.cs:14-35 |
| 主题与 11 命令 | mods/capabilities/live_skill_workbench/LiveSkillWorkbenchMod/LiveSkillWorkbenchIds.cs:5-18 |
| LatestWins | mods/capabilities/live_skill_workbench/LiveSkillWorkbenchMod/DataPlane/LiveSkillWorkbenchDataPlane.cs:52 |
| 运行时绑定与不可用清单 | mods/capabilities/live_skill_workbench/LiveSkillWorkbenchMod/Runtime/LiveSkillWorkbenchRuntime.cs:806-833 |

**相关文档**：[ed-01 PRD](../prd/ed-01-workbench-base.md) · [ed-02 reference](ed-02-hot-apply.md) · [ed-03 reference](ed-03-gap-roadmap.md)
