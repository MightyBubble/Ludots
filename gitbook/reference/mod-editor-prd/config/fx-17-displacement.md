# fx-17 配置说明 · 位移

> 配置写法与行为。第一性需求见 [fx-17 PRD](../prd/fx-17-displacement.md)；编辑器需求见 [UXD](../uxd/fx-17-displacement.md)；现状见 [reference](../reference/fx-17-displacement.md)。

## 1. 示例配置

真实条目一（`mods/showcases/interaction/InteractionShowcaseMod/assets/GAS/effects.json`，闪现步）：

```json
{
  "id": "Effect.Interaction.BlinkStep",
  "presetType": "Displacement",
  "lifetime": "Instant",
  "displacement": {
    "directionMode": "ToTarget",
    "totalDistanceCm": 520,
    "totalDurationTicks": 2,
    "overrideNavigation": true
  }
}
```

真实条目二（`mods/showcases/moba_demo/MobaDemoMod/assets/GAS/effects.json`，击退）：`directionMode: "AwayFromSource"`、`totalDistanceCm: 350`、`totalDurationTicks: 12`、`overrideNavigation: true`（Effect.Moba.Displacement.R）。

## 2. 字段与行为

| 字段 | 这样配会产生什么效果 |
|---|---|
| `directionMode` | `ToTarget` 朝方向目标推；`AwayFromSource` 背离源；`TowardSource` 朝源拉；`Fixed` 世界固定角 |
| `fixedDirectionDeg` | 仅 Fixed 必填：世界系角度；其他模式写了即启动失败 |
| `totalDistanceCm` | 总位移距离，必须 >0 |
| `totalDurationTicks` | 总时长 tick 数，必须 >0；两者共同决定分段速度 |
| `overrideNavigation` | true 时位移期间压制移动输入（导航接管） |

方向目标解析优先级：上下文目标实体位置 → 保留目标点（`_ep.targetPosX/Y` 系）→ 施法实例的 TargetPos。块只允许挂在 `presetType: Displacement` + Instant。

## 3. 文件结构

`assets/GAS/effects.json` 效果条目的 `displacement` 块。

## 4. 运行时加载效果

loader 校验方向模式与正数合同；运行期组装位移状态：同目标已有活跃位移则就地替换（覆写预算与方向、撤销旧段压制），否则新建位移段。数值改动经工作台热通道为下次施放生效级。

## 5. 异常处理

| 异常情形 | 系统响应 |
|---|---|
| 非 Displacement 带块 / Displacement 缺块 / 非 Instant | 启动失败，指明效果 |
| 未知 directionMode | 启动失败，列四种合法值 |
| 非 Fixed 配 `fixedDirectionDeg` | 启动失败，指明"directionMode=当前模式" |
| 距离或时长 <=0 | 启动失败 |

## 6. 实例

- 位移技族：`mods/showcases/interaction/InteractionShowcaseMod/assets/GAS/effects.json`（BlinkStep、ArcDash、BannerLeap、ChargeDash）
- 击退：`mods/showcases/moba_demo/MobaDemoMod/assets/GAS/effects.json`（Displacement.R）
- 冲击波击退：`mods/showcases/capability_standard/CapabilityStandardCrowdPhysicsArenaMod/assets/GAS/effects.json`（Shockwave.Knockback）

**相关文档**：[fx-17 PRD](../prd/fx-17-displacement.md) · fx-06 config（独占计划）
