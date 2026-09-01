# Raylib 引擎工程分层与关卡容器格式

> 状态：规范定稿，未实现。规范本文档为格式与分层的单一事实源；实现按本文档"落地次序"分步交付。
> 谱系：#1321（Raylib 引擎工程化收敛）的后续工作。与 #1350（Nav 烘焙源架构 / LogicTerrain 退役）、#1402（navmesh 烘焙 showcase）的关系见"边界"一节。

## 1. 解决什么问题

Ludots 的定位是"配一个 Unity 工程"：raylib 侧是一个自持的引擎工程，像 Unity 工程一样管理场景、网格、材质、动画、相机与环境；Ludots 世界（Core / mod / ECS）不干涉引擎工程内部资产怎么放，需要渲染时才通过 binding 把世界实体接到引擎资产上。

现状是这条分层的下半段已经成型（渲染库零 Core、资产租约、每帧合同缓冲都在 main），缺上半段：引擎工程没有自己的关卡文档——场景仍由 C# 类硬编码（画廊的 `SceneCatalog.cs` 注册表）。本规范补上这一层：关卡容器格式、材质资产格式、以及两侧的分界线。

目标用户：

- 给引擎做场景与资产的美术 / 工具开发者——只面对引擎工程根，不接触 Ludots 概念；
- 把世界实体绑到引擎上的 mod 作者——只面对 host_assets 绑定清单与平台无关 id。

## 2. 三层结构与依赖方向

```
引擎工程层（自持，零 Core）
    项目根：scenes / materials / models / textures / camera / environment
    物理载体：src/Client/Ludots.Raylib.Render + 自持 app（画廊泛化为 level player）
    独立可打开、可播放、可截图
        ▲ 单向依赖
binding 层
    物理载体：src/Adapters/Raylib/Ludots.Adapter.Raylib（RaylibHostLoop / RaylibFieldRenderPresenter）
    绑定清单：assets/Presentation/host_assets.schema.json（平台无关 id → 引擎 URI）
        ▲ 只认识合同
Ludots 世界（Core / mod / ECS）
    每帧数据合同：PrimitiveDrawItem / SkinnedVisualBatchItem / 快照接口
```

依赖方向单向：世界 → 合同 ← binding → 引擎工程。引擎工程层零 Core 由 `src/Tests/ArchitectureTests/CoreBoundaryTests.cs` 机器强制，不靠约定。

## 3. 三条铁律（验收线）

1. **引擎工程资产声明是装载真源。** 关卡文件的 `assets[]` 是资产唯一入口：组件与节点通过 assetId 引用，装载器按清单解析 URI。组件代码禁止硬编码资产路径。引用清单外的 URI、清单项无人消费，装载 fail-fast。
2. **host_assets 是世界侧引用引擎资产的唯一通道。** mod 想用引擎工程的网格 / 材质 / 动画，只能在 host_assets 清单里声明"平台无关 id → 引擎 URI"，不得反向把引擎路径写进世界侧数据。
3. **`PrimitiveDrawItem` 与 `SkinnedVisualBatchItem` 是两层之间唯一的每帧数据通道。** 引擎工程内的美术动画、世界侧的游戏动画、binding 的实体驱动，最终都只通过这两个纯数据结构进渲染。

三条各对应一个合同测试，是实现完成的判定线。

## 4. 复用清单：main 上已在位的接口

本规范不新造装载与渲染基建，全部复用以下既有接口：

| 接口 | 位置 | 承担 |
|---|---|---|
| `RaylibAssetStore<T>` | `src/Client/Ludots.Raylib.Render/Rendering/RaylibAssetStore.cs` | URI 去重、Lease 租约、两阶段异步（worker CPU 相 + 渲染线程帧泵）、负缓存重试、fail-loud |
| `IRenderAssetPathResolver` / `GalleryAssetPaths` | `src/Apps/Raylib/Ludots.App.RaylibEngineGallery/GalleryAssets.cs` | 项目 URI → 物理路径，拒绝路径逃逸 |
| `IRenderMeshAssets` / `GalleryMeshAssets` | 同上 | 网格描述符注册（Primitive 图元 / Model GLB），MeshAssetId 分配 |
| `IRenderMaterialAssets` / `GalleryMaterialAssets` | 同上 | 材质注册、实例链解析 |
| `IPrimitiveDrawSnapshot` / `GalleryPrimitiveSnapshot` | 同上 | 每帧图元缓冲，stableId 驱动 static mesh 增量同步 |
| `ISkinnedVisualBatchSnapshot` / `GallerySkinnedBatch` | 同上 | 每帧蒙皮批次缓冲 |
| `PrimitiveDrawItem` | `src/Platform/Ludots.Platform.Abstractions/PrimitiveDrawItem.cs` | 节点实例纯数据：TRS（四元数旋转）+ MeshAssetId / MaterialId + 颜色 / LOD / 可见性 / Mobility / TemplateId / Animator |
| `SkinnedVisualBatchItem` | `src/Platform/Ludots.Platform.Abstractions/SkinnedVisualBatchItem.cs` | 蒙皮实例纯数据 |
| `RaylibIsmRenderBridge` + `StaticMeshAdapterSyncPlanner` | `src/Client/Ludots.Raylib.Render/Rendering/` | 同 mesh 多实例 ISM 合批车道、stableId dirty sync |
| `RaylibMaterialLibrary` | `src/Client/Ludots.Raylib.Render/Rendering/RaylibMaterialLibrary.cs` | 材质解析 / 贴图应用 / 实例链 |
| `RaylibFrameLighting` / `RaylibSkyEnvironment` | 同目录 | 光照 JSON 装载、天空环境配置 |

新增代码只有一样东西：关卡 / 材质文档 → 上述接口的装载映射器。

## 5. 引擎工程根布局

一个引擎工程是一个目录，充当项目根；根内 URI 一律根相对、正斜杠：

```
<engine-project>/
  project.json          项目清单：name、schemaVersion、场景目录入口
  scenes/               关卡容器，一景一文件
  materials/            材质资产
  models/               GLB 网格与动画
  textures/             贴图
```

第一个引擎工程实例就是画廊的项目根：`src/Apps/Raylib/Ludots.App.RaylibEngineGallery/assets/engine_gallery/`。`catalog.json` 保留为项目内场景注册表（id + asset），画廊菜单与验收 CLI 从它枚举。

## 6. 关卡容器格式（scenes）

文件为 JSON 文本，`schemaVersion` 从 1 起（PR #1420 草稿的版本号作废，该格式未合入）。

顶层字段：

| 字段 | 类型 | 约束 |
|---|---|---|
| `schemaVersion` | int | 必填，当前 1 |
| `id` / `title` / `summary` | string | 必填非空；`id` 项目内唯一 |
| `world.units` / `world.upAxis` | string | 固定 `meters` / `Y`，与 raylib 一致 |
| `world.bounds` | min/max 各 [x,y,z] | 必填，max 逐轴大于 min |
| `camera` | object | mode=`orbit`；target[3]、distance>0、pitchDegrees∈[0,90]、fovyDegrees∈(0,180) |
| `assets[]` | list | 装载真源，见下 |
| `rootNode` / `nodes[]` | string / list | 节点层级，见下 |

`assets[]` 每项：`id`（场景内唯一）、`kind`（mesh / model / material / texture / terrain）、`source`（项目根相对 URI）。装载器按 kind 注册进 `GalleryMeshAssets` / `GalleryMaterialAssets` 并经 `RaylibAssetStore` 装载。清单里出现未被任何节点引用的资产、或节点引用清单外的 id，装载 fail-fast。

节点与组件：

```
{ "id": "…", "parent": "…(根节点省略)", 
  "transform": { "position": [x,y,z], "rotation": [x,y,z,w], "scale": [x,y,z] },
  "components": [ … ] }
```

节点 id 唯一；parent 必须已声明、不得成环；rotation 为单位四元数（xyzw）。

组件是纯美术能力，kind 用短名经特性注册（对应实现类打 `[EngineSceneComponent(kind)]`），**关卡文件不出现 C# 类型名**。v1 组件面：

| kind | 字段 | 映射 |
|---|---|---|
| `static_mesh` | `asset`(mesh)、`material`?、`instances[]`（各含 position/rotation/scale，可选 color） | 每个 instance 摊成一个 `PrimitiveDrawItem`；同 MeshAssetId 自动进 ISM 合批车道 |
| `animator` | `asset`(model)、`clip`、`loop`、`phase`、`speed` | 美术动画播放，产出 `SkinnedVisualBatchItem`；clip 来自 GLB 内嵌动画，v1 不另立 clip 文件格式 |
| `terrain` | `asset`(.height/.grid)、`profile` | 高度图 / 网格地形渲染源 |
| `environment` | sky / fog 配置 | 下发 `RaylibSkyEnvironment` / 雾参数 |
| `decal` / `water` | 资产引用与参数 | 对应现有 renderer 配置面 |

## 7. 材质格式（materials）

`.mat.json` 沿用世界侧 `assets/Presentation/material_assets.schema.json` 的行形状，两者的参照系：

- `parent` 实例链（子材质禁改 shaderKey/flags、只覆盖参数）= Unreal Material Instance；
- `shaderKey` + `params.floats/colors` = Unity 的 shader + properties；
- `flags`（Opaque / Cutout / AlphaBlend / Additive / DoubleSided / Unlit）+ 顶层 `roughness` / `metalness` = Godot BaseMaterial3D 属性集。

与世界侧的唯一差异：引擎工程材质**自带贴图槽**（工程内 URI），因为工程自持资产；世界侧材质贴图槽仍在 host_assets 绑定清单里。同一形状、两个归属、同一实现（`RaylibMaterialLibrary`）：

```
{ "id": "stone_wall", "domain": "Surface",
  "shaderKey": null, "flags": ["Opaque"],
  "roughness": 0.8, "metalness": 0.0,
  "textures": { "albedo": "textures/stone_albedo.png" },
  "params": { "floats": {}, "colors": {} } }
```

实例级不复制材质：节点组件通过 material 引用切换 + 实例参数覆盖表达（对应 Unity MaterialPropertyBlock 的位置）。

## 8. 动画分层

- **引擎工程持有美术动画**：关卡里的 `animator` 组件做展示性播放（clip / loop / phase / speed），独立 level player 不接世界也能动。
- **Ludots 世界持有游戏动画**：`assets/Presentation/animation_clips.schema.json`、`animation_profiles.schema.json`、`animator_controllers.schema.json` 走 presenter 通道，由游戏状态驱动。
- 两条路径不共享代码，共享的只有 `SkinnedVisualBatchItem` 数据合同。

## 9. 装载管线

```
关卡 / 材质文档
    │ 装载映射器（本规范唯一新增代码）
    ├─ assets[] ──注册──► GalleryMeshAssets / GalleryMaterialAssets
    │                        └─ GLB / 贴图 ──► RaylibAssetStore（租约 / 异步 / 帧泵）
    ├─ nodes[].components ──映射──► PrimitiveDrawItem / SkinnedVisualBatchItem ──► 快照
    └─ camera / environment ──► 轨道相机默认值 / RaylibFrameLighting / RaylibSkyEnvironment
渲染器零改动，照常消费快照
```

## 10. 验收

- 三个合同测试，对应三条铁律：装载真源（含"组件零硬编码 URI"静态扫描）、host_assets 唯一通道、每帧数据通道唯一性。
- 一个组合验收场景：地形 + 同 mesh 多实例阵 + animator 角色 + 材质实例链 + 相机，走现有画廊验收链（preset + 截图 + 帧统计）。
- 存量迁移：画廊 21 个能力场景逐个搬进容器（机械迁移，一景一文件）；`SceneCatalog.cs` 改为装载 catalog.json；`scripts/record-engine-galleries.py` 同步改读 catalog.json。

## 11. 边界与落地次序

非目标：Core 地形 / nav 烘焙链归 #1350 执行序；navmesh 烘焙 showcase 归 #1402。PR #1420 关闭处置：其 Core 改动退回 #1350 子票序列；scene.json 骨架、严格校验、相机默认值数据化可回收为本规范实现的草稿。

落地次序：

1. 装载映射器 + 关卡 / 材质文档格式 + 三个合同测试；
2. 存量 21 场景迁移 + `SceneCatalog.cs` / 录像脚本切换 catalog.json；
3. 组合验收场景 + preset + 门户登记。
