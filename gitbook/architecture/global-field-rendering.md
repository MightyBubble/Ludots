# Global Field Rendering

Global Field Rendering 是面向全图或大面积栅格数据的正式 Presentation lane。它服务 fog、weather、water、flow、heat、influence 等非实体绑定数据，避免为每一种全图效果创建一套 per-entity performer 或 adapter 私有旁路。

## 决策

- Core 只拥有 `GlobalFieldVisualBuffer`、`GlobalFieldVisualDescriptor`、`GlobalFieldVisualId`、`GlobalFieldVisualCell` 与 dirty rect contract。
- 运行时允许存在一个平台端 field render performer 实例；这里的“单例”是 host loop 中的长期 renderer resource，不是 process-wide static，也不是 Core service。
- Vision、Weather、Water 等领域模块负责把自己的 field 投影到 `GlobalFieldVisualBuffer`。adapter 只消费该 buffer，不读取领域 store。
- Raylib 端以 `RaylibFieldRenderPerformer` 持有平台 texture/mesh/material resource，按 `GlobalFieldVisualId` 复用 texture，并只上传 dirty rect。
- adapter 可以按 backend 能力为某个 `GlobalFieldVisualKind` 增加显式渲染 contract；禁止临时从 `FogFieldStore`、weather store 或 water store 直接画。

## 帧生命周期

1. Host loop 在 simulation/presentation tick 后调用 `GlobalFieldVisualBuffer.BeginFrame()`。
2. 领域 projector 写入本帧 active field records。
3. 每条 record 携带稳定 id、cell size、cell bounds、value kind、cells 和 dirty rects。
4. Raylib field performer 根据 stable id 查找或创建 texture state。
5. bounds 变化触发整张 texture 重建；dirty rect 变化只上传对应区域。
6. draw pass 在 3D mode 中绘制 map-aligned textured plane。

## 边界红线

- `src/Core/Presentation/Rendering/GlobalFieldVisualBuffer.cs` 不能引用 Raylib、Skia、窗口、GPU texture 或平台句柄。
- Raylib field renderer 的 public input 只能是 Presentation field buffer；不能公开 `FogFieldStore` 或其它领域 store。
- Fog 的可视化适配必须走 `FogGlobalFieldVisualProjector -> GlobalFieldVisualBuffer -> RaylibFieldRenderPerformer`。
- Influence 的可视化适配必须走 `InfluenceGlobalFieldVisualProjector -> GlobalFieldVisualBuffer(Influence) -> RaylibFieldRenderPerformer`（float 量化为 byte 热力）。
- 不允许保留 per-cell overlay fallback 作为生产渲染路径。
- 不允许为 weather、water、flow、heat、influence 分别新建平行 renderer family；先扩展 shared buffer 的 kind/value contract。

## Diagnostics

`PresentationTimingDiagnostics` 记录全局 field render 数据：

- `field`: 本帧 field render pass 耗时。
- `fieldCount`: 本帧 active texture/record 数。
- `fieldDirty`: dirty texture upload 次数。
- `fieldArea`: dirty upload cell 面积。
- `fieldDraws`: field draw call 数。

这些值会进入 Raylib diagnostic line，用于 UAT 和性能回归判断。

## 当前实现入口

- `src/Core/Presentation/Rendering/GlobalFieldVisualBuffer.cs`
- `src/Core/Vision/FogGlobalFieldVisualProjector.cs`
- `src/Core/Presentation/Fields/InfluenceGlobalFieldVisualProjector.cs`
- `src/Client/Ludots.Client.Raylib/Rendering/RaylibFieldRenderPerformer.cs`
- `src/Adapters/Raylib/Ludots.Adapter.Raylib/RaylibHostLoop.cs`
- `src/Tests/GasTests/Vision/GlobalFieldVisualBufferTests.cs`
- `src/Tests/RaylibAdapterTests/RaylibFieldRenderPerformerTests.cs`
