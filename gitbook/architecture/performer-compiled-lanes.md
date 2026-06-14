# Performer 编译式执行分层

本文定义 Performer 运行时的执行模型：如何将 authoring 语义编译为分层 execution lanes，在同屏 30K 实体（10K 动态 + 20K 静态，150K performer instances）场景下维持 60FPS。

本文是 [Performer-as-Actor 架构总览](performer-as-actor-architecture.md) §9.4 的实现展开。不改变 Performer 的语义模型（PerformerDefinition / BehaviorSlot / PerformerCommand / performers.json schema 全部不变），只改变执行模型。

---

## 1 动机与问题量化

### 1.1 当前瓶颈

| 热路径 | 当前行为 | 复杂度 |
|--------|---------|--------|
| `PerformerEmitSystem.ProcessActive()` | 每帧遍历全部 active instances | O(active) |
| `PerformerBehaviorSystem` | 每帧对每个 active instance 求值全部 behavior | O(active × behaviors) |
| `PerformerInstanceBuffer.ReleaseDeadEntityAnchors()` | 每帧全 slot 扫描检查 entity 存活 | O(capacity) |
| Param resolve | 每次 emit 走 Override → Binding → Default 链，无缓存 | O(active × params) |
| Draw buffer | 8192 容量 AoS，每帧 clear + rebuild | O(visible) |

### 1.2 第一性需求

```
同屏 30,000 实体，带特效、带属性、带血条
其中 10,000 动态（移动中）+ 20,000 静态（不动）
每个实体 ~5 performer children → 150,000 performer instances，全部可见
16.6ms 帧预算（60FPS），表现层最多分配 4ms
```

关键约束：30K 全部在屏幕上，不能靠 camera culling 把工作量砍到 2K。优化必须从"每帧做什么"入手，而不是"每帧看谁"。

### 1.3 静态/动态分区分析

| 分类 | 实体数 | Performer 实例 | 每帧特征 |
|------|--------|---------------|---------|
| 静态（不动） | 20,000 | 100,000 | 位置不变、属性极少变化、VFX 稳态、血条稳态 |
| 动态（移动中） | 10,000 | 50,000 | 位置每帧变、属性偶尔变（战斗时）、VFX 可能有连续动画 |

静态实体的 performer 树在创建后几乎不变。如果每帧仍然遍历它们做 behavior eval + emit，等于白白浪费 100K 次迭代。

### 1.4 LOD 子节点分档

即使全部在屏幕上，远处实体不需要全部 5 个 children：

| 距离档 | 实体数（典型） | Children/实体 | Performer 实例 |
|--------|--------------|--------------|---------------|
| Close（< 40m） | ~2,000 | 5（mesh + VFX + 血条 + 阴影 + 细节） | 10,000 |
| Medium（< 100m） | ~8,000 | 3（mesh + 血条 + 简化 VFX） | 24,000 |
| Far（> 100m） | ~20,000 | 2（mesh + billboard 血条） | 40,000 |
| **合计** | **30,000** | | **74,000** |

LOD 分档把 150K 降到 ~74K。这里的 LOD 只控制视觉质量/子节点启用策略，不等于 camera culling，也不能把距离阈值当成可见性剔除。

### 1.5 根因

当前实现将 `PerformerDefinition` 同时用作 authoring IR 和 runtime interpreter input。每帧对每个 instance 重新解释 definition 的 behaviors/bindings/params，等价于"每帧全量解释整棵对象树"。不区分静态/动态，不区分 dirty/stable，不区分 close/far。

正确做法：
1. 静态实体的 performer 树创建后进入 frozen 状态，只在事件驱动时唤醒
2. 动态实体只更新 transform，behavior 仍然 dirty-driven
3. Draw buffer 持久化，不每帧 clear+rebuild
4. Definition 在注册时编译成 compiled lanes，运行时只执行 lane

## 2 核心原则

### 2.1 统一语义，不统一执行

同一种配置语义（BehaviorKind）可以被编译到不同执行时机。不允许默认都走 per-frame。

### 2.2 SSOT First

`performers.json` 是唯一语义真相。编译出的 runtime lanes / tables / caches 只是派生产物，不是第二套配置。

### 2.3 热路径面向数据，不面向对象树

高频路径必须按 visible set、dirty set、asset kind、lane 分批处理，SoA、零分配。

### 2.4 行为语义和执行时机解耦

`BehaviorKind` 只表达"是什么"（AttributeBinding / TagBinding / Animator / ...），runtime lane 决定"何时算"（on-dirty / on-visible / continuous-tick / event-driven）。

## 3 运行时分层模型

```
┌──────────────────────────────────────────────────────┐
│  Layer 1: Semantic / Authoring                        │  加载时一次
│  PerformerDefinition, children, rules, behaviors,     │
│  bindings, paramDefaults                              │
├──────────────────────────────────────────────────────┤
│  Layer 2: Compiled Binding Table                      │  注册时一次
│  CompiledBinding[], pre-resolved attribute/tag IDs,   │
│  threshold tables, material swap tables               │
├──────────────────────────────────────────────────────┤
│  Layer 3: Runtime State                               │  命令驱动
│  PerformerInstance: identity, owner, scope,            │
│  parent-child, BehaviorActiveMask,                     │
│  OwnerCullVisible, cached transform, blackboard       │
├──────────────────────────────────────────────────────┤
│  Layer 4: Execution Lanes                             │  分频执行
│  Lifecycle lane, Dirty sync lane,                     │
│  Continuous tick lane, Visible projection lane        │
├──────────────────────────────────────────────────────┤
│  Layer 5: Frame Projection                            │  仅 visible
│  StableDrawCache → PrimitiveDrawBuffer                │
│  只投影 stable runtime state，不重新求语义             │
└──────────────────────────────────────────────────────┘
```

### 3.1 Layer 1 → Layer 2：编译

`PerformerDefinitionConfigLoader` 在注册 definition 时，同步编译 `CompiledBindingTable`：

```csharp
struct CompiledBinding
{
    public int SourceAttributeId;   // -1 = not attribute-bound
    public int SourceTagId;         // -1 = not tag-bound
    public int TargetParamKey;
    public ValueSourceKind Mode;    // Attribute / AttributeRatio / AttributeBase
    public ThresholdMapping[] Thresholds; // pre-sorted, pre-validated
}
```

运行时 `PerformerBehaviorSystem` 直接读 compiled table，不走 `ResolveParam()` 链。Graph VM 执行只在 effect apply 时触发（已经是这样），不在 behavior eval 时。

### 3.2 Presence vs Active：两层位图

一个 performer "拥有哪些 behavior" 和 "哪些 behavior 当前激活" 是两个独立概念：

| 位图 | 归属 | 确定时机 | 语义 |
|------|------|---------|------|
| `BehaviorPresenceMask : uint` | `PerformerDefinition` | 注册时编译 | bit=1 表示该 slot 有 behavior，编译期不可变 |
| `BehaviorActiveMask : uint` | `PerformerInstance` | 运行时命令驱动 | bit=1 表示该 behavior 当前激活，始终是 PresenceMask 的子集 |

GAS 的 entity-level dirty 信号（`GameplayTagEffectiveChangedBits` + `DirtyFlags.AttributeDirty`）已经是变化检测的 SSOT，performer 层不需要自建 DirtyMask。

`BehaviorPresenceMask` 在 `PerformerDefinitionConfigLoader` 注册时从 `Behaviors[]` 编译：

```csharp
uint presenceMask = 0;
foreach (var b in definition.Behaviors)
    presenceMask |= (1u << b.SlotIndex);
definition.BehaviorPresenceMask = presenceMask;
```

运行时约束：
- `ActivateBehavior(slot)` 只允许 `(PresenceMask >> slot) & 1 == 1`，否则忽略
- 创建 instance 时，`BehaviorActiveMask` 初始化为 `PresenceMask` 中 `ActiveByDefault=true` 的 slot 子集

### 3.3 Layer 3：Runtime State 最小化

`PerformerInstance` 只保留运行时必需状态：

| 字段 | 用途 | 写入时机 |
|------|------|---------|
| DefId, StableId, Owner | 身份 | 创建时 |
| ScopeId, ParentHandle, FirstChildHandle, NextSiblingHandle | 树结构 | 创建/销毁时 |
| BehaviorActiveMask | 行为激活位图（PresenceMask 子集） | ActivateBehavior/DeactivateBehavior 命令 |
| OwnerCullVisible | 可见性 | CameraCullingSystem 同步 |
| WorldPosition, WorldRotation, WorldScale | 缓存变换 | transform dirty 时 |
| Elapsed | 生命周期 | 每帧（仅 transient performer） |

Dirty 检测不在 performer instance 上——直接读 owner entity 的 GAS 组件（`GameplayTagEffectiveChangedBits` + `DirtyFlags`）。

不负责保存每帧最终渲染产物——那是 Layer 5 StableDrawCache 的职责。

### 3.3 Layer 4：Execution Lanes

| Lane | 触发条件 | 处理范围 | 包含的 BehaviorKind |
|------|---------|---------|-------------------|
| Lifecycle | CreatePerformer / DestroyPerformer 命令 | 命令数量 | 树管理、scope 管理 |
| Dirty Sync | owner entity 的 GAS dirty 信号非零 | dirty entities only | AttributeBinding, TagBinding, Material |
| Continuous Tick | 每帧 | active animator candidates | Animator（仅 active + visible） |
| Visible Projection | 每帧 | visible instances | AssetBinding emit → draw buffer |

关键：Dirty Sync lane 直接读 entity 的 `GameplayTagEffectiveChangedBits` 和 `DirtyFlags.AttributeDirty`，不在 performer 层重复建设 dirty 信号。一个 instance 可以 visible 但 owner not dirty（stable emit），也可以 owner dirty 但 not visible（更新 state 但不 emit）。

## 4 行为执行分类

### 4.1 创建时一次性操作（不是 behavior）

以下操作在 `PerformerRuntimeSystem.HandleCreatePerformer` 中作为创建流程的一部分直接执行，不建模为 behavior：

| 操作 | 执行时机 | 说明 |
|------|---------|------|
| Grounding 初始计算 | 创建时 | 根据 GroundingMode 计算初始贴地位置。后续只在 owner transform dirty 时由 runtime system 重算，不是 behavior |
| Transform 初始设置 | 创建时 | 根据 TransformSource + LocalOffset/LocalRotation/LocalScale 计算初始世界变换 |
| Blackboard 默认值 | 创建时 | 从 definition.ParamDefaults 写入 blackboard |
| Children 展开 | 创建时 | PerformerCreated 事件触发 Rule → CreatePerformer 子节点 |

Transform 后续更新也不是 behavior，而是 runtime system 的 dirty sync 职责：

| TransformSource | 更新触发 | 处理者 |
|----------------|---------|--------|
| WorldFixed | 不更新 | — |
| EntityTransform | owner VisualTransform dirty | PerformerRuntimeSystem dirty sync |
| InheritParent | parent WorldPosition dirty | PerformerRuntimeSystem dirty sync（级联） |
| BoneAttached | bone pose dirty | PerformerRuntimeSystem dirty sync |
| SplineDriven | 每帧（仅 active + visible） | Continuous tick lane |

### 4.2 真正的 Behavior（运行时持续响应变化）

| BehaviorKind | 语义 | 执行 Lane | 触发条件 |
|-------------|------|----------|---------|
| **AttributeBinding** | 属性→param + 阈值映射 | Dirty Sync | `AttributeBuffer` 版本号变化 |
| **TagBinding** | tag→param | Dirty Sync | `GameplayTagEffectiveChangedBits` 非零 |
| **Material** | param→materialId 查表 | Dirty Sync | param 变化（SetParam / binding 写入） |
| **Animator** | 动画状态推进 | Continuous Tick | 每帧（仅 active + visible candidate） |
| **Sound** | 声音请求 | Event-Driven | ActivateBehavior / DeactivateBehavior / scope 销毁 |
| **AssetBinding** | 投影为 mesh/vfx/hud/text/spline/decal | Visible Projection | 每帧（仅 visible），dirty 时重算，stable 时 memcpy |

核心区分：

- **每帧投影**（AssetBinding emit）可以保留——但只处理 visible candidate
- **每帧重新求语义**（attribute resolve、tag check、param chain）必须拆掉——改为 dirty-driven
- presentation buffer 每帧 clear/rebuild，不等于 grounding / transform / binding / attachment 都要每帧重算
- Grounding 和 Transform 是创建时操作 + dirty sync，不是 behavior

## 5 面向 30K 同屏的迭代路线

### 迭代 1：Persistent Draw Buffer + Static Freeze（最大收益）

当前 draw buffer 每帧 clear + rebuild，150K 实例全部重写。这是 30K 同屏场景的第一瓶颈。

改动：
- Draw buffer 改为持久化，不每帧 clear
- 新增 `StableDrawCache`：SoA 结构，按 `StableId` 索引
- 静态实体的 performer 实例创建时写入 cache + draw buffer，之后不再每帧触碰
- 动态实体每帧只更新 position lane（12B/instance），其余字段从 cache 读
- Dirty 实例（属性/tag 变化）全量重算并更新 cache + draw buffer

```
20K 静态 × 5 children = 100K instances → 创建时写入，之后 0 cost/帧
10K 动态 × 5 children = 50K instances → 每帧只更新 position
稳态 dirty ~500 instances → 全量重算

帧开销：50K × 12B position write = 600KB → ~0.3ms
        500 × full re-eval → ~0.1ms
        总计 ~0.4ms（vs 当前 >50ms）
```

### 迭代 2：Dirty-Driven Behavior（消除无变化重算）

GAS 基建已有完整的 entity-level 变化信号：

| GAS 信号 | 组件 | 粒度 |
|----------|------|------|
| Tag 变化 | `GameplayTagEffectiveChangedBits`（256-bit） | per-entity per-frame |
| Attribute 变化 | `DirtyFlags.AttributeDirty[64]` | per-entity per-attribute |
| Effect 应用 | `GasPresentationEventBuffer` | per-event |

改动：
- `PerformerBehaviorSystem` 直接读 owner entity 的 GAS dirty 信号，不自建 DirtyMask
- TagBinding：查 `GameplayTagEffectiveChangedBits` 对应 bit
- AttributeBinding：查 `DirtyFlags.IsAttributeDirty(attrId)`
- Material/Sound：由 SetParam 命令驱动（命令本身就是事件）
- 只有 dirty 的 behavior 才重新求值

```
收益：稳态下 behavior eval ≈ 0
      战斗中 ~500 entity 受伤/帧 → 500 × 5 children × ~3 binding = 7500 eval → ~0.2ms
      零新增基建，复用 GAS 已有 dirty 信号
```

### 迭代 3：LOD Children Pruning（减少实例总量）

改动：
- `PerformerRuntimeSystem` 读 entity 的 `CullState.LOD`，按距离档控制 children 激活数量
- Close（< 40m）：全部 5 children
- Medium（< 100m）：3 children（mesh + 血条 + 简化 VFX）
- Far（> 100m）：2 children（mesh + billboard 血条）
- 通过 `ActivateBehavior` / `DeactivateBehavior` 控制，不销毁/重建 instance

```
收益：150K instances → ~74K active instances
      动态实例从 50K → ~30K（大部分动态实体在 medium/far 档）
      position write 从 600KB → ~360KB
```

### 迭代 4：Culling Gate（屏幕边缘优化）

虽然 30K 全部"在场景中"，但相机视锥仍然有边界。`CameraCullingSystem` 已在 entity 上写 `CullState.IsVisible`。
`CullState.LOD` 只写 High/Medium/Low 质量层；可见性只看 `CullState.IsVisible` / `PerformerCullState.OwnerCullVisible`。全局阈值显式配置在 `presentation.cameraCulling`，由 `GameConfig.Presentation.CameraCulling` 提供给各 host。

改动：
- `PerformerInstance` 新增 `bool OwnerCullVisible`
- `ProcessActive()` 跳过 `!OwnerCullVisible` 的实例
- 子 performer 继承父的 cull 状态

```
收益：如果实际视锥覆盖 ~80% 场景 → 74K × 0.8 = ~59K active
      边际收益，但实现成本极低
```

### 迭代 5：SoA Instance Buffer + Compiled Binding Table

改动：
- `PerformerInstanceBuffer` 内部从 AoS 拆为 SoA lanes
- `PerformerDefinitionConfigLoader` 注册时编译 `CompiledBindingTable`
- 运行时不走 `ResolveParam()` 的 override → binding → default 链

```
收益：遍历判断开销 AoS 9.6MB → SoA 450KB（cache 效率 ~20x）
      dirty instance 的 binding eval 从 ~10 resolve → ~3 direct read
```

### 迭代 6：LOD-Aware Behavior Ticking

改动：
- `PerformerBehaviorSystem` 按 LOD 分档执行：

| LOD | Behavior 策略 |
|-----|-------------|
| Close（< 40m） | 全部 behavior 每帧 |
| Medium（< 100m） | AttributeBinding + TagBinding 每帧；Animator 降到 15fps；Sound 降到 5fps |
| Far（> 100m） | 只保留 AssetBinding emit，禁用 Animator/Sound/Material |

```
收益：Medium/Far 档位的 behavior eval 进一步减少 50-80%
```

## 6 性能预算表

30K 同屏（10K 动态 + 20K 静态），每实体 ~5 children：

| 阶段 | 无优化 | +迭代 1 (persistent + freeze) | +迭代 2 (dirty) | +迭代 3 (LOD prune) | +迭代 5 (SoA + compiled) |
|------|--------|------------------------------|----------------|--------------------|-----------------------|
| 静态 behavior eval | 100k/帧 | 0/帧 | 0/帧 | 0/帧 | 0/帧 |
| 动态 behavior eval | 50k/帧 | 50k/帧 | ~500 dirty/帧 | ~500/帧 | ~500/帧 |
| 静态 emit | 100k rebuild/帧 | 0（persistent buffer） | 0 | 0 | 0 |
| 动态 emit | 50k rebuild/帧 | 50k position write | 50k position write | ~30k position write | ~30k position write |
| 总 instance 遍历 | 150k × 64B | 150k × 1B (static flag) | same | 74k × 1B | 74k × 3B SoA |
| **预估帧时间** | **>50ms** | **~2ms** | **~0.5ms** | **~0.4ms** | **~0.3ms** |

迭代 1 是决定性的：persistent draw buffer + static freeze 把 100K 静态实例的每帧开销降到零，把 50K 动态实例的开销从全量 rebuild 降到 position-only write。迭代 1 单独就能达到 60FPS。后续迭代是面向战斗高峰（大量 dirty）和更大规模的余量。

## 7 Emit 的定义纠偏

当前 `PerformerEmitSystem` 的 "emit" 实际承担了两个职责：

1. **语义求值**：resolve param → evaluate binding → check threshold → determine asset
2. **帧投影**：把求值结果写入 draw buffer

这两个职责必须分离：

- **语义求值** → 移入 `PerformerBehaviorSystem`，dirty-driven
- **帧投影** → 保留在 `PerformerEmitSystem`，visible-driven

未来的 Emit 应该：
- 只处理需要投影的 visible candidate
- 读取已计算好的 transform / params / activation state
- 不再承担全量行为求值、全量绑定求值、全量树遍历职责
- Dirty instance 重算 proxy 并更新 StableDrawCache
- Stable instance 从 cache 批量复制

## 8 Arch ECS 层面优化

基于项目已有的 Arch 用法模式（chunk iteration + `GetSpan<T>()` + `InlineQuery` + `CommandBuffer`）：

### 8.1 Cull Sync 批量化

```csharp
// 用 chunk iteration 批量同步 CullState → performer buffer
foreach (ref var chunk in World.Query(in _cullSyncQuery))
{
    var cullSpan = chunk.GetSpan<CullState>();
    var stableIdSpan = chunk.GetSpan<PresentationStableId>();
    foreach (var i in chunk)
    {
        _instances.SetCullVisible(stableIdSpan[i].Handle, cullSpan[i].IsVisible);
    }
}
```

### 8.2 Dead Entity Cleanup 批量化

```csharp
// 替代当前的逐 slot 全扫描
// 只检查 owner entity 是否存活，用 World.IsAlive() 批量判断
for (int i = 0; i < _highWaterMark; i++)
{
    if (_active[i] && !World.IsAlive(_ownerEntity[i]))
        _pendingRelease.Add(i);
}
```

### 8.3 Draw Buffer 批量写入

```csharp
// 用 Span 批量写入替代逐项 TryAdd
var dst = drawBuffer.GetWriteSpan(count);
stableDrawCache.CopyVisibleTo(dst, visibleHandles);
```

### 8.4 Wave 7 核心目标：PerformerInstance 迁移为 Entity-Backed Runtime

当前 `PerformerInstanceBuffer` 本质上是手写 mini-ECS：AoS slot buffer + free-list + 全扫描 `ProcessActive()`。这与项目已有的 Arch 热路径风格不一致。Wave 7 的核心探索目标是将 PerformerInstance 的运行时承载从手写 buffer 迁移为 Arch entity-backed runtime。

**关键边界：语义层 ≠ 运行时层**

- `PerformerDefinition`（语义 SSOT）不是 ECS entity，仍是 authoring IR
- `PerformerInstance`（运行时状态）迁移为 Arch entity
- Compiled definition tables、blackboard、StableDrawCache 仍可保持外部专用存储，不强行全组件化

**精简组件模型**

不把 definition config 膨胀复制到 component 里。Entity 上只放运行时状态和轻量索引：

```csharp
// ── 核心身份（所有 performer entity 都有）──
struct PerformerState
{
    int DefId;              // 索引 compiled definition table
    int StableId;
    int ScopeId;
    Entity OwnerEntity;
    Entity ParentPerformer; // Entity.Null = root
    uint BehaviorActiveMask;
    float Elapsed;
}

// ── 运行时缓存 ──
struct PerformerTransformCache
{
    Vector3 WorldPosition;
    Quaternion WorldRotation;
    Vector3 WorldScale;
}

// ── 纯运行时过滤标记 ──
struct PerformerVisible {}   // 由 cull sync 添加/移除
struct PerformerLodState { LODLevel Level; }

// ── Behavior 存在性 marker（用于 query 过滤）──
struct PerfHasAssetBinding {}
struct PerfHasAttributeBinding {}
struct PerfHasTagBinding {}
struct PerfHasAnimator {}
struct PerfHasMaterial {}
struct PerfHasSound {}
struct PerfHasSpline {}
```

**数据职责分离**

| 数据 | 存储位置 | 原因 |
|------|---------|------|
| 运行时 identity / flags / transform | Entity component | 需要 query/filter/chunk iteration |
| Behavior 存在性 | Marker component（零大小） | 需要 archetype query 过滤 |
| AttributeBindingConfig / ThresholdMapping[] | Compiled definition table（按 DefId 索引） | 静态数据，SSOT，不复制 |
| MaterialSwapTable / SplineConfig | Compiled definition table | 同上 |
| Blackboard 三 lane + parent 继承 | 外部 PerformerParamBlackboard（按 handle 索引） | 树形继承不适合 flat component |
| StableDrawCache | 外部 SoA buffer | 帧投影专用，不属于 entity 状态 |

**收益**

- 消除手写 mini-ECS（`PerformerInstanceBuffer` 的 slot/free-list/全扫描）
- `WithAll<PerformerState, PerfHasAttributeBinding, PerformerVisible>()` → 只遍历 visible + 有 attribute binding 的 performer，chunk iteration，天然 SoA
- Cull sync 变成 add/remove `PerformerVisible` marker，query 自动过滤
- 不复制 authoring config，不破坏 blackboard 继承，不把语义层和运行时层搅成一锅

**仍需解决的问题**

- 树形关系（parent/child/sibling）存为 Entity 引用，递归操作仍需手动遍历
- Scope 销毁需要 query `ScopeId == X` 的 performer entity
- `PerformerVisible` marker 的 add/remove 是 structural change，需要评估 cull 变化频率下的开销（可能改用 `bool` 字段替代 marker 以避免 archetype churn）
- Blackboard 的 handle 从 slot index 改为 entity-based 索引方案
- 创建/销毁走 `CommandBuffer`，需要确认 deferred playback 与 presentation phase 的时序

**定位**

这是 Wave 7 的核心探索与迁移目标，不是已定论的架构公理。迭代 1-3（culling gate、dirty-driven、stable cache）可以先在现有 buffer 上实现，迭代 4（SoA）自然过渡到 entity-backed runtime。

**实现状态（2026-04-20）**：Entity-backed runtime 已实现。`PerformerInstanceBuffer` / `PerformerInstance` / `PerformerParamBlackboard` 已删除，替换为 Arch entity 组件：`PerformerState`、`PerformerWorldPosition`/`Rotation`/`Scale`、`PerformerTransformSource`、`PerformerParent`、`PerformerChildren`、`PerformerFloatParams`/`IntParams`/`VectorParams`（含 Defaults 变体）、`PerformerCullState`、`PerformerEmitCache`、Behavior marker 组件。`PerformerEntityRuntime` 提供等价 API。Blackboard 父→子继承链通过 `PerformerParamResolver` + `PerformerParent` entity 引用实现。

## 9 与现有架构文档的关系

| 文档 | 关系 |
|------|------|
| [performer-as-actor-architecture.md](performer-as-actor-architecture.md) | 本文是 §9.4 的实现展开 |
| [performer-param-blackboard.md](performer-param-blackboard.md) | Blackboard 语义不变，compiled binding 是其执行优化 |
| [performer-transform-and-attachment.md](performer-transform-and-attachment.md) | Transform 计算改为 dirty-driven，语义不变 |
| [performer-raylib-uat.md](performer-raylib-uat.md) | UAT 测试不变，性能迭代是透明优化 |
| [performer-development-kanban.md](performer-development-kanban.md) | 新增 Wave 7 性能迭代任务 |
| [entity-simulation-layering.md](entity-simulation-layering.md) | LOD 分档与 entity 仿真车道协调 |

## 10 一句话总结

Performer 统一创作语义（SSOT），然后被编译回高性能 ECS 热路径；不能为了统一配置，把表现系统退化成每帧解释整棵对象树的慢路径。
