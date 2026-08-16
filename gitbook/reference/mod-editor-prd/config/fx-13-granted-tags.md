# fx-12 配置说明 · 效果授予 Tag

> 配置写法与行为。第一性需求见 [fx-12 PRD](../prd/fx-13-granted-tags.md)；编辑器需求见 [UXD](../uxd/fx-13-granted-tags.md)；现状见 [reference](../reference/fx-13-granted-tags.md)。

## 1. 示例配置

真实条目（`mods/showcases/rts_red_alert_like/RtsRedAlertLikeShowcaseMod/assets/GAS/effects.json`，建造期状态 tag）：

```json
{
  "id": "Effect.Rts.RedAlert.Construction", "presetType": "Buff", "lifetime": "After",
  "duration": { "durationTicks": 45, "periodTicks": 0, "clockId": "FixedFrame" },
  "grantedTags": [ { "tag": "State.Rts.RedAlert.Constructing", "formula": "Fixed", "amount": 1 } ]
}
```

层数放大写法（教学骨架，仓库暂无 Linear 实例）：

```json
"grantedTags": [ { "tag": "Status.Poison", "formula": "Linear", "amount": 2 } ]
```

## 2. 字段与行为

| 字段 | 这样配会产生什么效果 |
|---|---|
| `tag` | 首现注册的 tag 名；效果活跃期间向目标贡献计数 |
| `formula` | 贡献公式，取值见下表 |
| `amount` | 单位量：Fixed 即贡献值；Linear 为每层贡献 |
| `base` | 仅 LinearPlusBase：层数贡献的保底基线 |

| 公式 | 目标得到的计数 |
|---|---|
| `Fixed` | = amount，与层数无关 |
| `Linear` | = 层数 × amount |
| `LinearPlusBase` | = base + 层数 × amount |
| `GraphProgram` | 加载期拒绝（评估器未接线） |

单效果授予条数上限见[事实与取值表](../facts.md)。

## 3. 文件结构

`grantedTags` 是 `assets/GAS/effects.json` 效果条目的可选块（条目骨架见 fx-02），与 modifiers 并存：数值改属性走修改器、状态标记走授予 tag。

## 4. 运行时加载效果

loader 逐条注册 tag 名、锁定公式并把 amount/base 钳到计数上限；应用时在效果事务内分阶段授予，层数变化走差量，过期或移除时按移除时层数回收。模板改动经工作台热通道为下次施放生效级。

## 5. 异常处理

| 异常情形 | 系统响应 |
|---|---|
| 授予条数超上限（事实页） | 启动失败，指明效果与位置 |
| LinearPlusBase 缺 `base`；Fixed/Linear 带 `base`；`formula: "GraphProgram"` | 启动失败，指明条目 |
| 运行期 tag 规则拒绝 | 回滚后上抛 `GAS.TAG.ERR.RuleRejected` |
| 运行期计数容量满 | 回滚后上抛 `GAS.TAG.ERR.TagCountOverflow` 并计入预算 |

## 6. 实例

- 建造状态：`mods/showcases/rts_red_alert_like/RtsRedAlertLikeShowcaseMod/assets/GAS/effects.json`（Construction）
- 减速/沉默状态：`mods/showcases/moba_demo/MobaDemoMod/assets/GAS/effects.json`（Debuff.Slow、Debuff.Silence）
- 科研状态：`mods/showcases/fourx_demo/FourXDemoMod/assets/GAS/effects.json`（TechResearch）

**相关文档**：[fx-12 PRD](../prd/fx-13-granted-tags.md) · [tag-01 配置说明](tag-01-basics.md)
