# fx-19 配置说明 · 弹道

> 配置写法与行为。第一性需求见 [fx-18 PRD](../prd/fx-15-projectile.md)；编辑器需求见 [UXD](../uxd/fx-15-projectile.md)；现状见 [reference](../reference/fx-15-projectile.md)。

## 1. 示例配置

真实条目（`mods/showcases/moba_demo/MobaDemoMod/assets/GAS/effects.json`，追踪箭）：

```json
{
  "id": "Effect.Moba.Projectile.Arrow",
  "presetType": "LaunchProjectile", "lifetime": "Instant", "participatesInResponse": true,
  "projectile": {
    "speed": 1200, "range": 2000, "arcHeight": 0,
    "impactEffect": "Effect.Moba.Damage.R", "hitEffect": "Effect.Moba.Damage.R",
    "presentationEffect": "Effect.Moba.Damage.R",
    "travelMode": "TrackTarget", "impactPolicy": "DestroyOnFirstHit",
    "collisionHalfWidth": 80, "collisionRelationFilter": "All",
    "collisionExcludeSource": true, "maxHitCount": 1
  }
}
```

## 2. 字段与行为

| 字段 | 这样配会产生什么效果 |
|---|---|
| `speed` / `range` / `arcHeight` | 整数：飞行速度、射程、抛物线高度（0 为平射） |
| `travelMode` | `Direction` 直射（方向解析失败即抛错）；`TrackTarget` 追踪目标；`Legacy` 启动报错 |
| `impactPolicy` | `DestroyOnFirstHit` 首杀；`ContinueOnHit` 贯穿到 maxHitCount；`Legacy` 启动报错 |
| `hitEffect` | 必填：每次命中结算的子效果 |
| `impactEffect` / `presentationEffect` | 可选：落点效果与表现效果 |
| `collisionHalfWidth` | 必填正数：碰撞判定的半宽 |
| `collisionRelationFilter` | 必填：可命中目标的敌我关系过滤 |
| `collisionExcludeSource` | 只可 `true` 或省略（写 `false` 启动报错）：排除发射者自身 |
| `maxHitCount` | 1..命中历史容量（事实页/常量 32）：贯穿策略的最大命中数 |

块只允许挂在 `presetType: LaunchProjectile`；该 preset 必须 Instant（preset 合同@@fx3@@）。

## 3. 文件结构

`assets/GAS/effects.json` 效果条目的 `projectile` 块；命中/落点子效果是同表普通条目，被本块按 id 引用（引用许可序@@fx2@@）。

## 4. 运行时加载效果

loader 校验上述合同并把子效果名解析为模板 id；运行期发射原点取源位置，组装弹道状态经实体生成队列入世界。数值改动经工作台热通道为下次施放生效级。

## 5. 异常处理

| 异常情形 | 系统响应 |
|---|---|
| 非 LaunchProjectile 带块 / 反向缺块 / 非 Instant | 启动失败，指明效果 |
| `travelMode`/`impactPolicy` 为 Legacy 或未知值 | 启动失败，报"was removed"并列合法值 |
| 缺 `hitEffect`、`collisionHalfWidth<=0`、`maxHitCount` 越界 | 启动失败（越界报 1..容量 区间） |
| 引用未注册子效果名 | 启动失败，指明字段与名字 |
| Direction 模式方向不可解析；生成队列容量满 | 运行期抛错，带源/目标实体 id |

## 6. 实例

- 追踪箭：`mods/showcases/moba_demo/MobaDemoMod/assets/GAS/effects.json`
- 直射/多段弹：`mods/showcases/champion_skill_sandbox/ChampionSkillSandboxMod/assets/GAS/effects.json`（MysticShot 等）
- 火箭：`mods/showcases/arpg_demo/ArpgDemoMod/assets/GAS/effects.json`（FireArrow）

**相关文档**：[fx-18 PRD](../prd/fx-15-projectile.md) · @@fx11@@ config（子效果派发）
