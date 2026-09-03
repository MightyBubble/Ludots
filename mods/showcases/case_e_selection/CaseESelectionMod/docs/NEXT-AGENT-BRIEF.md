# Case E 交接：现网蠢决定

> 结构说明：[case-e-config-structure.html](./case-e-config-structure.html)  
> 可调用函数远景（另单方案）：[graph-callable-function-vision.md](../../../../../gitbook/architecture/graph-callable-function-vision.md)

## 1. 概述

Case E 是框选演示。玩家按下拖框、抬起落定。  
合同纠偏（起角落指挥官黑板、屏幕框直读黑板+指针、不靠地图变量、不硬塞开机键）已经到位。  
还欠一笔：**名单不该靠引擎特供通道写。**

## 2. 结构

```text
该用图的地方
  算谁在框里     → 图（已有）
  关框选态       → 图听落定事件（已有）
  写可框选/预览/已选中名单 → 现网不是图（蠢）
```

## 3. 详情：蠢在哪

框选图算出「这些人」后，会派一个自定义事件。  
真正改三份名单的，**不是下一张图**，而是引擎里一段写死的代码。  
还要在 Case E 的 `Input/collection_event_writers.json` 里把事件名登一遍，引擎才肯改名单；不登就当没听见。

三份名单都走了这条路：

| 玩家看见 | 名单 |
|---|---|
| 谁能被框 | 可框选 |
| 拖着时谁亮黄环 | 预览 |
| 抬起后谁亮选中环 | 已选中 |

关框选态已经有一张图在听落定事件。写名单本该同一套路：图听事件，图改名单。  
硬走引擎特供，等于把「RTS 框选写名单」焊进核心。射击玩法根本用不上；换玩法这份 Case E 配置就是死肉。

`Events/custom_events.json` 只管「事件能发」。  
`collection_event_writers.json` 才管「谁改名单」——结构说明里没把它写成作者合同，别当成必须字段去扩。

## 4. 场景

1. 下一位接手 Case E：先读本页，知道合同已纠偏、名单落账仍是蠢决定。  
2. 写「图当可调用函数」方案时：必须写清 Case E 三份名单怎么改回图听事件写名单。  
3. 别在 Case E 玩法单里再扩 `collection_event_writers.json` / 引擎写名单特供。

## 5. 边界

- 本页只记 Case E。不往全局图能力入口堆第二份交接。  
- 合同纠偏（黑板起角、屏幕框、键位投影）已合，别重做。  
- 拆引擎特供另开基建；失败要报错，禁止静默空跑。

## 6. UAT

```gherkin
Feature: Case E 交接说得清

  Scenario: 我知道蠢决定是什么
    Given 我是接手 Case E 的人
    When 我读本页
    Then 我知道三份名单现在不是图写的
    And 我知道不该再扩那份事件白名单配置

  Scenario: 我知道下一步写什么
    Given 我要交「图当可调用函数」方案
    When 我对照 Case E
    Then 方案里写清名单改回图听事件落账
    And 我不会把引擎特供当成正确合同
```
