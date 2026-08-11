# 通用图查表节点族（ResolveTableRow + TableRead*）

## 1. 概述

为面板作者提供**通用查表**：指定表、用 key/id 定位一行、再读具体字段（Int / Float / TextToken）。  
与 Tag 状态文案快捷路径（`SelectTagInMask` + `LookupTagDisplayToken`）分离——后者只服务互斥 GameplayTag → 展示 token。

实现跟踪：[#870](https://github.com/MightyBubble/Ludots/issues/870)。Epic：[#858](https://github.com/MightyBubble/Ludots/issues/858)。

硬约束：同一 L0 VM；禁止 `GraphKind.Presentation`；图内无字符串寄存器；热路径 0Alloc；缺行/类型不符 fail-closed。

## 2. 结构

```text
Mod table asset
  → GraphLookupTableRegistry（加载期建索引）
  → ResolveTableRow(key) → rowHandle(Int)
  → TableReadInt / TableReadFloat
  → GraphOutput Summary
  → Panel binding → Surface（仅 Text token 在表面 Format）
```

| 层 | 职责 | 非职责 |
|----|------|--------|
| GraphLookupTableRegistry | 只读表索引 | locale、DOM |
| ResolveTableRow | key → 行柄 | 字段语义 |
| TableRead* | 行柄 + 字段 → typed 值 | 字符串拼接 |
| Query + Agg* | 跨实体聚合/排序 | 表内「假 SQL」 |

## 3. 详情

### 3.1 P0 ops

| L0 op | 输入 | 输出 |
|-------|------|------|
| `ResolveTableRow` | Int key, table 符号 | Int rowHandle |
| `TableReadInt` | rowHandle, field 符号 | Int（含 TextToken id） |
| `TableReadFloat` | rowHandle, field 符号 | Float |

P1：`TableReadEntity` / `TableReadBool`。  
禁止：`TableReadString`、图内字符串聚合、`GraphNodeOp.Panel`。

### 3.2 资产形状（示意）

```json
[
  {
    "id": "entity.rank.display",
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

### 3.3 与 Tag 快捷查表的关系

| 需求 | 路径 |
|------|------|
| 互斥 State.* → 面板文案 | 已有 `SelectTagInMask` + `LookupTagDisplayToken` |
| 任意 int key → 多字段 | 本族 `ResolveTableRow` + `TableRead*` |
| 跨实体合计/排序 | 既有 Query / Filter / Agg*，**不**新增表聚合 opcode |

### 3.4 性能

- Resolve / Read：0Alloc；dense 或加载期 open-addressing，运行期只读。
- 同一 rowHandle 连读多字段，避免重复 key 查找。
- 多表面同 scope 共享 `GraphOutputValueStore(owner,key)`。

## 4. 场景

1. 等级面板：BB/属性给出 rank=2 → Resolve → 读 displayToken + powerScale → 两个 Panel 引脚。  
2. EntityInfoCard 的 curState：**不**走本族，走 Tag 快捷路径。

## 5. 边界

- 不做表内 sort/aggregate 新节点。  
- 不做 Text Blackboard（另债）。  
- 禁止 Attribute 假冒表字段；禁止 `TagRegistry.GetName` 当玩家文案。

## 6. UAT

```gherkin
Feature: 作者按 key 查表输出多字段
  作为面板作者
  我想用一次 row lookup 读取显示名和数值
  以便避免为同一 key 重复建查表节点

  Scenario: rank key 查出 TextToken 与 Float
    Given 表 "entity.rank.display" 有 key 2
    And key 2 的 displayToken 为 "rank.veteran"
    And key 2 的 powerScale 为 1.2
    When 作者连接 ResolveTableRow 到 TableReadInt(displayToken) 和 TableReadFloat(powerScale)
    Then Panel.rankName 接收到 TextToken
    And Panel.powerScale 接收到 Float
    And 图 VM 没有 string 寄存器
```
