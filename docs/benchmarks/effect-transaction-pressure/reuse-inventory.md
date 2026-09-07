# Effect 事务规模退化修复 —— 复用清单与通道复杂度审计

基线 commit: `81a1b5f543`。红色基线见 `docs/benchmarks/effect-transaction-pressure/baseline-before.log`。
参考补丁：`LudotsProd-raylib-audit-20260906` 未提交的 `_gameplayEffectIndex` 最小补丁
（证明 GameplayEffect 线性定位是主回退：修复前 928 ms），仅作参考，不整体照搬。

## 一、生产调用方与规模上限

`EffectPhaseSideEffectTransaction` 由 `EffectLifetimeSystem` 每帧构造一次（`snapshotCapacity`），
批内可承载整帧所有活动的持续效果实体。生产规模上限 = `EffectLifetimeSystem` snapshot capacity
（10k showcase 用 ~16k）。因此「事务内参与实体数」可到 10k+，线性扫描逐实体累计即为 O(n²)。

单实体内部小容量集合（不替换）：
- `EffectModifiers`（CAPACITY 固定、每次 fresh，无查找）
- `ChildrenBuffer.Contains`（预分配固定、逐实体访问，无跨实体累计）
- `ActiveEffectContainer`、`GameplayTagContainer`、`EffectPhaseListenerBuffer` 等组件内部
  都是单实体的有界小集合，属「单个实体内有明确小容量上限」，一律不动。

## 二、通道清单（跨实体、规模随事务参与实体数增长）

| # | 通道 | 线性扫描点 | 每实体操作 | 修复前复杂度(10k) | 处理 |
|---|------|-----------|-----------|------------------|------|
| 1 | GameplayEffect 状态 | `GetOrAddGameplayEffectEntity` / `TryGetGameplayEffectState` | 每效果每帧 | ~5e7 次比较(928ms 证据) | 预分配索引 |
| 2 | 属性(Attribute) | `GetOrAddAttributeEntity` / `FindAttributeEntity`(TryGetAttributeCurrent) | 每个属性写/读 | O(n²) | 预分配索引 |
| 3 | 脏实体(Dirty) | `StageDirtyEntity` 全量线性去重 | 每属性/标签变更 | O(n²) | 预分配索引 |
| 4 | 标签(Tag) | `GetOrAddTagEntity` / `TryHasTag` / `StageGrantedTagGrant/Revoke` | 每个标签效果 | O(n²) | 预分配索引 |
| 5 | 活动效果容器(ActiveEffect) | `GetOrAddActiveEffectEntity` / `TryGetActiveEffectContainer` | 每个到期效果 | O(n²) | 预分配索引 |
| 6 | 黑板 float/int/entity | `GetOrAddBlackboard*` / `TryReadBlackboard*` | 每个黑板写/读 | O(n²) | 预分配索引 |
| 7 | 取消(Cancelled) | `StageEffectCancellation` 内 `Contains(_cancelledEffects)` | 每个取消 | O(n²) | 预分配索引 |
| 8 | 销毁(Destroyed) | `StageEffectDestroy` 内 `Contains(_destroyedEffects)` | 每个销毁 | O(n²) | 预分配索引 |
| 9 | 聚合标记(AggregateDirty) | `StageAggregateDirty` 内 `Contains(_aggregateDirtyEntities)` | 每个聚合目标 | O(n²) | 预分配索引 |
| 10 | 监听器注册 | `PrepareListenerValues` 内 `FindEntity(_listenerEntities)` | 每个 listener 实体 | O(n²) | 预分配索引 |
| 11 | 监听器移除 | `StageListenerRemoval(Entity,ownerEffectId)` 全量线性去重 | 每个到期/移除 | O(n²) | 预分配索引(键=实体+ownerEffectId) |
| 12 | 监听器校验 | `ValidateListenerRegistrations` 内 `HasEarlierListenerEntity` / `CountListenerEntriesForEntity` 全量重扫 | 每个注册条目 | O(R²) / O(R²·S) | 消除重复全量扫描 |
| 13 | 父子关系 | `GetOrAddRelationParent/Child` 及 staged 读(`TryReadRelationWorldPosition` 等) | 每个挂接/拆边 | 通常少(非 10k 级)，但同批可累积 | 预分配索引 |

## 三、复用决策

PR 要求：优先复用仓库已有实体索引结构；用字典时预分配容量、禁止运行期隐式扩容、以完整实体身份为键。

候选评估：
- `RegionEntityIndex`(Dictionary<long,Entity>)：长期可增长表、非固定容量、不可清空复用 —— 不适用。
- `MapLoadEntityIndex`(Dictionary<string,Entity>)：字符串键、非固定容量 —— 不适用。
- `RelationshipReverseIndex`：世界级长期索引、需要事件驱动维护 —— 不是事务内暂存索引，不适用。
- `DomainRoutedCollectionWriter._domainIndexMap`(Dictionary<Entity,int>)：模式相近（Entity→row 下标），
  但内嵌私有、容量 8 起，未抽象成可复用类型。
- **结论**：仓库没有现成的「固定容量 + 完整实体身份 + 清空复用 + 稳定零分配」的事务内
  `Entity→下标` 索引类型可直接复用。采用 `Dictionary<Entity,int>`，构造时 `new(capacity)`、
  `Begin()`/`End()` 时 `Clear()`（字典容量保留、不产生运行期扩容分配），键即完整 `Entity`
  （Arch Entity 的 GetHashCode/Equals 已含 Id+WorldId+Version，身份完整）。
- 零分配约束：`Dictionary.Clear()` 不清 buckets、不缩容；预配容量 ≥ 数组容量即可保证
  事务全生命周期零扩容分配。加载后第一个事务可能有一次 bucket 分配（构造函数参数容量
  精确 -> 初始化即到位，无后续扩容）。

小容量通道保留数组线性扫描的判据（见 PR 指令）：单实体有界集合（EffectModifiers、
ChildrenBuffer 内部、各容器组件内部）不动。

## 四、保留的事务语义（修复不可破坏）

1. 提交前不可见：所有写仍走 staged 数组，Commit 前不触碰世界。
2. 失败完整回滚：Rollback 路径逐数组还原，新增索引只做定位、不回写世界。
3. 执行顺序确定：提交顺序 = 数组下标顺序；索引只承担「定位/去重」，不参与枚举。字典枚举顺序从不用于提交。
4. 容量不足明确报错：`*_Count >= *_Entities.Length` 的 CapacityExceeded 检查原样保留。
5. 实体销毁后编号复用不串数据：键为完整 Entity（含 Version），Destroy 后旧 Version 不再命中。

## 五、无变化暂存修复（EffectLifetimeSystem）

- `ProcessPeriod`/`ProcessExpiration`（或 wrapper）显式返回「是否改变了 effect 状态」。
- 仅返回 true 时 `StageGameplayEffectState`；计时初始化、周期推进、剩余时间变化等真变化分支返回 true。
- 到期条件与取消检查保留。不在本 PR 引入计时轮、不改变周期相位、不缩小事务原子范围。
- 测试：等待阶段减少暂存（WaitingPeriod allocated 保持 0 且 stage 不存在时无副作用），
  到期/取消/回滚行为不变（场景 2/3/4/5 已锁语义）。
