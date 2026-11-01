# C4: 死亡友方 — 复活

## 交互层

- **InputConfig**: ReactsTo = **Down**
- **TargetMode**: **Unit**
- **Acquisition**: **Explicit**

## Ludots 映射

目标规则图是 `graph.aim.rule.unit_only`，拾取图是 `graph.aim.pick.friendly`。按下由控制方案上的输入上下文进入 `graph.aim.press`，槽号来自 `MapTrigger.BindingInt`。手势读偏好 `pref.aim.cast_mode`，取值是 `interaction.context.aim.click`、`interaction.context.aim.release`、`interaction.context.aim.smart`。指针采样是 `GraphPointerSampleRuntime`。下令演员写 `role:commandSource`，开局用 `Input/collection_keys.json` 把角色映射到集合名。座位要带 `controlSchemeId`。`Command` 由使用它的 mod 自己声明。


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
需求: TargetFilter 需支持 "DeadAlly" 过滤
实现:
  1. 死亡单位保留 Entity (添加 "dead" tag, 不立即销毁)
  2. TargetFilter 新增: filter=DeadFriendly
  3. Ability Phase Graph:
     a. 检查 target HasTag("dead")
     b. RemoveTag("dead"), SetAttribute(health, base_health * 0.5)
```

- **需要**: `FilterProfileRegistry` / target filter profile 新增 DeadFriendly filter，或 relationship filter 扩展

## 依赖组件

| 组件 | 状态 |
|------|------|
| `graph.aim.rule.unit_only` | AimInteractionMod |
| `pref.aim.cast_mode` | 三个手势状态 |
| Entity Tag System | ✅ 已有 |
| HasTag/RemoveTag Graph ops | ✅ 已有 |
| SetAttribute Graph op | ✅ 已有 |

## 新增需求

| 需求 | 优先级 | 说明 |
|------|--------|------|
| Target filter profile 支持 DeadFriendly filter | P1 | 需要扩展 FilterProfileRegistry 或 relationship filter |
| 死亡单位保留机制 | P1 | 死亡时添加 "dead" tag 而非立即销毁 Entity |
