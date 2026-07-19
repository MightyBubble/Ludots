# Core Field2D

Core Field2D 是全图或大面积二维栅格数据的共享数据结构层。Fog 已迁移到该层；后续 wind flow、heat map、water mask、influence map 等都应复用它，而不是在各能力中重复维护 chunk、dirty 和 SoA 存储。

## 决策

- `ChunkedField2D<T>` 是 Core-owned field container，按 `FieldGridSpec2D` 定义 cell size 与 chunk size。
- `FieldChunk2D<T>` 是固定 chunk 内的 SoA storage；`byte`、`float`、`Vector2`、`Vector3`、`Vector4` 等值通过 `FieldValueCodec<T>` 映射到 lanes。
- dirty cell、non-default cell、chunk indexing 都在 field 层维护，调用方通过 caller-provided span 复制数据。
- warm path 不分配：读、写、dirty enumerate、copy non-default、fog projector、Raylib texture plan 在 warmup 后都必须保持 0 alloc。
- default value 表示未显式写入状态；fog 中的 `CellVisibility.Unseen` 是 default。

## 术语

- Cell: 逻辑格坐标，使用 `FieldCell2D`。
- Grid: world cm 与 cell/chunk 的映射，使用 `FieldGridSpec2D`。
- Chunk: 固定尺寸 cell block，按 chunk x/y 寻址。
- Lane: SoA 通道。`byte` 是 struct lane，`Vector2` 是两个 float lanes。
- Dirty: 本帧或上次清理后变化过的 cell，用于增量投影与 texture upload。
- Non-default: 当前值不等于 default value 的 cell，用于稀疏复制和 snapshot。

## 边界红线

- Core Field2D 不包含 Presentation、Raylib、Skia、texture 或 renderer 类型。
- 领域能力不能各自复制 chunked dirty field；缺能力先扩展 `Core.Fields`。
- 不允许在 hot path 返回 `IEnumerable`、LINQ 结果或新数组；调用方提供 `Span<T>`。
- dirty 被消费后由 projector 或调用方显式 `ClearDirty()`，不能由 renderer 反向修改领域数据。
- adapter 不能直接读取 `ChunkedField2D<T>`；adapter-facing 数据必须先投影到 Presentation buffer。

## Fog 迁移规则

Fog 的正式链路是：

```text
VisionResolver -> FogField(ChunkedField2D<CellVisibility>) -> FogGlobalFieldVisualProjector -> GlobalFieldVisualBuffer -> adapter
```

`FogField.CopyCells` 只复制 non-default cells；`FogField.EnumerateDirtyCells` 只复制 dirty cells。`FogGlobalFieldVisualProjector` 会保留每个 fog field 的历史 bounds，并把 dirty rect 纳入 bounds，保证 texture 不因 cell 收缩而漏掉清空区域。

## 当前实现入口

- `src/Core/Fields/FieldCell2D.cs`
- `src/Core/Fields/FieldGridSpec2D.cs`
- `src/Core/Fields/FieldChannelKind.cs`
- `src/Core/Fields/FieldValueCodec.cs`
- `src/Core/Fields/FieldChunk2D.cs`
- `src/Core/Fields/ChunkedField2D.cs`
- `src/Core/Vision/FogField.cs`
- `src/Tests/GasTests/Spatial/CoreField2DTests.cs`
- `src/Tests/GasTests/Vision/FogFieldTests.cs`
