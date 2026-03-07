# Feature: Placement / Summon (K1–K8), Mark / Detonate (L1–L6), Channel (M1–M7)

> 合并三个较小 feature 为一个文档

---

## K: Placement / Summon (K1–K8)

### 交互层
- **InputConfig**: Down
- **TargetMode**: Point (K1-K6, K8) 或 None (K7)
- **Acquisition**: Explicit

### 实现方案

**K1-K2: 陷阱/炮塔放置**
```
AbilityExecSpec:
  Item[0]: EffectSignal → create_trap
    → EffectPresetType: CreateUnit
    → BuiltinHandler: CreateUnit
    → UnitCreationDescriptor:
      templateId: "trap_mine"
      position: order_point
      ownerId: caster
      lifetime: 600 ticks (或 permanent until triggered)
```
- 已有: `CreateUnit` handler

**K3-K4: 召唤单位**
```
同 K1, 但 UnitCreationDescriptor:
  templateId: "bear_summon" (可控) 或 "treant" (AI)
  controllable: true/false
  aiGraphId: (如有AI)
```

**K5: 双端传送门**
```
AbilityExecSpec:
  Item[0]: SelectionGate → 选第一个点 (入口)
  Item[1]: EffectSignal → CreateUnit(portal_entrance, point_1)
  Item[2]: SelectionGate → 选第二个点 (出口)
  Item[3]: EffectSignal → CreateUnit(portal_exit, point_2)
  Item[4]: 建立 link (Blackboard 或 Tag 关联两个 portal)
```

**K7: 可操控投射物**
```
Down → CreateUnit(controllable_projectile)
  → 切换 camera/input focus 到 projectile entity
  → 移动控制映射到 projectile
  → 爆炸/撞墙时: destroy + AoE damage
```
- **需要**: Input focus 切换机制 (将输入从 player entity 转移到 summoned entity)

**K8: 持续区域效果**
```
EffectPresetType: PeriodicSearch
  position: order_point (fixed, 不跟随)
  period: 30 ticks
  radius: 200cm
  duration: 300 ticks
```

### K 新增需求

| 需求 | 优先级 |
|------|--------|
| Input focus 切换 (K7) | P2 |
| Portal link 机制 (K5) | P3 |

---

## L: Mark / Detonate (L1–L6)

### 交互层
全部通过 Tag 实现, 交互层与 base ability 相同 (Unit/Point/Direction target)。

### 实现方案

**L1: A标记 → B引爆**
```
Skill A (target unit):
  EffectSignal → AddTag("marked", target=enemy, duration=180 ticks)

Skill B (press, no target):
  Phase Graph:
    1. QueryRadius(self, 999, filter=HasTag("marked"))
    2. FanOutApplyEffect(detonate_damage)
    3. RemoveTag("marked") on all detonated
```

**L2: 叠层满自动引爆**
```
每次命中:
  ModifyAttributeAdd(target, mark_stacks, +1)

ResponseChainListener on target (auto):
  eventTagId: attribute_changed
  precondition Graph: mark_stacks >= 3
  responseType: Chain → proc_damage + reset stacks to 0
```

**L3: 叠层+手动引爆**
```
叠层同 L2
手动引爆: 同 L1 的 Skill B
damage = base * mark_stacks
```

**L4: 放置标记→回溯**
```
持续记录位置: Periodic effect (tick=1) → write position to ring buffer attribute
激活回溯: 读取 N ticks 前的 position → teleport
```

**L5: Debuff→技能增强**
```
Skill A: AddTag("frosted", target)
Skill B Phase Graph:
  if target HasTag("frosted"):
    damage *= 1.5
    RemoveTag("frosted") → AddTag("shattered")
```

**L6: 技能链(A命中自动触发B)**
```
Skill A OnHit:
  ResponseChainListener: Chain → ApplyEffectTemplate(follow_up_effect)
```

### L 新增需求
**无。** 全部可用现有 Tag + Attribute + ResponseChain 表达。

---

## M: Channel / Sustained (M1–M7)

### 交互层
- **InputConfig**: Down (M1-M4) 或 DownAndUp (M5-M7)
- **TargetMode**: varies

### 实现方案

**M1: 站桩引导**
```
AbilityExecSpec:
  Item[0]: TagClip "channeling" @ tick 0, duration=180 ticks
  Item[1-N]: EffectSignal @ periodic ticks → damage/heal per tick

CC 打断:
  Stun/Silence effect 的 Phase Graph:
    if target HasTag("channeling"):
      RemoveTag("channeling")
      AbilityExecSystem 检测到 channeling tag 丢失 → InterruptAny → Finish
```

**M2: 引导中可调方向 (Jhin R)**
```
AbilityExecSpec:
  Item[0]: TagClip "channeling" + "aiming_cone"
  Item[1]: InputGate → 等第 1 发 (玩家按 fire)
  Item[2]: EffectSignal → shot_1 (读当前 cursor direction)
  Item[3]: InputGate → 等第 2 发
  ...repeat...

每帧: cursor direction 持续更新到 Blackboard
```

**M3: 引导中可移动**
```
同 M1, 但不添加 "rooted" tag
移动系统正常运行
```

**M4: 引导TP**
```
AbilityExecSpec:
  Item[0]: TagClip "channeling" + "teleporting", duration=180 ticks
  Item[1]: EffectSignal @ tick 180 → teleport to order_point
```

**M5: 持续光束**
```
DownAndUp:
  Down → EffectClip: beam_effect (period=3 ticks)
    每 period: damage to locked target
    cursor tracking: 光束方向跟随光标
  Up → 停止
```

**M6: 牵引/连接 (距离断裂)**
```
Down + Unit target:
  EffectClip: tether_effect
  OnPeriod Graph:
    distance = CalcDistance(caster, target)
    if distance > break_range → destroy effect
    else → apply_tick_effect
```

**M7: 持续追踪**
```
Down + Unit target:
  EffectClip: tracking_beam
  target locked = order_target
  OnPeriod: heal/damage to locked target
  no distance break (unlike M6)
```

### M 新增需求

| 需求 | 优先级 |
|------|--------|
| InterruptAny tag 检查 (channeling 丢失 → 中断) | ✅ 已有 |
| Cursor direction 持续写入 Blackboard | P1 (复用 F1) |
| Beam renderer (Performer) | P2 (表现层) |
