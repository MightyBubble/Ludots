# 面板视图投影：独立 Item 模板 · 容器只编排

本页是面板「画面只消费一种投影」合同的 SSOT（落实 G12）。与[四皮面板](panel-skins.md)、[面板目录总合同](panel-catalog-designs.md)正交。

## 1. 概述

核心拆分（**Item 不知道自己挂在谁下面**）：

| 资产 | 管什么 | 不写什么 |
|---|---|---|
| **Entity Query 图** | 圈人、过滤、排序 → `EntityCollection` + Summary | 不关心怎么画一行 |
| **Item 模板** | 「一个实体怎么画」：`fields` + `layout` | 无 `graph`、无 `collectionKey`、不知 list/grid |
| **容器面板**（list / 日后 grid） | 哪份集合 + **引用哪个 item** + 怎么排版 | 不内联行控件、不写 filter/sort |
| **皮 / 主题** | 长相 | 不改数据合同 |

同一份 `item.unit.roster`：今天挂在 list 面板，明天挂在 grid 面板——**item 配置零改动**。

通货：

- **叶子**：标量（float / bool / string）
- **唯一复合**：同构集合（行序 = 图写出的集合序；每行按 **被引用的 item 模板** 填列）

本切片配置面：

1. `Panels/item_templates.json`：独立 item
2. 容器 `collections[]`：`name` + `collectionKey` + `item`（模板 id）
3. 容器 `layout`：`label` / `list`（日后 `grid`）；**禁止** `itemControls` 内联
4. Showcase：查询图存活+血量降序；item 管名字/血条/晕眩；list 容器只编排

## 2. 结构

```text
Query 图
  └─ EntityCollection + Summary pins
           │
           ▼
容器面板 collections[] ── item:"item.unit.roster" ──► Item 模板
  layout: list / grid（只编排）                      fields + layout（一行怎么画）
           │
           ▼
         皮 / 主题
```

| 配置块 | 所在资产 | 职责 |
|---|---|---|
| `fields` + `layout` | **Item 模板** | 一行/一格的列与控件 |
| `pins` / `graph` / `collections` / `layout` | **容器面板** | 面板级标量、集合绑定、排版 |
| `skin` | 容器或实例 | 长相 |

## 3. 详情

### 3.1 Item 模板（独立、可复用）

路径：`Panels/item_templates.json`（ArrayById 合并）。

```jsonc
{
  "id": "item.unit.roster",
  "fields": [
    { "name": "displayName", "kind": "name" },
    { "name": "health", "kind": "attribute", "attribute": "Health" },
    { "name": "healthMax", "kind": "attributeBase", "attribute": "Health" },
    { "name": "stunned", "kind": "tag", "tag": "Status.Stunned" }
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
| 允许根字段 | `id` / `fields` / `layout`（及日后皮级扩展若另立合同） |
| **禁止** | `graph`、`pins`、`collections`、`collectionKey`、`filter`、`sort`、任何父容器语义 |
| `fields[].kind` | `attribute` / `attributeBase` / `tag` / `name`（同前） |
| `layout` 绑定域 | 只见本 item 的 `fields[].name` |
| 未知字段 | 装载 fail-closed |

Item **不**需要知道「我是谁的 item」。

### 3.2 容器面板

路径：仍为 `Panels/panel_templates.json`。

在既有 `id/graph/pins/events/intents/skin` 之外：

- `collections`：数组，可空
- `layout`：对象；缺省 = 旧自动堆行

**禁止**根上再写内联 `lists[].item.fields` / `itemControls`（旧形状 → 装载失败）。

#### `collections[]`

```jsonc
{
  "name": "units",                        // layout list/grid.bind
  "collectionKey": "panel.roster.units",  // 对齐图 EntityCollection
  "item": "item.unit.roster"              // 引用独立 item 模板 id
}
```

| 字段 | 含义 |
|---|---|
| `name` | 容器内集合槽名 |
| `collectionKey` | 图写出的集合键 |
| `item` | **必须**是已注册的 item 模板 id |

行序、成员 = 图集合；容器不得再筛再排。

#### 容器 `layout.controls[]`

| type | 绑定 | 用途 |
|---|---|---|
| `label` | `text` 或 `bind`→pin；可选 `prefix` | 标题、计数 |
| `list` | `bind`→collection 名 | **竖排**重复 item.layout |
| `grid`（日后） | `bind`→collection 名 + 列数等 | **网格**重复同一 item.layout |
| `progressBar` / `badge` | 面板级 pin（若需要） | 少用；行内控件应在 item 里 |

- `list` / `grid` **不得**带 `itemControls`（出现 → 装载失败）
- 行怎么画，只看 `collections[].item` 指向的模板

### 3.3 图侧（查询唯一入口）

与前一版相同：Query 家族过滤/排序 → `EntityCollection` + 推荐 `AggCount` → Summary pin。示例见案 13 / Showcase `Graph.Entity.List`。

### 3.4 运行时合同

1. 装载：先 item 目录，再面板；面板 `collections[].item` 必须解析到存在的 item
2. 刷新：按 item.`fields` 对集合逐行填列（目标定容 SoA）
3. 呈现：容器只选排版；每行/格实例化 **同一份** item.`layout`
4. 结构错误 fail-closed；缺组件 → 字段缺省，不炸面板

## 4. 场景

**名册 list（今天）**

- Item：`item.unit.roster`（名字、血条、晕眩）
- 容器：`panel.entity.list` → `collections` 引用该 item + `layout` 一个 `list`

**单位墙 grid（明天，同 item）**

```jsonc
{
  "id": "panel.entity.grid",
  "graph": "Graph.Entity.List",
  "pins": [ { "name": "rowCount", "key": "panel.roster.rowCount", "default": 0 } ],
  "collections": [
    { "name": "units", "collectionKey": "panel.roster.units", "item": "item.unit.roster" }
  ],
  "layout": {
    "controls": [
      { "type": "label", "prefix": "在编 ", "bind": "rowCount" },
      { "type": "grid", "bind": "units", "columns": 3 }
    ]
  }
}
```

`item.unit.roster` **一行不改**。

## 5. 边界

- Item 不是完整面板：无 CreatePanel 生命周期、无独立 graph 圈人
- 不做面板内 filter/sort
- 不做点击行选中（#1015）
- 不把 Entity 暴露给控件
- 小地图 marker 仍走 Core Minimap SoA
- 旧无 `collections`/`layout` 的标量面板：行为不变
- 本切片落地 `list`；`grid` 合同预留、控件实现可后开

## 6. UAT

```gherkin
Feature: 一行怎么画是独立模板，list 和 grid 都能引用
  作为一个做关卡的人
  我想先写好「一个单位格子长什么样」
  再把它挂到名册或格子墙，而不用复制两份行配置

  Scenario: Item 模板不包含父容器语义
    Given 存在 item.unit.roster，只有 fields 与 layout
    Then 其中没有 collectionKey、没有 list/grid、没有 filter/sort

  Scenario: List 容器只引用 item
    Given panel.entity.list 的 collections 指向 item.unit.roster
    And layout 只有 list 编排、没有 itemControls
    When 名册打开
    Then 每一行按 item 模板画出名字、血条与晕眩徽标

  Scenario: 同一 item 可挂到另一种编排（合同）
    Given 另有容器用 grid 绑定同一 collection 与同一 item id
    Then 不必修改 item.unit.roster 即可复用行画面

  Scenario: 误把行控件写进 list 容器会装载失败
    Given 某 list 控件写了 itemControls
    When 配置装载
    Then 装载失败并指出非法字段

  Scenario: 图仍决定名单与顺序
    Given 查询图已过滤排序
    When 我看名册
    Then 只见存活单位且血量从高到低
    And 在编人数与行数一致
```
