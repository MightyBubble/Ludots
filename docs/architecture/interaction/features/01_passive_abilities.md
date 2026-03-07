# Feature: Passive Abilities (A1–A8)

> 清单覆盖: A1 永久属性加成, A2 条件叠层触发, A3 阈值触发, A4 受击触发, A5 死亡触发, A6 光环, A7 击杀触发, A8 连击叠层被动

## 交互层

- **InputConfig**: 无 (Passive 不需要 InputBinding)
- **TargetMode**: None
- **Acquisition**: N/A

## Ludots 实现方案

### A1: 永久属性加成

**现有基础设施**: `AttributeBuffer` + `EffectModifiers`

```
实现:
  1. 在 entity spawn 时挂载一个 Duration=Permanent 的 GameplayEffect
  2. Effect 携带 EffectModifiers (Add/Multiply)
  3. AttributeAggregatorSystem 每帧聚合: Base → +Modifiers → Current
```

- **Effect 配置**: `EffectPresetType.None` + `LifetimeKind.Permanent` + `ModifierOp.Add`
- **无新代码**, 现有 EffectLifetimeSystem 已支持 Permanent 生命周期

### A2: 条件叠层触发

**现有基础设施**: `GameplayTagContainer` + `AttributeBuffer` + `EffectPhaseExecutor` (OnPeriod)

```
实现:
  每次普攻命中 → ApplyEffectTemplate(stack_counter)
    → stack_counter Effect 的 OnApply Phase Graph:
      1. LoadSelfAttribute(stack_count)
      2. AddInt(1)
      3. WriteSelfAttribute(stack_count)
      4. CompareEqInt(stack_count, threshold)
      5. if true → ApplyEffectTemplate(proc_effect)
      6. WriteSelfAttribute(stack_count, 0)  // 重置
```

- **依赖**: `GraphExecutor` + `GasGraphRuntimeApi.ModifyAttributeAdd()`
- **无新代码**, 现有 Graph ops (`LoadSelfAttribute`=330, `WriteSelfAttribute`=331) 已支持

### A3: 血量阈值触发

```
实现:
  PeriodicSearch Effect (period=每10 tick) + Phase Graph:
    1. LoadSelfAttribute(health_current)
    2. LoadSelfAttribute(health_max)
    3. DivFloat → ratio
    4. CompareGtFloat(threshold, ratio)
    5. if true → AddTag(low_health_buff)
    6. if false → RemoveTag(low_health_buff)
```

- 或者用 `AttributeConstraints` + `AttributeBindingSystem` 在属性变化时触发

### A4: 受击触发

**现有基础设施**: `ResponseChainListener` + `ResponseType.Chain`

```
实现:
  1. Entity 挂载 ResponseChainListener 监听 damage_applied EventTag
  2. ResponseType = Chain
  3. Chain 的 EffectTemplateId = return_damage_effect
  4. return_damage_effect 的 Phase Graph 读取 incoming damage → 按比例反弹
```

- **完美匹配** 现有 ResponseChain 架构
- **无新代码**

### A5: 死亡触发

```
实现:
  1. Entity 挂载 ResponseChainListener 监听 entity_death EventTag
  2. ResponseType = Chain
  3. Chain 的 Effect = resurrection_effect (ApplyModifiers: HP=1, AddTag: invulnerable)
  4. 或: GameplayEvent("on_death") → TriggerManager.FireEvent() → Mod 脚本处理
```

### A6: 光环

**现有基础设施**: `EffectPresetType.PeriodicSearch` + `BuiltinHandlers.HandleSpatialQuery`

```
实现:
  1. Entity 挂载 PeriodicSearch Effect (period=每30 tick, radius=300cm)
  2. PeriodicSearch → SpatialQuery → 找周围友方单位
  3. 对每个找到的单位 FanOutApplyEffect(aura_buff)
  4. aura_buff 的 Lifetime = 40 ticks (略大于 period, 确保不断档)
  5. SameTypePolicy = Replace (防止叠加)
```

- **无新代码**, PeriodicSearch preset 已实现

### A7: 击杀触发

```
实现:
  1. 击杀时系统发 PresentationEvent(Kind=EntityKilled, Source=killer)
  2. Killer 的 ResponseChainListener 监听 entity_killed EventTag
  3. Chain → reset_cooldown_effect 或 refund_mana_effect
  4. 或: AbilityExecSpec 的 EventGate 等待 kill_confirmed tag
```

### A8: 连击叠层被动

```
实现:
  与 A2 相同, 但 stack 来源是连击而非普攻
  每次技能命中 → OnHit Effect → stack_count Attribute++
  N 层后 → proc effect → 重置
```

## 依赖组件

| 组件 | 文件 | 状态 |
|------|------|------|
| AttributeBuffer | `src/Core/Gameplay/GAS/Components/AttributeBuffer.cs` | ✅ 已有 |
| EffectModifiers | `src/Core/Gameplay/GAS/Components/EffectModifiers.cs` | ✅ 已有 |
| ResponseChainListener | `src/Core/Gameplay/GAS/Components/ResponseChainComponents.cs` | ✅ 已有 |
| PeriodicSearch preset | `src/Core/Gameplay/GAS/PresetTypeRegistry.cs` | ✅ 已有 |
| GraphExecutor | `src/Core/NodeLibraries/GASGraph/GraphExecutor.cs` | ✅ 已有 |
| Graph ops 330/331 | `src/Core/NodeLibraries/GASGraph/GraphOps.cs` | ✅ 已有 |

## 新增需求

**无。** 全部被动能力可用现有 Effect + Tag + Attribute + Graph + ResponseChain 组合表达。
