# GAS Composition Gate — 小地图指针交互状态

- **Task / Issue**: 小地图指针改走前台交互状态；Core 指针采样改名 PointerSample
- **Date**: 2026-10-12
- **Agent / Author**: cloud agent

## GAS Composition Gate — Self Review

- **Task / Issue**: 小地图点击、拖拽、缩放条、切换按钮、命令点地改为交互状态上的图
- **Date**: 2026-10-12
- **Agent / Author**: cloud agent

### 1. Core judgment

新变体主要交付物是（A/B/C/D）: A

结论: PASS

一句话理由: 行为是六颗原子节点在 `graph.minimap.*` 里连线，上下文 profile 只声明已有的 foreground / pointerRouting / bindings / triggers，没有新的玩法枚举。

### 2. Layer assignment

| 步骤/能力 | Layer (0/1/2/3) | 实现载体 |
|-----------|-----------------|----------|
| 命中哪一块、屏幕点投地面、跳相机、按指针写缩放、切预设、切旋转 | 0 | GraphNodeOp 544–549 |
| 按下 / 松开 / 移动 / 命令 / 悬停 | 2 | LudotsCoreMod `assets/GAS/graphs/graph.minimap.*.json` |
| 前台状态与指针归 UI | 2 | 已有 `InteractionContextProfile` 字段 `foreground`、`pointerRouting` |
| 滚轮缩放 | 2 | `MinimapRuntime.TryConsumePointerWheel`，再走已有 `suppressOnUiWheelCaptured` |

### 3. Reuse list

- Handlers: `GasGraphOpHandlerTable`、`IGraphRuntimeApi` / `GasGraphRuntimeApi`
- Queues / Systems: `TriggerGraphActionBindingSystem`、`InteractionContextTriggerMountSystem`、`InputRuntimeSystem`、`CommandIntentBufferDrainSystem`
- Resolvers / Registries: `InteractionContextProfileRegistry`、`MinimapRuntime`、`AuthoritativeGroundPointerHelper`
- Existing presets / graphs: `ActivateContext`（省略 `parentContext`）、`DeactivateOwnContext`、`JumpIfFalse`、`SubmitCommandIntent`、`QueryFromCollection`

### 4. New Layer 0 ops (if any)

| Op 名 | 单一职责 | 为何不能组合现有 op |
|-------|----------|---------------------|
| QueryMinimapRegion | 屏幕点落在小地图哪一块 | 没有现成节点读小地图命中矩形；区域码是小地图布局，不是通用 UI 查询 |
| MinimapScreenToWorld | 屏幕点按小地图投到地面厘米 | `ScreenPointToGround` 走相机射线，不读小地图投影 |
| MinimapJumpCamera | 把 TargetPosCm 交给小地图跳相机 | 没有现成节点写 `MinimapRuntime.JumpCameraTo` |
| MinimapSetZoomFromPointer | 用屏幕点在缩放条上写缩放 | 没有现成节点写 `SetZoomFromSliderPointer` |
| MinimapTogglePreset | 全图 / 跟随相机切一次 | 没有现成节点切 `MinimapPreset` |
| MinimapToggleRotate | 跟不跟相机转切一次 | 没有现成节点翻转 `RotateWithCamera` |

### 5. Transaction boundary

必须原子 rollback 的步骤: 无。每颗节点一次写入；图用 `JumpIfFalse` 在投地失败时停掉后续跳相机或提交。

### 6. Config SSOT

行为配置落在: `mods/LudotsCoreMod/assets/GAS/graphs/graph.minimap.*.json` 与 `mods/LudotsCoreMod/assets/Input/interaction_context_profiles.json`

是否新增 JSON schema: NO — 沿用交互上下文 profile 和 TriggerGraph 文档。滚轮不新加图节点，仍走 `presentation.minimap.actions.zoom`。

### 7. Red flag scan

- [x] 未新增 profile inherit/placement enum
- [x] 未新建与 spawn 平行的物化管线
- [x] 未把 placement 校验塞进 lifecycle op
- [x] 未添加「说不清的」默认 fallback

### 8. Next variant test

「下一个 Mod 变体」将修改: graph 连线。别的战斗上下文挂上 `graph.minimap.hover` 即得到同一套指针行为；不改 Core enum。
