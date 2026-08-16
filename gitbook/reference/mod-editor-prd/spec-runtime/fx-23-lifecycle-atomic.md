# fx-22 runtime spec · 生命周期原子操作

> 引擎实现任务书。第一性需求见 [fx-22 PRD](../prd/fx-23-lifecycle-atomic.md)；现状见 [reference](../reference/fx-23-lifecycle-atomic.md)。

## 1. 概述
部署消费合同：事务态组装、六步原子链、失败回滚边界。

## 2. 设计
- BeginLifecycleTransaction：拒绝嵌套与挂起销毁源；按 `_ep.targetEntityTemplate` 解析模板、按 DeployAtTargetPoint 解析放置点、捕获源快照，再按配置（属性键 + 取值来源）组装事务态。
- 六步 op 序列保持：MaterializeTemplate / CopyIdentityComponents / CopyAttributeSlice / ClearActiveEffects / TransferStableId / ConsumeEntity；执行器任一步失败即回滚已物化目标后上抛。
- **治理项 E15**：预设默认图含 Unsupported(Lifecycle) 的六个内建，无法通过 FinalizeAll；现有测试全部绕过 FinalizeAll 直连执行器。收口：认证 Lifecycle 原子域（六步整体作为单一外部原子操作）或提供经认证组合预设——验收 = 部署效果走完启动编译 + 运行（todo/effect.md E15）。

## 3. 精确语义与不变量
- 事务态在 Begin 时一次性组装，执行期只消费不再读配置。
- 全链原子：目标要么完整物化并接管身份，要么世界回到执行前（源不被消费）。
- 稳定 id 至多一个持有者：TransferStableId 完成前源仍持有，ConsumeEntity 后仅目标持有。

## 4. 迁移与治理
现状即基线；E15 处置见 todo/effect.md。

## 变更记录
- v1（2026-08-15）：初版。

**相关文档**：[fx-22 PRD](../prd/fx-23-lifecycle-atomic.md) · [reference](../reference/fx-23-lifecycle-atomic.md)
