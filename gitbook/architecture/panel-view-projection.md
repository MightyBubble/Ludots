# 面板视图投影：图管集合 · 面板只绑列

本页是面板「画面只消费一种投影」合同的 SSOT（落实 G12）。与[四皮面板](panel-skins.md)、[面板目录总合同](panel-catalog-designs.md)正交：皮管长相，**图管谁在名单里、什么顺序**，**本页只管「行 → 标量列」与控件绑定**。

## 1. 概述

职责劈开（禁止在面板里再造一套查询语言）：

| 层 | 管什么 | 不写什么 |
|---|---|---|
| **Entity Query 图** | 圈人、过滤、排序、写出 `EntityCollection` + Summary 计数 | 不关心血条怎么画 |
| **面板模板 `lists`** | 给集合的每一行声明要读哪些标量列 | 不再写 `filter` / `sort` |
| **面板模板 `layout`** | 把 pin / 列绑到 builtin 控件 | 不看见 Entity |
| **皮 / 主题** | 长相（CSS、九宫、三宫） | 不改数据合同 |

通货仍是：

- **叶子**：标量（float / bool / string）
- **唯一复合**：同构列表（行序 = 图写出的集合序；每行一袋由 `item.fields` 声明的叶子）

本切片配置面：

1. `lists[]`：`name` + `collectionKey` + `item.fields`（**无** filter/sort）
2. `layout.controls[]`：`label` / `progressBar` / `badge` / `list`
3. Showcase：查询图内完成「存活 + 按血量降序」，面板只绑名字/血条/晕眩徽标

运行时目标（与小地图/血条同纪律）：对 `EntityCollection` **定容填列**，热路径不按行 `new Dictionary`；本页先锁配置合同，缓冲 SoA 化可随后落地。

## 2. 结构

```text
Query 图
  QueryAll / QueryFilter* / QuerySortByAttribute / AggCount
       │
       ├─ Summary ──► pins[]（计数等）
       └─ EntityCollection（已过滤、已排序）──► lists[].collectionKey
                │
                ▼
         列绑定（item.fields → 按行读组件）
                │
                ▼
         layout.controls[]（只吃 pin 名 / 列名）
                │
                ▼
         皮 / 主题
```

| 配置块 | 职责 |
|---|---|
| `pins` | 面板级标量（在编人数等）——既有 |
| `lists` | 指向图集合 + 行内列声明 |
| `layout` | 控件树 |

## 3. 详情

### 3.1 模板根字段

在既有 `id/graph/pins/events/intents/skin` 之外允许：

- `lists`：数组，可空
- `layout`：对象；缺省 = 旧自动堆行

未知字段 fail-closed。`lists[]` 上出现 `filter` / `sort` → **装载失败**（请改写到图里）。

### 3.2 `lists[]`（只绑列）

```jsonc
{
  "name": "units",                       // layout list.bind 引用
  "collectionKey": "panel.roster.units", // 与图 EntityCollection 输出一致
  "item": {
    "fields": [
      { "name": "displayName", "kind": "name" },
      { "name": "health", "kind": "attribute", "attribute": "Health" },
      { "name": "healthMax", "kind": "attributeBase", "attribute": "Health" },
      { "name": "stunned", "kind": "tag", "tag": "Status.Stunned" }
    ]
  }
}
```

| kind | 产出 | 读法 |
|---|---|---|
| `attribute` | float | `AttributeBuffer.GetCurrent` |
| `attributeBase` | float | `AttributeBuffer.GetBase` |
| `tag` | bool | `GameplayTagContainer.HasTag` |
| `name` | string | 显示名组件；缺省 `""` |

行序、成员集合 **完全等于** 图写入的 `EntityCollection`；面板不得重排、不得再筛。

### 3.3 `layout.controls[]`

| type | 绑定 | 用途 |
|---|---|---|
| `label` | `text` 或 `bind`；可选 `prefix` | 标题、名字、计数 |
| `progressBar` | `current` + `max`（列名） | 血条 |
| `badge` | `bind`（bool）+ `text` + `showWhen` | 状态徽标 |
| `list` | `bind`→list 名；`itemControls[]` | 按行重复 |

- 面板级 `bind` → `pins[].name`
- `itemControls` 内 → 该 list 的 `item.fields[].name`

### 3.4 图侧约定（查询唯一入口）

值图必须：

1. 用 Query 家族完成过滤/排序（例：`QueryFilterTeam`、`QueryFilterAttributeRange`、`QuerySortByAttribute`）
2. 写出 `EntityCollection`（`destination: EntityCollection`，`type: TargetList`，`collectionKey` 对齐模板）
3. （推荐）`AggCount` → Summary pin，标题「在编 N」与名单人数一致

示例骨架：

```jsonc
{
  "id": "Graph.Entity.List",
  "kind": "Query",
  "entry": "all",
  "nodes": [
    { "id": "all", "op": "QueryAllMapEntities" },
    { "id": "team", "op": "QueryFilterTeam", "teamId": 1 },
    { "id": "minHp", "op": "ConstFloat", "floatValue": 0.001 },
    { "id": "maxHp", "op": "ConstFloat", "floatValue": 999999 },
    { "id": "alive", "op": "QueryFilterAttributeRange", "attribute": "Health" },
    { "id": "sorted", "op": "QuerySortByAttribute", "attribute": "Health", "descending": true },
    { "id": "rowCount", "op": "AggCount" }
  ],
  "outputs": [
    {
      "id": "units",
      "destination": "EntityCollection",
      "type": "TargetList",
      "collectionKey": "panel.roster.units",
      "role": "Display"
    },
    {
      "id": "rowCount",
      "destination": "Summary",
      "type": "Int",
      "source": "rowCount",
      "key": "panel.roster.rowCount"
    }
  ]
}
```

### 3.5 运行时合同

- 刷新：图 eval 写出集合后，按 `item.fields` **按行填列**（目标：定容 SoA，不按行分配字典）
- 死亡成员：图侧应已滤掉；若集合仍含死实体，填列时该行用字段缺省（0 / false / `""`），不炸面板
- 符号：attribute / tag 装载期绑定 id；运行期缺组件 → 缺省，对齐 pin default
- 结构错误（未知字段、`filter`/`sort`、重名、layout 绑错列）→ 装载 fail-closed

## 4. 场景

**实体名册**（`panel_entity_list`）

- 图：己方 → 血量>0 → 按血量降序 → 集合 + 计数
- 面板：只声明列与 layout（名字、血条、晕眩徽标）
- 玩家：进图见名册；掉血后顺序与条长随图刷新变化；晕眩行出徽标

## 5. 边界

- 不做面板内第二套 filter/sort
- 不做技能/效果列表产品面（列绑定合同可复用）
- 不做点击行选中（#1015）
- 不把 Entity 暴露给控件
- 小地图 marker 热路径仍走 Core Minimap SoA，不进本列表投影
- 旧模板无 `lists`/`layout`：行为不变

## 6. UAT

```gherkin
Feature: 名册的人由查询图决定，面板只声明怎么展示
  作为一个做关卡的人
  我想在图里圈存活单位并排好序
  面板模板只写每一行要显示的名字、血条和状态

  Scenario: 图决定名单与顺序
    Given 名册地图已加载且值图已按血量过滤并排序
    When 我看左侧名册
    Then 我只看到存活单位且从上到下血量从高到低
    And 标题上的在编人数与列表行数一致

  Scenario: 面板只声明列与控件
    Given 模板 lists 没有 filter/sort 字段
    And item.fields 声明了名字、血量、晕眩
    When 名册渲染
    Then 每一行有名字和血条
    And 带晕眩状态的那一行出现「晕眩」徽标

  Scenario: 误把过滤写进面板会装载失败
    Given 某模板 lists 条目写了 filter 或 sort
    When 配置装载
    Then 装载失败并指出非法字段

  Scenario: 旧标量面板不受影响
    Given 火球状态模板没有 lists/layout
    When 面板打开
    Then 仍按自动堆行显示 pin
```
