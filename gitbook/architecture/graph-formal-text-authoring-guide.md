# 图正式文字：作者拼句指南

作者在蓝图里拼运行态句子、填空，再送到字幕或对话框。运行时合同正本见 [图正式文字](graph-formal-text.md)；进度只认 [图能力唯一入口](graph-capability-status.md)。玩家短剧见 [拼一句上字幕](../acceptance/graph-formal-text-subtitle.md)。

---

## 1. 概述

配置态台词（对话本、本地化表）继续走 TextToken。本指南只管**图里当场拼出来的句子**：写死短句、拼接、数字念成字、花括号填空、送到字幕/对话框。超容量、模板坏了、没绑出口，一律直接报错，不截断、不静默。

## 2. 结构

```text
查表出句（本地化键）见 [图 TextKey 发现糖](graph-textkey.md) 的 `LoadTextKey`——与下面字面量拼句不要混用。

写死短句 ConstText
    ↓
拼接 ConcatText（或 FormatText 糖自动降级）
    ↓
数字 IntToText / FloatToText
    ↓
送到 SinkPresentationText（Subtitle / Dialogue）
```

编辑器节点表只露出运行时已登记的文字节点和 `FormatText` 糖；合同没齐时看不到可保存假节点。

## 3. 详情

### 3.1 写死一句

放 `ConstText`，在 `text` 里写短句。值边接到下一节点的 Text 入边。字面量进图的符号表，不走 TextToken 回写。

### 3.2 两段拼一句

两段 `ConstText` 接到 `ConcatText` 的 `a` / `b`，再把结果送到出口。总长超过单个文字槽容量 → 执行失败关闭。

### 3.3 数字进句子

整数用 `IntToText`，小数用 `FloatToText`，再接到拼接或 `FormatText` 引脚。不要在热路径用 C# 字符串拼接假装图内文字。

### 3.4 花括号模板（FormatText）

作者糖，不是运行时 opcode。模板写在 `text`：`击杀 {0}` 或命名 `{name}`。每个占位自动长出 Text 入边；保存前编成 `ConstText` + `ConcatText`（数字先 ToText）。未闭合 `{`、空名、越界下标 → 编译失败。

### 3.5 送到字幕 / 对话框

`SinkPresentationText`：值入边 `a` 为 Text；`presentationSurface` 取 `Subtitle` 或 `Dialogue`。宿主每帧从固定环里取走；环满失败关闭。引擎必须绑定 PresentationTextSink，否则执行到出口就报错。

## 4. 场景

1. 写「守卫」+「倒下了」→ 拼接 → 字幕口 → 玩家看到「守卫倒下了」。
2. 写 `FormatText("击杀 {0}")`，把击杀数经 `IntToText` 接到 `arg0` → 字幕「击杀 1」。
3. 模板写成 `坏掉的 {` → Validate 点名失败，图不保存。

## 5. 边界

- 不替代 TextToken；不恢复查表拼串。
- 不把自由字符串地图变量当台词真相。
- 编辑器不得露出引擎不认的假 Concat / FormatText。
- 容量与表面枚举以合同页为准。

## 6. UAT

```gherkin
Feature: 作者按指南在蓝图里拼句

  Scenario: 固定句进字幕
    Given 我按指南接好 ConstText、ConcatText 和字幕出口
    When 图跑完
    Then 字幕口能读到完整句子

  Scenario: 花括号引脚可接线
    Given 我在 FormatText 写下「击杀 {0}」
    When 编辑器刷新端口
    Then 我看到 Text 入边 arg0
    And 保存前编译把它降成原子文字节点

  Scenario: 坏模板不能保存
    Given 模板花括号未闭合
    When 我点 Validate
    Then 保存被拒绝并点名模板错误
```
