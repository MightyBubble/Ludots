# ai-00 配置说明 · AI 行为层总论

> 配置写法与行为。第一性需求见 [ai-00 PRD](../prd/ai-01-utility-overview.md)；编辑器需求见 [UXD](../uxd/ai-01-utility-overview.md)；现状见 [reference](../reference/ai-01-utility-overview.md)。

## 1. 示例配置

主仓 `assets/AI/` 只装行为资产（教学骨架即全量）：

```json
[
  { "id": "bt.patrolChaseAttack", "root": "root", "nodes": [
    { "id": "root", "kind": "Selector", "children": ["engage", "patrol"] },
    { "id": "patrol", "kind": "Action", "leaf": "ScriptSlice", "action": "bt.patrol" }
  ] }
]
```

真实 mod 全景（utility_autocast，`assets/AI/` 11 文件）：

```text
atoms.json  inputs.json  normalizations.json  curves.json
target_filters.json  tasks.json  decisions.json  decision_makers.json
profiles.json  stances.json  actuators.json
```

## 2. 字段与行为

18 张表 = 效用十表 + 世界状态六表 + 行为两表：

| 组 | 表 | 专篇 |
|---|---|---|
| 效用感知 | inputs / normalizations / curves | ai-01 / ai-02 |
| 效用决断 | target_filters / decisions / decision_makers / profiles | ai-05 / ai-03 / ai-04 |
| 效用行动 | tasks / stances / actuators | ai-06 / ai-07 |
| 世界状态 | atoms / projection / utility / goap_actions / goap_goals / htn_domain | ai-10 |
| 图行为 | behavior_trees / hfsm | ai-08 / ai-09 |

全部 ArrayById 按 `id` 去重合并；唯 `htn_domain.json` DeepObject。

## 3. 文件结构

`assets/AI/<表名>.json`，mod 与主仓同路径叠加。主仓现状仅 4 文件（BT+schema、HFSM+schema），效用十表全部由 mod 提供。

## 4. 运行时加载效果

AiConfigLoader.LoadAndCompile 按固定序消费 18 表：先注册 atoms 与投影规则，再编译 GOAP/HTN，随后十表聚成 UtilityAiCompiledRuntime，最后 GraphBehaviorDefinitionLoader 收 BT/HFSM。效用十表任一有条目即要求 AiConfigValidationContext（order_types 与 ability 注册表）在场。

## 5. 异常处理

| 异常情形 | 系统响应 |
|---|---|
| 效用十表非空但 profiles 为空 | 启动失败：must declare at least one profile |
| 缺校验上下文而配置引用 AbilityKey/OrderTypeKey | 启动失败：requires AiConfigValidationContext |
| 表间引用名未定义 | 启动失败，带 表:id.字段 路径 |

## 6. 实例

- 主仓：`assets/AI/behavior_trees.json`、`assets/AI/hfsm.json`（各配 schema，见 ai-08/ai-09）
- mod 全集：`mods/showcases/utility_autocast/UtilityAutocastShowcaseMod/assets/AI/`（11 文件）
- 旧栈样本：`mods/showcases/ai_demo/AIDemoMod/assets/AI/`（世界状态五文件）

**相关文档**：[ai-00 PRD](../prd/ai-01-utility-overview.md) · [ai-01 配置说明](ai-02-inputs.md) · [cfg-04 配置说明](cfg-04-config-tables.md)
