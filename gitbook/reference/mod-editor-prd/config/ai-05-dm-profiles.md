# ai-07 配置说明 · 决策者与档案

> 配置写法与行为。第一性需求见 [ai-06 PRD](../prd/ai-05-dm-profiles.md)；编辑器需求见 [UXD](../uxd/ai-05-dm-profiles.md)；现状见 [reference](../reference/ai-05-dm-profiles.md)。

## 1. 示例配置

真实例（utility_autocast 两表全量）：

```json
[
  {
    "id": "DM.UtilityAutocast.Mage",
    "SelectionMode": "UtilityScore",
    "SwitchMargin": 0,
    "Decisions": [
      "Decision.UtilityAutocast.Attack",
      "Decision.UtilityAutocast.HealBurst",
      "Decision.UtilityAutocast.Curse"
    ]
  }
]
```

```json
[
  {
    "id": "Profile.UtilityAutocast.Mage",
    "DecisionIntervalSteps": 1,
    "MaxCandidates": 32,
    "DecisionMakers": ["DM.UtilityAutocast.Mage"]
  }
]
```

教学骨架（补 stance 绑定）：

```json
[ { "id": "Profile.Example.Guard", "DecisionIntervalSteps": 3,
    "MaxCandidates": 16, "DecisionMakers": ["DM.Example.Combat"],
    "DefaultStance": "Stance.Example.HoldFire" } ]
```

## 2. 字段与行为

decision_makers：

| 字段 | 默认 | 这样配会产生什么效果 |
|---|---|---|
| Decisions | 必填 ≥1 | 决策 id 数组；须解析为**连续区间**（I3） |
| SelectionMode | UtilityScore | UtilityScore 按分；FixedPriority 按 Priority |
| SwitchMargin | 0 | 仅 UtilityScore：挑战者须超 best+margin 才换；margin 内先比优先桶再比距离 |

profiles：

| 字段 | 默认 | 这样配会产生什么效果 |
|---|---|---|
| DecisionMakers | 必填 ≥1 | 决策者 id 数组；须连续区间 |
| DecisionIntervalSteps | 1 | 思考步频，须为正 |
| MaxCandidates | 64 | 单次评估候选上限，须为正 |
| DefaultStance | 可选 | 语义键；`DefaultStanceId` 数字写法显式拒绝（编译了但暂无系统消费，@@ai7@@） |

## 3. 文件结构

目录条目 `AI/decision_makers.json`（根数据为空，由 mod 贡献）、目录条目 `AI/profiles.json`（根数据为空，由 mod 贡献）（各自 ArrayById）。实体挂接：模板加 `UtilityAiAgent` 组件写 ProfileId（ent-01）。

## 4. 运行时加载效果

编译时两层各解析成 offset+count 区间；profiles 空且十表非空即整包报错；DefaultStance 在此解析为 stance 槽位。

## 5. 异常处理

| 异常情形 | 系统响应 |
|---|---|
| Decisions/DecisionMakers 空或不连续 | 启动失败：contiguous compiled range |
| DecisionIntervalSteps/MaxCandidates ≤ 0 | 启动失败：must be positive |
| 写 DefaultStanceId | 启动失败：Use DefaultStance with a stance key |
| 十表非空而 profiles 空 | 启动失败：at least one profile |

## 6. 实例

- `mods/showcases/utility_autocast/UtilityAutocastShowcaseMod/assets/AI/decision_makers.json`（1 条）与 `profiles.json`（1 条，interval 1 / MaxCandidates 32）

**相关文档**：[ai-06 PRD](../prd/ai-05-dm-profiles.md) · [ai-05 配置说明](ai-04-decisions.md) · [ent-01 配置说明](ent-01-templates.md)
