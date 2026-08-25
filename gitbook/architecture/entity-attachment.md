# Entity Attachment 玩法绑定

## 1. 概述

Entity Attachment 让一个玩法实体以局部位姿绑定到另一个实体，例如炮塔跟随底盘、炮管跟随炮塔、骑乘单位随载具移动。架构决策仍以 GitHub issue #239 与 #244 为唯一正本；本文只提供使用入口、运行合同与验收索引，不复制 ADR。

## 2. 结构

- `ChildOf` 与 `ChildrenBuffer` 保存父子关系。
- `AttachedLocalPose` 保存子实体相对父实体的局部位置与朝向规则。
- Attach/Detach 原子操作负责建边、拆边、初始落位与位姿写权切换。
- `AttachmentPositionSyncSystem` 在移动系统之后按父先子后顺序派生世界位姿。
- 模板 `children` 复用同一套局部位姿解析和运行时生成队列。

## 3. 详情

绑定时，系统先验证实体存活、父实体位姿、关系环、子槽容量和位姿写权，再一次性建立关系并写入局部位姿。任何一步失败都会恢复关系、位姿、导航成员身份和待结算写权。

同步器容量来自 `game.json` 的 `gasRuntimeCapacity.attachmentSyncScratchCapacity`。每帧一次收集挂接实体、一次解析深度、一次计数排序，然后按深度单遍写回；容量不足立即报错，不扩容、不丢实体。

解除绑定默认保持当前世界位姿。周边落位必须由调用方提供拆除前快照中的稳定槽位，走 `DetachToPerimeter`；普通 `Detach` 不猜测已被 swap-remove 改写的槽序。

## 4. 场景

- 坦克底盘移动时，炮塔与炮管在同一固定步内跟到最新位置。
- 炮塔保留独立朝向，炮管按炮塔朝向旋转局部偏移。
- 骑乘单位上车时暂停独立导航并获得 Attached 位姿写权。
- 骑乘单位下车时恢复导航，并按稳定批次槽位散布到载具周边。
- 静态建筑通过模板 `children` 形成多层组合，运行时生成前先完整预演队列容量。

## 5. 边界

- 不允许父子关系成环，也不设置人为最大层数。
- 不允许同一局部位姿同时继承父朝向又按子朝向旋转偏移。
- 不允许同步热路径临时扩容或补加缺失组件。
- 不允许直接周边拆除推测槽位；没有稳定槽位就立即失败。
- Presenter 骨骼挂点属于表现层能力，合同见 [Presenter Transform、Grounding 与 Attachment](presenter-transform-and-attachment.md)，不与玩法实体绑定共用证据。

## 6. UAT

```gherkin
Feature: 玩家观察并操作实体挂接

  Scenario: 多层载具组件在同一帧跟随
    Given 玩家进入实体挂接验收场景
    When 坦克底盘向前移动且炮塔转向侧面
    Then 炮塔应出现在底盘本步最新位置
    And 炮管应沿炮塔朝向保持 authored 局部偏移

  Scenario: 骑乘单位上车后随车并安全下车
    Given 骑乘单位可独立移动且站在坦克旁
    When 玩家触发上车效果并让坦克继续前进
    Then 骑乘单位应保持相对坦克的固定位置
    When 玩家触发周边散布下车效果
    Then 骑乘单位应落在坦克周边的稳定槽位
    And 骑乘单位应恢复独立导航能力
```

自动验收：`EntityAttachmentCapabilityAcceptanceTests`。证据目录：`artifacts/acceptance/entity-attachment/`。
