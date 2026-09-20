# fx-04 配置说明 · 生命周期与时长

> 配置写法与行为。第一性需求见 [fx-04 PRD](../prd/fx-04-lifetime.md)；编辑器需求见 [UXD](../uxd/fx-04-lifetime.md)；现状见 [reference](../reference/fx-04-lifetime.md)。

## 1. 示例配置

三种寿命各一条（前两条取自 rts 与 blacksmith 演示 mod，真实；第三条教学骨架）：

```json
[
  { "id": "Effect.Rts.RedAlert.Construction", "presetType": "Buff",
    "lifetime": "After", "participatesInResponse": false,
    "duration": { "durationTicks": 45, "periodTicks": 0, "clockId": "FixedFrame" } },
  { "id": "Effect.Showcase.Blacksmith.RandomDrift", "presetType": "DoT",
    "lifetime": "Infinite", "participatesInResponse": false,
    "duration": { "durationTicks": 0, "periodTicks": 60, "clockId": "FixedFrame" } },
  { "id": "Effect.Example.Corrosive", "presetType": "DoT",
    "lifetime": "After", "participatesInResponse": false,
    "duration": { "durationTicks": 300, "periodTicks": 30, "clockId": "FixedFrame" },
    "expireCondition": { "kind": "TagPresent", "tag": "State.Example.Purged", "sense": "Effective" } }
]
```

## 2. 字段与行为

| 字段 | 这样配会产生什么效果 |
|---|---|
| `lifetime: "Instant"` | 同帧内联完成（条件：模板 Instant 且无周期）；禁带 duration 块 |
| `lifetime: "After"` | 必带 duration 且 durationTicks>0；periodTicks、clockId 显式（值可 0） |
| `lifetime: "Infinite"` | duration 可整块省略或省字段；显式块不得全零；clockId 缺省 FixedFrame |
| `duration.durationTicks` | 存活总 tick；热字段（可工作台热替换） |
| `duration.periodTicks` | 周期节拍，>0 时 OnPeriod 每周期触发；首拍按确定性散列落在 1..period 内 |
| `duration.clockId` | 计量时钟；EntityLocal 以目标实体为准 |
| `expireCondition` | 可选块 kind/tag/sense：After 到时求值，为真才过期；Infinite 亦可借此提前终止 |

## 3. 文件结构

写在效果模板顶层（`GAS/effects.json` 分片，fx-02）；时钟 id 来自 clock 表（rt-01）。Turn 时钟已移除，存量写法迁移为 FixedFrame。

## 4. 运行时加载效果

loader 按 lifetime 逐块校验 duration 矩阵与周期字段；运行期存活系统惰性初始化周期首拍（仅 After/Infinite 且有周期者参与，未提交状态不参与），到期时点同样惰性计算。

## 5. 异常处理

| 异常情形 | 系统响应 |
|---|---|
| Instant 带 duration / After 缺块或 durationTicks≤0 | 启动失败 |
| Infinite 显式全零块 / 未注册 clockId | 启动失败 |
| After 模板 periodTicks、clockId 未显式 | 启动失败 |

## 6. 实例

- `mods/showcases/rts_red_alert_like/RtsRedAlertLikeShowcaseMod/assets/GAS/effects.json`（Construction：After 45）
- `mods/showcases/presenter_blacksmith/PresenterBlacksmithShowcaseMod/assets/GAS/effects.json`（RandomDrift：Infinite + 周期 60）
- `mods/showcases/champion_skill_sandbox/ChampionSkillSandboxMod/assets/GAS/effects.json`（CourageAura：Infinite + expireCondition）

**相关文档**：[fx-04 PRD](../prd/fx-04-lifetime.md) · [fx-12 配置说明](fx-12-stack.md) · [rt-01](rt-01-clocks.md)
