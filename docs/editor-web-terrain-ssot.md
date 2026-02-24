---
文档类型: 架构设计
创建日期: 2026-02-05
最后更新: 2026-02-05
维护人: X28技术团队
文档版本: v0.1
适用范围: 工具链 - Web 编辑器 - 地形 SSOT
状态: 草案
---

# Web 编辑器地形：Single Source of Truth（WASM）方案

## 目标
- 地形数据的读写只在 C#（WASM）侧发生，JS 只负责渲染与输入采样。
- 刷子拖拽不再直接改 JS 的 `Uint8Array chunkData`，避免双份状态漂移。
- 渲染更新按 Chunk 局部增量推送，避免全量重建与无谓 interop。

## 当前问题
- JS 侧维护 `editorState.chunkData` 并直接修改；C# 侧并不知道这些临时修改，直到保存/同步才可能接收。
- 交互期既有几何重建开销，又叠加 JS→.NET 高频调用带来的 UI 重渲染压力。

## 建议数据流（推荐）
1. JS 输入层（鼠标/触控）只做：
   - 计算 brush 的中心 hex 与影响半径。
   - 发送一条“刷子命令”给 WASM：`ApplyBrush(category, mode, value, size, centerC, centerR)`。
2. WASM（C#）负责：
   - 持有 `VertexMap`（或更贴近渲染的紧凑缓冲）作为唯一数据源。
   - 执行 brush 逻辑（Set/Raise/Lower/Smooth），并计算受影响的 chunk 列表。
3. WASM→JS 渲染层：
   - 对每个受影响 chunk，返回该 chunk 的层数据（最好是 `byte[]` 或 `ArrayBuffer` 语义）。
   - JS 只根据回传 chunk 数据触发局部网格更新，不自己改数据。

## Interop 形态建议
- 短期：WASM 返回 `base64` 的 chunk 数据（与现有格式兼容），JS 批量接收后更新。
- 中期：使用 `IJSUnmarshalledRuntime` 或相近机制传递 `Span<byte>`/内存视图，减少 base64 成本。
- 长期：WASM 直接产出顶点缓冲（position/normal/indices），JS 只绑定 `BufferGeometry`，实现低拷贝渲染。

## 最小落地步骤
1. 在 Blazor 侧把 `VertexMap` 作为组件/服务状态持久化（不再临时 new 后丢弃）。
2. 增加 `JSInvokable ApplyBrush(...)`，返回受影响 chunk 列表与数据。
3. JS 侧把 `applyBrush()` 替换成调用 WASM 并按返回结果更新。
4. 完成后删除 JS 侧对 `chunkData` 的写路径，仅保留只读缓存（或完全移除）。

## 调试落地：从 Web 编辑器导出到引擎加载
当前 Web 编辑器导出文件固定为 `map_data.bin`（布局见 `docs/CONFIGURATION.md`），该格式与 Core 运行期并不直接兼容。为了便于“快速把编辑器产物灌进引擎调试”，建议使用调试中间格式 `VertexMapBinary`（VTXM）。

推荐流程：
1. Web 编辑器导出 `map_data.bin`
2. 使用工具转换为 `*.vertexmap.bin`：
   - `dotnet run --project src/Tools/Ludots.Tool/Ludots.Tool.csproj -- map import-react --in <path-to-map_data.bin> --name <mapId> --force`
3. 在 `Maps/<mapId>.json`（或 `Configs/Maps/<mapId>.json`）中设置：
   - `"DataFile": "Data/Maps/<mapId>.vertexmap.bin"`
4. `GameEngine.LoadMap(<mapId>)` 会把 `DataFile` 加载到 `GameEngine.VertexMap`，并注入脚本上下文 `ContextKeys.VertexMap`。
