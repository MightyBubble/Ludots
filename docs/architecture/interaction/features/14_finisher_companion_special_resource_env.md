# Feature: Finisher / Execution (Q1–Q8), Companion (R1–R6), Special Input (S1–S7), Resource (T1–T8), Environment (U1–U7)

> 合并五个较小 feature 类别

---

## Q: Finisher / Execution (Q1–Q8)

### 交互层
全部是 **Down + (None 或 Unit) + (Explicit 或 ContextScored)**
差异全在 **Precondition** (Tag/Attribute 门控)。

### 实现要点

| 场景 | Precondition | 交互 |
|------|-------------|------|
| Q1 血量阈值 | target.health_ratio < threshold | Unit + Explicit |
| Q2 Posture破满 | target HasTag("posture_broken") | Unit + Context |
| Q3 背后位置 | angle_to_target_back < 30° | Unit + Context |
| Q4 居高临下 | self.height > target.height + threshold | Unit + Context |
| Q5 弹反后 | self HasTag("parry_success") | Unit + Context |
| Q6 大招仪表 | self.ultimate_charge >= 100 | None + Explicit |
| Q7 连击数 | self.combo_meter >= threshold | None + Explicit |
| Q8 QTE | InputGate sequence | None + Explicit |

**Q8 QTE 实现**:
```
AbilityExecSpec:
  Item[0]: Animation + UI prompt "Press X"
  Item[1]: InputGate (wait for correct key, deadline=30 ticks)
  Item[2]: if correct → heavy_damage, if timeout → reduced_damage
  Item[3]: InputGate "Press O"  (second QTE prompt)
  ...
```

### Q 新增需求
| 需求 | 优先级 |
|------|--------|
| AbilityActivationRequireTags + Attribute precondition | P0 (复用 G 需求) |
| Angle/Height precondition (Graph ops 或 builtin) | P2 |

---

## R: Companion / Multi-Unit (R1–R6)

### 交互层

| 场景 | 交互 |
|------|------|
| R1 指挥攻击 | Down + Unit (目标) → Order 发给 companion entity |
| R2 指挥特定技能 | Down + None → 切换 companion 行为模式 |
| R3 多单位微操 | SC2 式: 选中 → 下指令 (已有 Entities selection) |
| R4 装载 | Down + Unit (transport → target) |
| R5 合并 | SelectionGate → 选 2 单位 → merge |
| R6 集结点 | Down + Point → 设 rally Blackboard |

### R1 实现
```
InputOrderMapping:
  actionId: "CompanionAttack"
  selectionType: Entity
  orderTypeKey: "commandCompanion"

OrderSubmitter:
  Order.Actor = companion_entity (不是 local player)
  Order.Target = selected_enemy
```

- **需要**: Order 可以指定 Actor 为非 local player 的 entity (已有: Order.Actor 字段)

### R3 多单位微操
```
已有:
  OrderSelectionType.Entities → SelectionGroupBuffer → 框选多单位
  Order 对每个选中单位发送副本
```

### R5 合并 (Archon)
```
SelectionGate → 选 2 个 High Templar
Phase Graph:
  1. 销毁两个 HT entity
  2. CreateUnit("archon", position=midpoint)
```

### R 新增需求
| 需求 | 优先级 |
|------|--------|
| Input focus / actor routing | P2 (K7 复用) |

---

## S: Special Input (S1–S7)

### 实现要点

**S1: 组合键 (L1+R1)**
```
InputOrderMapping:
  actionId: "RunicAttackLight"  // 已在 InputBackend 配为 L1+R1 组合
  trigger: PressedThisFrame
```
- 组合键由 `PlayerInputHandler` 的 Binding 配置处理 (CompositeBinding)

**S2: 双击**
```
InputOrderMapping:
  trigger: DoubleTap  // 需新增 trigger type
  或: 用 Tag 模拟 — 第一次按 AddTag("first_tap", duration=15 ticks)
      第二次按 precondition HasTag("first_tap") → 执行
```

**S3: 方向+按键**
```
InputOrderMapping:
  selectionType: Direction
  InputDirection + ActionId → 不同 argsTemplate.i0
  或: ContextGroup 根据 direction dot 评分
```

**S4: Alt+技能 → 自我施放**
```
已有: InputOrderMappingSystem 在 SmartCast 模式下,
  如果 selectionType=Entity 但无可用目标 → fallback self-cast
  或: Alt modifier → force target = self
```

**S5: Quick Cast**
```
已有: InteractionModeType.SmartCast = Quick Cast
  全局设置或 per-ability CastModeOverride
```

**S6: 小地图施放**
```
需要: Minimap click → world position 转换 (Adapter 层)
OrderArgs.Spatial 接收转换后的世界坐标
```

**S7: Shift+Queue**
```
已有: InputOrderMapping.ModifierBehavior
  Shift held → Order.SubmitMode = Queued
  OrderBuffer 的 QueuedOrder 队列接收
```

### S 新增需求
| 需求 | 优先级 |
|------|--------|
| DoubleTap trigger type | P2 |
| Minimap click adapter | P3 |

---

## T: Resource / Gating (T1–T8)

### 实现要点

全部通过 **Attribute + Precondition** 实现, 不影响交互层。

**T1 蓝量**: `Precondition: mana >= cost` → Execute → `ModifyAttribute(mana, -cost)`
**T2 冷却**: `Attribute: cooldown_remaining` → Precondition: == 0 → Execute → set to max
**T3 充能**: `Attribute: charges` → Precondition: > 0 → Execute → charges -= 1; timer refills
**T4 怒气**: `Attribute: rage` → Precondition: >= threshold
**T5 血量**: `Execute → ModifyAttribute(health, -cost)`
**T6 弹药**: `Attribute: ammo` → Precondition: > 0 → Execute → ammo -= 1
**T7 连击条**: `Attribute: combo_meter` → 被打时 reset to 0
**T8 消耗品**: `Attribute: item_count` → Precondition: > 0 → Execute → count -= 1

### T 新增需求
| 需求 | 优先级 |
|------|--------|
| Ability-level Attribute precondition (cost check) | P0 (复用 G8 需求) |
| Cooldown system (auto-decrement attribute per tick) | P1 |
| Charge refill timer | P1 |

---

## U: Environmental Interaction (U1–U7)

### 交互层

全部 **Down + (None/Unit/Point) + ContextScored** — 环境交互是典型的上下文自动。

### 实现要点

**U1 投掷物体**:
```
ContextGroup:
  candidate: throw_object
    precondition: env entity HasTag("throwable") in radius 200cm
    执行: 拾取 (attach to caster) → InputGate (选方向) → 投掷 (LaunchProjectile)
```

**U2 撞墙/推下悬崖**:
```
Displacement effect OnCollision:
  if hit wall → stun + bonus_damage
  if hit ledge → fall_damage / instant_kill
```
- **需要**: Displacement 碰撞回调

**U3 可破坏物**:
```
Phase Graph: QueryRadius + QueryFilterLayer(Destructible) → FanOutApplyEffect(destroy)
```

**U7 地形创造**:
```
EffectSignal → CreateUnit(wall_segment, position)
  wall entity 有 Navigation blocker 组件 → 影响寻路
```

### U 新增需求
| 需求 | 优先级 |
|------|--------|
| Displacement 碰撞回调 (wall hit, ledge) | P2 |
| Environment tag scanning (nearby throwable/interactable) | P1 |
| Navigation blocker for spawned walls | P2 |
