# Feature: Context-Scored Abilities (N1–N8)

> 清单覆盖: N1 自动选目标, N2 距离决定, N3 自身状态决定, N4 目标状态决定, N5 环境决定, N6 连击仪表决定, N7 摇杆偏移, N8 锁定辅助

## 交互层

- **InputConfig**: ReactsTo = **Down**
- **TargetMode**: **Unit** (系统自动选取)
- **Acquisition**: **ContextScored** ← 这是唯一的新增值

## 核心概念: ContextGroup + ScorePipeline

这是当前 Ludots 架构中**唯一需要新增的交互层概念**。

现有的 4 种 InteractionMode (TargetFirst/SmartCast/AimCast/SmartCastWithIndicator) 全部是 Explicit — 玩家明确选择目标。

ContextScored 模式下:
1. 一个 InputBinding 绑定一个 **ContextGroup** (候选能力集合)
2. 每个候选有 **Precondition** (准入条件) 和 **ScoreWeight** (评分权重)
3. 按键时, ScorePipeline 对所有通过 Precondition 的候选评分, 选最高分
4. 系统自动获取目标, 执行

## 实现方案

### 数据结构设计

```csharp
// 新增: ContextGroup 定义
public struct ContextCandidate
{
    public int AbilityId;            // 候选能力
    public int TargetModeOverride;   // 该候选的 TargetMode (None/Unit/Point/Direction)
    public int PreconditionGraphId;  // 准入条件 Graph (B[0]=pass/fail)
    public int ScoreGraphId;         // 评分 Graph (F[0]=score)
    public int BasePriority;         // 基础优先级
}

public struct ContextGroup
{
    public const int MAX_CANDIDATES = 16;
    public int Count;
    public ContextCandidate[] Candidates;  // 排序 by BasePriority DESC
}

// 新增: ContextGroupRegistry
public class ContextGroupRegistry
{
    // groupId → ContextGroup
    public ContextGroup Get(int groupId);
}
```

### 评分 Graph 约定

```
输入寄存器:
  E[0] = caster
  E[1] = candidate_target (由系统遍历)
  F[0] = (output) score
  B[0] = (output) valid

内置评分因子 (Graph ops 或 builtin):
  distance_score = 1.0 - (distance / max_range)
  angle_score = dot(caster_forward, to_target_dir)
  input_bias = dot(input_direction, to_target_dir)
  tag_bonus = target HasTag("stunned") ? +50 : 0
  env_bonus = caster HasTag("near_wall") ? +30 : 0
```

### N1: 自动选最优近战目标 (Arkham/Spider-Man)

```
ContextGroup "melee_attack":
  candidate[0]: light_attack
    precondition: NearestEnemy distance < 300cm
    score: distance_score * 0.5 + angle_score * 0.3 + input_bias * 0.2

  candidate[1]: leap_attack
    precondition: NearestEnemy distance IN [300, 800cm]
    score: distance_score * 0.3 + angle_score * 0.3 + input_bias * 0.4

流程:
  1. 按下攻击键
  2. SpatialQuery 收集半径 800cm 内所有 Enemy
  3. 对每个 enemy, 评分 light_attack 和 leap_attack
  4. 如果最近敌人 < 300cm → light_attack 得分高
  5. 如果最近敌人 300-800cm → leap_attack 得分高
  6. 选取最高分, 自动锁定该 enemy, 执行
```

### N2: 距离决定技能变体

```
ContextGroup "attack":
  candidate: punch       (distance < 150cm, score: proximity)
  candidate: leap_strike (distance 150-500cm, score: angle)
  candidate: lunge       (distance 500-800cm, score: input_bias)
```

### N3: 自身状态决定

```
ContextGroup "attack":
  candidate: ground_combo     (precondition: NOT HasTag("airborne"))
  candidate: air_combo        (precondition: HasTag("airborne"))
  candidate: wall_combo       (precondition: HasTag("on_wall"))
```

### N4: 目标状态决定

```
ContextGroup "attack":
  candidate: ground_takedown  (precondition: target HasTag("knocked_down"), priority: 100)
  candidate: disarm           (precondition: target HasTag("armed"), priority: 90)
  candidate: normal_strike    (precondition: always true, priority: 10)
```

### N5: 环境决定

```
ContextGroup "attack":
  candidate: wall_slam        (precondition: caster HasTag("near_wall"), priority: 95)
  candidate: throw_object     (precondition: env HasTag("throwable") in radius 200cm, priority: 90)
  candidate: ledge_kick       (precondition: target HasTag("near_ledge"), priority: 85)
  candidate: normal           (precondition: always, priority: 10)
```

### N6: 连击仪表决定

```
ContextGroup "special":
  candidate: special_takedown (precondition: Attribute(combo_meter) >= 12, priority: 100)
  candidate: basic_attack     (precondition: always, priority: 10)
```

### N7: 摇杆偏移影响选取

```
评分因子中:
  input_bias = dot(stick_direction, to_target_dir)
  权重 0.3-0.4 → 推向某个敌人就倾向选他
```

### N8: 锁定辅助 (Soft Lock-on)

```
实现:
  1. Toggle lock-on → AddTag("locked_on") + store locked_entity in Blackboard
  2. 攻击时:
     if HasTag("locked_on"):
       直接使用 locked_entity 作为 target, 跳过 ContextScoring
     else:
       正常 ContextScoring
```

- 锁定辅助不是 ContextScored 的替代, 而是覆盖

## Ludots 集成点

### 在 InputOrderMappingSystem 中新增 ContextScored 路径

```
现有流程:
  TargetFirst → HandleTargetFirst()
  SmartCast → HandleSmartCast()
  AimCast → HandleAimCast()
  SmartCastWithIndicator → HandleSmartCastWithIndicator()

新增:
  ContextScored → HandleContextScored()
    1. 获取 ContextGroup (from InputOrderMapping.ContextGroupId)
    2. SpatialQuery 收集候选目标
    3. 遍历 candidates × targets, 执行 precondition + score graphs
    4. 选最高分 (candidate, target) 对
    5. 构建 Order (ability=candidate.AbilityId, target=best_target)
    6. Submit
```

### 配置

```json
{
  "interactionMode": "ContextScored",
  "mappings": [
    {
      "actionId": "Attack",
      "trigger": "PressedThisFrame",
      "contextGroupId": "melee_attack",
      "isSkillMapping": false
    }
  ]
}
```

## 依赖组件

| 组件 | 状态 |
|------|------|
| SpatialQueryService | ✅ 已有 |
| GraphExecutor (precondition/score) | ✅ 已有 |
| Tag/Attribute 查询 | ✅ 已有 |
| InputOrderMappingSystem | ✅ 已有 (需扩展) |

## 新增需求

| 需求 | 优先级 | 说明 |
|------|--------|------|
| **ContextGroup 数据结构** | **P0** | ContextCandidate, ContextGroup, ContextGroupRegistry |
| **ContextScored InteractionMode** | **P0** | InputOrderMappingSystem.HandleContextScored() |
| **ContextGroup JSON 配置 + Loader** | **P0** | 配置驱动 |
| Score Graph 评分因子 ops | P1 | distance_score, angle_score, input_bias 等便利 ops |
| Lock-on 集成 (覆盖 ContextScored) | P2 | N8 |
