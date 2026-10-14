# C9: 牵引/连接

## 交互层

- **InputConfig**: ReactsTo = **Down**
- **TargetMode**: **Unit**
- **Acquisition**: **Explicit**

## Ludots 映射

目标规则图是 `graph.aim.rule.unit_only`，拾取图是 `graph.aim.pick.hostile`。按下由控制方案上的输入上下文进入 `graph.aim.press`，槽号来自 `MapTrigger.BindingInt`。手势读偏好 `pref.aim.cast_mode`，取值是 `interaction.context.aim.click`、`interaction.context.aim.release`、`interaction.context.aim.smart`。指针采样是 `GraphPointerSampleRuntime`。下令演员写 `role:commandSource`，开局用 `Input/collection_keys.json` 把角色映射到集合名。座位要带 `controlSchemeId`。`Command` 由使用它的 mod 自己声明。


### 手势

手势不写在技能上。玩家偏好 `pref.aim.cast_mode` 的取值就是交互状态：

| 取值 | 行为 |
|------|------|
| `interaction.context.aim.smart` | 按下后该状态激活即提交，然后关掉自己 |
| `interaction.context.aim.click` | 按下进入瞄准，再点确认 |
| `interaction.context.aim.release` | 按下进入瞄准，松开确认 |

先选再放要另加一个取值和对应状态，规则图不用改。



## 实现方案

```
实现:
  1. EffectClip (duration=tether_duration)
  2. GrantedTags on caster: "tethered"
  3. GrantedTags on target: "tethered_target"
  4. Tether Effect 的 OnPeriod Graph:
     a. 计算 caster-target 距离
     b. if 距离 > break_range → RemoveTag, 销毁 effect
     c. else → ApplyEffectTemplate(heal/damage per tick)
```

- 已有: Periodic effect + Graph 计算距离

## 依赖组件

| 组件 | 状态 |
|------|------|
| `graph.aim.rule.unit_only` | AimInteractionMod |
| `pref.aim.cast_mode` | 三个手势状态 |
| EffectClip duration | ✅ 已有 |
| GrantedTags | ✅ 已有 |
| Periodic effect | ✅ 已有 |
| 距离计算 Graph ops | ✅ 已有 |

## 新增需求

| 需求 | 优先级 | 说明 |
|------|--------|------|
| Tether 组件 (持续距离监测 + 断裂) | P1 | 需要专门的 Tether 系统或通过 Periodic effect + Graph 实现 |
