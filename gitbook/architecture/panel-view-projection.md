# 面板视图投影：元素模板 · 主体类型 · 容器只编排

本页是面板「画面只消费一种投影」合同的 SSOT（落实 G12）。与[四皮面板](panel-skins.md)、[面板目录总合同](panel-catalog-designs.md)正交。

**集合从哪来、成员是实例还是模板 id**：上游类型 SSOT 见[查询图集合输出](query-graph-collection-outputs.md)。本页不发明集合种类；只规定元素 `subject` 如何消费已类型化的集合。

## 1. 概述

| 资产 | 管什么 |
|---|---|
| **查询图（容器）** | 圈人/圈任务/… → 写出集合 + 面板级 Summary |
| **元素模板** | 声明 **subject（解什么类型）** + 自有 **graph/pins/layout**；不知道挂在 list 还是 grid |
| **容器面板** | 集合键 + 引用哪个元素模板 + list/grid 编排；默认把成员 **透传** 给元素作 scope |
| **皮** | 长相 |

同一份 `panel.unit.roster`：今天挂 list，明天挂 grid——元素配置不改。

**透传（主路径）**：父级不拆列；每一行把集合里的成员交给元素，由元素自己的图解析 pins。

**复合编排**（嵌套名单 / 反查名单 / 聚合 present）：配置形状见[查询图集合输出 §2.3 · §3.7](query-graph-collection-outputs.md)；本页规定面板如何声明子集合与 `present`，仍不发明集合种类。

## 2. 结构

```text
容器 graph ──► 类型化集合（见查询图集合输出）+ pins
                    │
                    ▼ 透传成员为 scope
元素模板（subject + graph + pins + layout）
                    │
                    ▼
              list / grid 编排
```

> 今日已接线：`EntityCollection` ↔ `subject: Entity`。  
> Effect 实例/模板、Ability 槽/定义、Item 实例/定义等：合同见[查询图集合输出](query-graph-collection-outputs.md)；未接线前配置即 fail-closed。
## 3. 详情

### 3.1 元素模板（可嵌面板模板）

与宿主面板同一目录：`Panels/panel_templates.json`。  
**命名不带 `item`**；用 `panel.<域>.<名>`。

元素 **必须** 声明 `subject`（自己解析的数据类型）：

| subject | 集合侧 | 元素 graph 的 scope | 主体表面（图 Summary 尚不能表达的身份信息） |
|---|---|---|---|
| `Entity` | `EntityCollection`（成员=实体实例） | 该实体 | `displayName` ← Name 组件 |
| `Task` / `Ability` 等 | 见[查询图集合输出](query-graph-collection-outputs.md) 相容表 | 按成员身份透传 | 未接线前引用 fail-closed |

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
| 元素 **可** 声明子 `collections` | **复合切片**：子袋须由图以该成员为 scope/owner 写出；禁止在元素里内联过滤排序 |
| 平面名册切片 | 无子集合的元素仍合法（今日 `panel.unit.roster`） |
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
      {
        "type": "list",
        "bind": "units",
        "viewportHeight": 120,
        "itemExtent": 56,
        "virtualize": true,
        "overscan": 2
      }
    ]
  }
}
```

| 字段 | 含义 |
|---|---|
| `collections[].collectionKey` | 容器图写出的集合 |
| `collections[].template` | 元素模板 id（必须带匹配的 `subject`） |
| `list` / `grid` / `column` / `aggregate` | 只编排；**禁止** `itemControls` |
| `present` | 消费形态：`list`（默认）/ `grid`（必填 `columns`）/ `column`（横向）/ `aggregate`（必填 `aggregate.count`） |
| `columns` | 仅 `present=grid`；每行格数 ≥ 1 |
| `aggregate.count` | 仅 `present=aggregate`；`from: totalCount` + 作者自有 `prefix` |

#### list 滚动与虚拟窗口

| 字段 | 含义 |
|---|---|
| `viewportHeight` | 可视高度；有值则包进 `ScrollView`，可滚 |
| `itemExtent` | 行高（虚拟化用）；`virtualize` 时必填或默认 56 |
| `virtualize` | 只对可见窗（+ `overscan`）跑元素图并挂 spacer；**禁止**每帧全量投影 |
| `overscan` | 窗外缓冲行数，默认 2 |

`virtualize: true` 必须带 `viewportHeight`。名册人数仍来自集合 `TotalCount` / 面板 pin，不因虚拟窗口变少。

#### 聚合 present（同一集合，不同画面）

`present: "aggregate"` 时仍绑定完整类型化集合；用配置取首位成员外观 + 总数（`TotalCount` 或并行 pin）。  
空袋不得静默画「有货」图标。形状示意见[查询图集合输出 §3.7.3](query-graph-collection-outputs.md)。

装载期：`template.subject` 与集合种类必须相容（今日：`EntityCollection` ↔ `Entity`；扩展见集合输出合同）。

### 3.3 复合：显式 inputs 与 collections.source（与集合输出合同同字）

跨层接线 **正表** 在[查询图集合输出 §2.4](query-graph-collection-outputs.md)；本页只列消费侧必写字段，字段名不得另起同义词。

| 字段 | 必填 | 含义 |
|---|---|---|
| `inputs[].name` | 用 inputs 时 | 子空间逻辑名 |
| `inputs[].from.space` | 用 inputs 时 | 仅 `parent` |
| `inputs[].from.output` | 用 inputs 时 | 父 graph output id / collectionKey / Summary key |
| `inputs[].type` | 用 inputs 时 | 与父 output 装载期强校验 |
| `collections[].source` | 是（复合袋） | 仅 `selfGraph` \| `input` |
| `collections[].collectionKey` | `source=selfGraph` | 本图 Collection output 键 |
| `collections[].input` | `source=input` | 指向 `inputs[].name` |
| list `bind` | 是 | 仅 `collections[].name`（或合同允许时的 input 名） |

| 规则 | |
|---|---|
| 子袋来源 | 唯一：selfGraph **或** 已校验 input；禁止只写 key 不写 source |
| subject 相容 | 消费该袋的元素 subject ↔ 集合类型 |
| 过滤排序 | 只在图内 |
| 嵌套深度 | 建议 ≤ 2 |
| 禁止 | 同名撞袋；控件私扫世界 |

### 3.4 元素图示例（Entity）

```jsonc
{
  "id": "Graph.Unit.RosterCard",
  "kind": "Query",
  "entry": "hp",
  "nodes": [
    { "id": "hp", "op": "LoadSelfAttribute", "attribute": "Health" },
    { "id": "hpMax", "op": "LoadSelfAttribute", "attribute": "Health" },
    { "id": "caster", "op": "LoadCaster" },
    { "id": "stunned", "op": "HasTag", "tag": "Status.Stunned" }
  ],
  // control/value edges + Summary outputs → unit.roster.*
}
```

每行：以该实体为 owner 跑一遍元素图，再读 pins。

### 3.5 运行时

1. 容器图 eval → 集合 + 面板 pin  
2. 非虚拟列表：对集合每个成员跑元素图 → pins + subject 表面  
3. 若元素声明子集合：以该成员为 scope 确保子袋已由相关查询写出，再按子 `present` 投影  
4. 虚拟列表：只对可见窗（+ overscan）成员跑元素图；UI 用 spacer + ScrollView  
5. `aggregate`：读 TotalCount（或 pin）+ 首位成员表面/图标；不展开全部行  
6. 结构错误 fail-closed；图失败 → pin 缺省，不炸面板  

压测基线（`PanelListVirtualizationPerfTests`）：1000 成员时，窗口投影行数与分配量须显著低于全量。

## 4. 场景

- 名册 list + `panel.unit.roster`（Entity）  
- 日后 grid 引用同一 `panel.unit.roster`  
- 单位详情嵌技能栏（元素子集合 + Ability 槽袋）  
- 技能格反查持有者（子集合 + Entity 袋）  
- 背包堆叠聚合（`present: aggregate`）  
- 任务条等：subject 与集合类型见[查询图集合输出](query-graph-collection-outputs.md)  

## 5. 边界

- 不做父级拆列与元素自解对等双轨  
- 父→子数据必须经显式 `inputs`/`source` 接线并类型校验；不做隐式同名撞袋  
- 父→子额外「非引脚参数」本切片不做；需要时另立显式 `params` 合同  
- Task/Ability/Effect 等集合类型未接线前，配置写了对应 subject/引用 → 装载或绑定 fail-closed（类型表见[查询图集合输出](query-graph-collection-outputs.md)）  
- 不硬编码 EntityInfo / AbilityIcon / ItemStack 控件；复合靠配置  
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

  Scenario: 千人名单也能滚得动
    Given 名册集合有一千个单位且 list 开启 virtualize
    When 我打开名册并向下滚动
    Then 同一时间只画出视口附近的行
    And 名单人数仍显示一千

  Scenario: 短视口可以滚动名册
    Given 名册 list 配置了 viewportHeight 且行总高超过视口
    When 我向下滚动名单
    Then 滚动偏移更新且集合总人数不变
    And 晕眩徽标仍能在可见窗内找到

  Scenario: 单位详情可以嵌自己的技能名单
    Given 单位详情元素的图明文输出技能槽名单
    And 详情 list 绑定该输出
    When 我打开该单位详情
    Then 技能栏只显示该单位的技能

  Scenario: 反查必须声明父级候选引脚输入
    Given 技能格要显示持有者
    And 配置写明 inputs 来自父级「候选编队」实体集合输出
    When 装载成功且父级候选里有人会该技能
    Then 持有者名单只包含会该技能的人

  Scenario: 集合可以聚合成首位加数量
    Given 某栏位绑定完整物品实例名单且 present 为 aggregate
    When 名单里有多件同类物
    Then 画面显示首位外观与总数
    And 不会把名单改写成只含一个成员的假集合
```
