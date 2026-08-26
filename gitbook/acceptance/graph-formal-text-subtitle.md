# 拼一句上字幕

## 1. 概述

走进展厅，场上躺着倒下的守卫。进图那一瞬间，蓝图自己拼出两句字幕：先是「守卫倒下了」，再是「击杀 1」。字不是 C# 里手写拼出来的，也不是查本地化表拿到的配置台词——是图里写死短句、拼接、把数字念成字、用花括号填空，再送到字幕口。

## 2. 结构

```text
进图 MapLoaded
        ↓
ConstText「守卫」+「倒下了」→ ConcatText → SinkPresentationText(Subtitle)
        ↓
ConstInt 1 → IntToText → FormatText「击杀 {0}」→ SinkPresentationText(Subtitle)
        ↓
表现系统从 PresentationTextSink 抽出两句，叠到屏幕字幕
```

启动入口：`capability_standard_graph_formal_text`  
地图：`capability_standard_graph_formal_text`  
拼句图：`Graph.FormalText.SubtitleBeat`（`TriggerGraph`）

## 3. 详情

- 场上只有一个倒下的守卫，用来当视觉锚点；玩家不用点技能。
- 固定句走 `ConstText` + `ConcatText`；带数字的一句走 `FormatText`（编译期降成原子文字节点）。
- 两句都进 `Subtitle` 出口；短剧表现只读 sink，不许再用 C# 字符串假装字幕。
- 缺 sink 绑定、超容量、模板花括号坏了，一律失败关闭。

## 4. 场景

玩家打开「拼一句上字幕」，站着看字幕。先看到「守卫倒下了」，再看到「击杀 1」。

## 5. 边界

- 不走 TextToken / Narrative 配置台词当这句的真相。
- 不把画廊 vignette 的 detailTemplate 当成玩家字幕证明。
- Dialogue 通道本短剧不验收；只认 Subtitle。
- 收到第三句意外字幕直接失败，不许静默丢掉。

## 6. UAT

```gherkin
Feature: 拼一句上字幕

  Scenario: 图拼出的固定句出现在字幕
    Given 我走进「拼一句上字幕」
    And 场上有倒下的守卫
    When 拼句图跑完进图那一拍
    Then 屏幕字幕出现「守卫倒下了」
    And 这句话来自 GraphPresentationTextSink 的 Subtitle 出口

  Scenario: 数字进花括号再上字幕
    Given 我走进「拼一句上字幕」
    When 拼句图跑完带数字的一拍
    Then 屏幕字幕出现「击杀 1」
    And 作者图使用 FormatText（或等价降级）而不是第二套查表文案
```
