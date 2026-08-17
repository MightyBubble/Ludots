# ai-04 配置说明 · 效用输入

> 配置写法与行为。第一性需求见 [ai-03 PRD](../prd/ai-02-inputs.md)；编辑器需求见 [UXD](../uxd/ai-02-inputs.md)；现状见 [reference](../reference/ai-02-inputs.md)。

## 1. 示例配置

真实例（utility_autocast 目录条目 `AI/inputs.json`（根数据为空，由 mod 贡献） 全量）：

```json
[
  { "id": "Input.UtilityAutocast.Distance", "Kind": "DistanceToTarget" },
  { "id": "Input.UtilityAutocast.TargetHealth", "Kind": "GraphScore", "GraphKey": "Graph.UtilityAutocast.TargetHealth" }
]
```

教学骨架（覆盖其余 Kind）：

```json
[
  { "id": "In.Example.Const",    "Kind": "Constant", "Value": 2 },
  { "id": "In.Example.Bucket",   "Kind": "TargetPriorityBucket", "DefaultPriority": 3 },
  { "id": "In.Example.Ready01",  "Kind": "ActuatorReadiness01", "ActuatorId": 0 },
  { "id": "In.Example.TgtBoss",  "Kind": "TargetHasTag", "Tag": "State.Boss" },
  { "id": "In.Example.SrcStealth","Kind": "SourceHasTag", "Tag": "State.Stealth" },
  { "id": "In.Example.SkillUp",  "Kind": "AbilityReady", "AbilityKey": "Ability.Example.Fire" }
]
```

## 2. 字段与行为

| Kind | 专属字段 | 采样返回 |
|---|---|---|
| Constant | Value（默认 1，整数） | Value 本身 |
| DistanceToTarget | — | actor 到目标的距离 |
| TargetPriorityBucket | DefaultPriority（默认 0） | 目标 UtilityAiTargetPriority.Bucket，无组件回默认 |
| ActuatorReadiness01 | ActuatorId（必填正数） | ActuatorReadiness.Ready01，无组件回 0 |
| GraphScore | GraphKey 或 GraphId | Score 图执行输出 |
| TargetHasTag / SourceHasTag | Tag（必填） | 有 tag 返 1，否则 0 |
| AbilityReady | AbilityKey 或 AbilityId（必填） | 技能就绪返 1，否则 0 |

注意 Constant 只吃整数（问题 I1）：要 0.5 这类小数基线，须绕 GraphScore。Kind 匹配大小写不敏感，但同目录 BT/HFSM 的枚举区分大小写（问题 I2）。

## 3. 文件结构

目录条目 `AI/inputs.json`（根数据为空，由 mod 贡献）（ArrayById，id 去重合并）。无 schema（I10），字段名错了启动期才报。

## 4. 运行时加载效果

CompileInputs 逐条解析 Kind 与参数，登记进 inputIds 字典（Ordinal）供考量引用；GraphScore 在此完成 RequireKind=Score 校验与写 op 黑名单检查。

## 5. 异常处理

| 异常情形 | 系统响应 |
|---|---|
| 未知 Kind | 启动失败：Unsupported input kind |
| ActuatorId ≤ 0 | 启动失败：must be positive |
| GraphScore 指非 Score 图或含写 op | 启动失败（安全校验） |
| AbilityKey/AbilityId 未注册或不一致 | 启动失败：unknown ability key |
| Tag 未注册 | 启动失败，带路径 |

## 6. 实例

- `mods/showcases/utility_autocast/UtilityAutocastShowcaseMod/assets/AI/inputs.json`（真实，2 条）

**相关文档**：[ai-03 PRD](../prd/ai-02-inputs.md) · [ai-04 配置说明](ai-03-norm-curves.md) · [ai-05 配置说明](ai-04-decisions.md)
