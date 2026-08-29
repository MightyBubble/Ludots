# 受限标记语法说明

对应 issue [#1389](https://github.com/MightyBubble/Ludots/issues/1389)。文案仍走 `text_tokens.json` + `text_locales.json`；本页只讲**模板值里能写哪些行内标记**。

实现挂靠：`PresentationTextCatalogLoader`（装载期解析）→ `StoryTextResolution.FormatTokenRuns` → 对话 / NarrativeFrontend 的 `BodyRuns` → Skia `DrawTextRuns`。

## 1. 概述

作者要在台词里高亮一两个词（加粗、斜体、变色），又不想另起一套 HTML 编辑器。

做法：在 locale 模板字符串里写受限标记。引擎在**装载期**一次解析成 styled runs；非法标记会直接让启动失败，不会把标记原文或 token id 糊给玩家。

| 你想要的 | 怎么写 |
|----------|--------|
| 加粗一词 | `<b>别走神</b>` |
| 斜体一词 | `<i>focused</i>` |
| 指定颜色 | `<color=#AARRGGBB>见证者</color>` |
| 标记包住参数 | `<b>{0}</b>` |
| 字面 `{` / `}` | `{{` / `}}`（先于标记解析） |

## 2. 结构

```text
text_locales.json 模板值
  └─ 装载期：转义 → 占位符 → 受限标记
       └─ PresentationTextTemplate（Literal | Argument | StyledLiteral）
            ├─ FormatToken → 纯文本（历史 / 日志 / 桥）
            └─ FormatTokenRuns → BodyRuns（渲染正视图）
```

多语言结构不变：仍是 token 表 + 各 locale 模板。zh-CN 与 en-US 可以各自标记不同的词。

## 3. 详情

### 3.1 合法标记（首版全集）

- `<b>…</b>`：加粗
- `<i>…</i>`：斜体
- `<color=#AARRGGBB>…</color>`：行内色；**必须** 8 位十六进制，alpha 在前（与 `UiColor` / Skia 一致），例如 `#FFF6C56B`

不允许：嵌套、未闭合、未知标签、6 位 `#RRGGBB`、坏色值。

### 3.2 与占位符同层

- `称呼：<b>{0}</b>` 合法：标记包住参数，运行时参数文本继承该样式。
- `{{` / `}}` 先变成字面花括号，再解析标记与 `{N}`。
- 无标记模板走原快路径；`FormatToken` 签名与纯文本行为不变。

### 3.3 双出口（正式合同，不是降级）

| 出口 | 用途 |
|------|------|
| `Body` / `ResolvedText` | 纯文本投影：历史、日志、无障碍、桥接 |
| `BodyRuns` | 可选；有样式时必填，composer 优先走 runs 绘制 |

### 3.4 字符串参数（配套能力）

模板参数除 Int32 / Float32 外可进 String（catalog 字符串池，packet 仍 blittable）。适合 `{speaker}：{body}` 这类组合回到数据侧。

## 4. 场景

```gherkin
Feature: locale 模板行内富文本

  Scenario: 作者在模板里加粗一个词
    Given text_locales.json zh-CN 模板含 "<b>别走神</b>"
    When 引擎装载该 token 并在对话中显示
    Then "别走神" 以粗体渲染，其余正常
    And en-US 模板可独立标记不同词

  Scenario: 非法标记装载期失败
    Given 模板含未闭合 "<b>词" 或非法色值 "<color=#ZZ>x</color>"
    When 引擎装载
    Then 启动抛错并指出 token id

  Scenario: 零标记模板不回归
    Given 既有纯文本 token
    When 装载与渲染
    Then 走原快路径，玩家看到的文案与标记上线前一致
```

旗舰 narrative 示例：`story.warden.intro.elder`（zh-CN 高亮「见证者」，en-US 加粗 `witness`）。

## 5. 边界

- 不做完整 HTML / 任意 CSS / 内容编辑器
- 不动 CEF web 皮；不做玩家输入态富文本
- 不做 inline 图标或混合内容
- Markup 引擎侧 `b/strong/em/i` 仅补标签映射与 UA 默认（粗体 / 斜体）；**不做** inline flow（仍按 Column 各占一行如实保留）

## 6. UAT

```gherkin
Feature: 受限标记可玩验收

  Scenario: 守望者开场台词出现高亮词
    Given 玩家进入叙事旗舰图并触发与守望者的对话
    When 第一句台词出现在 Overlay
    Then 玩家能读完整句，且高亮词视觉上与周围不同
    And 切换到 en-US 后对应词仍有独立标记，无缺失 locale

  Scenario: 坏模板进不了游戏
    Given 某 locale 模板少了闭合标签
    When 启动装载 Presentation 文案
    Then 进程以配置错误退出，错误信息含 token id
```
