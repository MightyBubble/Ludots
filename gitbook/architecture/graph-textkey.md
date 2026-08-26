# 图 TextKey 发现糖

作者在蓝图里像选 GameplayTag 一样挑一条文案键，运行时仍走真正的本地化表。进度与边界只认 [图能力唯一入口](graph-capability-status.md)。运行态当场拼句见 [图正式文字](graph-formal-text.md)；配置态台词与故事 Line 见故事运行时合同与 `Presentation/text_*`。

---

## 1. 概述

两条轨，禁止混成一条：

| 轨 | 作者手感 | 运行时真相 | i18n |
|----|----------|------------|------|
| FormalText | `ConstText` / `ConcatText` / `FormatText` 糖 / ToText / Sink | 图内 Text 堆 + 符号表字面量 | 否（当场字面量） |
| TextKey | 发现键名（Tag 式选择器）→ `LoadTextKey` | `PresentationTextCatalog` token → locale 模板 | 是 |

本页只管 **TextKey 发现糖**：编辑器能列出键、图节点只存键名、编译把键变成 tokenId、执行把默认语言模板写入 Text 槽，再可接到既有 `SinkPresentationText`。

**禁止**：让 `ConstText` 接受 token 键；禁止平行第二套本地化表；禁止热路径静默回退到键名本身。

---

## 2. 结构

```text
作者
  图节点 LoadTextKey { textKey: "gallery.hello" }
  或 Story Line { textToken: "..." }（同一名册）

发现（编辑器）
  GET /api/graph/text-keys/{modId}
    → 扫描各 mod Presentation/text_tokens.json（+ locales 预览）

编译
  textKey Intern → Symbols[Imm]
  GraphProgramSymbolPatcher：Imm = ResolveTextToken(Symbols[Imm])

执行（本切片：零参）
  PresentationTextCatalog.DefaultLocaleId + tokenId → 模板 Source
  写入 GraphTextHeap[T[Dst]]
  可再 SinkPresentationText → 字幕/对话环

后续切片（本页边界外）
  FormatTextKey：声明 argCount 的键 + Int/Float 值边 → 定长格式化进堆
```

| 层 | 职责 | 非职责 |
|----|------|--------|
| Bridge 名册 | 键发现、来源 mod、argCount | 改写 catalog |
| `LoadTextKey` | 键 → Text 槽 | 拼字面量、富文本 DSL |
| Catalog | token / locale SSOT | 图寄存器布局 |
| FormalText ops | 字面量拼句 | 查表 |

---

## 3. 详情

### 3.1 节点合同

- Opcode：`LoadTextKey`（可保存于 Script / TriggerGraph）。
- 字段：`textKey`（非空字符串，与 catalog `id` 一致）。
- 值边：本切片 **无** 值入边；若 token 声明 `argCount > 0`，加载失败关闭（等 `FormatTextKey`）。
- 输出：`GraphValueType.Text`（固定容量槽）。

### 3.2 符号与 patch

- 编译：`Imm = Intern(textKey)`（符号下标）。
- 装载 patch：`Imm = ResolveTextToken(name)` → 正的 tokenId；未知键失败关闭。
- **与 ConstText 对照**：ConstText 的 Imm **永不** patch；LoadTextKey **必须** patch。两轨 Imm 语义不可互换。

### 3.3 运行时取文

- 需要宿主已 `BindPresentationTextCatalog`；未绑定 → 明确异常。
- Locale：本切片用 `DefaultLocaleId`（与现行 Dialogue/Sequencer `ResolveLineText` 对齐）。ActiveLocale 对齐属后续债，不在本切片假装已接。
- 缺模板、超槽容量 → 失败关闭，不截断、不回退键名。

### 3.4 编辑器露出

- `authoredFields` kind `textKey`：下拉来自 Bridge 名册，禁止手写假键当「能保存就行」。
- Story Line 的 `textToken` 共用同一名册 API（DRY）。
- 未登记 descriptor / 未齐 handler 前，Bridge 不得投影该 op。

---

## 4. 场景

1. 作者在图上放 `LoadTextKey`，从下拉里选 `gallery.guard.down`，接到 `SinkPresentationText(Subtitle)`。玩家看到字幕是 locales 里默认语言的那句，不是键名。
2. 作者改 `text_locales.json` 默认语言文案后重跑同一张图，字幕跟着变；图 JSON 里仍只有 `textKey`。
3. 作者误选一个声明了参数的键（`argCount>0`）：图能保存，但执行失败关闭并点名「本切片不接参」。
4. 作者把键名写进 `ConstText`：那是字面量轨，不会查表——合同要求工具与文档把两轨分开教。

---

## 5. 边界

- 不替代 FormalText；不恢复查表字符串聚合旧路径。
- 不在本切片做富文本表达式、WebUiRichText、或 Dialogue 主机自动 drain sink（生产 Dialogue/Sequencer 是否接 sink 另线）。
- 不把 ActiveLocale 假装已接到 LoadTextKey。
- 总进度只改 [图能力唯一入口](graph-capability-status.md) 与相关 SUMMARY 链接。

---

## 6. UAT

```gherkin
Feature: 从图上按文案键出字幕

  Scenario: 新作者挑到键就能出本地化句子
    Given 画廊模组已登记文案键 gallery.hello，默认语言模板是「你好」
    And 我打开一张 Script 图
    When 我放下 LoadTextKey，从列表里选 gallery.hello
    And 把它接到字幕出口后点运行
    Then 字幕口吐出「你好」
    And 我看不到键名 gallery.hello 被当成字幕

  Scenario: 选错成带参数的键会当场失败
    Given 键 gallery.hp 声明需要一个数字参数
    When 我只用 LoadTextKey 加载它并运行
    Then 运行失败并告诉我本节点还不接参数
    And 字幕口保持空，不会吐出半截字

  Scenario: 字面量拼句与查表键分开
    Given 我用 ConstText 写了「gallery.hello」四个英文字母当字面量
    When 图跑完送进字幕
    Then 字幕是字面量本身
    And 系统没有去本地化表里查找
```
