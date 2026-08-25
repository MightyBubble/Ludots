# 行为 BehaviorKind 逐条

BehaviorKind 回答"这个槽位上的行为怎么驱动可视输出"。作者写在 behavior 的 `kind` + 同名小写载荷对象；行为可运行时激活/停用。总目录见 [README.md](README.md)。铁匠铺 showcase（preset `presenter_blacksmith_showcase_raylib`）覆盖其中大部分行为的全链路演示。

### AssetBinding — 资产绑定

- **是什么**：把一种资产（十种 AssetKind）绑到 presenter 槽位，声明车道、材质、局部变换与参数键。
- **怎么写**：`kind: "AssetBinding"` + `assetBinding` 载荷（`assetKind`/`assetId`/`renderPath`/`mobility`/`materialId`/`localScale`…，含 swapTable 换装表）。
- **跑/证据**：preset `engine_raylib_material_binding`；`artifacts/acceptance/engine_raylib_material_binding/screen.png`。

### AttributeBinding — 属性绑定

- **是什么**：读 owner entity 的 GAS 属性，按阈值表映射写黑板参数（如耐久度→完好/破损/废墟 mesh 切换）。
- **怎么写**：`attributeBinding`（`attributeId`/`mode` 五种取值/`thresholds`：threshold→outputParamKey/outputValue）。
- **跑/证据**：preset `presenter_blacksmith_showcase_raylib`（耐久度链路）；UAT 属性绑定章节。

### TagBinding — 标签绑定

- **是什么**： GameplayTag 得失写黑板参数（如 "working" tag → 烟囱可见/工人进工作态），支持 InvertLogic。
- **怎么写**：`tagBinding`（`tagId`/`targetParamKey`/`invertLogic`）。
- **跑/证据**：preset `presenter_blacksmith_showcase_raylib`（开工/停工链路）；UAT 标签绑定章节。

### Animator — 动画状态机

- **是什么**：状态机驱动骨骼动画（控制器/档案/通道注册表），速度与状态参数统一走 presenter 黑板；反馈事件写回黑板供规则消费。
- **怎么写**：`animator`（`animatorControllerId`/`animationProfileId`/`speedParamKey`/`stateParamKey`）。
- **跑**：preset `engine_raylib_crowd_anim`（4096 实例）、`raylib_client_parity_raylib`（locomotion 状态机）。
- **证据**：`artifacts/acceptance/animator-runtime-mvp/battle-report.md`；`artifacts/evidence/engine_raylib_crowd_anim/play.mp4`。

### Attachment — 骨骼挂点

- **是什么**：子 presenter 挂到父/骨骼位置（武器握点、头顶标记），可继承或不继承父缩放。
- **怎么写**：`attachment`（`target: "Parent"`/`boneId`/`offset`/`rotationOffset`/`inheritScale`）。
- **跑/证据**：`artifacts/acceptance/entity-attachment/battle-report.md`；变换/贴合/挂点合同见 [Transform、Grounding 与 Attachment](../../architecture/presenter-transform-and-attachment.md)。

### Sound — 声音行为（契约就绪，执行缺口）

- **是什么**：按行为激活状态发出 PlayOrUpdate/Stop 声音请求（loop/volume/3D 位置/参数键）。
- **现状**：与 AssetKind.Sound 同源——`SoundRequestBuffer` 契约在，raylib 侧无音频消费。见 [asset-kinds.md](asset-kinds.md) Sound 条目。

### Material — 材质切换

- **是什么**：黑板参数驱动材质换装表（区域参数 0=北方黑砖、1=南方红砖这类查表切换）。
- **怎么写**：`material`（`baseMaterialId`/`materialSwapParamKey`/`swapTable`）。
- **跑/证据**：preset `engine_raylib_material_binding`（材质实例链+自发光）、`presenter_blacksmith_showcase_raylib`（区域砖色）。

### Spline — 样条行为

- **是什么**：Render（画条带）与 Patrol（驱动位置、到点事件、pingPong）两种用途，见 [asset-kinds.md](asset-kinds.md) Spline 条目。
- **怎么写**：`spline`（`splineAssetId`/`usage`/`speedParamKey`/`progressParamKey`/`waypointEventId`）。

### Grounding — 地面贴合

- **是什么**：SnapToGround / AlignToSurface 两种贴合模式（Once 或 EveryFrame），让放置物贴地不悬空。
- **怎么写**：`grounding`（`mode`/`offset`/`updatePolicy`）。
- **跑/证据**：preset `presenter_blacksmith_showcase_raylib`；合同见 [Transform、Grounding 与 Attachment](../../architecture/presenter-transform-and-attachment.md)。

### MinimapMarker — 小地图标记

- **是什么**：presenter 驱动的小地图标记（形状/颜色/尺寸/朝向全参数键化，可按黑板动态切换）。
- **怎么写**：`minimapMarker`（`shape`/`sizePx`/`colorParamKey`/`orientationMode`…）。
- **跑**：preset `browser_minimap_composited_overlay_cef_raylib`。
- **证据**：`artifacts/acceptance/minimap-showcase/battle-report.md`（含截图目录与 trace）。

### SurfaceSource — 地形表面源

- **是什么**：声明 chunk 地形的几何源/烘焙策略/材质集/LOD 档案，驱动表面车道（见 [asset-kinds.md](asset-kinds.md) Surface 条目）。
- **怎么写**：`surfaceSource`（`geometrySource`/`chunkBake`/`materialSet`/`lodProfileId`/`boundsPolicy`）。
- **跑/证据**：preset `engine_raylib_terrain_surface`；`artifacts/acceptance/chunk_streaming_showcase/battle-report.md`。

### WorldText — 浮动文字行为

- **是什么**：见 [asset-kinds.md](asset-kinds.md) WorldText 条目（textToken 本地化 + 数值参数绑定 + yDrift）。
- **怎么写**：`worldText`（`textToken`/`valueParamKey`/`secondaryValueParamKey`/`fontSize`），上浮速率写在 `motion.yDriftPerSecond`。

### InstancedBatch — 外部实例批量

- **是什么**：消费外部 factorized transform 源（`ludots.instanced_transform_factorized.v1`）做 5 万级静态实例合批，实例数由源声明、adapter 不拥有。
- **怎么写**：`instancedBatch`（`batchAssetId` 指向批量资产）；源合同见 [Instanced Batch 外部 Source Contract](../../architecture/instanced-batch-source-contract.md)。
- **跑**：preset `engine_raylib_instancing`（合批机制）、`capability_standard_static_presenter_30k_raylib`（3 万实例生产路径）。
- **证据**：`artifacts/acceptance/engine_raylib_instancing/screen.png` + `stats.json`。
