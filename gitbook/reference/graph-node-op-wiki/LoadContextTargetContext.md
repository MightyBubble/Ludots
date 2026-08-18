# 从情境信封找出额外那个人

信封第三格写着这一击还要照顾谁。

<video controls playsinline preload="metadata" poster="artifacts/evidence/capability_standard_graph_op_LoadContextTargetContext/poster.png" src="artifacts/evidence/capability_standard_graph_op_LoadContextTargetContext/play.mp4">
你的浏览器打不开这段录像。请从仓库打开 `artifacts/evidence/capability_standard_graph_op_LoadContextTargetContext/play.mp4`。
</video>

## 作者写法

第一次来的 mod 作者看这里：这颗节点在 `assets/GAS/graphs.json`（或 `GAS/graphs/` 分片）里怎么写。签名取自引擎描述表，用例摘自画廊作者图，两处都是单一事实源。

| 项 | 值 |
|----|----|
| 可用图种 | Effect / Score / Validation / Derived |
| 返回 | Entity → 实体寄存器 |
| 输入端口（值边 toPort） | 无（不收值边，靠 imm/自身上下文） |
| 特殊写法 | 结果写入 dst 寄存器 |

手册分册（全量字段与语义）：[黑板与配置 · gr-op-05](../mod-editor-prd/config/gr-op-05-blackboard.md)

真实用例（摘自 `mods/showcases/capability_standard/CapabilityStandardGraphOpsNodeGalleryMod/assets/GAS/graphs/LoadContextTargetContext.json`）：

```json
{"id": "tgtCtx", "op": "LoadContextTargetContext"}
```


## 1. 概述

这场短剧只讲一个图节点会在玩家眼里变成什么。标题用人话，不拿技术名当主角。

- 家族：黑板与配置
- 启动绑定：`capability_standard_graph_op_LoadContextTargetContext`
- 作者记号：`LoadContextTargetContext`（给写图的人对照，不出现在玩家字幕里）

## 2. 结构

| 角色 | 路径 |
|------|------|
| 玩家录像 | `artifacts/evidence/capability_standard_graph_op_LoadContextTargetContext/play.mp4` |
| 画廊海报 | `artifacts/evidence/capability_standard_graph_op_LoadContextTargetContext/poster.png` |
| 剧本 | `mods/showcases/capability_standard/CapabilityStandardGraphOpsNodeGalleryMod/assets/Vignettes/LoadContextTargetContext.json` |
| 作者图 | `mods/showcases/capability_standard/CapabilityStandardGraphOpsNodeGalleryMod/assets/GAS/graphs/LoadContextTargetContext.json` |

## 3. 详情

字幕模板（占位符由短剧填上）：

> 信封额外那格指向{named}。

## 4. 场景

1. 从画廊或启动器打开 `capability_standard_graph_op_LoadContextTargetContext`。
2. 舞台上能看见人和头顶血条（或这场短剧写明的可见反馈）。
3. 短剧演算时，字幕只讲这一件事。
4. 录像里不应夹带其它节点的完整剧情。

## 5. 边界

- 玩家入口是这一场，不是家族聚合场。
- 字幕禁止堆 opcode / True / False / 耗时数字。
- 缺 `play.mp4` 或 `poster.png` 时，站点与生成器必须失败关闭，不得用空片顶替。

## 6. UAT

```gherkin
Feature: 从情境信封找出额外那个人

  Scenario: 新玩家看懂这场短剧
    Given 玩家打开 capability_standard_graph_op_LoadContextTargetContext
    And 页面或本地能播 artifacts/evidence/capability_standard_graph_op_LoadContextTargetContext/play.mp4
    When 短剧演完
    Then 字幕讲的是「信封第三格写着这一击还要照顾谁。」这类人话
    And 画面反馈和字幕说的是同一件事
```

## 怎么进

```text
scripts/run-mod-launcher.cmd cli launch $capability_standard_graph_op_LoadContextTargetContext --adapter raylib
```
