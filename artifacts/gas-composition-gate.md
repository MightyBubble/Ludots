# GAS Composition Gate — 小地图指针区域与滚轮

- **Task / Issue**: 谁显示小地图谁进入小地图交互状态；滚轮改成该状态上的图
- **Date**: 2026-10-13
- **Agent / Author**: cloud agent

## GAS Composition Gate — Self Review

- **Task / Issue**: 小地图屏幕区域激活前台交互状态；滚轮缩放改为绑定与图；命令点地仍用显式投影
- **Date**: 2026-10-13
- **Agent / Author**: cloud agent

### 1. Core judgment

新变体主要交付物是（A/B/C/D）: A

结论: PASS

一句话理由: 区域只是屏幕矩形，激活走已有的 `Activate` / `Deactivate`；滚轮是一颗原子节点，上下文 profile 仍只声明已有的 foreground / pointerRouting / bindings / triggers。

### 2. Layer assignment

| 步骤/能力 | Layer (0/1/2/3) | 实现载体 |
|-----------|-----------------|----------|
| 滚轮增量写到小地图缩放 | 0 | GraphNodeOp `MinimapApplyWheelZoom`（550） |
| 命中区域、屏幕点投地面、跳相机、缩放条、两个切换 | 0 | 已有 GraphNodeOp 544–549 |
| 按下 / 松开 / 移动 / 命令 / 滚轮 | 2 | `graph.minimap.press` / `release` / `track` / `command` / `zoom` |
| 前台状态与指针归 UI | 2 | 已有 `foreground`、`pointerRouting` |
| 屏幕矩形打开哪个交互状态 | 2 | `presentation.minimap.interactionContextId` 加通用指针区域登记 |

### 3. Reuse list

- Handlers: `GasGraphOpHandlerTable`、`IGraphRuntimeApi` / `GasGraphRuntimeApi`、`MinimapRuntime.ApplyWheelZoom`
- Queues / Systems: `TriggerGraphActionBindingSystem`、`InteractionContextTriggerMountSystem`、`InteractionContextInstanceRuntime.Activate`、`CameraRuntimeSystem`、`suppressOnUiWheelCaptured`
- Resolvers / Registries: `InteractionContextProfileRegistry`、`ConfigMerger.MergeDeepObjectArrayById`
- Existing presets / graphs: `MinimapScreenToWorld`、`SubmitCommandIntent`、`DeactivateOwnContext`

### 4. New Layer 0 ops (if any)

| Op 名 | 单一职责 | 为何不能组合现有 op |
|-------|----------|---------------------|
| MinimapApplyWheelZoom | 把滚轮增量 F[A] 作用到指针 (F[B], F[C]) 处的小地图缩放 | `MinimapSetZoomFromPointer` 吃的是缩放条上的屏幕点，不是轴增量；轴值也不能冒充屏幕点 |

### 5. Transaction boundary

必须原子 rollback 的步骤: 无。滚轮一次写入；投地失败仍由已有 `JumpIfFalse` 停掉后续提交。

### 6. Config SSOT

行为配置落在: `mods/LudotsCoreMod/assets/GAS/graphs/graph.minimap.*.json`（含滚轮图和 `Minimap.Zoom` 绑定）、`mods/CoreInputMod/assets/Input/interaction_context_profiles.json`（把 `Command` 合并进 `interaction.context.minimap`）、`mods/LudotsCoreMod/assets/game.json` 的 `presentation.minimap.interactionContextId`

是否新增 JSON schema: NO — 交互上下文 profile、TriggerGraph、以及小地图配置上的一个已有对象字段。指针区域不是新的玩法枚举。

### 7. Red flag scan

- [x] 未新增 profile inherit/placement enum
- [x] 未新建与 spawn 平行的物化管线
- [x] 未把 placement 校验塞进 lifecycle op
- [x] 未添加「说不清的」默认 fallback

### 8. Next variant test

「下一个 Mod 变体」将修改: graph 连线，或 `presentation.minimap.interactionContextId` 指向的已有交互状态。不改 Core enum。
