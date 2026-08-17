# ai-11 配置说明 · GOAP 与 HTN 规划

> 配置写法与行为。第一性需求见 [ai-11 PRD](../prd/ai-11-goap-htn.md)；编辑器需求见 [UXD](../uxd/ai-11-goap-htn.md)；现状见 [reference](../reference/ai-11-goap-htn.md)。

## 1. 示例配置

真实例（ai_demo `assets/AI/` 旧栈五文件节选——现状主仓唯一的完整世界状态族样本）：

```json
[ { "id": "HasEnemy" } ]
```

```json
[ { "id": "HasEnemy.FromTarget", "Atom": "HasEnemy",
    "Op": "EntityIsNonNull", "EntityKey": "Attack.TargetEntity" } ]
```

```json
[ { "id": "Attack.Goap", "GoalPresetId": 1, "PlanningStrategyId": 1,
    "Weight": 1.0,
    "Bool": [ { "Atom": "HasEnemy", "TrueScore": 1.0, "FalseScore": 0.0 } ] } ]
```

```json
[ { "id": "Submit.Attack", "Cost": 1,
    "Pre": { "Mask": [], "Values": [] }, "Post": { "Mask": [], "Values": [] },
    "Order": { "OrderTypeKey": "attackTarget", "SubmitMode": 0, "PlayerId": 0 },
    "Bindings": [] } ]
```

htn_domain（DeepObject，ai_demo 现状空表）教学骨架：

```json
{ "Tasks": [ { "TaskId": 0, "FirstMethod": 0, "MethodCount": 1 } ],
  "Methods": [ { "MethodId": 0, "Cost": 1, "Condition": null, "SubtaskOffset": 0, "SubtaskCount": 1 } ],
  "Subtasks": [ { "Index": 0, "Kind": "Action", "RefId": 0 } ],
  "Roots": [ { "GoalPresetId": 1, "RootTaskId": 0 } ] }
```

## 2. 字段与行为

| 表 | 关键字段 | 行为 |
|---|---|---|
| atoms | id | 首现注册 256 位槽；他表引用未声明者报错 |
| projection | Atom/Op/IntKey+IntValue 或 EntityKey | Op 五值 IntEquals/IntGreaterOrEqual/IntLessOrEqual/EntityIsNonNull/EntityIsNull；键须 order_types 声明或内建；语义串数字拒；两组键互斥 |
| utility | GoalPresetId 正/PlanningStrategyId/Weight 默认 1/Bool[]{Atom,TrueScore 1,FalseScore 1} | 策略 None/Goap/Htn/DirectTask |
| goap_actions | Cost 默认 1/Pre+Post{Mask[],Values[]}/Order 必填/Bindings[] | Order 同任务四件（OrderTagId 显式拒）；Bindings Op：IntToOrderI0..I3/EntityToTarget/EntityToTargetContext+SourceKey |
| goap_goals | GoalPresetId/HeuristicWeight 默认 1/Goal{Mask,Values} | A* 启发权重 |
| htn_domain | Tasks/Methods/Subtasks/Roots 四数组 | Subtask Kind=Compound|Action；Roots 绑 GoalPresetId→RootTaskId |

## 3. 文件结构

目录条目 `AI/atoms.json`（根数据为空，由 mod 贡献）、`projection.json`、`utility.json`、`goap_actions.json`、`goap_goals.json`（ArrayById）+ `htn_domain.json`（**DeepObject**，唯一例外）。全族无 schema（I10）。

## 4. 运行时加载效果

atoms 先注册，projection 随后建 WorldStateProjectionTable（Order 黑板键→位）；utility goals 编译 GoalSelector；goap 两表进 ActionLibraryCompiled256（SoA 位掩码+候选索引）与 GoapGoalTable；htn_domain 编译 HtnDomainCompiled256 四数组与根表。

## 5. 异常处理

| 异常情形 | 系统响应 |
|---|---|
| 引用未声明 atom | 启动失败 |
| IntKey/EntityKey 与 Op 不匹配或互斥破坏 | 启动失败，带路径 |
| 黑板键未声明且非内建 | 启动失败 |
| goap_action 缺 Order | 启动失败：Order required |
| OrderTagId 字段 | 启动失败：显式拒绝 |
| PlanningStrategyId/GoalPresetId 非法 | 启动失败 |

## 6. 实例

- `mods/showcases/ai_demo/AIDemoMod/assets/AI/`（真实：atoms/projection/utility/goap_actions/goap_goals 各 1 条 + htn_domain 空表）

**相关文档**：[ai-11 PRD](../prd/ai-11-goap-htn.md) · [ai-01 配置说明](ai-01-utility-overview.md) · [cfg-07 配置说明](cfg-07-merge-rules.md)
