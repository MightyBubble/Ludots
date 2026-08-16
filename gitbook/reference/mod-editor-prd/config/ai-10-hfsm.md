# ai-09 配置说明 · 层次状态机

> 配置写法与行为。第一性需求见 [ai-09 PRD](../prd/ai-10-hfsm.md)；编辑器需求见 [UXD](../uxd/ai-10-hfsm.md)；现状见 [reference](../reference/ai-10-hfsm.md)。

## 1. 示例配置

引擎默认（主仓 `assets/AI/hfsm.json` 现状两台之一，生命周期版 combat 态节选）：

```json
[
  {
    "id": "hfsm.sentry.scripted",
    "root": "root",
    "states": [
      { "id": "root", "kind": "Compound", "children": ["idle", "alerting"], "defaultChild": "idle" },
      { "id": "idle", "kind": "Leaf" },
      { "id": "combat", "kind": "Leaf",
        "onEnter": "hfsm.combat.onEnter",
        "onTick":  "hfsm.combat.onTick",
        "onExit":  "hfsm.combat.onExit" }
    ],
    "transitions": [
      { "from": "idle", "to": "alert", "predicate": "StimulusLatched" },
      { "from": "alert", "to": "combat", "predicate": "Always", "condition": "hfsm.cond.alwaysTrue" },
      { "from": "retreat", "to": "idle", "predicate": "Always" }
    ]
  }
]
```

纯谓词版 `hfsm.sentry` 同结构但无 onEnter/onTick/onExit/condition——两台并存示范两种写法。

## 2. 字段与行为

| 字段 | 这样配会产生什么效果 |
|---|---|
| states[].kind | Leaf/Compound，**区分大小写**（I2）；Compound 须 defaultChild、Leaf 不得有 children |
| states[].onEnter/onTick/onExit | 生命周期图（ActionLib 名，host=Hfsm）；64 步预算内禁 Yield |
| transitions[].from/to/predicate | 必填；predicate 三值 Never/Always/StimulusLatched |
| transitions[].priority | 可选，同 from 降序取优；**平级后定义者胜**（问题 I8） |
| transitions[].condition | 可选条件图名；ReturnInt≠0 判真 |

StimulusLatched：运行期 LatchStimulus 置位后该谓词为真，触发即自动清零。

## 3. 文件结构

`assets/AI/hfsm.json`（ArrayById）。schema 存在（hfsm.schema.json：state kind/predicate 枚举、transitions 三必填+两可选），不参与流水线校验（I10）。

## 4. 运行时加载效果

GraphBehaviorDefinitionLoader 逐台解析：state kind 与 predicate 严格枚举、Compound defaultChild/Leaf children 校验、禁多父禁不可达；onEnter/onTick/onExit/condition 图名进 GraphActionCatalog.Require(host=Hfsm)。

## 5. 异常处理

| 异常情形 | 系统响应 |
|---|---|
| Compound 缺 defaultChild / Leaf 带 children | 启动失败 |
| 多父 / 不可达状态 | 启动失败 |
| predicate/kind 拼写或大小写错 | 启动失败（Enum.TryParse 严格） |
| 图名未注册 | 启动失败（ActionLib Require） |
| 生命周期图 64 步内未 halt | 运行期报错：must halt within budget |

## 6. 实例

- 引擎默认：`assets/AI/hfsm.json`（hfsm.sentry 纯谓词版 + hfsm.sentry.scripted 生命周期版，各 6 状态 4 转移）

**相关文档**：[ai-09 PRD](../prd/ai-10-hfsm.md) · [ai-08 配置说明](ai-09-behavior-trees.md) · [ai-00 配置说明](ai-01-utility-overview.md)
