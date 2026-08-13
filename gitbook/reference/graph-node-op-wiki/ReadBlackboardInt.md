# 从记事板读出层数

板上层数 4。

<video controls playsinline preload="metadata" poster="artifacts/evidence/capability_standard_graph_op_ReadBlackboardInt/poster.png" src="artifacts/evidence/capability_standard_graph_op_ReadBlackboardInt/play.mp4">
你的浏览器打不开这段录像。请从仓库打开 `artifacts/evidence/capability_standard_graph_op_ReadBlackboardInt/play.mp4`。
</video>

## 1. 概述

这场短剧只讲一个图节点会在玩家眼里变成什么。标题用人话，不拿技术名当主角。

- 家族：黑板与配置
- 启动绑定：`capability_standard_graph_op_ReadBlackboardInt`
- 作者记号：`ReadBlackboardInt`（给写图的人对照，不出现在玩家字幕里）

## 2. 结构

| 角色 | 路径 |
|------|------|
| 玩家录像 | `artifacts/evidence/capability_standard_graph_op_ReadBlackboardInt/play.mp4` |
| 画廊海报 | `artifacts/evidence/capability_standard_graph_op_ReadBlackboardInt/poster.png` |
| 剧本 | `mods/showcases/capability_standard/CapabilityStandardGraphOpsNodeGalleryMod/assets/Vignettes/ReadBlackboardInt.json` |
| 作者图 | `mods/showcases/capability_standard/CapabilityStandardGraphOpsNodeGalleryMod/assets/GAS/graphs/ReadBlackboardInt.json` |

## 3. 详情

字幕模板（占位符由短剧填上）：

> 从记事板读出层数 {result}。

## 4. 场景

1. 从画廊或启动器打开 `capability_standard_graph_op_ReadBlackboardInt`。
2. 舞台上能看见人和头顶血条（或这场短剧写明的可见反馈）。
3. 短剧演算时，字幕只讲这一件事。
4. 录像里不应夹带其它节点的完整剧情。

## 5. 边界

- 玩家入口是这一场，不是家族聚合场。
- 字幕禁止堆 opcode / True / False / 耗时数字。
- 缺 `play.mp4` 或 `poster.png` 时，站点与生成器必须失败关闭，不得用空片顶替。

## 6. UAT

```gherkin
Feature: 从记事板读出层数

  Scenario: 新玩家看懂这场短剧
    Given 玩家打开 capability_standard_graph_op_ReadBlackboardInt
    And 页面或本地能播 artifacts/evidence/capability_standard_graph_op_ReadBlackboardInt/play.mp4
    When 短剧演完
    Then 字幕讲的是「板上层数 4。」这类人话
    And 画面反馈和字幕说的是同一件事
```

## 怎么进

```text
scripts/run-mod-launcher.cmd cli launch $capability_standard_graph_op_ReadBlackboardInt --adapter raylib
```
