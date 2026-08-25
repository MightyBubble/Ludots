# pres-01 reference · 表现器档案

> 现状参考。第一性需求见 [pres-01 PRD](../prd/pres-01-performers.md)；配置说明见 [pres-01 配置说明](../config/pres-01-performers.md)。

## 1. 现状快照

- 表 `Presentation/presenters.json`：ArrayById、AllowEmpty、ShardDirectories 启用（分片目录 `Presentation/presenters`；见事实页分片清单）。
- 加载器字段：id、bindings（paramKey/source/constantValue）、behaviors（slot/kind/assetBinding，其中 AssetBinding.visibilityParamKey 以 Int param 声明可见性）、paramDefaults、rules、children；顶层 `visibility` 字段已移除，出现即在加载期报迁移错误。构造注入 mesh/material/text/template/effect/animator/profile/batch 的 id 解析委托与 kind/slot 白名单。
- `prefabs` 与 `presentation_behaviors` 两表**不存在**：MeshAssetConfigLoader 对 type:Prefab 显式抛错并指路"用带 AssetBinding 子项的表现器"；行为内联在 presenters 的 `behaviors` 数组与 instanced_batches 的 `behaviors` 字段。
- 消费方：表现实体生命周期 / Presenter 生成系统。

## 2. 代码锚点

| 机制 | 位置 |
|---|---|
| 加载器（Load 合并与解析入口） | src/Core/Presentation/Config/PresenterDefinitionConfigLoader.cs:76 |
| 引擎挂接（id 解析委托注入） | src/Core/Engine/GameEngine.cs:1208 |
| 分片声明 | assets/config_catalog.json:249-251 |
| 样例档案 | mods/LudotsCoreMod/assets/Presentation/presenters.json:2-50 |
| Prefab 拒绝与指路 | src/Core/Presentation/Config/MeshAssetConfigLoader.cs:59-63 |

**相关文档**：[pres-01 PRD](../prd/pres-01-performers.md) · [pres-02 reference](pres-02-asset-registry.md)
