# 持久 static mesh lane 的 adapter dirty sync contract

本文定义 `#53` 的边界：Core 继续输出逐帧 snapshot，adapter 依据 `PresentationStableId` 在本地维护持久 static lane，并从 snapshot 计算 `create / update / remove` 操作。该 contract 只覆盖 `StaticMesh`、`InstancedStaticMesh`、`HierarchicalInstancedStaticMesh`，不吸收 skinned runtime 语义。

## 1. 架构边界

- Core 责任：
  - `src/Core/Presentation/Systems/EntityVisualEmitSystem.cs` 继续写入 `PresentationVisualSnapshotBuffer`
  - `src/Core/Presentation/Rendering/PrimitiveDrawItem.cs` 提供 `StableId`、`RenderPath`、`MaterialId`、`Mobility`、`Visibility`、`transform`
- Adapter 责任：
  - 基于 snapshot 做 stableId-driven diff
  - 本地维护 `stableId -> lane + slot + generation`
  - 把 `Visibility`、transform、材质、lane 变化转成 adapter 自己的 scene object / instance slot 更新
- 非目标：
  - 不把 dirty-only emit 回灌到 Core
  - 不要求 Web wire format 在本 issue 内承载该 contract
  - 不处理 `SkinnedMesh` / `GpuSkinnedInstance`

## 2. Lane Key

static lane 的持久 ownership 由以下 batch key 决定：

```text
StaticMeshLaneKey = (RenderPath, MeshAssetId, MaterialId, Mobility)
```

证据：

- `src/Core/Presentation/AdapterSync/StaticMeshLaneKey.cs`
- `src/Core/Presentation/Components/VisualRenderPath.cs`
- `src/Core/Presentation/Rendering/PrimitiveDrawItem.cs`

说明：

- `RenderPath` 决定对象走 `StaticMesh`、`InstancedStaticMesh` 还是 `HierarchicalInstancedStaticMesh`
- `MeshAssetId` 与 `MaterialId` 共同决定静态几何与材质/variant
- `Mobility` 保留给 Unity / Unreal / Godot adapter 做 lane 细分
- `TemplateId` 不是 batch key；它用于审计和模板追踪，不决定 slot 归属

## 3. Stable Binding Contract

adapter 本地必须维护：

```text
PresentationStableId -> (LaneKey, Slot, Generation, LastSnapshotItem)
```

语义：

- `Slot`：
  - lane 内部位置
  - 对 `StaticMesh` 可映射为持久 scene object 索引
  - 对 `InstancedStaticMesh` / `HierarchicalInstancedStaticMesh` 可映射为 batch slot
- `Generation`：
  - slot 被释放后再次复用必须递增
  - 防止 adapter 把旧实例句柄误认成新对象
- `LastSnapshotItem`：
  - 保存上次已同步的 transform / visibility / flags / color / template 信息
  - 用于判断本帧是否需要 `Update`

证据：

- `src/Core/Presentation/AdapterSync/StaticMeshAdapterBindingState.cs`
- `src/Core/Presentation/AdapterSync/StaticMeshAdapterSyncPlanner.cs`

## 4. Diff 规则

对每一帧 `PresentationVisualSnapshotBuffer`：

1. 过滤：
   - 只消费 `StaticMesh`、`InstancedStaticMesh`、`HierarchicalInstancedStaticMesh`
   - `StableId <= 0` 直接报错
   - 同一帧出现重复 `StableId` 直接报错
2. 创建：
   - 当 `StableId` 第一次出现时，按 lane key 分配 slot，发出 `Create`
3. 更新：
   - lane key 不变，但 transform / visibility / color / flags / template 等字段变化，发出 `Update`
4. lane 迁移：
   - `RenderPath` / `MeshAssetId` / `MaterialId` / `Mobility` 任一变化，必须先 `Remove` 旧 lane，再 `Create` 新 lane
5. 移除：
   - 本帧 snapshot 不再包含该 `StableId`，发出 `Remove` 并释放 slot
6. slot 复用：
   - 释放的 slot 可被后续对象复用，但 generation 必须递增

证据：

- `src/Core/Presentation/AdapterSync/StaticMeshAdapterSyncOp.cs`
- `src/Core/Presentation/AdapterSync/StaticMeshAdapterSyncOpKind.cs`
- `src/Core/Presentation/AdapterSync/StaticMeshAdapterSyncPlanner.cs`
- `src/Tests/PresentationTests/StaticMeshAdapterSyncPlannerTests.cs`

## 5. Visibility 语义

该 contract 明确区分：

- `Visible`：
  - 对象存在且当前应参与可见渲染
- `Hidden`：
  - 对象仍存在于 adapter 持久状态中，但语义上应隐藏
- `Culled`：
  - 对象仍存在于 adapter 持久状态中，只是当前被镜头/预算裁掉
- snapshot absence：
  - 对象已从 presentation world 移除，adapter 应 `Remove / release slot`

因此：

- `Hidden` / `Culled` 不是 destroy
- adapter 不得再从 draw buffer 的缺席反推 destroy
- static lane 必须以 snapshot absence 作为 remove 唯一来源

证据：

- `src/Core/Presentation/Components/VisualVisibility.cs`
- `src/Core/Presentation/Systems/EntityVisualEmitSystem.cs`
- `src/Tests/PresentationTests/PresentationFoundationTests.cs`

## 6. Raylib 参考接入

本仓库的参考接入不引入平台 API 到 contract 本身，只把 planner 用作 adapter-local snapshot diff：

- `src/Adapters/Raylib/Ludots.Adapter.Raylib/RaylibHostLoop.cs`
  - 读取 `PresentationVisualSnapshotBuffer`
- `src/Client/Ludots.Client.Raylib/Rendering/RaylibPrimitiveRenderer.cs`
  - 通过 `StaticMeshAdapterSyncPlanner` 同步 static lane
  - 从持久 lane 状态绘制 visible static visuals
  - draw buffer 仅继续处理非持久 static lane 项与其他非 static lane 项

这个接入是 contract proof，不是 Unity / Unreal 的最终实现；真正的平台 scene object / instance manager 仍由对应 adapter 自己完成。

## 7. 与 #55 的边界

- 本文不定义骨骼对象生命周期
- 不定义 `AnimatorPackedState` 到骨骼 pose / palette 的消费方式
- 不定义 `SkinnedMesh` 的 scene object / skeletal component contract
- skinned lane 只共享 snapshot 输入，不共享 static lane 的 slot ownership 语义

## 8. 回归证据

- `src/Tests/PresentationTests/StaticMeshAdapterSyncPlannerTests.cs`
  - 首次创建 static lanes
  - reorder 不产生 dirty ops
  - visibility / transform 变化只做 update
  - remove 后 slot 复用且 generation 递增
  - lane key 变化必须 remove + create
  - 非法 stableId / duplicate stableId 直接失败
- `src/Tests/PresentationTests/PresentationFoundationTests.cs`
  - snapshot 含 visibility / transform / stable identity
- `src/Tests/PresentationTests/ProjectionMapPresentationRuntimeTests.cs`
  - fixture visuals 暴露稳定 snapshot identity

## 9. 相关文档

- `docs/architecture/presentation_snapshot_contract.md`
- `docs/architecture/adapter_pattern.md`
