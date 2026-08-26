# 图正式文字合同

作者在蓝图里拼句子、填空，再送到字幕或对话框。进度与边界只认 [图能力唯一入口](graph-capability-status.md)；本页是运行时合同，不另开进度账。作者怎么接节点看 [拼句指南](graph-formal-text-authoring-guide.md)；玩家短剧看 [拼一句上字幕](../acceptance/graph-formal-text-subtitle.md)。

---

## 1. 概述

图内第一次有正式的「文字值」：固定槽位、固定字数、热路径不分配、字面量走程序符号表、拼好的句子只从一个表现出口出去。

作者能做的事：

1. 用固定短句节点写出一段字。
2. 用组合节点把两段字接成一句。
3. 把整数或小数变成文字再接进去。
4. 在模板里写花括号，编辑器自动长出对应引脚，保存前就编成上面那些原子节点。
5. 把拼好的句子推进字幕或对话框通道。

配置态给玩家看的台词仍走 TextToken（见故事运行时合同）。本页管的是**图里当场拼出来的运行态句子**，不是第二套本地化表。

---

## 2. 结构

```text
作者 JSON
  ConstText / ConcatText / IntToText / FloatToText / SinkPresentationText
  FormatText（糖）──编译期──▶ ConstText + ConcatText（+ ToText）

编译
  Text 寄存器分配（GraphRegisterFile）
  字面量进程序 Symbols；ConstText.Imm = 符号下标（不 patch）

执行（0Alloc）
  GraphTextHeap：固定槽 × 固定字数
  读写只在堆内拷贝；超长失败关闭

表现
  SinkPresentationText → IGraphRuntimeApi.PushPresentationText
  → GraphPresentationTextSink 固定环（Subtitle / Dialogue）
```

| 层 | 职责 | 非职责 |
|----|------|--------|
| Text 寄存器 | 槽位编号与容量记账 | 本地化、locale |
| GraphTextHeap | 字符与长度 | UI 排版 |
| 原子 op | 写字 / 拼接 / 数转字 / 送出 | 查表拼串（查表合同禁止） |
| FormatText 糖 | 花括号 → 引脚与降级 | 运行时再扫模板 |
| Presentation sink | 固定环交接字幕/对话 | Narrative 第二解释器 |

---

## 3. 详情

### 3.1 文字值怎么存

- `GraphValueType.Text`：编译期银行与值边类型。
- 运行时每个 Text 寄存器对应 `GraphTextHeap` 的一个槽；槽内是定长 UTF-16 字符区 + 长度。
- 容量 SSOT：`GraphVmLimits.MaxTextRegisters`、`GraphVmLimits.MaxTextCharsPerRegister`。
- 空槽长度 0。越界读写失败关闭，不截断、不静默。

### 3.2 固定容量怎么传 / 零分配

- 执行入口清空当前线程复用的 `GraphTextHeap`（容量固定，不按次 new）。
- ConstText：从程序 `Symbols[Imm]` 拷进目标槽；字面量长于槽容量 → 失败。
- ConcatText：`T[dst] = T[a] + T[b]`，总长超槽 → 失败。
- IntToText / FloatToText：`TryFormat` 进栈上缓冲再拷入槽；格式化结果超槽 → 失败。
- 禁止 `string.Concat` / `StringBuilder` 上热路径；禁止把 BCL `string` 当寄存器值传来传去。

### 3.3 符号 patch

- ConstText / FormatText 模板字面量：编译期 `Intern` 进该图 `Symbols`；指令 `Imm` 恒为符号下标。
- **不做** TextToken id 回写：运行态拼句不依赖 catalog patch；配置态台词仍走既有 TextToken 装载链。
- `GraphProgramSymbolPatcher` 跳过上述 Imm，避免把下标误当解析 id。

### 3.4 花括号自动引脚

- 作者糖名：`FormatText`（不是 `GraphNodeOp`）。
- 模板字段：`text`。占位只认 `{0}`…`{n}` 或命名 `{name}`（命名在编译期换成顺序下标）；`{{` / `}}` 转义。
- 每个占位生成一个 **Text** 值入边端口：`arg0`… 或命名端口；未接线失败关闭。
- 编译降级：按字面片段 `ConstText` 与引脚值交替 `ConcatText`；数要先经 IntToText/FloatToText 再接入。
- 未终止的 `{`、空名、越界下标 → 编译失败。

### 3.5 Presentation sink

- `SinkPresentationText`：值入边 `a`（Text）；`Imm` = 表面（0=Subtitle，1=Dialogue）。
- `IGraphRuntimeApi.PushPresentationText(surface, ReadOnlySpan<char>)` → `GraphPresentationTextSink` 固定环。
- 环满失败关闭。宿主（对话/字幕）只读 sink，不另造图内字符串地图变量。
- 缺 Api / 未接线 sink 实现 → 明确异常，禁止空操作假装送出。

### 3.6 编辑器露出

- 可保存节点只能来自运行时 descriptor 名册与已登记作者糖。
- 合同与 handler / descriptor / 糖降级未齐之前，Bridge **不得**投影 ConstText/ConcatText/FormatText/Sink*。
- React 不得手写假 Concat 节点。

---

## 4. 场景

1. 作者写 `ConstText(" orth ")` 与 `ConstText("倒下了")`，中间 `ConcatText`，再 `SinkPresentationText(Subtitle)`。玩家看到字幕「 orth 倒下了」。
2. 作者放 `FormatText`，模板 `敌人 {0} 倒下了`，把 `IntToText(击杀数)` 接到 `arg0`，Validate 通过后保存。
3. 模板写成 `敌人 {` 未闭合：Validate 点名失败，图不保存。
4. 两段字拼起来超过槽容量：运行失败关闭，不截断句子。

---

## 5. 边界

- 不替代 TextToken 配置态文案；不恢复 `TableReadString` / 查表路径字符串聚合。
- 不在编辑器做「能保存但引擎不认」的假节点。
- 不把自由字符串地图变量当台词真相。
- 本页不收 TriggerGraph 旗舰、画廊短剧、叙事换皮；那些另线。玩家拼句短剧见 [拼一句上字幕](../acceptance/graph-formal-text-subtitle.md)。
- 总进度只改 [图能力唯一入口](graph-capability-status.md) 与 [编辑器手册](graph-editor-and-live-debug.md)。

---

## 6. UAT

```gherkin
Feature: 作者在蓝图里拼句子并送到字幕

  Scenario: 两段固定字拼成一句字幕
    Given 运行时已登记 ConstText、ConcatText、SinkPresentationText
    When 我用两段固定字拼成「守卫倒下了」并送到字幕出口
    Then 表现环里能读到完整句子
    And 热路径没有新的堆分配用于拼接

  Scenario: 花括号自动长出引脚
    Given 我在 FormatText 里写下「击杀 {0}」
    When 编辑器从运行时糖描述刷新端口
    Then 我看到 Text 入边 arg0
    And 保存前编译把它降成 ConstText 与 ConcatText

  Scenario: 没有合同就不露假节点
    Given 文字 op 尚未进入 descriptor 名册
    When 我打开蓝图节点表
    Then 我看不到可保存的 Concat 或 FormatText 假节点

  Scenario: 超长失败关闭
    Given 两段字拼起来超过单个文字槽容量
    When 图执行到 ConcatText
    Then 执行失败并说明容量
    And 句子不会被截断后继续送出
```
