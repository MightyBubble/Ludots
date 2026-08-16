# fx-00 reference · 效果执行管线总览

> 现状参考。第一性需求见 [fx-00 PRD](../prd/fx-01-pipeline.md)；配置说明见 [fx-00 配置说明](../config/fx-01-pipeline.md)。

## 1. 现状快照

- 阶段机 EffectLoopStage：ProposalAndApply → Lifetime → PostLifetimeProposalAndApply → Done；提案段子阶段循环，pass 上限见事实页；队列无后续才进 Lifetime；第三段消化存活期新请求。
- 切片双预算 UpdateSlice(dt, timeBudgetMs)：工作单元上限缺省不限，归零走 YieldIncomplete 下帧续跑；毫秒预算 ≤0 视 1，Stopwatch 逐子系统传递，超耗抛 InvalidStageConsumption；每片首根预算 NextFrame，根预算表构造期三子系统共享。
- 三子系统：EffectProposalProcessingSystem（出队→响应链窗口→Instant 内联或创建持久实体，含堆叠合并、CallerParams 预合并、监听器注册暂存）；EffectApplicationSystem（提交入容器、OnResolve/OnHit/OnApply 事务、FanOut 发布、监听器注册回放）；EffectLifetimeSystem（周期 tick 惰性首拍、OnPeriod/OnExpire/OnRemove 事务、grantedTags 回收、容器移除销毁）。
- 事务 EffectPhaseSideEffectTransaction：暂存属性双掩码、DirtyFlags、请求、命令、事件、效果状态、tag、容器、销毁、黑板、监听器、父子关系、结构命令；Commit 按固定顺序落地，失败四段回滚。

## 2. 代码锚点

| 机制 | 位置 |
|---|---|
| 阶段机定义 | src/Core/Gameplay/GAS/Systems/EffectProcessingLoopSystem.cs:22-28 |
| 子阶段循环与 pass 上限 | EffectProcessingLoopSystem.cs:164-170 |
| 切片双预算与超耗抛错 | EffectProcessingLoopSystem.cs:99-142, 224-234 |
| 根预算每片重置 | EffectProcessingLoopSystem.cs:112-120 |
| 提案内联事务 | src/Core/Gameplay/GAS/Systems/EffectProposalProcessingSystem.cs:342-349 |
| 应用事务、FanOut、监听器回放 | src/Core/Gameplay/GAS/Systems/EffectApplicationSystem.cs:403-458, 475-515 |
| 存活事务、回收与销毁 | src/Core/Gameplay/GAS/Systems/EffectLifetimeSystem.cs:390-429, 663-671 |
| 事务类定位与暂存范围 | src/Core/Gameplay/GAS/EffectPhaseSideEffectTransaction.cs:11-15, 32-131 |
| Commit 顺序与回滚 | EffectPhaseSideEffectTransaction.cs:851-997, 999-1021 |

**相关文档**：[fx-00 PRD](../prd/fx-01-pipeline.md) · [fx-01 reference](fx-02-template.md)
