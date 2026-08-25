# Presenter-as-Actor 开发看板

## 任务依赖图

```
T1 BehaviorSlot + AssetKind + SplineConfig 类型定义
T2 PresenterParamBlackboard（多类型三 lane）
T3 PresenterCommand 扩展（7 种 CommandKind）
    │
    ├──→ T4 PresenterInstance 重写（树形指针 + BehaviorActiveMask + TransformSource）
T3 PresenterCommand 扩展（7 种 CommandKind）
    │       └──→ T5 PresenterInstanceBuffer 重写（树形操作 + blackboard 集成）
    │               │
    │               ├──→ T7 PresenterRuntimeSystem 重写（CreatePresenter 含 parentHandle、递归销毁、scope 销毁、behavior mask）
    │               │       │
    │               │       ├──→ T9 PresenterBehaviorSystem（AttributeBinding + TagBinding + Material + Sound）
    │               │               │
    │               │       │       └──→ T11 PresenterEmitSystem 重写（只处理 AssetBinding emit）
    │               │               │
    │               │       │               └──→ T13 Raylib UAT Layer 1（逐 AssetKind）
    │               │               │
    │               │       │                       └──→ T15 Raylib UAT Layer 3（铁匠铺完整）
    │               │       │
    │               │       └──→ T10 Animator 参数统一（AnimatorRuntimeSystem 改读 blackboard）
    │               │               │
    │               │               └──→ T14 Raylib UAT Layer 2（逐 BehaviorKind）
    │               │               │
    │               │                       └──→ T15
    │               │
    │               └──→ T8 PresenterGroundingUtility + Transform 计算
    │
    └──→ T6 PresenterDefinition 重写 + ConfigLoader（新 JSON schema + children 语法糖 + extends 继承）

T12 GlobalPresentationEventProjectionSystem（日夜/区域/天气）── 可与 T9 并行
T16 VisualRenderPayload 提取（渲染三重镜像整合）── 可与 T11 并行
T17 遗留代码清理（删除 EntityVisualEmitSystem / BuiltinPresenterDefinitions / Perform 命名空间 / Prefab 系统）── T15 之后
T18 UE5 适配 ── T17 之后
```

## 看板任务列表

### Wave 1 — 基础类型（无依赖，可并行）

| ID | 任务 | 产出文件 | 验收标准 | 状态 |
|----|------|---------|---------|------|
| T1 | BehaviorSlot + AssetKind + SplineConfig 类型定义 | `Presenters/BehaviorSlot.cs`, `Presenters/AssetKind.cs` | `dotnet build` 通过；BehaviorKind 8 种、AssetKind 8 种、所有 Config struct 字段与架构文档一致 | **通过** |
| T2 | PresenterParamBlackboard 多类型三 lane | `Presenters/PresenterParamBlackboard.cs` | 单元测试：SetFloat/SetInt/SetVector + ResolveFloat 父→子继承链 + ClearAll | **通过** |
| T3 | PresenterCommand 重写 | `Presenters/PresenterCommand.cs` | 独立 PresenterCommandKind 枚举（7 种）；多类型 SetParam 字段；`dotnet build` 通过 | **通过** |

#### Wave 1 验收记录

**全部通过（2026-04-17）*

- T1：3 项修复全部到位（AttachmentConfig 4 字段、GroundingMode 枚举+AssetBindingConfig 字段、VFX 大写），全部 Config struct 无回归
- T2：三 lane 存储正确，Resolve parent chain 内聚方案接受，架构文档已回填
- T3：独立 PresenterCommandKind(0-7)、多类型 SetParam(ParamLane+IntValue+VectorValue)、命名统一(ScopeTag/TargetBehaviorSlot)、旧 PresentationCommand 链路全部删除
- 新增 7 个守卫测试（PerformContractsAndLegacyLaneTests）
- `ScopeSource` 和 `ParamGraphProgramId` 保留在 PresenterCommand 中（Rule 系统运行时需要），需补进架构文档

**M1 里程碑达成 ✓ — Wave 2 (T4/T5/T6) 已解锁*

### Wave 2 — 实例与定义（依赖 Wave 1）

| ID | 任务 | 依赖 | 产出文件 | 验收标准 | 预估 |
|----|------|------|---------|---------|------|
| T4 | PresenterInstance 重写 | T1 | `Presenters/PresenterInstance.cs` | 新增 ParentHandle/FirstChildHandle/NextSiblingHandle/BehaviorActiveMask/TransformSource/WorldRotation/WorldScale | S |
| T5 | PresenterInstanceBuffer 重写 | T2, T4 | `Presenters/PresenterInstanceBuffer.cs` | 单元测试：Allocate 含 parentHandle → 树形链表正确；Release 递归销毁子树；ReleaseScope 按 tag 销毁；blackboard 集成 | L |
| T6 | PresenterDefinition + ConfigLoader | T1, T3 | `Presenters/PresenterDefinition.cs`, `Config/PresenterDefinitionConfigLoader.cs` | 加载铁匠铺 JSON → 定义正确解析；children 展开为 Rule；extends 继承链展开；无效字段报错不崩溃 | L |

#### Wave 2 验收记录

**T4: 通过（2026-04-18）*

- 7 个新增字段与架构文档 §4.3 逐字段一致
- TransformSource 枚举 5 值正确（InheritParent=0 ~ WorldFixed=4）
- 守卫测试 `PresenterInstanceContract_ExposesT4TreeAndTransformFields` + `TransformSourceContract_MatchesArchitectureValues` 通过

**T5: 通过（2026-04-18）*

- Allocate(parentHandle) → 树形链表正确（FirstChildHandle/NextSiblingHandle 头插法 + blackboard SetParent）
- Release 递归销毁子树 + UnlinkFromParent 正确
- ReleaseScope 按 scopeId 销毁 + 递归子树
- Blackboard 集成：SetParam 三 lane 分发、ResolveFloat/Int/Vector 父→子继承链、SetParamDefault 应用定义默认值
- 9 个单元测试覆盖全部验收项

**T6: 通过（2026-04-18）*

- children 展开为 PresenterCreated Rule ✓（`ExpandChildrenRules`）
- extends 继承链展开 ✓（`ExpandDefinition` + 循环检测）
- 8 种 BehaviorKind 全部有 Parse 方法 ✓
- 无效字段报错不崩溃 ✓（`RejectLegacyFields` + try/catch）
- `dotnet build` 0 error ✓
- T6-F1 修复 ✓：全部 fallback 别名已删除，JSON 字段只保留规范名（commit ebd379fa）
- T6-F2 修复 ✓：ScopeTagRegistry 滥用已替换为 throw InvalidOperationException
- T6-F3 修复 ✓：新增 7 个 ConfigLoader 专项单元测试（children/extends/behaviors/legacy/cycle/alias rejection）
- GameEngine.cs 正确接入新 resolver（materialAssets/animatorControllers/animationProfiles/behaviorAssetId）
- 73 个 Presenter pipeline 测试 + 25 个架构守卫测试全部通过

修复记录（已关闭）：

| ID | 严重度 | 描述 | 修复 |
|----|--------|------|------|
| T6-F1 | HIGH | ConfigLoader 8+ 处 fallback 别名 | 全部删除，ParseLegacyIntValue/NormalizeParamDefaultLane 方法已移除 |
| T6-F2 | HIGH | PresenterScopeTagRegistry 滥用 | 替换为 throw InvalidOperationException |
| T6-F3 | HIGH | 缺少 ConfigLoader 单元测试 | 新增 PresenterDefinitionConfigLoaderTests（7 个测试） |

#### ConfigLoader 别名完整清单（T6-F1）

以下每处 `??` 别名必须只保留一个规范名（右列），删除左列别名。如果现有 JSON 文件使用旧名，一次性迁移 JSON。

| 行号 | 别名（删除） | 规范名（保留） | 上下文 |
|------|-------------|---------------|--------|
| 575 | `sourceKey` | `textToken` | ResolveTextTokenId |
| 592 | `sourceId`（仅 attribute 上下文） | `attributeId` | ResolveAttributeId（注意：ValueRef 绑定上下文中 `sourceId` 是 graph/entityColor 的规范名，不受影响） |
| 746 | `ParseLegacyIntValue(obj["value"])` | `intValue` | ParseParamDefaults Int lane |
| 749 | `value`（作为 array） | `vectorValue` | ParseParamDefaults Vector lane |
| 752 | `value`（作为 float） | `floatValue` | ParseParamDefaults Float lane |
| 815 | `tag` | `tagId` | ParseTagBinding |
| 895 | `splinePathId` | `splineAssetId` | ParseSpline |
| 292-310, 1038-1054 | 隐式 lane 推断 | 显式 `lane` 字段 | NormalizeParamDefaultLane / ParseParamLane |

注意：`ParseLegacyIntValue` 方法名本身就是命名异味，修复后应删除该方法。

#### JSON 迁移范围

现有 mod JSON 文件中 `sourceId` 用于 ValueRef 绑定（graph/entityColor），这是该上下文的规范名，不需要迁移。仅 `ResolveAttributeId` 中的 `sourceId` 别名需要删除（attribute 上下文应使用 `attributeId` 或 `attributeName`）。其余别名（`tag`、`value`、`splinePathId` 等）目前无 mod JSON 使用，删除别名即可，无需迁移。

#### 跨里程碑别名治理规则

后续每个 Wave 审计时同步检查：
1. 新增的 ConfigLoader 解析代码不得引入 `??` 别名
2. JSON schema 每个字段只有一个规范名，与架构文档一致
3. 不得将 PresenterScopeTagRegistry 用于非 scope tag 用途

#### Wave 2 验收总结

**全部通过（2026-04-18）*

- T4：PresenterInstance 7 新增字段与架构文档 §4.3 逐字段一致，TransformSource 5 值正确
- T5：树形链表（头插法）+ 递归销毁 + ReleaseScope + blackboard 三 lane 集成，9 个单元测试全覆盖
- T6：ConfigLoader 新 JSON schema 解析正确，children/extends/behaviors/rules 全链路验证，别名全部清除，7 个专项测试

**M2 里程碑达成 ✓ — Wave 3 (T7/T8) 已解锁*

### Wave 3 — 系统重写（依赖 Wave 2）

当时的实现工作树记录：`C:\001_AI\LudotsProd_pr129_impl`，分支：`codex/pr129-pr135-integration`

| ID | 任务 | 依赖 | 产出文件 | 验收标准 | 预估 |
|----|------|------|---------|---------|------|
| T7 | PresenterRuntimeSystem 重写 | T5, T6 | `Systems/PresenterRuntimeSystem.cs` | 单元测试：CreatePresenter(parentHandle) 建立正确层级；DestroyPresenter 递归销毁；DestroyPresenterScope 按 tag；ActivateBehavior/DeactivateBehavior 翻转 mask；SetParam 写入 blackboard | L |
| T8 | PresenterGroundingUtility + Transform 计算 | T5 | `Presenters/PresenterGroundingUtility.cs` | 单元测试：5 种 TransformSource 的位置/旋转/缩放计算正确；SnapToGround/AlignToSurface/None 三种 grounding 模式；BoneAttached 跳过 grounding | M |

#### Wave 3 验收记录

**T7: 通过（2026-04-19，commit 43ce36ef 修复）*

- CreatePresenter(parentHandle) 层级正确 ✓
- DestroyPresenter 递归销毁 ✓
- DestroyPresenterScope 在 ScopeTag<=0 时 throw ✓（T7-F1 已修复）
- ActivateBehavior / DeactivateBehavior 翻转 mask ✓
- SetParam 写入 blackboard 三 lane ✓
- 使用独立 `PresenterCommandKind`，旧 `PresentationCommand` / `PresentationCommandKind` 已删除 ✓
- DestroyPresenterScope 专项测试已补充 ✓（T7-F2 已修复）

**T8: 通过（2026-04-19，commit 43ce36ef 修复）*

- 5 种 TransformSource 全部有实现和测试 ✓
- SplineDriven 测试已补充 ✓（T8-F1 已修复）
- EntityTransform hasOwnerTransform=true 直接测试已补充 ✓（T8-F2 已修复）
- GroundingMode.None 显式测试已补充（ThrowingHeightmap 守卫）✓（T8-F3 已修复）
- ResolveBatch 测试已补充 ✓（T8-F4 已修复）
- BoneAttached 跳过 grounding ✓

**Wave 3 全部通过 ✓*

### Wave 4 — 行为与发射（依赖 Wave 3）

当时的实现工作树记录：`C:\001_AI\LudotsProd_pr129_impl`，分支：`codex/pr129-pr135-integration`

| ID | 任务 | 依赖 | 产出文件 | 验收标准 | 预估 |
|----|------|------|---------|---------|------|
| T9 | PresenterBehaviorSystem | T7, T8 | `Systems/PresenterBehaviorSystem.cs` | 单元测试：AttributeBinding 读属性→写 param + 阈值映射；TagBinding tag on/off→param；Material param→materialId 查表；Sound 请求发射/停止 | L |
| T10 | Animator 参数统一 | T7 | 修改 `Systems/AnimatorRuntimeSystem.cs` | 单元测试：Animator 从 blackboard 读 speed/trigger；AnimatorFeedback 写回 blackboard；删除 AnimatorParameterBuffer 后 build 通过 | M |
| T11 | PresenterEmitSystem 重写 | T9 | `Systems/PresenterEmitSystem.cs` | ~200 行；只处理 AssetBinding emit；单元测试：Mesh/SkinnedMesh/Decal/VFX/Spline/WorldHud/WorldText 各 AssetKind 发射正确的 proxy | L |
| T12 | GlobalPresentationEventProjectionSystem | T3 | `Systems/GlobalPresentationEventProjectionSystem.cs` | 单元测试：日夜/区域/天气事件正确发射到 PresentationEventStream | S |

#### Wave 4 验收记录

**T9: 通过（2026-04-19，commit 43ce36ef 修复）*

- AttributeBinding 读属性→写 param + 阈值映射 ✓
- TagBinding tag on/off→param ✓，InvertLogic 测试已补充 ✓（T9-F1 已修复）
- Material param→materialId 查表 ✓
- Sound PlayOrUpdate + Stop 均由 PresenterBehaviorSystem 发射 ✓（T9-F2 已修复）
- Animator 逻辑已从 PBS 移除，无双重路径 ✓（T9-F3 已修复）

**T10: 通过（2026-04-19）*

- Animator 从 blackboard 读 speed/trigger ✓
- AnimatorFeedback 写回 blackboard ✓
- `AnimatorParameterBuffer` 已删除，全仓库无引用 ✓

**T11: 通过（2026-04-19，commit 43ce36ef 修复）*

- `PresenterEmitSystem.cs` 176 行，只调用 `EmitAssetBindings` ✓
- `LegacyPresenterEmitSystem.cs` 已删除 ✓（T11-F1 已修复）
- 构造函数死参数已清理 ✓（T11-F2 已修复）
- 7 种 AssetKind 逐项验证 proxy 内容 ✓（T11-F3 已修复）
- 旧 GasTests 回归已修复 ✓（T11-F5 已修复）

**T12: 通过（2026-04-19）*

- GlobalDayNight=30 / GlobalRegionChanged=31 / GlobalWeather=32 ✓
- 三种全局事件正确桥接到 PresentationEventStream ✓

**Wave 4 全部通过 ✓ — M3 里程碑达成*

### Wave 5 — Raylib UAT（依赖 Wave 4）

当时的实现工作树记录：`C:\001_AI\LudotsProd_pr129_impl`，分支：`codex/pr129-pr135-integration`

| ID | 任务 | 依赖 | 产出文件 | 验收标准 | 预估 |
|----|------|------|---------|---------|------|
| T13 | Raylib UAT Layer 1 — 逐 AssetKind | T11 | `Tests/PresentationTests/PresenterAssetKindTests.cs` | 7 个 AssetKind 各一个渲染验证测试通过（详见 presenter-raylib-uat.md §1-5） | M |
| T14 | Raylib UAT Layer 2 — 逐 BehaviorKind | T9, T10 | `Tests/PresentationTests/PresenterBehaviorKindTests.cs` | 9 个 BehaviorKind 各一个驱动验证测试通过（详见 presenter-raylib-uat.md §6-11） | M |
| T15 | Raylib UAT Layer 3 — 铁匠铺完整 | T13, T14 | `Tests/PresentationTests/BlacksmithPresenterUatTests.cs`, `mods/fixtures/blacksmith/` | 9 个铁匠铺场景全部通过（详见 presenter-raylib-uat.md §13） | L |

#### Wave 5 验收记录

**T13: 通过（2026-04-19）*

- 7 种 AssetKind 全部有独立测试 ✓（Mesh/SkinnedMesh/Decal/VFX/Spline/WorldHud/WorldText）
- 额外覆盖 Sound 和 GroundOverlay（共 9 种）✓
- 每种 AssetKind 逐项断言 proxy 内容（assetId/materialId/renderPath/position/scale）✓
- 架构守卫 `AssetKindContract_ArchitectureExposesNineKinds` + `PreservesExplicitEnumValues` ✓

**T14: 通过（2026-04-19）*

- 7 种非 AssetBinding BehaviorKind 全部有独立测试 ✓
- AttributeBinding 含阈值映射 ✓，TagBinding 含 tag-off + InvertLogic ✓
- Animator 含 blackboard→packed state ✓，Attachment 含 bone offset + inheritScale ✓
- Sound 含 Play + Stop ✓，Material 含 swapTable ✓，Spline 含 patrol progress ✓
- AssetBinding 由 T13 覆盖，无重复 ✓

**T15: 通过（2026-04-19，含 2 个低优先级备注）**

- 9 个 §13 铁匠铺场景全部覆盖 ✓（出现/开工/停工/日夜/北方/南方/耐久度下降/归零/销毁）
- `mods/fixtures/blacksmith/` 目录存在，含完整 presenters.json + templates.json ✓
- JSON schema 使用 `"kind": "AssetBinding"` 无 `"visualKind"` ✓
- 额外架构守卫：无 PrefabRegistry 依赖 + 无 visualKind 字段 ✓

备注（不阻塞验收）：

| ID | 严重度 | 描述 |
|----|--------|------|
| T15-N1 | LOW | GlobalDayNight 测试中 rule 硬编码 `paramValue: 1.0`，未验证 phase 值传播 |
| T15-N2 | LOW | `SetDurability` helper 复制了阈值 band 逻辑（9100/9101/9102），与生产代码耦合 |

**Wave 5 全部通过 ✓ — M4 里程碑达成*

§12 树生命周期测试（`PresenterTreeLifecycleTests.cs`）8 个场景全部覆盖 ✓

### Wave 6 — 整合与清理（依赖 Wave 5）

当时的实现工作树记录：`C:\001_AI\LudotsProd_pr129_impl`，分支：`codex/pr129-pr135-integration`

| ID | 任务 | 依赖 | 产出文件 | 验收标准 | 预估 |
|----|------|------|---------|---------|------|
| T16 | VisualRenderPayload 提取 | T11 | 修改 `Rendering/PresentationVisualProxy.cs` 等 | 提取 13 字段公共 struct；ProxyEmitter 改为 payload 整体赋值；`dotnet build` + 现有测试通过 | M |
| T17 | 遗留代码清理 | T15 | 删除 ~20 个文件 | 删除 EntityVisualEmitSystem / BuiltinPresenterDefinitions / EntityScopeFilter / PresenterVisualKind / Perform 命名空间 / Prefab 系统 / VisualTemplate 系统；`dotnet build` + 全量测试通过 | L |
| T18 | UE5 适配 | T17 | 修改 UE5 adapter | 6 种 AssetKind 到 UE5 渲染的映射；跑铁匠铺 UAT JSON 通过 | XL |

#### Wave 6 验收记录

**T16: 通过（2026-04-19）*

- `VisualRenderPayload` 为 `public struct`，含 13 个字段 ✓
- `PresentationVisualProxyEmitter` 使用 `Payload = proxy.Payload` 整体赋值 ✓
- `PrimitiveDrawItem` / `SkinnedVisualBatchItem` 通过 `VisualRenderPayload Payload` 字段暴露，不重复定义原始字段 ✓
- 架构守卫 `VisualRenderPayloadContract_ExposesOnlySharedThirteenFields` + `Containers_StoreSharedStateOnlyThroughPayload` ✓

**T17: 待收尾（2026-04-19）*

安全删除子集（确认不存在）：
- `BuiltinPresenterDefinitions.cs` ✓
- `PresenterVisualKind.cs` ✓
- `EntityScopeFilter.cs` ✓
- `LegacyPresenterEmitSystem.cs` ✓
- `Perform/` 命名空间（PerformCommand, PerformCommandBuffer 等）✓
- `PresentationCommand` / `PresentationCommandKind` ✓

T17-F1 ~ F4 进展（2026-04-19 审计，基于未提交改动 + commit 2948d443）：

| ID | 状态 | 描述 |
|----|------|------|
| T17-F1 | ✓ 已完成 | `EntityVisualEmitSystem.cs` 已删除，`GameEngine.cs` 不再注册 |
| T17-F2 | ✓ 已完成 | VisualTemplate 系统 5 文件全部删除（Definition/Registry/Ref/ConfigLoader/PresentationAuthoringContext），6 个 mod 的 `visual_templates.json` 全部删除，8 个 mod 全部迁移到 `presenters.json` |
| T17-F3 | 待确认 | 架构守卫测试需确认是否已补充（反射断言旧类型不存在） |
| T17-F4 | 待确认 | `LoadFromJson_AllCoreBuiltinIds_Present` 测试适配状态待确认 |

全量 grep 验证（2026-04-19）：C# 源码和 JSON 配置中 `VisualTemplate`/`EntityVisualEmit`/`PresentationAuthoringContext`/`ModelPerformBinding`/`visual_templates.json`/`visualTemplateId` 引用全部为零。

阶段性修复提交（2026-04-19，commit 2948d443，由人工审核后提交）：
- `RtsShowcaseMod/assets/Presentation/presenters.json`：从裸对象修正为规范数组格式
- `UxPrototypePlayableAcceptanceTests.cs`：修复 `InvokeState` 重载解析；`AssertEntityUsesBillboardVisual` 从读 `VisualRuntimeState` 组件改为从 `PresentationPrimitiveDrawBuffer` 匹配 presenter 主链路渲染输出
- 验证：62/62 测试全绿（PerformContracts 16/16 + Presentation 24/24 + UxPrototype 4/4 + ChampionSkill+SplineSurface+UxPrototype 18/18）

新增遗留项（阻塞 M5 最终达成）：

| ID | 严重度 | 描述 |
|----|--------|------|
| T17-F5 | HIGH | `VisualRuntimeEmitSystem.cs` + `VisualRuntimeState` 组件构成绕过 presenter 的第二条渲染入口。当前唯一生产者是 `ChunkSurfaceBakeSystem`（程序化网格烘焙）。应将程序化网格改为通过 `PresenterCommand.CreatePresenter` + `AssetBinding(Mesh)` 创建 presenter instance，然后删除 `VisualRuntimeEmitSystem` / `VisualRuntimeState`，消除 `CameraCullingSystem` 中的双路判断。 |
| T17-F6 | MEDIUM | Codex 未提交改动（60 文件）需跑全量构建 + 测试后提交 |

注意：Codex 在实现工作树中单方面修改了 T17 验收标准和 M5 里程碑定义，将旧文件重新归类为"现役遗留通道"。这不符合原始架构设计意图——Presenter 是唯一的表现编排者，任何绕过 Presenter 的渲染入口都是双重真相，必须消除。看板验收标准以本文件（主工作区）为准，实现工作树中的修改无效。

**Wave 6 T16 通过，T17 待收尾（F1/F2 已完成，F5/F6 待处理）— M5 未达成*

### Wave 7 — 性能迭代（依赖 Wave 6 M5）

设计文档：[Presenter 编译式执行分层](presenter-compiled-lanes.md)

§8.4 Entity-Backed Runtime 迁移已完成（2026-04-20）：`PresenterInstanceBuffer` / `PresenterInstance` / `PresenterParamBlackboard` 已删除，替换为 `PresenterEntityRuntime` + Arch entity 组件。所有系统和测试已迁移。

目标：同屏 30K 实体（10K 动态 + 20K 静态），带特效、属性、血条，150K presenter instances @ 60FPS。

文档前置 gate：
- 主架构、编译式执行、Raylib UAT 三份文档必须先统一 working showcase 语义：常驻 presenter tree + behavior/tag/param 驱动，不再把 smoke/worker 写成动态创建/销毁示例
- UAT 文档中的配置错误口径必须统一为 fail-fast / reject-load，不再接受 fallback、自动排序或静默兜底
- 主架构中的类型片段必须与当前正式合同一致（AssetKind 9 种、AttachmentConfig 5 字段等）

| ID | 任务 | 依赖 | 产出文件 | 验收标准 | 预估 |
|----|------|------|---------|---------|------|
| T19 | Persistent Draw Buffer + Static Freeze | T17 | 新增 `StableDrawCache` + 修改 `PresenterEmitSystem` + `PresenterInstanceBuffer` | Draw buffer 持久化不每帧 clear；静态实体 presenter 创建后 0 cost/帧；动态实体每帧只更新 position lane；dirty 实例全量重算；30K 同屏稳态 < 2ms | L | 基建就绪（`StableDrawCache`、`PresenterEmitCache` 组件已实现），验收标准待验证 |
| T20 | Dirty-Driven Behavior | T19 | 修改 `PresenterBehaviorSystem` | 直接读 owner entity 的 `GameplayTagEffectiveChangedBits` + `DirtyFlags.AttributeDirty`；只对 dirty entity 的 presenter 求值 behavior；稳态下 behavior eval ~0；不自建 DirtyMask，复用 GAS 已有信号 | L | 基建就绪（`DirtyFlags.IsAnyAttributeDirty`、`GameplayTagEffectiveChangedBits.IsAnyBitSet` 已实现），验收标准待验证 |
| T21 | LOD Children Pruning | T20 | 修改 `PresenterRuntimeSystem` | 读 `CullState.LOD` 控制 children 激活数量；Close=5/Medium=3/Far=2；通过 ActivateBehavior/DeactivateBehavior 控制；150K → ~74K active instances | M |
| T22 | Culling Gate | T19 | 修改 `PresenterRuntimeSystem` + `PresenterInstanceBuffer` | `ProcessActive` 跳过 `!OwnerCullVisible`；子 presenter 继承父 cull | S |
| T23 | SoA Instance Buffer + Compiled Binding Table | T20 | 重构 `PresenterInstanceBuffer` 内部 + 修改 `PresenterDefinitionConfigLoader` | AoS→SoA 内部存储；注册时编译 `CompiledBinding[]`；运行时不走 `ResolveParam` | L |
| T24 | LOD-Aware Behavior Ticking | T21 | 修改 `PresenterBehaviorSystem` | 按 LOD 分档执行；Medium=Animator 15fps；Far=只 AssetBinding | M |

## Codex 开发 → 验收流程

每个任务由 Codex 开发，完成后我来验收：

1. Codex 领取任务，按依赖关系选择可开始的任务
2. Codex 完成开发，提交 PR
3. 我验收：
   - `dotnet build` 通过
   - 任务指定的单元测试全部通过
   - 代码与架构文档一致（类型定义、字段、枚举值）
   - 无引入新的多重真相或硬编码
   - 无遗漏的 TODO 或 placeholder
4. 验收通过 → 标记完成，解锁下游任务
5. 验收不通过 → 反馈具体问题，Codex 修复后重新提交

## 关键里程碑

| 里程碑 | 完成条件 | 包含任务 | 状态 |
|--------|---------|---------|------|
| M1 基础类型就绪 | T1-T3 全部通过 | Wave 1 | **达成** |
| M2 实例树可运行 | T4-T7 全部通过 | Wave 1-3 | **达成** |
| M3 行为系统可运行 | T8-T12 全部通过 | Wave 1-4 | **达成** |
| M4 Raylib UAT 全绿 | T13-T15 全部通过 | Wave 1-5 | **达成** |
| M5 清理完成 | T16-T17 全部通过 | Wave 1-6 | T17 待收尾（F1/F2 旧链路已删除，F5 VisualRuntimeEmitSystem 双路残留待消除，F6 未提交改动待验证提交） |
| M6 UE5 适配完成 | T18 通过 | 全部 |
| M7 性能达标 | T19-T24 全部通过 | Wave 1-7 | 同屏 30K 实体（10K 动态 + 20K 静态）150K presenter @ 60FPS |
