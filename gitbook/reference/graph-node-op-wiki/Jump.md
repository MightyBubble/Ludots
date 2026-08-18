# 满了就跳过续杯

杯是满的：续杯那几行被划掉，指针直接飞到收工行。

<video controls playsinline preload="metadata" poster="artifacts/evidence/capability_standard_graph_op_Jump/poster.png" src="artifacts/evidence/capability_standard_graph_op_Jump/play.mp4">
你的浏览器打不开这段录像。请从仓库打开 `artifacts/evidence/capability_standard_graph_op_Jump/play.mp4`。
</video>

## 作者写法

第一次来的 mod 作者看这里：这颗节点在 `assets/GAS/graphs.json`（或 `GAS/graphs/` 分片）里怎么写。签名取自引擎描述表，用例摘自画廊作者图，两处都是单一事实源。

| 项 | 值 |
|----|----|
| 可用图种 | 仅 Script |
| 返回 | 无（副作用节点） |
| 输入端口（值边 toPort） | 无（不收值边，靠 imm/自身上下文） |
| 特殊写法 | — |

手册分册（全量字段与语义）：[脚本控制流 · gr-op-14](../mod-editor-prd/config/gr-op-14-control-flow.md)

真实用例（摘自 `mods/showcases/capability_standard/CapabilityStandardGraphOpsNodeGalleryMod/assets/GAS/graphs/Jump.json`）：

```json
{"id": "skip", "op": "Jump"}
```


## 1. 概述

这场短剧只讲一个图节点会在玩家眼里变成什么。标题用人话，不拿技术名当主角。

- 家族：脚本控制流
- 启动绑定：`capability_standard_graph_op_Jump`
- 作者记号：`Jump`（给写图的人对照，不出现在玩家字幕里）

## 2. 结构

| 角色 | 路径 |
|------|------|
| 玩家录像 | `artifacts/evidence/capability_standard_graph_op_Jump/play.mp4` |
| 画廊海报 | `artifacts/evidence/capability_standard_graph_op_Jump/poster.png` |
| 剧本 | `mods/showcases/capability_standard/CapabilityStandardGraphOpsNodeGalleryMod/assets/Vignettes/Jump.json` |
| 作者图 | `mods/showcases/capability_standard/CapabilityStandardGraphOpsNodeGalleryMod/assets/GAS/graphs/Jump.json` |

## 3. 详情

字幕模板（占位符由短剧填上）：

> 满了就不续了。茶水 {water}/{limit}，续杯那几行划掉，直接收工。

## 4. 场景

1. 从画廊或启动器打开 `capability_standard_graph_op_Jump`。
2. 舞台上能看见人和头顶血条（或这场短剧写明的可见反馈）。
3. 短剧演算时，字幕只讲这一件事。
4. 录像里不应夹带其它节点的完整剧情。

## 5. 边界

- 玩家入口是这一场，不是家族聚合场。
- 字幕禁止堆 opcode / True / False / 耗时数字。
- 缺 `play.mp4` 或 `poster.png` 时，站点与生成器必须失败关闭，不得用空片顶替。

## 6. UAT

```gherkin
Feature: 满了就跳过续杯

  Scenario: 新玩家看懂这场短剧
    Given 玩家打开 capability_standard_graph_op_Jump
    And 页面或本地能播 artifacts/evidence/capability_standard_graph_op_Jump/play.mp4
    When 短剧演完
    Then 字幕讲的是「杯是满的：续杯那几行被划掉，指针直接飞到收工行。」这类人话
    And 画面反馈和字幕说的是同一件事
```

## 怎么进

```text
scripts/run-mod-launcher.cmd cli launch $capability_standard_graph_op_Jump --adapter raylib
```
