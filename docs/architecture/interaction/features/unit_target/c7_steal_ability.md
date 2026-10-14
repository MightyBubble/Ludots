# C7: 窃取技能

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
  1. Target Unit (enemy)
  2. Phase Graph:
     a. 读取 target 最近使用的 ability ID (需要 LastUsedAbility attribute 或 tag)
     b. 将该 ability 注册到 caster 的 AbilityStateBuffer
     c. 替换特定 slot 的 ability
```

- **需要**: `LastUsedAbilityId` 记录机制 + 动态修改 AbilityStateBuffer 的 Graph op

## 依赖组件

| 组件 | 状态 |
|------|------|
| `graph.aim.rule.unit_only` | AimInteractionMod |
| `pref.aim.cast_mode` | 三个手势状态 |
| AbilityStateBuffer | ✅ 已有 |
| Phase Graph System | ✅ 已有 |

## 新增需求

| 需求 | 优先级 | 说明 |
|------|--------|------|
| LastUsedAbilityId 记录 | P2 | 需要在 ability 使用时记录到 attribute 或 Blackboard |
| 动态修改 AbilityStateBuffer Graph op | P2 | 新增 Graph op 用于运行时替换 ability slot |
