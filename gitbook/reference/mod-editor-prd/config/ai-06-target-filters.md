# ai-05 配置说明 · 目标过滤器

> 配置写法与行为。第一性需求见 [ai-05 PRD](../prd/ai-06-target-filters.md)；编辑器需求见 [UXD](../uxd/ai-06-target-filters.md)；现状见 [reference](../reference/ai-06-target-filters.md)。

## 1. 示例配置

真实例（utility_autocast 目录条目 `AI/target_filters.json`（根数据为空，由 mod 贡献） 之一）：

```json
[
  {
    "id": "TF.UtilityAutocast.Hostile",
    "MaxResults": 16,
    "Ops": [
      { "Kind": "SpatialRadius", "RadiusCm": 1600 },
      { "Kind": "Relationship", "Value": "Hostile" }
    ]
  }
]
```

教学骨架（覆盖其余 op）：

```json
[ { "id": "TF.Example.Precise", "MaxResults": 8, "Ops": [
  { "Kind": "SourceSelf" },
  { "Kind": "HasAllTags", "Tags": ["State.Boss"] },
  { "Kind": "HasNoneTags", "Tags": ["State.Untargetable"] },
  { "Kind": "LayerAny", "Mask": 3 },
  { "Kind": "DistanceMax", "MaxCm": 800 },
  { "Kind": "AbilityEligible", "AbilityKey": "Ability.Example.Fire" },
  { "Kind": "RecentAttacker", "TtlSteps": 30 }
] } ]
```

## 2. 字段与行为

| 字段 | 默认 | 这样配会产生什么效果 |
|---|---|---|
| MaxResults | 64 | 产出候选上限，须为正 |
| Ops | 必填 | 判定序列，顺序 AND |

九种 op：

| Kind | 专属字段 | 判定 |
|---|---|---|
| SourceSelf | — | 以 actor 自身为候选起点 |
| SpatialRadius | RadiusCm 正 | 半径内 |
| Relationship | Value（Hostile/Friendly 等） | 双方 Team 关系 |
| HasAllTags | Tags[] | 目标须含全部 tag |
| HasNoneTags | Tags[] | 目标不得含任一 tag |
| LayerAny | Mask 正 | 层级掩码相交 |
| DistanceMax | MaxCm 正 | 平方距离比较 |
| AbilityEligible | AbilityKey/AbilityId 必填 | 技能可施 |
| RecentAttacker | TtlSteps 默认 30 正 | LastAttacker 存活且 TTL 内 |

注意：HasAllTags 的优先桶加权字段（IntB）编译端固定 0、运行时恒加 0——死字段未接线（问题 I4）。

## 3. 文件结构

目录条目 `AI/target_filters.json`（根数据为空，由 mod 贡献）（ArrayById）。op 平铺进全局数组，过滤器记 offset+count。无 schema（I10）。

## 4. 运行时加载效果

编译期校验 op Kind 与正数参数、解析 tag/技能引用；op 数组按条目出现顺序连续排布。

## 5. 异常处理

| 异常情形 | 系统响应 |
|---|---|
| Ops 缺失 | 启动失败：must declare Ops |
| 未知 Kind | 启动失败：Unsupported target filter op |
| RadiusCm/Mask/MaxCm/TtlSteps ≤ 0 | 启动失败：must be positive |
| AbilityEligible 无技能 | 启动失败（required） |

## 6. 实例

- `mods/showcases/utility_autocast/UtilityAutocastShowcaseMod/assets/AI/target_filters.json`（真实，2 条：Hostile 1600cm/Friendly 1200cm，均 MaxResults 16）

**相关文档**：[ai-05 PRD](../prd/ai-06-target-filters.md) · [ai-03 配置说明](ai-04-decisions.md) · [ai-01 配置说明](ai-02-inputs.md)
