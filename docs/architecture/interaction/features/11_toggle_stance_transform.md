# Feature: Toggle / Stance / Transform (J1–J9)

> 清单覆盖: J1 Toggle, J2 全技能组切换, J3 临时变身, J4 武器切换, J5 单双手, J6 弹药切换, J7 选择增强, J8 元素组合, J9 感知模式

## 交互层

全部是 **Down + None + Explicit** — 每种都是按键即生效, 差异全在 Tag 层。

## 实现方案

### J1: Toggle (Singed Q)

```
AbilityDefinition:
  abilityToggleSpec: { toggleTagId: "poison_trail_active" }

AbilityExecSystem 处理:
  if caster HasTag("poison_trail_active"):
    RemoveTag → 停止 periodic effect
  else:
    AddTag → 启动 periodic effect (trail DoT)
```

- 已有: `AbilityToggleSpec` 在 `AbilityDefinition`

### J2: 全技能组切换 (Jayce)

```
Down → Execute:
  1. if HasTag("form_ranged"):
     RemoveTag("form_ranged") + AddTag("form_melee")
  2. else:
     RemoveTag("form_melee") + AddTag("form_ranged")

技能路由:
  ability_slot_0 的 AbilityDefinition 实际绑定两个 ability:
    form_melee → melee_Q
    form_ranged → ranged_Q

  AbilityExecSystem 激活时:
    读取 form tag → 选择对应 ability
```

- **需要**: AbilityStateBuffer 支持 form-based ability mapping (slot → tag → actual ability)

### J3: 临时变身 (大招)

```
Down → Execute:
  AddTag("ultimate_form", duration=360 ticks)
  替换 ability set (同 J2)
  OnExpire: 恢复原 ability set
```

### J4/J5: 武器切换 / 单双手

```
同 J2, 但 form tag 是 weapon_id:
  AddTag("weapon:axe") → axe combo set
  AddTag("weapon:fists") → fists combo set
```

### J6: 弹药/元素切换

```
Down → cycle ammo_type Attribute:
  current = ReadAttribute(ammo_type)
  next = (current + 1) % max_types
  WriteAttribute(ammo_type, next)

技能执行时读 ammo_type → 选不同 EffectTemplate (火/冰/雷箭)
```

### J7: 选择增强哪个技能 (Karma R)

```
Down → AddTag("empowered_next", duration=180 ticks)

下一个技能施放时:
  precondition: HasTag("empowered_next")
  → 使用增强版 CallerParams (slot 1 而非 slot 0)
  → RemoveTag("empowered_next")
```

- 已有: `AbilityExecCallerParamsPool` (4 slots)

### J8: 元素组合 (Invoker)

```
Invoker QWE:
  Q press → cycle element slot:
    Attribute(element_0/1/2) 轮替记录
    AddTag("quas_count:N")

  Invoke press:
    Phase Graph:
      1. 读 element_0, element_1, element_2
      2. 查表 (QQQ=Cold Snap, QQW=Ghost Walk, ...)
      3. 将对应 ability 写入 slot 4/5 (invoked spell slots)
      4. RemoveTag 所有 element tags, 重置
```

### J9: 感知模式切换

```
Down → Toggle tag "detective_vision"
  Performer rules:
    if HasTag("detective_vision"):
      enable highlighting performers on all entities with specific tags
      enable X-ray overlay performer
```

## 新增需求

| 需求 | 优先级 | 清单项 |
|------|--------|--------|
| Form-based ability mapping (slot+tag→ability) | P1 | J2, J3, J4 |
| AbilityToggleSpec (已有, 验证完备性) | P2 | J1 |
