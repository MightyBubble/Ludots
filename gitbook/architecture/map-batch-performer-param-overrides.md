# Map Batch Performer Param Overrides

本文定义 map-authored entity batch spawn 如何把 per-instance performer param overrides 带入 entity-anchored root performer bootstrap。

## Contract

`EntitySpawnData.PerformerParamOverrides` 是 map authoring 的一部分，只能随现有 template batch path 流动：

- `MapLoader` 解析 `ParamKey`、`Lane` 和 lane 对应的 typed value。
- `TemplateBatchSpawnRequest` 保存 `ParamDefault[]` sidecar，不改变实体组件填充。
- direct performer bootstrap batch 把每个 owner 的 overrides 传给 root performer。
- `PerformerEntityRuntime.CreateEntityAnchoredRootBatch` 在 root `ParamDefaults` 之后、child 创建之前应用 overrides。
- child performer 通过 parent param resolver 读取到的是 map-authored override，而不是 root 默认值。

`RuntimeEntitySpawnRequest.PerformerParamOverrides` 使用同一条语义，服务 runtime template spawn 与后续 ScenarioPlan materialization：

- 只支持 `RuntimeEntitySpawnKind.Template`。
- 单个 template spawn 与 batch template spawn 都必须有 presentation runtime 和 direct entity-spawn performer bootstrap。
- 如果 direct bootstrap 因 inline condition / scope 等原因没有为带 override 的 owner 创建任何 root performer，必须显式失败，禁止把 override 静默丢弃。
- per-instance overrides 只进入 root performer params，不写入 entity component，不引入 adapter 私有缓存。
- UnitType / Assembly spawn 不支持 performer param overrides，出现即失败。

## Validation

Core 必须拒绝以下情况：

- `PerformerParamOverrides` 出现在非 template batch-compatible entity 上。
- batch template 没有 direct performer bootstrap。
- presentation runtime 没有安装。
- runtime UnitType / Assembly spawn 声明 performer param overrides。
- runtime template spawn 声明 performer param overrides 但没有 direct performer bootstrap。
- override 缺少 `Lane`。
- `ParamKey` 为空、空白或带首尾空白。
- `Vector` lane 的 `VectorValue` 不是 4 个值。

这些错误必须在 Core map load / batch request 构建阶段显式失败，不能由 adapter 或后置扫描修补。

## Boundary

adapter 不拥有 map performer param 真相。adapter 只消费由 performer params 派生出的 presentation payload，不能通过平台私有缓存、post-load scan 或 renderer repair path 补回 map-authored params。
