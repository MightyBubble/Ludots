# Gap Analysis & Modification Proposals

> 对照 163 条用户体验清单, 分析 Ludots 现有架构的缺口, 按优先级提出修改建议。

---

## 1. 缺口总览

### 1.1 交互层缺口

| 缺口 | 影响清单项 | 严重度 |
|------|----------|--------|
| **ContextScored Acquisition 不存在** | N1-N8 (动作游戏核心) | **P0** |
| ~~AbilityActivationRequireTags 不存在~~ | ~~G1-G10, H6, Q1-Q5~~ | ✅ **已有** — `AbilityActivationBlockTags.RequiredAll` |
| ~~Ability-level Attribute precondition 不存在~~ | ~~G8, Q1, Q6, Q7, T1-T8~~ | ✅ **Tag 表达** — 见下方说明 |

### 1.2 Ability 结构缺口

| 缺口 | 影响 | 严重度 |
|------|------|--------|
| **Form-based ability routing** (slot+tag→ability) | J2-J5 变身/姿态 | **P1** |
| **ContextGroup → ability dispatch** | N1-N8, G1, G3, H3, H8 | **P0** |
| ~~AbilityDefinition 缺少 cost/resource check~~ | ~~T1-T8~~ | ✅ **Tag 表达** — 资源足够→AddTag, 不够→RemoveTag, RequiredAll/BlockedAny 门控 |

### 1.3 Effect/Handler 缺口

| 缺口 | 影响 | 严重度 |
|------|------|--------|
| Projectile 穿透模式 (hitMode) | E2 | P1 |
| Projectile 回旋/反转 | E6 | P1 |
| Projectile unit-seeking (homing) | E7 | P2 |
| Projectile arc trajectory | E5 | P2 |
| Teleport handler (instant position set) | B4, I3, I8 | P1 |
| Displacement 碰撞回调 | I11, U2 | P2 |
| ResponseType.Modify 支持修改 target | O3 | P1 |
| Position history ring buffer | B5 | P2 |
| Batch tag/effect removal Graph op | B7 | P2 |
| Tether 组件 (距离监测+断裂) | C9, M6 | P1 |
| Cooldown auto-decrement system | T2 | P1 |
| Charge refill timer | T3 | P1 |

### 1.4 Input 层缺口

| 缺口 | 影响 | 严重度 |
|------|------|--------|
| DoubleTap trigger type | S2 | P2 |
| Cursor direction continuous write | F1, M2 (蓄力/引导中瞄准) | P1 |
| Minimap click → world position | D4, S6 | P3 |
| Input focus routing (切换控制对象) | K7, R1 | P2 |

### 1.5 Performer/UI 缺口

| 缺口 | 影响 | 严重度 |
|------|------|--------|
| Beam renderer performer | M5-M7 | P2 |
| Response chain rich UI (非 debug overlay) | O1-P7 | P2 |
| GroundOverlay Ring shape | D2 | P2 |
| AoE indicator during aiming | 所有 AimCast 技能 | P1 |

---

## 2. P0 修改方案

### 2.1 AbilityDefinition 扩展: ContextGroup (唯一 P0 新增)

**现有 AbilityDefinition 结构**:
```csharp
public struct AbilityDefinition
{
    public AbilityExecSpec ExecSpec;
    public AbilityExecCallerParamsPool CallerParams;
    public AbilityOnActivateEffects OnActivateEffects;
    public AbilityActivationBlockTags BlockTags;  // ← 已包含 RequiredAll + BlockedAny
    public AbilityToggleSpec ToggleSpec;
    public AbilityIndicatorConfig IndicatorConfig;
}

// 现有的 AbilityActivationBlockTags 已覆盖全部前置条件:
public unsafe struct AbilityActivationBlockTags
{
    public GameplayTagContainer RequiredAll;   // ✅ 连击/处决/变身前置
    public GameplayTagContainer BlockedAny;    // ✅ 冷却/沉默/控制阻止
}
// 资源门控也通过 Tag 表达:
//   蓝量: 周期 Effect 检查 mana >= cost → AddTag("mana_ready_Q") → RequiredAll
//   冷却: OnCast → AddTag("cd_Q", duration=N) → BlockedAny
//   充能: 检查 charges > 0 → AddTag("has_charge_Q") → RequiredAll
//   怒气: 检查 rage >= threshold → AddTag("rage_ready") → RequiredAll
```

> **RequireTags ✅ 已有, CostCheck ✅ Tag 表达, 无需新增 Ability 前置结构。**
> **唯一需要新增的是 ContextGroup 路由字段。**

**提议新增字段** (仅 ContextGroup 相关):
```csharp
public struct AbilityDefinition
{
    // ... 全部现有字段不变 ...

    // P0: ContextGroup 路由 (仅动作游戏需要)
    public int ContextGroupId;                            // 0=none, >0=属于某个 ContextGroup
    public int ContextPreconditionGraphId;                // 准入条件 Graph
    public int ContextScoreGraphId;                       // 评分 Graph (F[0]=score)
}
```

**AbilityActivationCostCheck**: 不需要。通过 Tag 表达:
```
冷却: OnCast → AddTag("cd_Q", duration=600) → BlockedAny 含 "cd_Q"
蓝量: 周期 Effect → mana >= 80 ? AddTag("mana_ok_Q") : RemoveTag → RequiredAll 含 "mana_ok_Q"
       OnCast → Effect: ModifyAttribute(mana, -80)
充能: 检查 charges > 0 → AddTag("has_charge") → RequiredAll
怒气: 检查 rage >= 50 → AddTag("rage_ready") → RequiredAll
```

### 2.2 ContextGroup 机制

**拟新增类型**: `ContextGroupRegistry`（GAS/Input 命名空间；当前仓库尚未实现，文件路径待落地时确定）

```csharp
public struct ContextCandidate
{
    public int AbilityId;
    public int PreconditionGraphId;  // B[0]=valid
    public int ScoreGraphId;         // F[0]=score
    public int BasePriority;
}

public unsafe struct ContextGroup
{
    public const int MAX_CANDIDATES = 16;
    public int Count;
    // SoA layout:
    public fixed int AbilityIds[MAX_CANDIDATES];
    public fixed int PreconditionGraphIds[MAX_CANDIDATES];
    public fixed int ScoreGraphIds[MAX_CANDIDATES];
    public fixed int BasePriorities[MAX_CANDIDATES];
}

public class ContextGroupRegistry
{
    private readonly ContextGroup[] _groups = new ContextGroup[256];
    public ref ContextGroup Get(int groupId) => ref _groups[groupId];
    public void Register(int groupId, in ContextGroup group) { ... }
}
```

**拟新增类型**: `ContextScoredResolver`（Input/Orders 命名空间；当前仓库尚未实现，文件路径待落地时确定）

```csharp
public static class ContextScoredResolver
{
    public static bool TryResolve(
        in ContextGroup group,
        Entity caster,
        GraphExecutor executor,
        IGraphRuntimeApi api,
        ISpatialQueryService spatial,
        float searchRadius,
        out int bestAbilityId,
        out Entity bestTarget)
    {
        // 1. SpatialQuery 收集候选目标
        // 2. 遍历 candidates × targets
        // 3. 执行 precondition graph → filter
        // 4. 执行 score graph → rank
        // 5. 返回最高分 (ability, target)
    }
}
```

### 2.3 InteractionModeType 扩展

```csharp
public enum InteractionModeType
{
    TargetFirst = 0,
    SmartCast = 1,
    AimCast = 2,
    SmartCastWithIndicator = 3,
    ContextScored = 4              // 新增
}
```

在 `InputOrderMappingSystem` 新增:
```csharp
case InteractionModeType.ContextScored:
    HandleContextScored(mapping, casterEntity);
    break;
```

### 2.4 AbilityExecSystem: 无需修改

现有 Phase 1 (Order → Ability Activation) 已检查 `RequiredAll` + `BlockedAny`:
```csharp
// ✅ 已有: RequiredAll — combo_stage, posture_broken, form, mana_ready, has_charge 等
// ✅ 已有: BlockedAny — cooldown, stunned, silenced 等
// 资源扣除 → OnActivate Effect (ModifyAttribute)
// 无需新增检查逻辑
```

唯一新增: ContextScored 路由在 `InputOrderMappingSystem` 层处理,
`AbilityExecSystem` 收到的 Order 已经是确定的 (ability, target) 对。

---

## 3. P1 修改方案

### 3.1 Projectile 扩展

**ProjectileConfig 新增字段**:
```csharp
public struct ProjectileConfig
{
    // ... 现有 ...
    public ProjectileHitMode HitMode;  // FirstEnemy, Penetrate, Homing
    public ProjectileTrajectory Trajectory;  // Linear, Arc, Seeking
    public int MaxBounces;  // 弹射次数
    public int OnMaxRangeEffect;  // 到达最大距离时的效果 (reverse, split, explode)
    public float ArcHeight;  // 弧线高度
    public Entity SeekTarget;  // homing 目标
}

public enum ProjectileHitMode { FirstEnemy, Penetrate, PenetrateN }
public enum ProjectileTrajectory { Linear, Arc, Seeking }
public enum ProjectileEndBehavior { Destroy, Reverse, Split }
```

### 3.2 Teleport Handler

**新增 BuiltinHandlerId**: `Teleport = 50`

```csharp
BuiltinHandlers.HandleTeleport:
  读取 EffectContext.Target position (或 ConfigParams position)
  直接设置 entity position = target position
  无 displacement, 即时生效
```

### 3.3 Tether Effect 支持

通过 PeriodicSearch effect + distance check Graph 实现, 不需新增 preset:
```
PeriodicSearch (period=3 ticks):
  Phase Graph:
    distance = CalcDistance(E[0], stored_target)
    if distance > break_range → SendEvent("tether_break") → remove effect
```

### 3.4 Cooldown System

**选项 A** (推荐): 在 `AttributeAggregatorSystem` 后新增 `CooldownTickSystem`:
```csharp
public class CooldownTickSystem : BaseSystem
{
    // 每 tick: 对所有有 cooldown attribute 的 entity
    // cooldown[slot] = max(0, cooldown[slot] - 1)
}
```

**选项 B**: 用现有 Periodic effect tick (但每个技能需一个 effect, 开销大)

### 3.5 Form-Based Ability Routing

在 `AbilityStateBuffer` 中:
```csharp
// 现有: slot → abilityId 固定映射
// 新增: slot + formTagId → abilityId

public struct FormAbilityMapping
{
    public int FormTagId;    // 0=default
    public int AbilityId;
}

// AbilityStateBuffer 查找时:
// 1. 检查 entity 当前 form tag
// 2. 查找 (slot, formTag) → abilityId
// 3. fallback 到 (slot, 0) → default abilityId
```

### 3.6 Cursor Direction Continuous Write

在 `InputOrderMappingSystem.Update()` 中, 当 `IsAiming` 或 `HasTag("charging")`:
```csharp
// 每帧写入 cursor direction 到 caster 的 Blackboard
var cursorDir = CalculateDirection(casterPos, cursorWorldPos);
blackboard.WriteFloat(CURSOR_DIR_X, cursorDir.X);
blackboard.WriteFloat(CURSOR_DIR_Y, cursorDir.Y);
```

---

## 4. P2/P3 修改方案 (低优先级)

| 方案 | 优先级 | 说明 |
|------|--------|------|
| Projectile arc trajectory | P2 | 抛物线公式加入 ProjectileRuntimeSystem |
| Projectile wall-reflect | P3 | 碰撞检测 + 反射角 |
| Displacement 碰撞回调 | P2 | DisplacementRuntimeSystem OnCollision |
| Position history component | P2 | Ring buffer, B5 回溯用 |
| Batch tag removal op | P2 | Graph op: RemoveTagsMatching |
| DoubleTap trigger | P2 | 或用 Tag 模拟 |
| Minimap click | P3 | Adapter 层 |
| Input focus routing | P2 | 控制对象切换 |
| Beam performer | P2 | 新 PerformerVisualKind |
| Ring GroundOverlay | P2 | PerformerEmitSystem 扩展 |
| Rich response chain UI | P2 | ReactivePage 组件 |

---

## 5. 实施优先级汇总

### Phase 1 (P0 — 解锁动作游戏核心, 仅需 ContextGroup)

```
1. ContextGroup + Registry          → 新文件
2. ContextScoredResolver            → 新文件
3. InteractionModeType.ContextScored → InputOrderMappingSystem
   (RequireTags ✅ 已有, CostCheck ✅ Tag 表达, 均无需新增)
```

### Phase 2 (P1 — 解锁完整技能多样性)

```
6. Projectile hitMode/trajectory    → ProjectileRuntimeSystem
7. Teleport handler                 → BuiltinHandlers
8. Tether distance check            → Graph pattern (无新代码)
9. Cooldown tick system             → 新 system
10. Form-based ability routing      → AbilityStateBuffer
11. Cursor direction continuous     → InputOrderMappingSystem
12. ResponseType.Modify target      → EffectProposalProcessingSystem
13. AoE indicator during aiming     → Performer + InputOrderMappingSystem
```

### Phase 3 (P2/P3 — 完善边缘场景)

```
14-25. 各种低优先级扩展 (见 P2/P3 表)
```

---

## 6. 关键约束

1. **零 GC**: 所有新增结构必须是 `unsafe struct` + 固定数组, 遵循现有 SoA 模式
2. **配置驱动**: ContextGroup, RequireTags, CostCheck 全部通过 JSON 配置, 不硬编码
3. **Graph 可扩展**: 复杂 precondition/scoring 通过 Graph program 表达, 不增加枚举
4. **向后兼容**: 新增字段默认值 = 0/empty, 现有 ability 不受影响
5. **Presenter 分离**: 交互层/GAS 层不依赖 Performer/UI, 只通过 PresentationEvent 通信
