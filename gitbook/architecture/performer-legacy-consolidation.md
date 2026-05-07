# Performer 现有基建收尾整合

> 状态：历史冻结页面。正式迁移口径已切换到 [表现层编译式 DSL 迁移计划](presentation-compiled-dsl-migration-plan.md)。
>
> 本页中的“保留 runtime performer 主线，再逐步优化”的处置方案不再是正式方向。后续删改以 owner runtime backend 和 typed recipe 架构为准。

本文定义从当前代码库迁移到 Performer-as-Actor 架构时，每个现有子系统的处置方案。

## 1 Animator 系统整合

| 文件 | 处置 | 说明 |
|------|------|------|
| `AnimatorControllerDefinition.cs` | 保留 | 状态机定义不变 |
| `AnimatorStateDefinition.cs` | 保留 | 状态定义不变 |
| `AnimatorTransitionDefinition.cs` | 保留 | 转移规则不变 |
| `AnimationProfileDefinition.cs` | 保留 | 剪辑绑定不变 |
| `AnimatorControllerRegistry.cs` | 保留 | 注册表不变 |
| `AnimationProfileRegistry.cs` | 保留 | 注册表不变 |
| `AnimatorRuntimeSystem.cs` | 重写输入源 | 从 `AnimatorParameterBuffer` 改为读 performer blackboard |
| `AnimatorRuntimeState.cs` | 保留 | 运行时状态追踪不变 |
| `AnimatorPackedState.cs` | 保留 | 128 位紧凑格式，adapter 消费 |
| `AnimatorParameterBuffer.cs` | 删除 | 被 performer blackboard 取代 |
| `AnimatorFeedbackBuffer.cs` | 重写输出 | 反馈事件写回 performer blackboard |
| `AnimationOverlayRequest.cs` | 保留 | 多层合成不变 |

## 2 VisualRuntimeState / VisualTemplate 整合

| 文件 | 处置 | 说明 |
|------|------|------|
| `VisualRuntimeState.cs` | 删除 | 字段迁移到 AssetBindingConfig + AnimatorConfig |
| `VisualTransform.cs` | 保留 | 纯数据变换，Performer 仍需读取 entity 位置 |
| `VisualRenderPath.cs` | 保留 | 枚举值不变，改由 AssetBindingConfig 持有 |
| `VisualMobility.cs` | 保留 | 枚举值不变 |
| `VisualVisibility.cs` | 保留 | 枚举值不变 |
| `VisualTemplateDefinition.cs` | 收编 | 转化为 performer definition 的 JSON 模板 |
| `VisualTemplateRegistry.cs` | 删除 | 被 PerformerDefinitionRegistry 取代 |
| `VisualTemplateConfigLoader.cs` | 删除 | 被 PerformerDefinitionConfigLoader 取代 |
| `VisualTemplateRef.cs` | 删除 | entity 不再引用视觉模板 |

字段映射：

```
VisualTemplateDefinition          →  Performer Behavior
─────────────────────────────────────────────────────────
MeshAssetId                       →  AssetBindingConfig.AssetId
MaterialId                        →  AssetBindingConfig.MaterialId
AnimatorControllerId              →  AnimatorConfig.AnimatorControllerId
AnimationProfileId                →  AnimatorConfig.AnimationProfileId
BaseScale                         →  AssetBindingConfig.LocalScale
RenderPath                        →  AssetBindingConfig.RenderPath
Mobility                          →  AssetBindingConfig.Mobility
```

## 3 Prefab 系统整合 — 由 Performer 嵌套取代

Prefab 的本质是"一组固定偏移的子 mesh"，这与 performer 树的 children + AssetBinding(Mesh) + LocalOffset 完全同构。不再需要独立的 Prefab 系统。

**等价关系：**

```
旧 Prefab                          →  新 Performer 树
─────────────────────────────────────────────────────────
PrefabDefinition                   →  PerformerDefinition（root，无自身 asset）
PrefabPart[0] (mesh_a, offset)     →  ChildPerformerRef → AssetBinding(Mesh, mesh_a, localOffset)
PrefabPart[1] (mesh_b, offset)     →  ChildPerformerRef → AssetBinding(Mesh, mesh_b, localOffset)
PrefabFinalizationPipeline         →  PerformerEmitSystem 遍历子树自然展平
```

| 文件 | 处置 | 说明 |
|------|------|------|
| `PrefabDefinition.cs` | 删除 | 由 PerformerDefinition + Children 取代 |
| `PrefabPart.cs` | 删除 | 由 AssetBindingConfig + LocalOffset 取代 |
| `PrefabRegistry.cs` | 删除 | 由 PerformerDefinitionRegistry 取代 |
| `PrefabFinalizationPipeline.cs` | 删除 | performer 树 emit 天然做了递归展平 |
| `PrefabFinalizedLeaf.cs` | 删除 | 不再需要中间展平结果 |
| `PrefabFinalizedLeafBuffer.cs` | 删除 | 同上 |
| `PrefabGroundingUtility.cs` | 收编 | 批量 grounding 逻辑移入 PerformerGroundingUtility |
| `PrefabGroundingBatchContext.cs` | 删除 | 20 个并行数组的 SoA 容器，由更简洁的接口取代 |
| `PrefabGroundingBatchBuffer.cs` | 删除 | 同上 |
| `MeshAssetDescriptor.cs` | 简化 | 移除 `MeshAssetType.Prefab` 和 `PrefabParts[]` 字段 |
| `MeshAssetRegistry.cs` | 保留 | 单个 mesh 的 ID 映射仍需要 |
| `MeshAssetConfigLoader.cs` | 简化 | 不再加载 prefabs.json，只加载 mesh_assets.json |

**迁移示例：**

旧 prefabs.json：
```jsonc
{
  "id": "cue_marker",
  "meshAssetId": "cube",
  "baseScale": 0.2,
  "parts": [
    { "meshAssetId": "cube", "localPosition": [0, 0, 0], "localScale": [0.2, 0.2, 0.2] }
  ]
}
```

新 performers.json：
```jsonc
{
  "id": "cue_marker",
  "children": [
    {
      "definitionId": "cue_marker_part_0",
      "scopeTag": "visual"
    }
  ]
},
{
  "id": "cue_marker_part_0",
  "behaviors": [
    {
      "slot": 0, "kind": "AssetBinding", "activeByDefault": true,
      "assetBinding": {
        "assetKind": "Mesh", "assetId": "cube",
        "localScale": [0.2, 0.2, 0.2]
      }
    }
  ]
}
```

对于简单的单 mesh performer（不需要子树），可以直接在自身定义 AssetBinding，不需要嵌套：

```jsonc
{
  "id": "simple_cube",
  "behaviors": [
    {
      "slot": 0, "kind": "AssetBinding", "activeByDefault": true,
      "assetBinding": { "assetKind": "Mesh", "assetId": "cube", "localScale": [0.2, 0.2, 0.2] }
    }
  ]
}
```

## 4 渲染类型三重镜像整合

提取公共结构体消除 94% 字段重复：

```
VisualRenderPayload (13 个共同字段)
├── MeshAssetId, Position, Rotation, Scale, Color
├── StableId, MaterialId, TemplateId, AnimationProfileId
├── RenderPath, Animator, AnimationOverlay, Visibility

PresentationVisualProxy = VisualRenderPayload + ProxyKind + Mobility + Flags
PrimitiveDrawItem       = VisualRenderPayload + Mobility + Flags
SkinnedVisualBatchItem  = VisualRenderPayload
```

## 5 PresentationBehavior 双轨 + PresentationCommand 链路清理

| 文件 | 处置 |
|------|------|
| `PresentationBehaviorDefinition.cs` | 删除（被 BehaviorSlot 取代） |
| `PresentationBehaviorRegistry.cs` | 删除 |
| `PresentationBehaviorResolver.cs` | 删除 |
| `PresentationBehaviorStateDefinition.cs` | 删除 |
| `Commands/PresentationCommandKind.cs` | 删除（被 PerformerCommandKind 取代） |
| `Commands/PresentationCommand.cs` | 删除（被 PerformerCommand 取代） |
| `Commands/PresentationCommandBuffer.cs` | 删除（被 PerformerCommandBuffer 取代） |
| `Commands/PresentationAnchorKind.cs` | 评估（如仍需要则移入 Performers/ 命名空间） |
| `Perform/PerformBehaviorDefinition.cs` | 删除（PR #135 双轨） |
| `Perform/PerformCommand.cs` | 删除 |
| `Perform/PerformRule.cs` | 删除 |
| `Perform/PerformCommandBuffer.cs` | 删除 |

## 6 其他清理

| 文件 | 处置 |
|------|------|
| `EntityVisualEmitSystem.cs` | 删除（双真值源） |
| `BuiltinPerformerDefinitions.cs` | 删除（迁移到 JSON） |
| `EntityScopeFilter.cs` | 删除 |
| `PerformerVisualKind.cs` | 删除（由 AssetKind 取代） |
| `PresentationStartupPerformers.cs` | 删除（子树自动展开） |
| `WellKnownPerformerParamKeys.cs` | 重写 |
