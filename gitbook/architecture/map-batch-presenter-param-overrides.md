# Map Batch Presenter Param Overrides

本文定义 map-authored entity batch spawn 如何把 per-instance presenter param overrides 带入 entity-anchored root presenter bootstrap。

## Contract

`EntitySpawnData.PresenterParamOverrides` 是 map authoring 的一部分，只能随现有 template batch path 流动：

- `MapLoader` 解析 `ParamKey`、`Lane` 和 lane 对应的 typed value。
- `TemplateBatchSpawnRequest` 保存 `ParamDefault[]` sidecar，不改变实体组件填充。
- direct presenter bootstrap batch 把每个 owner 的 overrides 传给 root presenter。
- `PresenterEntityRuntime.CreateEntityAnchoredRootBatch` 在 root `ParamDefaults` 之后、child 创建之前应用 overrides。
- child presenter 通过 parent param resolver 读取到的是 map-authored override，而不是 root 默认值。

同一条 template batch path 还携带地图实例对批量填充已经会写的组件的覆盖。前提是模板自己已经声明了该组件，行的原型不变。

允许留在这条路径上的覆盖键：

- `WorldPositionCm`
- `FacingDirection`
- `Name`
- `Team`
- `PlayerOwner`
- `AttributeBuffer`

`Name`、`Team`、`PlayerOwner`、`AttributeBuffer` 与模板组件的合并方式和 `EntityBuilder` 相同：对象字段深合并，数组整段替换，`__replace: true` 整组件替换。`AttributeBuffer` 里没写到的属性名保留模板初值。

模板上没有的组件，以及上面名单以外的组件，离开这条路径，改走 `EntityBuilder`。`PresenterParamOverrides` 仍然只能走批量路径，所以它可以和名单内的覆盖写在同一条布阵上；和会离开这条路径的覆盖写在一起时，加载直接失败。

`EntityInfoName` 不在批量填充名单里，也不改系统 `Name`。实例在 `overrides` 里写了它时，批量生成之后再把这个组件挂上，因此它可以和 `PresenterParamOverrides` 写在同一条布阵上。`Value` 必须是非空字符串，空值在进入批量生成前失败。模板自己声明了 `EntityInfoName` 的，离开批量路径。

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
