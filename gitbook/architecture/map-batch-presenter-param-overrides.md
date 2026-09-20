# Map Batch Presenter Param Overrides

本文定义 map-authored entity batch spawn 如何把 per-instance presenter param overrides 带入 entity-anchored root presenter bootstrap。

## Contract

`EntitySpawnData.PresenterParamOverrides` 是 map authoring 的一部分，只能随现有 template batch path 流动：

- `MapLoader` 解析 `ParamKey`、`Lane` 和 lane 对应的 typed value。
- `TemplateBatchSpawnRequest` 保存 `ParamDefault[]` sidecar，不改变实体组件填充。
- direct presenter bootstrap batch 把每个 owner 的 overrides 传给 root presenter。
- `PresenterEntityRuntime.CreateEntityAnchoredRootBatch` 在 root `ParamDefaults` 之后、child 创建之前应用 overrides。
- child presenter 通过 parent param resolver 读取到的是 map-authored override，而不是 root 默认值。

## Validation

Core 必须拒绝以下情况：

- `PresenterParamOverrides` 出现在非 template batch-compatible entity 上。
- batch template 没有 direct presenter bootstrap。
- presentation runtime 没有安装。
- override 缺少 `Lane`。
- `ParamKey` 为空、空白或带首尾空白。
- `Vector` lane 的 `VectorValue` 不是 4 个值。

这些错误必须在 Core map load / batch request 构建阶段显式失败，不能由 adapter 或后置扫描修补。

## Boundary

adapter 不拥有 map presenter param 真相。adapter 只消费由 presenter params 派生出的 presentation payload，不能通过平台私有缓存、post-load scan 或 renderer repair path 补回 map-authored params。
