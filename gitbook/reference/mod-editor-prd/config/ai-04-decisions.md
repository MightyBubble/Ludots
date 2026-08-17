# ai-06 配置说明 · 决策

> 配置写法与行为。第一性需求见 [ai-05 PRD](../prd/ai-04-decisions.md)；编辑器需求见 [UXD](../uxd/ai-04-decisions.md)；现状见 [reference](../reference/ai-04-decisions.md)。

## 1. 示例配置

真实例（utility_autocast 目录条目 `AI/decisions.json`（根数据为空，由 mod 贡献） 之一，教学骨架补齐 Flags 写法）：

```json
{
  "id": "Decision.UtilityAutocast.Attack",
  "TargetFilter": "TF.UtilityAutocast.Hostile",
  "Priority": 10,
  "BaseScore": 0.35,
  "Weight": 1,
  "CooldownSteps": 30,
  "AbilityKey": "Ability.UtilityAutocast.Attack",
  "AbilitySlotIndex": 0,
  "SharedCooldownTag": "Cooldown.UtilityAutocast.GCD",
  "Autocast": true,
  "OrdinaryAttack": true,
  "RequiresTarget": true,
  "Considerations": [
    { "Input": "Input.UtilityAutocast.Distance",
      "Normalization": "Norm.UtilityAutocast.CloseHostile",
      "Curve": "Curve.UtilityAutocast.Linear",
      "Aggregate": "WeightedSum", "Weight": 0.2 }
  ],
  "Tasks": ["Task.UtilityAutocast.Attack"]
}
```

Flags 数组写法与布尔等价：`"Flags": ["Autocast", "RequiresTarget"]`。

## 2. 字段与行为

| 字段 | 默认 | 这样配会产生什么效果 |
|---|---|---|
| TargetFilter | 必填 | 候选目标来自哪个过滤器（ai-07） |
| Considerations[] | 可空 | 每条：Input/Normalization/Curve 必填引用；Weight 默认 1；Aggregate 默认 Multiply |
| Aggregate | Multiply | Multiply 入乘积；WeightedSum/PriorityBucket 入加权和（后者另计优先桶）；Veto curved≤0 整决策归 0 |
| Tasks | 必填 ≥1 | 任务 id 数组；须解析为编译任务表的**连续区间**（问题 I3：跨 mod 分片拆同一决策者任务易触发 contiguous 报错） |
| Priority / BaseScore / Weight | 0 / 1 / 1 | 平局位、乘积基数、总分乘子 |
| MomentumBonus / MinDurationSteps / CooldownSteps | 0 / 0 / 0 | 惯性加分、最短保持步、冷却步 |
| AbilityKey（或 AbilityId） | 可选 | 决策级技能绑定（任务未绑时的回退） |
| AbilitySlotIndex | -1 | 决策级槽位回退 |
| SharedCooldownTag | 可选 | 未配置回退 ability 的冷却 tag |
| 五布尔/Flags[] | false | Autocast/OrdinaryAttack/RequiresTarget/KeepRunningUntilFinished/ExplicitOrderOnly，可混写 |

## 3. 文件结构

目录条目 `AI/decisions.json`（根数据为空，由 mod 贡献）（ArrayById）。考量内联在决策条目里，不单独成表。无 schema（I10）。

## 4. 运行时加载效果

编译时考量平铺进全局 Considerations 数组（决策记 offset+count）；Tasks 解析成任务表 offset+count 并强制连续；技能/tag 引用就地核验。

## 5. 异常处理

| 异常情形 | 系统响应 |
|---|---|
| TargetFilter/考量四件套引用未定义 | 启动失败，带路径 |
| Tasks 缺失/空 | 启动失败：must declare Tasks / at least one task |
| Tasks 不连续 | 启动失败：must resolve to a contiguous compiled task range |
| Aggregate/Flags 值未知 | 启动失败 |

## 6. 实例

- `mods/showcases/utility_autocast/UtilityAutocastShowcaseMod/assets/AI/decisions.json`（真实，3 条：Attack/HealBurst/Curse）

**相关文档**：[ai-05 PRD](../prd/ai-04-decisions.md) · [ai-06 配置说明](ai-05-dm-profiles.md) · [ai-08 配置说明](ai-07-tasks.md)
