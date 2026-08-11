# 通用图查表（唯一查表路径）

> **文档 SSOT**：本页是图内「按表读数」的唯一设计正本。  
> **计划 SSOT**：GitHub issue（本 PR 创建/改写的 ADR + 实现单，见文末链接区；合并后以 issue 编号为准）。  
> **废止**：`TagDisplayTableRegistry` / `LookupTagDisplayToken` / `tag_display_tables.json` 专线——不得再扩展、不得接入 `GameEngine` 生产路径。

## 1. 概述

面板/作者图需要查表时，框架只提供**通用查表**：

```text
用户/Mod 自建表 → 用 key 定位一行 → 读字段（Int / Float / TextToken id）
```

表里放什么语义（等级文案、状态文案、科技树数值……）由 **Mod 作者决定**，不是 Core 预置一张「Tag 显示专表」。

硬约束：

- 同一 L0 VM；禁止 `GraphKind.Presentation` / `GraphNodeOp.Panel`
- 图内无字符串寄存器；热路径 0Alloc
- 缺表 / 缺行 / 缺字段 / 类型不符 → **失败即炸**（无 fallback）
- 跨实体合计/排序继续用既有 Query / Filter / Agg*，**不**在表上做假 SQL

Epic：[#858](https://github.com/MightyBubble/Ludots/issues/858)。  
入口钉：[#878](https://github.com/MightyBubble/Ludots/issues/878) · ADR：[#876](https://github.com/MightyBubble/Ludots/issues/876) · 实现：[#871](https://github.com/MightyBubble/Ludots/issues/871) · 清理：[#877](https://github.com/MightyBubble/Ludots/issues/877)。

## 2. 结构

```text
Mod 表资产（作者自建）
  → GraphLookupTableRegistry（加载期建只读索引）
  → ResolveTableRow(key) → rowHandle(Int)
  → TableReadInt / TableReadFloat
  → GraphOutput Summary
  → Panel binding → Surface（仅 Text token 在表面 Format）
```

| 层 | 职责 | 非职责 |
|----|------|--------|
| Mod 表资产 | 行/列/语义（含「标签 id → 文案 token」若作者需要） | Core 预置业务表 |
| GraphLookupTableRegistry | 只读表索引 | locale、DOM、Tag 语义 |
| ResolveTableRow | key → 行柄 | 字段含义 |
| TableRead* | 行柄 + 字段 → typed 值 | 字符串拼接 |
| Query + Agg* | 跨实体聚合/排序 | 表内聚合新 opcode |

### 和玩法标签的关系（重要）

| 需求 | 正确路径 |
|------|----------|
| 读实体当前有效标签（玩法真相） | 既有/待补的**纯读标签**能力（如从 Effective tags 选出一个 tag id）——这是读真相，不是查显示表 |
| 把 tag id / rank / 任意 int key 翻成展示 token 或多字段 | **用户自建通用表** + `ResolveTableRow` + `TableRead*` |
| Core 内置 `TagDisplay*` 专表/专 op | **禁止**（误开平行基建，见废止声明） |

示例（状态文案，全是作者资产，不是框架专线）：

```text
读当前状态 tagId
  → ResolveTableRow(table = mod.self.state_display, key = tagId)
  → TableReadInt(field = displayToken)
  → Panel.curState
```

## 3. 详情

### 3.1 P0 ops

| L0 op | 输入 | 输出 |
|-------|------|------|
| `ResolveTableRow` | Int key, table 符号 | Int rowHandle |
| `TableReadInt` | rowHandle, field 符号 | Int（含 TextToken id） |
| `TableReadFloat` | rowHandle, field 符号 | Float |

P1：`TableReadEntity` / `TableReadBool`。  
禁止：`TableReadString`、图内字符串聚合、`LookupTagDisplayToken`、`TagDisplayTableRegistry`。

### 3.2 资产形状（示意）

表由 Mod 提供，框架不解释业务列名：

```json
[
  {
    "id": "mod.example.rank_display",
    "keyKind": "Int",
    "columns": [
      { "id": "displayToken", "kind": "TextToken" },
      { "id": "powerScale", "kind": "Float" }
    ],
    "rows": [
      { "key": 2, "displayToken": "rank.veteran", "powerScale": 1.2 }
    ]
  }
]
```

作者若要做「状态标签 → 文案」，再建一张自己的表即可，例如 `mod.example.state_display`，key 用 tag id——**仍是同一套通用 ops**。

### 3.3 废止清单（不得继续建设）

| 误开物 | 处置 |
|--------|------|
| `TagDisplayTableRegistry` / `CoreServiceKeys.TagDisplayTableRegistry` | 删除或降为测试夹具后删除；**禁止**接入 `CreateProduction` / `GameEngine` |
| `GraphNodeOp.LookupTagDisplayToken` 与作者糖 `LookupTagDisplayText` | 删除；场景改走通用表 |
| `tag_display_tables.json` 合同 | 不采用；统一走通用 lookup 表资产 |
| 设计文把「Tag 快捷路径」写成与通用查表并列的正线 | 作废；以本页为准 |
| 文档/issue 写「Tag 文案继续走 #868 快捷 op」 | 过时；#868 中 TagDisplay 部分不得再扩展 |

`SelectTagInMask` / `ReadGameplayTag`：**仅当**其语义是「从玩法 Effective tags 选出 tag id」时，可作为纯读原子保留或另单澄清；**不得**再绑定任何 Display 专表。

### 3.4 性能

- Resolve / Read：0Alloc；dense 或加载期 open-addressing，运行期只读。
- 同一 rowHandle 连读多字段，避免重复 key 查找。
- 多表面同 scope 共享 `GraphOutputValueStore(owner, key)`。

## 4. 场景

1. **等级面板**：BB/属性给出 rank=2 → Resolve → 读 displayToken + powerScale → 两个 Panel 引脚。  
2. **实体状态文案**：读出当前状态 tagId → 查**作者自建** `state_display` 表 → TextToken → Panel.curState（无 TagDisplay 专线）。  
3. **缺行**：表无 key → 图执行抛错，面板不得静默显示空串或内部 tag 名。

## 5. 边界

- 不做表内 sort/aggregate 新节点。  
- 不做 Text Blackboard（另债）。  
- 禁止 Attribute 假冒表字段；禁止 `TagRegistry.GetName` 当玩家文案。  
- 禁止为「Tag 显示」再开 Core 平行 registry。  
- 热重载 / Template·Instance·Router 不在本页范围（见 #858 / 热应用另单）。

## 6. UAT

```gherkin
Feature: 作者用自建表查多字段
  作为面板作者
  我想用一次 row lookup 读取显示名和数值
  以便避免为同一 key 重复建查表节点

  Scenario: rank key 查出 TextToken 与 Float
    Given Mod 提供表 "mod.example.rank_display" 且含 key 2
    And key 2 的 displayToken 为 "rank.veteran"
    And key 2 的 powerScale 为 1.2
    When 作者连接 ResolveTableRow 到 TableReadInt(displayToken) 和 TableReadFloat(powerScale)
    Then Panel.rankName 收到 TextToken
    And Panel.powerScale 收到 Float
    And 图 VM 没有 string 寄存器

  Scenario: 状态文案也走同一套通用表
    Given Mod 提供表 "mod.example.state_display"，key 为状态 tag id
    And 选中实体当前有效状态 tag id 已知
    When 作者用该 tag id 做 ResolveTableRow 并 TableReadInt(displayToken)
    Then Panel.curState 收到 TextToken
    And 过程中不存在 TagDisplay 专表或 LookupTagDisplay* 节点

  Scenario: 缺行失败关闭
    Given 表中没有 key 99
    When 图执行 ResolveTableRow(key=99)
    Then 执行失败并抛错
    And 面板不得显示内部 tag 名或空串兜底
```

## 链接区

| 角色 | Issue |
|------|-------|
| 入口钉（先读） | [#878](https://github.com/MightyBubble/Ludots/issues/878) |
| ADR / 计划 SSOT | [#876](https://github.com/MightyBubble/Ludots/issues/876) |
| 实现 | [#871](https://github.com/MightyBubble/Ludots/issues/871)（旧正文「与 TagDisplay 分离」作废，以 #876 + 本页为准） |
| TagDisplay 清理 | [#877](https://github.com/MightyBubble/Ludots/issues/877) |
| 占位误开（应关） | [#870](https://github.com/MightyBubble/Ludots/issues/870) |

作者形态：[`ui-panel-authoring-form.md`](ui-panel-authoring-form.md)  
旧文：[`tag-display-lookup.md`](tag-display-lookup.md)（**已废止**，仅作重定向）
