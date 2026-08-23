# ed-01 runtime spec · 实时技能工作台编辑基座

> 引擎实现任务书。第一性需求见 [ed-01 PRD](../prd/ed-01-workbench-base.md)；现状见 [reference](../reference/ed-01-workbench-base.md)。

## 1. 概述

工作台核心八件套合同：会话、三段流水线、四级分级、安全帧提交、保存计划、效果链追踪、立即命令执行器、AI 草稿。

## 2. 设计

- LiveEditSession 保持：TryStage 先校验后入（失败不污染补丁）、Revision 单调、Discard 清空且递增；三来源共用补丁模型。
- LiveGasEditPipeline 保持三段（Stage→Classify→Commit）与"Never Clear+Register-all"铁律；七操作分派七候选表；CommitImmediate 走 ILiveAttributeCommandSink；CommitNextCastSafeFrame 要求安全帧、按 Graph→EffectNumeric→TagRule→AttrConstraint→EffectRef→GrantedTag 固定顺序提交、失败逆序回滚。
- LiveApplyMode 四级语义保持；分类永不返回"未分类"终态（未分类=尚未预检的初态）。
- LiveEditModSaveService 保持：Preview 产计划并显式列排除的立即属性命令；Save 按计划 upsert。
- LiveEffectChainTracer 保持环形 256、七相位、溢出显式 Dropped 事件。
- LiveAttributeCommandExecutor 保持 fail-closed 四拒（未选中/实体死/无缓冲/未知属性）。
- LiveAiSkillDraft 保持结构化补丁合同；Unconfigured 生成器默认抛错。
- 诊断码 LSW0001-0021 保持稳定；DataPlane 主题+11 命令、快照 LatestWins 保持。
- **治理项（接 ed-03）**：会话撤销/重做栈未接入；文档投影源缺生产实现——处置见 ed-03 spec。

## 3. 精确语义与不变量

- 会话与注册表隔离：未提交的暂存对运行时不可见。
- 提交原子性：观察者要么见全部生效、要么见全部回滚。
- Revision 只增不减；快照发布即完整一致（LatestWins 不出半快照）。
- 溢出语义：效果链显式丢弃事件，不静默。

## 4. 迁移与治理

现状即基线；缺口处置统一入 ed-03 与 todo/runtime.md。

## 变更记录

- v1（2026-08-17）：初版。

**相关文档**：[ed-01 PRD](../prd/ed-01-workbench-base.md) · [reference](../reference/ed-01-workbench-base.md)
