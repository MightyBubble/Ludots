# 点名名单按编号排好，次次一样

每波点名，五个人 1 到 5 的编号顺序一模一样，灰影对得上。

<video controls playsinline preload="metadata" poster="artifacts/evidence/capability_standard_graph_op_QuerySortStable/poster.png" src="artifacts/evidence/capability_standard_graph_op_QuerySortStable/play.mp4">
你的浏览器打不开这段录像。请从仓库打开 `artifacts/evidence/capability_standard_graph_op_QuerySortStable/play.mp4`。
</video>

## 作者写法

第一次来的 mod 作者看这里：这颗节点在 `assets/GAS/graphs.json`（或 `GAS/graphs/` 分片）里怎么写。签名取自引擎描述表，用例摘自画廊作者图，两处都是单一事实源。

| 项 | 值 |
|----|----|
| 可用图种 | 六种全可用（Effect / Score / Validation / Derived / Query / Script） |
| 返回 | 无（副作用节点） |
| 输入端口（值边 toPort） | `list`（目标名单） |
| 特殊写法 | — |

手册分册（全量字段与语义）：[图文档写法 · gr-02](../mod-editor-prd/config/gr-02-document.md)

真实用例（摘自 `mods/showcases/capability_standard/CapabilityStandardGraphOpsNodeGalleryMod/assets/GAS/graphs/QuerySortStable.json`）：

```json
{"id": "sort", "op": "QuerySortStable"}
```


## 1. 概述

这场短剧只讲一个图节点会在玩家眼里变成什么。标题用人话，不拿技术名当主角。

- 家族：组合短剧
- 启动绑定：`capability_standard_graph_op_QuerySortStable`
- 作者记号：`QuerySortStable`（给写图的人对照，不出现在玩家字幕里）

## 2. 结构

| 角色 | 路径 |
|------|------|
| 玩家录像 | `artifacts/evidence/capability_standard_graph_op_QuerySortStable/play.mp4` |
| 画廊海报 | `artifacts/evidence/capability_standard_graph_op_QuerySortStable/poster.png` |
| 剧本 | `mods/showcases/capability_standard/CapabilityStandardGraphOpsNodeGalleryMod/assets/Vignettes/QuerySortStable.json` |
| 作者图 | `mods/showcases/capability_standard/CapabilityStandardGraphOpsNodeGalleryMod/assets/GAS/graphs/QuerySortStable.json` |

## 3. 详情

字幕模板（占位符由短剧填上）：

> 点名顺序稳定：{order}。

## 4. 场景

1. 从画廊或启动器打开 `capability_standard_graph_op_QuerySortStable`。
2. 舞台上能看见人和头顶血条（或这场短剧写明的可见反馈）。
3. 短剧演算时，字幕只讲这一件事。
4. 录像里不应夹带其它节点的完整剧情。

## 5. 边界

- 玩家入口是这一场，不是家族聚合场。
- 字幕禁止堆 opcode / True / False / 耗时数字。
- 缺 `play.mp4` 或 `poster.png` 时，站点与生成器必须失败关闭，不得用空片顶替。

## 6. UAT

```gherkin
Feature: 点名名单按编号排好，次次一样

  Scenario: 新玩家看懂这场短剧
    Given 玩家打开 capability_standard_graph_op_QuerySortStable
    And 页面或本地能播 artifacts/evidence/capability_standard_graph_op_QuerySortStable/play.mp4
    When 短剧演完
    Then 字幕讲的是「每波点名，五个人 1 到 5 的编号顺序一模一样，灰影对得上。」这类人话
    And 画面反馈和字幕说的是同一件事
```

## 怎么进

```text
scripts/run-mod-launcher.cmd cli launch $capability_standard_graph_op_QuerySortStable --adapter raylib
```
