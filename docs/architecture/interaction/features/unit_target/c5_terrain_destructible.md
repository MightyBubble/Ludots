# C5: 地形/可破坏物

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
需求: TargetFilter 支持 EntityLayer filter
实现:
  1. 树木/石头等设为特定 EntityLayer (Destructible)
  2. TargetFilter: filter=Destructible layer
  3. Phase Graph 处理: 对可破坏物施加效果 (HP减为0等)
```

- 已有: `QueryFilterLayer` Graph op (op 115)

## 依赖组件

| 组件 | 状态 |
|------|------|
| `graph.aim.rule.unit_only` | AimInteractionMod |
| `pref.aim.cast_mode` | 三个手势状态 |
| QueryFilterLayer Graph op | ✅ 已有 (op 115) |
| EntityLayer 系统 | ✅ 已有 |
| FilterProfileRegistry / target filter profiles | ✅ 已有 |

## 新增需求

无 — 所有依赖组件已实现。可通过 TargetFilter 配置 EntityLayer filter 实现。
