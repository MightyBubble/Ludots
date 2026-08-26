# 行为 BehaviorKind 逐条

BehaviorKind 回答"这个槽位上的行为怎么驱动可视输出"。作者写在 behavior 的 `kind` + 同名小写载荷对象；行为可运行时激活/停用。总目录见 [README.md](README.md)。逐条学习走 L1 单能力入口；铁匠铺（preset `presenter_blacksmith_showcase_raylib`）是 L3 故事集成巡演。翻新合同见 [Presenter 能力演示集体翻新](../../../docs/architecture/presenter-capability-showcase-refresh.md)。

每条在"是什么/怎么写/跑/证据"之后附**标准生产配置**：从真实 mod 文件提取的最小生产形态（含必要上下文字段），块后注明来源路径。配置里的资产 id（如 blacksmith.building.north.intact）是 mod 内语义 id，由该 mod 的 mesh_assets.json / host_assets.json / spline 资产目录解析；sink 键的写入→重发链路见 [param-sink.md](param-sink.md)。

### AssetBinding — 资产绑定

- **是什么**：把一种资产（十种 AssetKind）绑到 presenter 槽位，声明车道、材质、局部变换与参数键。
- **怎么写**：`kind: "AssetBinding"` + `assetBinding` 载荷（`assetKind`/`assetId`/`renderPath`/`mobility`/`materialId`/`localScale`…，含 swapTable 换装表）。
- **跑/证据**：preset `engine_raylib_material_binding`；`artifacts/acceptance/engine_raylib_material_binding/screen.png`。

标准生产配置（建筑体 + 耐久度资产换装表；同名定义还声明了 paramDefaults：blacksmith.workshop.assetState，Int，默认 0）：

```jsonc
{
  "slot": "body",
  "kind": "AssetBinding",
  "activeByDefault": true,
  "assetBinding": {
    "assetKind": "Mesh",
    "assetId": "blacksmith.building.north.intact",
    "assetSwapParamKey": "blacksmith.workshop.assetState",
    "assetSwapTable": [
      { "paramValue": 0, "assetId": "blacksmith.building.north.intact" },
      { "paramValue": 1, "assetId": "blacksmith.building.south.intact" },
      { "paramValue": 2, "assetId": "blacksmith.building.damaged" },
      { "paramValue": 3, "assetId": "blacksmith.building.ruined" }
    ],
    "materialId": "default_surface",
    "renderPath": "InstancedStaticMesh",
    "mobility": "Static"
  }
}
```

来源：`mods/showcases/presenter_blacksmith/PresenterBlacksmithShowcaseMod/assets/Presentation/presenters.json:571-648`（blacksmith_workshop_base）。

### AttributeBinding — 属性绑定

- **是什么**：读 owner entity 的 GAS 属性，按阈值表映射写黑板参数（如耐久度→完好/破损/废墟 mesh 切换）。
- **怎么写**：`attributeBinding`（`attributeId`/`mode` 五种取值/`thresholds`：threshold→outputParamKey/outputValue）。
- **跑/证据**：preset `presenter_blacksmith_showcase_raylib`（耐久度链路）；UAT 属性绑定章节。

标准生产配置（Durability 比值落到两条阈值，驱动上面的 assetState 换装；paramDefaults 给 blacksmith.durability.ratio=1、blacksmith.workshop.assetState=0 起始值）：

```jsonc
{
  "slot": "attribute",
  "kind": "AttributeBinding",
  "activeByDefault": true,
  "attributeBinding": {
    "attributeId": "Durability",
    "targetParamKey": "blacksmith.durability.ratio",
    "mode": "AttributeRatio",
    "thresholds": [
      { "threshold": 0, "outputParamKey": "blacksmith.workshop.assetState", "outputValue": 3 },
      { "threshold": 0.5, "outputParamKey": "blacksmith.workshop.assetState", "outputValue": 2 }
    ]
  }
}
```

来源：`mods/showcases/presenter_blacksmith/PresenterBlacksmithShowcaseMod/assets/Presentation/presenters.json:571-648`（blacksmith_workshop_base）。

### TagBinding — 标签绑定

- **是什么**：GameplayTag 得失写黑板参数（如 "working" tag → 烟囱可见/工人进工作态），支持 InvertLogic。
- **怎么写**：`tagBinding`（`tagId`/`targetParamKey`/`invertLogic`）。
- **跑/证据**：preset `capability_standard_live_skill_workbench_raylib`（ chilled tag → 冰冻条可见链路）；UAT 标签绑定章节。铁匠铺的开工/停工走的是 TagEffectiveChanged 事件规则（ActivateBehavior/DeactivateBehavior，见 [commands.md](commands.md)），不是 TagBinding——两者是"tag 写参数"与"tag 切行为"两条不同通道。

标准生产配置（chilled tag 写 Int 参数，再由同定义 WorldHud 的 visibilityParamKey 消费，形成 tag→参数→可见性全链）：

```jsonc
{
  "slot": "tag",
  "kind": "TagBinding",
  "activeByDefault": true,
  "tagBinding": {
    "tagId": "State.LSW.Chilled",
    "targetParamKey": "lsw.chill.visible",
    "invertLogic": false
  }
}
```

来源：`mods/showcases/capability_standard/CapabilityStandardLiveSkillWorkbenchShowcaseMod/assets/Presentation/presenters.json:236-303`（lsw.unit_chill_bar，paramDefaults 声明 lsw.chill.visible，Int，默认 0）。

### Animator — 动画状态机

- **是什么**：状态机驱动骨骼动画（控制器/档案/通道注册表），速度与状态参数统一走 presenter 黑板；反馈事件写回黑板供规则消费。
- **怎么写**：`animator`（`animatorControllerId`/`animationProfileId`/`speedParamKey`/`stateParamKey`）。
- **跑**：preset `engine_raylib_crowd_anim`（4096 实例）、`raylib_client_parity_raylib`（locomotion 状态机）。
- **证据**：`artifacts/acceptance/animator-runtime-mvp/battle-report.md`；`artifacts/evidence/engine_raylib_crowd_anim/play.mp4`。

标准生产配置（mannequin locomotion；同定义 paramDefaults 声明 speed 键 Float=1，body 槽为 GpuSkinnedInstance 车道的 SkinnedMesh）：

```jsonc
{
  "slot": "animator",
  "kind": "Animator",
  "activeByDefault": true,
  "animator": {
    "animatorControllerId": "raylib_client_parity.locomotion",
    "animationProfileId": "raylib_client_parity.profile",
    "speedParamKey": "raylib_client_parity.locomotion.speed",
    "stateParamKey": "none"
  }
}
```

来源：`mods/showcases/raylib_client_parity/RaylibClientParityShowcaseMod/assets/Presentation/presenters.json:90-127`（raylib_client_parity_crowd_actor）。

### Attachment — 骨骼挂点

- **是什么**：子 presenter 挂到父/骨骼位置（武器握点、头顶标记），可继承或不继承父缩放。
- **怎么写**：`attachment`（`target: "Parent"`/`boneId`/`offset`/`rotationOffset`/`inheritScale`）。
- **跑/证据**：`artifacts/acceptance/entity-attachment/battle-report.md`；变换/贴合/挂点合同见 [Transform、Grounding 与 Attachment](../../architecture/presenter-transform-and-attachment.md)。

标准生产配置（工具挂到工人父级腰前，不继承父缩放；父定义 blacksmith_dynamic_worker_actor 在 children 里以 scopeTag "structure" 引用它）：

```jsonc
{
  "slot": "attachment",
  "kind": "Attachment",
  "activeByDefault": true,
  "attachment": {
    "target": "Parent",
    "offset": [0, 0.65, -0.35],
    "rotationOffset": [0, 0, 0, 1],
    "inheritScale": false
  }
}
```

来源：`mods/showcases/presenter_blacksmith/PresenterBlacksmithShowcaseMod/assets/Presentation/presenters.json:370-393`（blacksmith_dynamic_worker_tool_attachment；boneId 骨骼挂点形态见 `mods/fixtures/presenter_schema_reference/PresenterSchemaReferenceMod/assets/Presentation/presenters.json:106-124`）。

### Sound — 声音行为（已实现）

- **是什么**：按行为激活状态发出 PlayOrUpdate/Stop 声音请求（loop/volume/3D 位置/参数键）。
- **跑**：行为激活产出 `SoundRequestBuffer` 请求，raylib 适配器 `RaylibSoundConsumer` 播放（距离衰减配置见 [asset-kinds.md](asset-kinds.md) Sound 条目）；preset `capability_standard_sound_showcase_raylib` 可听演示。

契约形态（铁匠铺工人锤打声，由 working tag 的 Activate/DeactivateBehavior 规则驱动）：

```jsonc
{
  "slot": "sound",
  "kind": "Sound",
  "activeByDefault": false,
  "sound": {
    "soundAssetId": "blacksmith.sound.anvil_hammering",
    "loop": true,
    "volume": 0.75
  }
}
```

来源：`mods/showcases/presenter_blacksmith/PresenterBlacksmithShowcaseMod/assets/Presentation/presenters.json:1185-1194`（blacksmith_worker_actor sound 槽）；volumeParamKey 等全字段见 `mods/fixtures/presenter_schema_reference/PresenterSchemaReferenceMod/assets/Presentation/presenters.json:125-135`。

### Material — 材质切换

- **是什么**：黑板参数驱动材质换装表（区域参数 0=北方黑砖、1=南方红砖这类查表切换）。
- **怎么写**：`material`（`baseMaterialId`/`materialSwapParamKey`/`swapTable`）。
- **跑/证据**：作者路径 L1 preset `capability_standard_presenter_material_behavior_showcase_raylib`（`C`/`W`/`Space`）；夹具 `blacksmith_test_raylib`。材质实例/自发光渲染课见 L4 preset `engine_raylib_material_binding`。铁匠铺「区域砖色」走 AssetBinding 的 assetSwapTable（整资产换装），与本行为的材质表换装是两条通道。

标准生产配置（区域参数 0=黑砖 1=红砖；同定义 body 槽 AssetBinding 用 materialParamKey 引用同一个 region 参数做实例级材质直推）：

```jsonc
{
  "slot": "material",
  "kind": "Material",
  "activeByDefault": true,
  "material": {
    "baseMaterialId": "blacksmith.fixture.brick.north",
    "materialSwapParamKey": "blacksmith.fixture.region",
    "swapTable": [
      { "paramValue": 0, "materialId": "blacksmith.fixture.brick.north" },
      { "paramValue": 1, "materialId": "blacksmith.fixture.brick.south" }
    ]
  }
}
```

来源：`mods/fixtures/blacksmith/BlacksmithTestMod/assets/Presentation/presenters.json:137-213`（blacksmith_workshop_base）。

### Spline — 样条行为

- **是什么**：Render（画条带）与 Patrol（驱动位置、到点事件、pingPong）两种用途，见 [asset-kinds.md](asset-kinds.md) Spline 条目。
- **怎么写**：`spline`（`splineAssetId`/`usage`/`speedParamKey`/`progressParamKey`/`waypointEventId`）。
- **跑**：preset `engine_raylib_ribbon_overlay`（Render 条带）、`presenter_blacksmith_showcase_raylib`（Patrol 巡逻）。

标准生产配置（工人沿样条巡逻，速度与进度走黑板；同定义 paramDefaults 声明 speed=0.35、progress=0，working tag 的规则控制该槽激活）：

```jsonc
{
  "slot": "spline",
  "kind": "Spline",
  "activeByDefault": false,
  "spline": {
    "splineAssetId": "blacksmith.worker.patrol",
    "usage": "Patrol",
    "speedParamKey": "blacksmith.worker.locomotion.speed",
    "progressParamKey": "blacksmith.worker.route.progress",
    "loop": true
  }
}
```

来源：`mods/showcases/presenter_blacksmith/PresenterBlacksmithShowcaseMod/assets/Presentation/presenters.json:1195-1249`（blacksmith_worker_actor spline 槽）；widthParamKey/colorParamKey/pingPong/waypointEventId 全字段形态见 `mods/fixtures/presenter_schema_reference/PresenterSchemaReferenceMod/assets/Presentation/presenters.json:156-170`。

### Grounding — 地面贴合

- **是什么**：SnapToGround / AlignToSurface 两种贴合模式（Once 或 EveryFrame），让放置物贴地不悬空。
- **怎么写**：`grounding`（`mode`/`offset`/`updatePolicy`）。
- **跑/证据**：preset `presenter_blacksmith_showcase_raylib`；合同见 [Transform、Grounding 与 Attachment](../../architecture/presenter-transform-and-attachment.md)。

标准生产配置两种形态（建筑出生贴一次；地面环贴地并抬 0.02 防 z-fighting）：

```jsonc
{ "slot": "grounding", "kind": "Grounding", "activeByDefault": true,
  "grounding": { "mode": "SnapToGround", "offset": 0, "updatePolicy": "Once" } }
```

```jsonc
{ "slot": "grounding", "kind": "Grounding", "activeByDefault": true,
  "grounding": { "mode": "AlignToSurface", "offset": 0.02, "updatePolicy": "Once" } }
```

来源：`mods/showcases/presenter_blacksmith/PresenterBlacksmithShowcaseMod/assets/Presentation/presenters.json:626-635`（blacksmith_workshop_base）与 `:989-998`（blacksmith_forge_decal）。

### MinimapMarker — 小地图标记

- **是什么**：presenter 驱动的小地图标记（形状/颜色/尺寸/朝向全参数键化，可按黑板动态切换）。
- **怎么写**：`minimapMarker`（`shape`/`sizePx`/`colorParamKey`/`orientationMode`…）。
- **跑**：preset `browser_minimap_composited_overlay_cef_raylib`（铁匠铺大世界 + 小地图合成）。
- **证据**：`artifacts/acceptance/minimap-showcase/battle-report.md`（含截图目录与 trace）。

标准生产配置（静态橙色圆点；colorParamKey/sizeParamKey/visibilityParamKey 三个参数 sink 写 "none" 表示不吃黑板，写真实键即可按参数换色/换尺寸/显隐）：

```jsonc
{
  "slot": "minimap",
  "kind": "MinimapMarker",
  "activeByDefault": true,
  "minimapMarker": {
    "shape": "Circle",
    "color": [1, 0.24, 0.06, 1],
    "sizePx": 11,
    "colorParamKey": "none",
    "sizeParamKey": "none",
    "visibilityParamKey": "none",
    "orientationMode": "PresenterForward",
    "orientationParamKey": "none",
    "orientationOffsetRad": 0,
    "orientationLengthPx": 15
  }
}
```

来源：`mods/showcases/presenter_blacksmith/PresenterBlacksmithShowcaseMod/assets/Presentation/presenters.json:177-273`（blacksmith_minimap_marker_ball）。

### SurfaceSource — 地形表面源

- **是什么**：声明 chunk 地形的几何源/烘焙策略/材质集/LOD 档案，驱动表面车道（见 [asset-kinds.md](asset-kinds.md) Surface 条目）。
- **怎么写**：`surfaceSource`（`geometrySource`/`chunkBake`/`materialSet`/`lodProfileId`/`boundsPolicy`）。
- **跑/证据**：preset `engine_raylib_terrain_surface`；`artifacts/acceptance/chunk_streaming_showcase/battle-report.md`。

标准生产配置（分块路网表面：常量控制点源 + Bezier12 分段 + PerChunk 烘焙 + 视觉高度贴合）：

```jsonc
{
  "slot": "surface",
  "kind": "SurfaceSource",
  "activeByDefault": true,
  "surfaceSource": {
    "kind": "SplineRibbon",
    "profileId": "road_surface",
    "geometrySource": {
      "controlPointSource": { "kind": "Constant", "id": "road_network_chunk_segments" },
      "widthSource": { "kind": "Constant", "id": "road_network_chunk_width" },
      "segmentationPolicy": "Bezier12"
    },
    "chunkBake": {
      "enabled": true,
      "ownership": "PerChunk",
      "chunkInfluencePolicy": "OwnerChunkOnly",
      "rebakePolicy": "OnPayloadVersionChange",
      "usageHint": "Static"
    },
    "materialSet": { "primaryMaterialId": "default_surface", "allowInstanceOverride": false },
    "lodProfileId": "default_surface_lod",
    "grounding": { "mode": "VisualHeight" },
    "boundsPolicy": "Auto"
  }
}
```

来源：`mods/showcases/road_network/RoadNetworkShowcaseMod/assets/Presentation/presenters.json:1-45`（road_surface_chunk；showcase.registry.json 登记 id road_network，launcher 目标 road_network_showcase）。

### WorldText — 浮动文字行为

- **是什么**：见 [asset-kinds.md](asset-kinds.md) WorldText 条目（textToken 本地化 + 数值参数绑定 + yDrift）。
- **怎么写**：`worldText`（`textToken`/`valueParamKey`/`secondaryValueParamKey`/`fontSize`），上浮速率写在 `motion.yDriftPerSecond`。
- **跑**：preset `presenter_blacksmith_showcase_raylib`。

标准生产配置（耐久度 "当前/上限" 文本；同定义还有两个 AttributeBinding 槽把 Durability 的当前值与上限写进这两个参数键）：

```jsonc
{
  "slot": "body",
  "kind": "WorldText",
  "activeByDefault": true,
  "worldText": {
    "textToken": "hud.attribute.current_over_base",
    "mode": "AttributeCurrentOverBase",
    "fontSize": 16,
    "valueParamKey": "blacksmith.durability.current",
    "secondaryValueParamKey": "blacksmith.durability.base"
  },
  "style": { "color": [1, 0.96, 0.86, 1] }
}
```

来源：`mods/showcases/presenter_blacksmith/PresenterBlacksmithShowcaseMod/assets/Presentation/presenters.json:1370-1433`（blacksmith_durability_hud_text）。

### InstancedBatch — 外部实例批量

- **是什么**：消费外部 factorized transform 源（`ludots.instanced_transform_factorized.v1`）做 5 万级静态实例合批，实例数由源声明、adapter 不拥有。
- **怎么写**：`instancedBatch`（`batchAssetId` 指向批量资产）；源合同见 [Instanced Batch 外部 Source Contract](../../architecture/instanced-batch-source-contract.md)。
- **跑**：preset `engine_raylib_instancing`（合批机制）、`capability_standard_static_presenter_30k_raylib`（3 万实例生产路径）。
- **证据**：`artifacts/acceptance/engine_raylib_instancing/screen.png` + `stats.json`。

配置形态（schema 仅一个必填字段 batchAssetId，指向批量资产注册表里的 key；**尚无生产 mod 以此 behavior 装载**——引擎画廊 instancing 场景由代码侧直接驱动合批，此 behavior 的装载合同由 `src/Tests/PresentationTests/Rendering/InstancedBatchContractTests.cs` 锁定）：

```jsonc
{
  "slot": "body",
  "kind": "InstancedBatch",
  "activeByDefault": true,
  "instancedBatch": { "batchAssetId": "<在 InstancedBatchAssetRegistry 注册的批量资产 key>" }
}
```

来源：字段白名单 `src/Core/Presentation/Config/PresenterDefinitionConfigLoader.cs:753-757`；装载合同测试 `src/Tests/PresentationTests/Rendering/InstancedBatchContractTests.cs`；作者装载夹具 preset `instanced_batch_demo_raylib`（`InstancedBatchDemoMod`）。合批渲染课见引擎画廊 `engine_raylib_instancing`。

### TrailMesh — 刀光轨迹

- **是什么**：行为激活期间按间隔采样刀刃 base/tip 世界坐标，写入 `TrailMeshBuffer`；停用后存量样本按寿命淡出。与画廊 `slash_trail` 共用 `TrailSampleHistory` 采样语义；作者路径写本 BehaviorKind。
- **怎么写**：`trailMesh`（`baseOffset`/`tipOffset`/`maxSamples`/`sampleIntervalSeconds`/`sampleLifetimeSeconds`/`headColor`/`tailColor`）。
- **跑**：作者路径 L1 preset `capability_standard_presenter_trailmesh_showcase_raylib`（按 `T` 开合拖尾）。渲染器验收见 L4 preset `engine_raylib_slash_trail`。
- **证据**：能力翻新合同见 `docs/architecture/presenter-capability-showcase-refresh.md`；画廊页 [挥砍的刀光弧线](../engine-gallery-wiki/slash_trail.md)。

标准生产配置（行为默认关闭，由 ActivateBehavior / activationCondition 点亮；同定义通常还有 AssetBinding 刀身）：

```jsonc
{
  "slot": "trail",
  "kind": "TrailMesh",
  "activeByDefault": false,
  "trailMesh": {
    "baseOffset": [0, 0, 0.1],
    "tipOffset": [0, 0, 1.2],
    "maxSamples": 20,
    "sampleIntervalSeconds": 0.012,
    "sampleLifetimeSeconds": 0.35,
    "headColor": [0.7, 0.9, 1.0, 0.9],
    "tailColor": [0.2, 0.4, 1.0, 0.0]
  }
}
```

来源：装载合同 `src/Tests/PresentationTests/Presenter/PresenterTrailMeshConfigTests.cs`；运行时写入方 `PresenterBehaviorSystem` → `TrailMeshRuntime`。

### activationCondition — 创建时条件激活（行为字段，非独立 Kind）

- **是什么**：写在 BehaviorSlot 上的 `activationCondition`（`{ "inline": "…" }` 或 `{ "graphProgramId": N }`）。loader 编译成 PresenterCreated 上的「无条件 Deactivate + 条件 Activate」规则对；条件为唯一权威（存在时强制 `activeByDefault=false`）。
- **怎么写**：`{ "inline": "…" }` 或 `{ "graphProgramId": N }`（loader 合同）。
- **跑/证据**：L1 preset `capability_standard_presenter_activation_condition_showcase_raylib`（左站有 `VisualTransform` 发光球亮，右站无则灭；按 1/2 重生切换）；闭环单测 `PresenterActivationConditionTests`。

标准生产配置（发光球仅在 SourceHasVisualTransform 成立时点亮）：

```jsonc
{
  "slot": "body",
  "kind": "AssetBinding",
  "activeByDefault": false,
  "activationCondition": { "inline": "SourceHasVisualTransform" },
  "assetBinding": {
    "assetKind": "Mesh",
    "assetId": "sphere",
    "materialId": "default_surface",
    "renderPath": "StaticMesh",
    "mobility": "Movable",
    "localScale": [0.9, 0.9, 0.9]
  },
  "style": { "color": [1.0, 0.92, 0.35, 1.0] }
}
```

来源：`mods/showcases/capability_standard/CapabilityStandardPresenterActivationConditionShowcaseMod/assets/Presentation/presenters/activation_condition_showcase.json`。
