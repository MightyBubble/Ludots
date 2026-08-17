# pres-02 reference · 表现资产清单

> 现状参考。第一性需求见 [pres-02 PRD](../prd/pres-02-asset-registry.md)；配置说明见 [pres-02 配置说明](../config/pres-02-asset-registry.md)。

## 1. 现状快照

- mesh_assets：MeshAssetConfigLoader；字段 id、type（Primitive/Model/Billboard）、primitiveKind、vfx；禁 sourceUris、禁 type:Prefab（抛错指路 Presenter AssetBinding）；注册进 MeshAssetRegistry 供渲染器。
- material_assets：PresentationMaterialConfigLoader；id、domain 必填枚举、flags（含 blend mode）；禁 sourceUris。
- host_assets：PresentationHostAssetConfigLoader.Apply(backendId)；id、backendId 行过滤、assetKind（Mesh/Material）、assetId、sourceUris 平台真实路径；由 RaylibHostComposer 消费。
- instanced_batches：InstancedBatchAssetConfigLoader；id、renderPath、ownerStableId、groups 非空、customDataChannels、behaviors、progressiveSubmission；支持 GAS/Presentation 事件键解析；**全仓库无 JSON 行数据（D1）**。
- 引擎侧：lod_profiles（id + high/medium/low 各 maxDistanceCm/minScreenCoverage01）、particle_vfx（根表空，内容在 mod）。

## 2. 代码锚点

| 机制 | 位置 |
|---|---|
| mesh 加载（Prefab 拒绝/禁 sourceUris） | src/Core/Presentation/Config/MeshAssetConfigLoader.cs:37,59-63 |
| material 加载 | src/Core/Presentation/Config/PresentationMaterialConfigLoader.cs:23-38 |
| host 加载（backendId 过滤） | src/Core/Presentation/Config/PresentationHostAssetConfigLoader.cs:26-62 |
| host 消费 | src/Adapters/Raylib/Ludots.Adapter.Raylib/RaylibHostComposer.cs:54 |
| instanced 批次加载 | src/Core/Presentation/Config/InstancedBatchAssetConfigLoader.cs:16,58-84 |
| 四表引擎挂接 | src/Core/Engine/GameEngine.cs:1108-1117 |
| lod_profiles / particle_vfx 挂接 | src/Core/Engine/GameEngine.cs:1106-1107 |

**相关文档**：[pres-02 PRD](../prd/pres-02-asset-registry.md) · [pres-01 reference](pres-01-performers.md)
