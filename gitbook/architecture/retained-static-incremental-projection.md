# Retained Static Incremental Projection

本文定义 retained presentation flush 在 static-instance lane 上的增量投影规则。

## Contract

`StableDrawCache` 持有完整 resident retained content。它同时维护：

- `ContentRevision`：所有 retained 内容变化。
- `NonStaticContentRevision`：非 static-instance-lane 内容变化。
- static mesh delta channel：changed static items 和 removed static stable ids。

`PresentationRequestFlushSystem` 的投影规则：

- transient target clearing、presentation target generation 变化、non-static retained 变化，必须 full projection。
- 只有 static-instance-lane 变化且 target generation 未变时，使用 static delta patch 更新 persisted projection。
- snapshot buffer 保留完整 resident span。
- visible draw buffer 只保留当前 visible static instances。

## Buffer Behavior

`PrimitiveDrawBuffer.ApplyStaticMeshDelta` 通过 static stable id 索引执行：

- changed static item 已存在时原地替换。
- changed static item 不存在且应保留时追加。
- removed stable id 只删除对应 static item。
- visible-only buffer 会移除不再 visible 的 static item。
- 非 static-instance-lane delta 是 contract violation，必须抛错。

## Boundary

这不是 progressive submission，也不改变 adapter 能力声明。它只减少 Core retained flush 在 static-only changed frame 上的重复投影工作；adapter 仍消费同一份 snapshot/draw buffers。
