# Case E 交接

> 结构说明：[case-e-config-structure.html](./case-e-config-structure.html)  
> 配置宪法：[input-config-constitution.html](./input-config-constitution.html)（§12 为下单合同）  
> 图当可调用函数（另单方案正本）：[graph-callable-function-vision.md](../../../../../gitbook/architecture/graph-callable-function-vision.md)

## 1. 概述

Case E 是框选演示：按下拖框，抬起落定。
合同纠偏已到位——起角落指挥官黑板，屏幕框直读黑板和活指针，不靠地图变量，开机不硬塞玩法键。
名单落账也已图化：三份名单（可框选 / 预览黄环 / 已选中）由图经 `WriteCollection` 写，引擎特供通道（`collection_event_writers.json`、`DispatchCollectionEvent`）已退役删除。

当前欠的一笔：**Case E 还不下单。**§12 接口空置。

## 2. 结构

```text
已按合同
  起角 → 指挥官黑板（box_begin 边沿图）
  活角 → 本机指针（LoadPointerScreenX/Y）
  屏幕框 → presenter ScreenRect 直读黑板+指针
  命中 → box_hit 查询函数（box_commit / box_hover_tick 复用同一张）
  关框选态 → box_commit 图 DeactivateContext，scope 整组清
  三份名单 → 图写集合（WriteCollection 单 op）
```

## 3. 详情：下一步是全链下单（迁移切1）

按宪法 §12：battle profile 声明 `activeCollectionKey（已退役：意图自带成员集，v2）: "selected"`；
新增右键 Command 动作 + 提交图（ScreenPointToGround → `SubmitCommandIntent` op → 意图缓冲）；
下令域按活跃 context 声明的键读集合，令下给成员。

验收目标：按下拖框 → 抬起选中 → 右键 → 选中的陆战队移动。
headless + trace 入 `artifacts/acceptance/`。

## 4. 场景

1. 接手 Case E：先读本页，再读宪法 §12。
2. 切1 需要 `SubmitCommandIntent` op 与意图缓冲先落地（引擎侧）。
3. 别在 Case E 玩法单里写 C#——下单触发也是图。

## 5. 边界

- 本页只记 Case E。
- 合同纠偏与名单图化别重做。
- 引擎零特权键：没有任何默认集合键或默认 profile 兜底，无声明路由的 context 下单即具名拒绝。
- 本页不替代可调用函数远景正本；方案仍按正本模板交。

## 6. UAT

```gherkin
Feature: Case E 交接说得清

  Scenario: 我知道现状
    Given 我是接手 Case E 的人
    When 我读本页
    Then 我知道三份名单已经由图写
    And 我知道引擎特供通道已退役

  Scenario: 我知道下一步
    Given 我要落切1 全链下单
    When 我对照宪法 §12
    Then battle 声明 activeCollectionKey（已退役：意图自带成员集，v2）
    And 右键提交图经 SubmitCommandIntent 下单
    And 全程没有 C# 玩法代码
```

---

## 附录：可调用函数方案单（另交，别在本单合 Core）

正本填满后再交人审。必读顺序与验收表见远景正本。  
对照 Case E 时，至少对齐：可框选 / 预览 / 已选中三份名单、whileActive 拖框、抬起复用同一张命中图。
