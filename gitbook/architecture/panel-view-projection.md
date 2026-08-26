# 面板视图投影：标量叶子 + 同构列表

本页是面板「画面只消费一种投影」合同的 SSOT（落实 G12）。与[四皮面板](panel-skins.md)、[面板目录总合同](panel-catalog-designs.md)正交：皮管长相，图管业务聚合，**本页管投影形状与控件绑定**。

## 1. 概述

面板与模拟域之间只流通一种通货：

- **叶子**：标量（float / int / bool / string）
- **唯一复合**：同构列表（每一项又是一袋标量叶子）

控件参数永远是标量。实体 / 技能 / 效果 / Tag 等业务身份不得进入控件合同；它们只出现在图的查询侧，或作为列表投影的内部身份（不绑到控件）。

本切片交付：

1. `panel_templates.json` 增补 `lists` + `layout` 声明面
2. 运行时：从图写出的 `EntityCollection` 投影出列表项标量袋（属性 / 属性基数 / Tag / 显示名）
3. 声明式过滤、排序（可叠在图结果之上）
4. builtin 控件：`label` / `progressBar` / `badge` / `list`
5. Showcase：实体名册（过滤存活、按血量排序、条目显示血条与晕眩徽标）

## 2. 结构

```text
图（Query）
  └─ Summary 标量 outputs ──► pins[]（既有路径）
  └─ EntityCollection output ──► lists[].collectionKey
         │
         ▼
PanelListProjector（过滤 / 排序 / item.fields 绑定）
         │
         ▼
投影快照：pins 标量 + lists[name].items[]（每项标量袋）
         │
         ▼
layout.controls[]（builtin 控件树，绑定路径）
         │
         ▼
皮 / 主题（CSS、九宫、三宫）——只改长相
```

| 配置块 | 职责 |
|---|---|
| `pins` | 面板级标量（计数、标题用数等）——既有 |
| `lists` | 同构列表：集合来源 + 可选过滤排序 + 项内字段绑定 |
| `layout` | 控件树：把投影路径绑到 builtin 控件 |

## 3. 详情

### 3.1 模板扩展字段

根对象在既有 `id/graph/pins/events/intents/skin` 之外允许：

- `lists`：数组，可空；缺省 = 无列表（旧模板行为不变）
- `layout`：对象；缺省 = 沿用自动堆行（旧行为）

未知字段仍 fail-closed。

### 3.2 `lists[]` 形状

```jsonc
{
  "name": "units",                         // 投影路径名；layout 用 bind 引用
  "collectionKey": "panel.roster.units",   // 图 EntityCollection 输出的 key
  "filter": [                              // 可选；声明式，叠在图结果上
    { "kind": "attribute", "attribute": "Health", "op": "gt", "value": 0 }
  ],
  "sort": [
    { "attribute": "Health", "descending": true }
  ],
  "item": {
    "fields": [
      { "name": "health", "kind": "attribute", "attribute": "Health" },
      { "name": "healthMax", "kind": "attributeBase", "attribute": "Health" },
      { "name": "stunned", "kind": "tag", "tag": "Status.Stunned" },
      { "name": "displayName", "kind": "name" }
    ]
  }
}
```

**字段 `kind`（项内标量来源）**

| kind | 产出 | 说明 |
|---|---|---|
| `attribute` | float | `AttributeBuffer.GetCurrent` |
| `attributeBase` | float | `AttributeBuffer.GetBase` |
| `tag` | bool | `GameplayTagContainer.HasTag`（有效态） |
| `name` | string | 实体显示名组件；缺省空串 |

**过滤 `op`**：`gt` / `gte` / `lt` / `lte` / `eq`（仅 `kind: attribute`）。  
**排序**：按单一属性；`descending` 缺省 false。多键排序本切片不做。

### 3.3 `layout.controls[]` builtin

| type | 关键绑定 | 用途 |
|---|---|---|
| `label` | `text` 常量，或 `bind`→标量/字符串；可选 `prefix` | 标题、名字、计数 |
| `progressBar` | `current` + `max`（字段名） | 血条/蓝条 |
| `badge` | `bind`（bool）+ `text` + `showWhen` | Tag/状态徽标 |
| `list` | `bind`→lists 名；`itemControls[]` | 同构重复子控件 |

路径规则：

- 面板级控件 `bind` 指向 `pins[].name` 或字符串字段（本切片面板级仅 pins 浮点）
- `list.itemControls` 内 `bind` / `current` / `max` 指向该 list 的 `item.fields[].name`

### 3.4 图侧约定

值图须：

1. 写出 `EntityCollection`（`type: TargetList` + `collectionKey` 与模板 `lists[].collectionKey` 一致）
2. （推荐）写出 Summary 计数 pin，供标题绑定

过滤/排序可在图内用 `QueryFilter*` / `QuerySortByAttribute` 完成；模板侧声明是**二次约束**（展示层可再收紧），不是第二套查询语言。Showcase 演示：图产出队伍全员集合，模板再按血量过滤存活并排序。

### 3.5 运行时合同

- 列表投影在每次 realtime 刷新时与 pin 一同重算；集合 revision 或成员属性变化都应可见
- 成员实体死亡：过滤掉或跳过不可读成员（fail-soft 于单行，不炸整面板）
- 未知 `collectionKey` / 未知 attribute / 未知 tag：**装载期**能解析的符号在装载绑定；运行期缺组件用字段缺省（float 0 / bool false / string ""），不抛——对齐 pin default 合同
- 装载期：`lists`/`layout` 结构错误、未知 kind/type、重名 → fail-closed

## 4. 场景

**实体名册 Showcase**（`panel_entity_list`）

- 地图上多单位：不同 Health、一名带 `status.stunned`
- 面板左侧列出存活单位，按 Health 降序
- 每行：名字 + 血条 + 晕眩徽标（仅 stunned 为真时显示）
- 头顶计数 pin 显示存活数

玩家预期：进图即见名册；单位掉血后顺序与条长变化；晕眩单位行上出现徽标。

## 5. 边界

- 本切片**不**做技能/效果/Tag 集合列表（同构投影合同已就绪，下一切片复用）
- 不做点击行→选中意图（仍属 #1015）；本 Showcase 纯展示
- 不做 `layout` 绝对定位/锚点嵌套；控件按声明顺序纵向堆叠（主题 CSS 可美化）
- 不把 Entity handle 暴露给控件绑定面
- 旧模板无 `lists`/`layout`：行为与本页落地前完全一致

## 6. UAT

```gherkin
Feature: 用通用面板声明式做实体名册
  作为一个做关卡的人
  我想只写模板和图，就得到可排序过滤的单位列表
  并且每一行的血条和状态徽标都是声明出来的

  Scenario: 进图看见按血量排好的存活单位
    Given 名册 Showcase 地图已加载
    And 场上有多名己方单位且血量不同
    When 我看着左侧名册面板
    Then 我只看到血量大于 0 的单位
    And 列表从上到下血量从高到低
    And 每一行都有名字和一条随血量变化的血条

  Scenario: 晕眩状态以徽标声明出现
    Given 其中一名单位带有晕眩状态（Status.Stunned）
    When 名册刷新
    Then 该单位那一行出现「晕眩」徽标
    And 没有晕眩的行不出现该徽标

  Scenario: 旧面板模板不受影响
    Given 一份没有 lists/layout 的火球状态模板
    When 面板按原方式打开
    Then 仍按自动堆行显示标量 pin
```
