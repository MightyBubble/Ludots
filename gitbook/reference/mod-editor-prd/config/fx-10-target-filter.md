# fx-14 配置说明 · 目标过滤

> 配置写法与行为。第一性需求见 [fx-12 PRD](../prd/fx-10-target-filter.md)；编辑器需求见 [UXD](../uxd/fx-10-target-filter.md)；现状见 [reference](../reference/fx-10-target-filter.md)。

## 1. 示例配置

champion 演示 mod（真实）——敌对、排除自己、至多 12 个：

```json
[
  { "id": "Effect.Champion.Garen.Judgment", "presetType": "Search",
    "lifetime": "Instant", "participatesInResponse": true,
    "targetQuery":  { "kind": "BuiltinSpatial", "shape": "Circle", "radius": 260 },
    "targetFilter": { "relationFilter": "Hostile", "excludeSource": true, "maxTargets": 12 },
    "targetDispatch": { "payloadEffect": "Effect.Champion.Garen.JudgmentHit" } }
]
```

## 2. 字段与行为

| 字段 | 这样配会产生什么效果 |
|---|---|
| `excludeSource` | 必填布尔；true 时施法者本人出候选 |
| `relationFilter` | 必填六值：All / Hostile / Friendly / Neutral / NotFriendly / NotHostile；双方须有阵营，否则滤掉 |
| `maxTargets` | 必填整数；0=不限量，N=截前 N 个候选 |
| `layerMask` | 可选字符串数组；按层注册表挑候选，缺省不滤层 |

过滤顺序（固定）：排除源 → 环内径 → 层掩码 → 敌我 → 数量 → 根预算（fx-09）。

## 3. 文件结构

`targetFilter` 是效果模板顶层组件块（fx-04）；层名来自层注册表。无 preset 在组件清单声明本块，按需自由携带。

## 4. 运行时加载效果

loader 校验必填与六值域；运行期过滤链在派发前执行，环内径一步仅对 Ring 查询有意义（其余形状自然通过）。

## 5. 异常处理

| 异常情形 | 系统响应 |
|---|---|
| relationFilter 非六值 / 必填缺失 | 启动失败 |
| layerMask 引用未注册层 | 启动失败 |

## 6. 实例

- `mods/showcases/champion_skill_sandbox/ChampionSkillSandboxMod/assets/GAS/effects.json`（Judgment：Hostile/排除/12；DemacianJustice：Hostile/排除/6）
- `mods/showcases/moba_demo/MobaDemoMod/assets/GAS/effects.json`（锥形技能过滤组）

**相关文档**：[fx-12 PRD](../prd/fx-10-target-filter.md) · [fx-11 配置说明](fx-09-target-query.md)
