# 面板开箱布局套件

给 mod 作者一份可照抄的「同一份元素芯片 × 三种编排」样板：竖列、网格、横栏；效果芯片带剩余时间与层数。合同正交于[面板视图投影](panel-view-projection.md)、[查询图集合输出](query-graph-collection-outputs.md)。

## 1. 概述

| 谁 | 要什么 |
|---|---|
| 新作者 | 复制 JSON 就能看到 list / grid / column 三种画面，不用猜引擎 |
| 玩家场 | 一眼分清「竖着点名」「格子墙」「头顶一排 buff」 |
| 引擎 | 只认封闭 `present` + 已有控件；不新造 EntityInfo / ItemStack 硬控件 |

**原则**：元素模板不知道自己挂在哪种编排上；换 `present` 不改芯片。示例数据走正式效果实例 + `LoadEffectTiming` / `LoadEffectStack`，禁止假实体、禁止面板里算层数。

## 2. 结构

```text
元素芯片 panel.kit.effect.chip
  subject: EffectInstance
  pins: remaining / total / stacks
  layout: 名 + 剩余时间条 + 层数徽标

容器（三选一，同一 collections 绑定）
  present: list    → 竖向逐条（可滚动 / 可虚拟化）
  present: grid    → columns 折行成格
  present: column  → 横向一排（buff 条 / 技能栏）

开箱 Showcase：panel_author_layout_kit
  一场三块面板并排（教学用）——与「一袋一场」玩家竖切正交；本场明确是作者教室
```

## 3. 详情

### 3.1 present 封闭集（落地）

| present | 画面 | 额外字段 | 虚拟化 |
|---|---|---|---|
| `list` | 竖向 | `viewportHeight` / `itemExtent` / `virtualize` / `overscan` | 允许 |
| `grid` | 按列折行 | **必填** `columns`（≥1）；`itemExtent` = 格高 | 本轮禁止 |
| `column` | 横向一排 | `itemExtent` = 行高（可选） | 禁止 |
| `aggregate` | 首位芯片 + 总数文案 | **必填** `aggregate.count` | 禁止 |

```jsonc
{
  "type": "list",
  "bind": "effects",
  "present": "grid",
  "columns": 3,
  "itemExtent": 72
}
```

```jsonc
{
  "type": "list",
  "bind": "effects",
  "present": "column",
  "itemExtent": 56
}
```

```jsonc
{
  "type": "list",
  "bind": "stacks",
  "present": "aggregate",
  "aggregate": {
    "count": { "from": "totalCount", "prefix": "×" }
  }
}
```

- `aggregate.count.from` 本轮只允许 `totalCount`（袋基数）。
- `aggregate.count.prefix` 必填字符串（可为空串，但字段必须出现）——禁止引擎写死 `×`。
- `head` / `icon` 仍属「日后图标控件」；本轮 head = 元素模板投影首位成员。

### 3.2 效果芯片（剩余时间 + 层数）

| pin | 图节点 | 画面 |
|---|---|---|
| `remaining` / `total` | `LoadEffectTiming` | `progressBar` |
| `stacks` | `LoadEffectStack` | `badge` 或 `label`（`prefix: "×"` 由配置写） |

无 `EffectStack` 组件时层数读为 `1`（与运行时「单层生效」语义一致），不静默成 0 假装没层。

### 3.3 开箱资产落点

| 资产 | 路径 |
|---|---|
| 设计本合同 | `gitbook/architecture/panel-author-layout-kit.md` |
| Showcase | `mods/showcases/panel_author_layout_kit/PanelAuthorLayoutKitShowcaseMod/` |
| 快速上手入口 | `panel-quickstart.md` 增「布局三种编排」链到本合同与 showcase |

作者复制：`panel_templates.json` 里的 `panel.kit.effect.chip` + 三个容器模板即可。

## 4. 场景

- 竖列：点名 buff，读剩余时间与层数。
- 网格：技能墙 / 图鉴格（同一芯片换 `present: grid`）。
- 横栏：头顶一排短 buff。
- 聚合：背包堆叠（`aggregate.count.prefix` 来自配置）。

## 5. 边界

- 不新增平行控件类型 `grid` / `column`——仍是 `type: list` + `present`。
- 过滤排序只在查询图；面板不筛层数、不造假效果。
- `grid`/`column`/`aggregate` 与 `virtualize` 互斥（装载失败）。
- 未知 `present` / 缺 `columns` / 缺 `aggregate.count` → 装载失败。
- 本教学场可同屏三面板；玩家竖切仍「一袋一场」。

## 6. UAT

```gherkin
Feature: 开箱布局套件
  Scenario: 同一芯片三种编排
    Given 我启动 panel_author_layout_kit
    When 地图加载完成
    Then 我能同时看到标题含「竖列」「网格」「横栏」的三块面板
    And 三块面板名单里都能认出带剩余时间的效果名
    And 至少有一个效果行能读到层数徽标

  Scenario: 网格按列折行
    Given 配置 present 为 grid 且 columns 为 3
    When 面板装载
    Then 引擎接受该模板
    And 画面按每行最多 3 格排布成员

  Scenario: 缺列数失败
    Given 配置 present 为 grid 但未写 columns
    When 装载 panel_templates
    Then 装载失败并指出模板 id

  Scenario: 聚合前缀来自配置
    Given present 为 aggregate 且 aggregate.count.prefix 为 "×"
    When 投影背包堆叠
    Then 总数文案以配置的前缀开头
    And 引擎源码不再写死乘号字符串作为唯一前缀
```
