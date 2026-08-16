# fx-08 配置说明 · 目标查询

> 配置写法与行为。第一性需求见 [fx-08 PRD](../prd/fx-09-target-query.md)；编辑器需求见 [UXD](../uxd/fx-09-target-query.md)；现状见 [reference](../reference/fx-09-target-query.md)。

## 1. 示例配置

champion 演示 mod 的四种形状（真实）：

```json
[
  { "id": "Effect.Champion.Garen.Judgment", "presetType": "Search",
    "lifetime": "Instant", "participatesInResponse": true,
    "targetQuery": { "kind": "BuiltinSpatial", "shape": "Circle", "radius": 260 } },
  { "id": "Effect.Champion.Jayce.Hammer.ThunderingBlow", "presetType": "Search",
    "lifetime": "Instant", "participatesInResponse": true,
    "targetQuery": { "kind": "BuiltinSpatial", "shape": "Cone", "radius": 300, "halfAngle": 40 } },
  { "id": "Effect.Champion.Geomancer.PrismaticBeam", "presetType": "Search",
    "lifetime": "Instant", "participatesInResponse": true,
    "targetQuery": { "kind": "BuiltinSpatial", "shape": "Line", "length": 920, "halfWidth": 80 } },
  { "id": "Effect.Champion.Jayce.Hammer.LightningField", "presetType": "Search",
    "lifetime": "Instant", "participatesInResponse": true,
    "targetQuery": { "kind": "BuiltinSpatial", "shape": "Ring", "radius": 280, "innerRadius": 90 } }
]
```

原点示例（真实）：`Effect.Interaction.Shockwave` 加 `"origin": "Source"` 以施法者为原点。

## 2. 字段与行为

| 形状 | 必填字段 | 约束 |
|---|---|---|
| Circle | `radius` | >0 |
| Cone | `radius` + `halfAngle` | 均 >0 |
| Rectangle | `halfWidth` + `halfHeight` | 均 >0；`rotation` 可选 |
| Line | `length` + `halfWidth` | 均 >0 |
| Ring | `radius` + `innerRadius` | radius>0；0 ≤ innerRadius < radius |

| 字段 | 这样配会产生什么效果 |
|---|---|
| `origin: Default / Source` | 查询中心参考；方向形状（锥/线/矩形）始终偏施法者 |
| `kind: GraphProgram` + `graphProgramId` | 动态查询；九个空间字段全禁 |
| 过滤类字段 | 不写在查询块——过滤正路是 `targetFilter`（fx-09） |

## 3. 文件结构

`targetQuery` 是效果模板顶层组件块（fx-01）；查询图在 `GAS/graphs.json`。Search/PeriodicSearch 的组件清单声明本块（声明性提示，合法性以模板联动规则为准）。

## 4. 运行时加载效果

loader 按形状互斥矩阵校验边界字段；运行期查询在裁决相位产出候选数，供过滤与派发消费（fx-09/10）。

## 5. 异常处理

| 异常情形 | 系统响应 |
|---|---|
| 形状边界缺失 / 非正 / 环内径越界 | 启动失败 |
| GraphProgram 查询残留空间字段或缺图 id | 启动失败 |

## 6. 实例

- `mods/showcases/champion_skill_sandbox/ChampionSkillSandboxMod/assets/GAS/effects.json`（圆/锥/线/环各有实例）
- `mods/showcases/interaction/InteractionShowcaseMod/assets/GAS/effects.json`（origin: Source）

**相关文档**：[fx-08 PRD](../prd/fx-09-target-query.md) · [fx-09 配置说明](fx-10-target-filter.md)
