# 参数 sink 机制：从黑板到资产属性

参数 sink 回答一个问题：**黑板参数变了，画面怎么知道、怎么变**。作者在 behavior 上声明 sink 键（如 colorParamKey），指令/绑定行为写黑板，编译期把 sink 键收进定义的"视觉参数集合"，写入期检测变更标脏，重发期只重画脏实例、按 AssetKind 差异解析成资产属性。总目录见 [README.md](README.md)；指令层见 [commands.md](commands.md)。

## 声明面：作者写哪些 sink 键

AssetBinding 载荷的参数键（字段白名单 `src/Core/Presentation/Config/PresenterDefinitionConfigLoader.cs:647-656`）：

| sink 键 | lane | 效果 |
|---|---|---|
| `scaleParamKey` | Float | 缩放乘子，乘到 localScale/worldScale 上（Mesh 类） |
| `colorParamKey` | Vector | 实例色，覆盖 style.color |
| `materialParamKey` | Int | Mesh：直推材质 id；WorldHud Bar：血条填充值；WorldText：次值 |
| `assetIdParamKey` | Int | 直推资产 id（动态换资产，无查表） |
| `assetSwapParamKey` + `assetSwapTable` | Int | 查表换资产（paramValue→assetId，无命中 fail-loud） |
| `visibilityParamKey` | Int | 0=隐藏，非 0=可见 |
| `materialCustomData` | Float/Int/Vector 分槽 | 逐槽把参数直推到材质自定义数据 |

注意 `surfaceLayerKey` 是字符串图层名（表面合成的层排序），不是黑板参数 sink。

除 AssetBinding 外的参数入口：

- **WorldText behavior**：`valueParamKey`/`secondaryValueParamKey`（发射时映射为主值/次值，见下文写入差异表）。
- **MinimapMarker behavior**：三键 `colorParamKey`（Vector 换色）/`sizeParamKey`（换尺寸）/`visibilityParamKey`（显隐），外加 `orientationParamKey`（朝向注入）；字段白名单 `src/Core/Presentation/Config/PresenterDefinitionConfigLoader.cs:714-719`，消费在 `src/Core/Presentation/Systems/PresenterMinimapMarkerSystem.cs:535` 起。
- **well-known 键（不声明即生效）**：GroundOverlay 与 Spline 的几何/颜色参数走全局保留键，直接按 key id 写黑板即可（无需在 behavior 上声明）：OverlayRadius/OverlayInnerRadius/OverlayAngle/OverlayRotation/OverlayFill RGBA/OverlayBorder RGBA/OverlayBorderWidth/OverlayLength/OverlayWidth 与 SplineP0-P3/SplineWidth/SplineFillColor/SplineBorderColor/SplineBorderWidth，见 `src/Core/Presentation/Presenters/WellKnownPresenterParamKeys.cs:53-103`。

生产样例：schema 参考夹具把 Mesh 的六个 sink 键全声明在一处（assets/Presentation/presenters.json 的 ref_base_definition，`mods/fixtures/presenter_schema_reference/PresenterSchemaReferenceMod/assets/Presentation/presenters.json:8-60`）；GroundOverlay 换色用 colorParamKey 的活例在同文件 ref_ground_overlay（:418-450）。

## 编译面：sink 键收进定义

装载期 `CollectStaticVisualParams`（`src/Core/Presentation/Presenters/PresenterDefinition.cs:930-943`）把每个 AssetBinding 槽的 scaleParamKey/materialParamKey/assetIdParamKey/assetSwapParamKey/visibilityParamKey/colorParamKey 与 materialCustomData 各槽收集、排序成三个数组 `StaticVisualFloatParamKeys` / `StaticVisualIntParamKeys` / `StaticVisualVectorParamKeys`（:677-680）。GroundOverlay/Spline 的 well-known 键由 `CollectRetainedPresentationRequestParams`（:947-967）收进 retained-request 路径的集合。查询入口 `AffectsStaticVisualParam`（:907-921）按 lane 二分判断"这个 paramKey 是否影响视觉"。

这一步把"参数变化要不要重画"从运行时查表变成编译期预算——运行时只做一次数组包含判断。

## 写入面：SetParam 检测变更并标脏

`SetParamInternal`（`src/Core/Presentation/Presenters/PresenterEntityRuntime.cs:597-638`）按 lane 写 PresenterFloatParams / PresenterIntParams / PresenterVectorParams 三个黑板组件，值未变直接返回（不产生任何重画）；值变了则 state.Version++ 并调 `MarkStaticDirtyIfVisualParamChanged`（:4169-4211）：`AffectsStaticVisualParam` 命中时给静态视觉路径（PerfStaticStableVisual）置 `PresenterEmitCache.StaticDirty`，GroundOverlay/Spline 走 retained-request 脏标记；`AffectsMaterialSourceParam` 命中时另标材质脏。SetParam 指令用的是 `SetParamAndPropagateToAffectedChildren`（:580-590），会把参数传播给受影响的子 presenter（如建筑根上的 assetState 传给两侧 workshop mesh）。

## 重发面：脏查询与属性解析

`PresenterEmitSystem` 用 `DirtyStaticEmitQuery` 只扫带 PerfStaticStableVisual 的实例（`src/Core/Presentation/Systems/PresenterEmitSystem.cs:29-30`），逐个检查 `StaticDirty != 0` 才处理（:713 起），处理完清脏——干净实例零开销。解析在 `src/Core/Presentation/Systems/PresenterAssetEmitRuntime.cs`：

- `ResolveScale`（:690-699）：scaleParamKey 解析为乘子。
- `ResolveColor`（:719-724）：colorParamKey 命中即覆盖 style.color。
- `ResolveAssetId`（:635-671）：assetIdParamKey 直推；assetSwapParamKey 查 swap 表（paramValue 容差匹配，无命中抛错）。
- `ResolveMaterialId`（:673-688）与可见性（:629-633）：materialParamKey 直推、visibilityParamKey=0 隐藏。

## 各 AssetKind 的消费差异（发射期）

同一批 sink 键在不同资产上的语义不同（`src/Core/Presentation/Systems/PresenterAssetEmitRuntime.cs:422-627`）：

| AssetKind | 消费差异 |
|---|---|
| Mesh 类（Mesh/SkinnedMesh/Decal/VFX） | 全量消费：scale/color/material/assetSwap/visibility 全生效 |
| Spline（:409-456） | SplineWidth 覆盖 scaleParamKey 推导的宽度；SplineFillColor/BorderColor/SplineBorderWidth 与 SplineP0-P3 控制点全部走 well-known 键 |
| WorldHud Bar（:458-498） | materialParamKey = 血条填充值（0-1）；scale.X/scale.Y = 条宽高（像素） |
| WorldText（:500-552） | behavior 的 valueParamKey/secondaryValueParamKey 编译为 ScaleParamKey/MaterialParamKey 通道（`src/Core/Presentation/Presenters/BehaviorSlot.cs:45-46`），发射时落到主值/次值；textToken 决定文案格式 |
| GroundOverlay（:554-627） | OverlayRadius/InnerRadius/Angle/Rotation/Length/Width 覆盖 localScale 推导的几何缺省；OverlayFill/Border RGBA 分四键覆盖 style.color |

## 全链时序：从 SetParam 到换砖变色

铁匠铺"区域参数 0=北方黑砖、1=南方红砖"的真实链路（preset `presenter_blacksmith_showcase_raylib`）：

```text
GlobalRegionChanged 事件（keyId=1 南方）
  │  blacksmith_root 规则：SetParam blacksmith.workshop.assetState Int=1（valueSource EventKeyId）
  ▼
PresenterRuleSystem：事件 → PresenterCommand 入 PresenterCommandBuffer
  ▼
PresenterRuntimeSystem：SetParam 分支 → 定位 scoped 实例 → SetParamAndPropagateToAffectedChildren
  ▼
SetParamInternal：PresenterIntParams[assetState]=1；值变了 → state.Version++
  ▼
MarkStaticDirtyIfVisualParamChanged：assetSwapParamKey 在 StaticVisualIntParamKeys 里
  → PresenterEmitCache.StaticDirty=1；参数同时传播给受影响子 presenter（两侧 workshop mesh）
  ▼
PresenterEmitSystem：DirtyStaticEmitQuery 只扫 StaticDirty!=0 的实例
  ▼
PresenterAssetEmitRuntime.ResolveAssetId：swap 表查 paramValue=1 → blacksmith.building.south.intact
  ▼
PresentationRequest → raylib 适配器 draw buffer → 画面换砖
```

值未变的 SetParam 在第二步就返回 false，不会走到标脏——这就是"黑板写入是廉价的，视觉重画只发生在真变更"。

强制重发（值未变也要刷新画面）的指令入口是 SinkParamToAsset，见 [commands.md](commands.md) 的 SinkParamToAsset 条目（实现随配套 PR 落地）。
