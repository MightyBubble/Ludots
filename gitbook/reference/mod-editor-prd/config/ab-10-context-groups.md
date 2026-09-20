# ab-10 配置说明 · 上下文组

> 配置写法与行为。第一性需求见 [ab-10 PRD](../prd/ab-10-context-groups.md)；编辑器需求见 [UXD](../uxd/ab-10-context-groups.md)；现状见 [reference](../reference/ab-10-context-groups.md)。

## 1. 示例配置

真实实例（interaction 沙盒第一组，节选两候选）：

```json
[ { "id": "interaction_arcweaver_action",
    "rootAbilityId": "Ability.Interaction.Arcweaver.ActionContext",
    "searchRadiusCm": 900,
    "candidates": [
      { "abilityId": "Ability.Interaction.Arcweaver.ArcDash", "basePriority": 40,
        "maxDistanceCm": 900, "distanceWeight": 30, "maxAngleDeg": 110, "angleWeight": 10,
        "hoveredBiasScore": 8, "requiresTarget": true },
      { "abilityId": "Ability.Interaction.Arcweaver.NovaPulse", "basePriority": 10,
        "requiresTarget": false } ] } ]
```

两图骨架（教学骨架）：

```json
"candidates": [ { "abilityId": "Ability.Ex.Greet", "basePriority": 5, "requiresTarget": false,
  "preconditionGraph": "Graph.Ex.CanGreet", "scoreGraph": "Graph.Ex.GreetScore" } ]
```

## 2. 字段与行为

| 字段 | 这样配会产生什么效果 |
|---|---|
| `id` | 组身份（同 id 深合并） |
| `rootAbilityId` | 必填已知技能：组的入口（上下文动作槽） |
| `searchRadiusCm` | 必填非负：候选空间搜索半径 |
| `candidates[]` | 必填非空：候选清单 |
| `candidate.abilityId` / `basePriority` / `requiresTarget` | 必填：候选技能 / 打分起点 / 是否需要目标实体 |
| `maxDistanceCm`+`distanceWeight` | requiresTarget=true 必填：距离硬过滤 + (1−d/max)×权重 计分 |
| `maxAngleDeg`+`angleWeight` | 同上：对朝向的硬过滤与归一化计分 |
| `hoveredBiasScore` | 同上：悬停目标加成分 |
| `preconditionGraph` / `scoreGraph` | 可选：Validation 图不过即出局 / Score 图返回值累加（均须可解析） |

平分裁决：先比实体 id，再比槽号。requiresTarget=false 时距离/角度/悬停字段可缺省（计 0）。

## 3. 文件结构

目录条目 `GAS/context_groups.json`（根数据为空，由 mod 贡献）（目录登记的表，数组按 id 深合并）。引用许可序：abilities → context_groups。

## 4. 运行时加载效果

编译期校验组结构与技能/图引用并注册；打分消费在根技能激活时按"I0=根槽位"解析组，做空间查询与逐候选打分。

## 5. 异常处理

| 异常情形 | 系统响应 |
|---|---|
| rootAbilityId 缺失/未知；searchRadiusCm 缺失/负；candidates 空；候选缺必填项 | 启动失败 |
| requiresTarget=true 缺距离或角度件 | 启动失败 |
| preconditionGraph/scoreGraph 不可解析 | 启动失败（kind 校验到运行期才做，见 reference） |

## 6. 实例

- `mods/showcases/interaction/InteractionShowcaseMod/assets/GAS/context_groups.json`（3 组：arcweaver/vanguard/commander）

**相关文档**：[ab-10 PRD](../prd/ab-10-context-groups.md) · [ab-01 配置说明](ab-01-definition.md) · [gr-03 配置说明](gr-03-kinds.md)
