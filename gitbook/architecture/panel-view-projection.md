# 面板视图投影：元素模板 · 主体类型 · 容器只编排

本页是面板「画面只消费一种投影」合同的 SSOT（落实 G12）。与[四皮面板](panel-skins.md)、[面板目录总合同](panel-catalog-designs.md)正交。

## 1. 概述

| 资产 | 管什么 |
|---|---|
| **查询图（容器）** | 圈人/圈任务/… → 写出集合 + 面板级 Summary |
| **元素模板** | 声明 **subject（解什么类型）** + 自有 **graph/pins/layout**；不知道挂在 list 还是 grid |
| **容器面板** | 集合键 + 引用哪个元素模板 + list/grid 编排；默认把成员 **透传** 给元素作 scope |
| **皮** | 长相 |

同一份 `panel.unit.roster`：今天挂 list，明天挂 grid——元素配置不改。

**透传（主路径）**：父级不拆列；每一行把集合里的成员交给元素，由元素自己的图解析 pins。

## 2. 结构

```text
容器 graph ──► EntityCollection /（日后 Task·Ability 集合）+ pins
                    │
                    ▼ 透传成员为 scope
元素模板（subject + graph + pins + layout）
                    │
                    ▼
              list / grid 编排
```

## 3. 详情

### 3.1 元素模板（可嵌面板模板）

与宿主面板同一目录：`Panels/panel_templates.json`。  
**命名不带 `item`**；用 `panel.<域>.<名>`。

元素 **必须** 声明 `subject`（自己解析的数据类型）：

| subject | 集合侧 | 元素 graph 的 scope | 主体表面（图 Summary 尚不能表达的身份信息） |
|---|---|---|---|
| `Entity` | `EntityCollection` | 该实体 | `displayName` ← Name 组件 |
| `Task` | （预留） | 该任务句柄 | 预留，未落地前引用 fail-closed |
| `Ability` | （预留） | 该技能句柄 | 预留，未落地前引用 fail-closed |

未知 `subject` → 装载失败。

```jsonc
{
  "id": "panel.unit.roster",
  "subject": "Entity",
  "graph": "Graph.Unit.RosterCard",
  "pins": [
    { "name": "health", "key": "unit.roster.health", "mode": "realtime", "default": 0 },
    { "name": "healthMax", "key": "unit.roster.healthMax", "mode": "realtime", "default": 0 },
    { "name": "stunned", "key": "unit.roster.stunned", "mode": "realtime", "default": 0 }
  ],
  "layout": {
    "controls": [
      { "type": "label", "bind": "displayName" },
      { "type": "progressBar", "current": "health", "max": "healthMax" },
      { "type": "badge", "bind": "stunned", "text": "晕眩", "showWhen": true }
    ]
  }
}
```

| 规则 | |
|---|---|
| 有 `subject` | 可被 `collections[].template` 引用 |
| 无 `subject` | 普通宿主面板（CreatePanel），不可作集合元素 |
| 元素 **禁止** | `collections`（本切片不嵌套集合） |
| 数值 / bool | 一律元素 **graph → pins** |
| `displayName`（Entity） | 主体表面，layout 可 bind；不是父级拆列 |

### 3.2 容器面板

```jsonc
{
  "id": "panel.entity.list",
  "graph": "Graph.Entity.List",
  "pins": [
    { "name": "rowCount", "key": "panel.roster.rowCount", "mode": "realtime", "default": 0 }
  ],
  "collections": [
    {
      "name": "units",
      "collectionKey": "panel.roster.units",
      "template": "panel.unit.roster"
    }
  ],
  "layout": {
    "controls": [
      { "type": "label", "prefix": "在编 ", "bind": "rowCount" },
      { "type": "list", "bind": "units" }
    ]
  }
}
```

| 字段 | 含义 |
|---|---|
| `collections[].collectionKey` | 容器图写出的集合 |
| `collections[].template` | 元素模板 id（必须带匹配的 `subject`） |
| `list` / 日后 `grid` | 只编排；**禁止** `itemControls` |

装载期：`template.subject` 与集合种类必须相容（本切片：`EntityCollection` ↔ `Entity`）。

### 3.3 元素图示例（Entity）

```jsonc
{
  "id": "Graph.Unit.RosterCard",
  "kind": "Query",
  "entry": "hp",
  "nodes": [
    { "id": "hp", "op": "LoadSelfAttribute", "attribute": "Health" },
    { "id": "hpBase", "op": "LoadSelfAttributeBase", "attribute": "Health" },
    { "id": "self", "op": "LoadSelf" },
    { "id": "stunned", "op": "HasTag", "tag": "Status.Stunned" }
  ],
  // control/value edges + Summary outputs → unit.roster.*
}
```

每行：以该实体为 owner 跑一遍元素图，再读 pins。

### 3.4 运行时

1. 容器图 eval → 集合 + 面板 pin  
2. 对集合每个成员：元素图 eval(scope=成员) → 读 pins；按 subject 附带表面（Entity→displayName）  
3. 用元素 `layout` 画每一行/格  
4. 结构错误 fail-closed；图失败 → pin 缺省，不炸面板  

## 4. 场景

- 名册 list + `panel.unit.roster`（Entity）  
- 日后 grid 引用同一 `panel.unit.roster`  
- 日后任务条：`subject: "Task"` + 任务集合（合同先占位）  

## 5. 边界

- 不做父级拆列与元素自解对等双轨  
- 父→子额外参数（非成员本身）本切片不做；需要时另立显式 `params` 合同  
- Task/Ability 集合管线未落地前，配置写了对应 subject/引用 → 装载或绑定 fail-closed  
- 点击行选中仍属 #1015  
- 小地图 marker 不进本投影  

## 6. UAT

```gherkin
Feature: 元素自己声明解什么，并自己跑图
  Scenario: 元素标明 Entity
    Given panel.unit.roster 声明 subject 为 Entity 且自带 graph
    When 名册 list 引用该模板
    Then 每一行以该实体为 scope 跑元素图并画出 pins 与名字

  Scenario: 容器不内联行控件
    Given list 控件没有 itemControls
    And collections.template 指向元素 id
    Then 装载成功且行画面来自元素 layout

  Scenario: subject 与集合种类不一致则失败
    Given 某元素 subject 为 Task
    And 容器集合是 EntityCollection
    When 装载或绑定
    Then 失败并指出不相容

  Scenario: 命名不使用 item 前缀
    Given 元素 id 为 panel.unit.roster
    Then 配置与目录中不出现 item.* 作为正式命名
```
