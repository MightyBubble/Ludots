# Presenter Transform、Grounding 与 Attachment

本文定义 Presenter 树中变换（position / rotation / scale）的传播规则、地面贴合（grounding），以及挂载（Attachment）的语义。下文把 Attachment 称为挂载，把 `children` 条目称为子 presenter。

## 1 问题域

一个铁匠铺 presenter 树中，变换传播涉及多种场景：

- root presenter 跟随 gameplay entity 的 `WorldPositionCm` → 子 presenter 需要继承
- 工房 mesh 有 `LocalOffset` 相对于 root → 这个偏移只作用到该资产的图元，不传给子 presenter
- 烟囱 VFX 在锅炉顶部 → 需要相对于锅炉 presenter 的位置
- 工人沿样条巡逻 → 位置由 Spline behavior 驱动，不继承父位置
- 工人手持锤子 → 挂载到工人骨骼，位置由骨骼驱动
- 所有地面物体需要 grounding（贴合地形高度）
- 有些物体（如飞行 VFX）不需要 grounding

## 2 变换空间定义

```
WorldSpace     — 绝对世界坐标（最终发射到 adapter 的坐标）
ParentSpace    — 相对于父 presenter 的坐标
LocalSpace     — 相对于自身 AssetBinding 的偏移
BoneSpace      — 相对于骨骼挂点的坐标
```

## 3 变换传播模型

### 3.1 位姿组件

位姿不在一个 `PresenterInstance` 结构里。每个 presenter 实体上是这些组件：

```csharp
public struct PresenterWorldPosition { public Vector3 Value; }
public struct PresenterWorldRotation { public Quaternion Value; }
public struct PresenterWorldScale { public Vector3 Value; }
public struct PresenterWorldFacing { public float AngleRad; public byte HasValue; }
public struct PresenterWorldPlanePosition { public Vector2 ValueCm; }

public struct PresenterTransformSource { public TransformSource Value; }

public enum TransformSource : byte
{
    InheritParent = 0,    // 从父 presenter 继承
    EntityTransform = 1,  // 从属主的 VisualTransform 读取
    SplineDriven = 2,     // 由 Spline behavior 写入
    BoneAttached = 3,     // 挂到父 presenter 的骨骼
    AttachedToParent = 4, // 挂到父 presenter 本身
    WorldFixed = 5,       // 使用当前世界坐标，不跟父节点、不跟属主
}
```

`anchor.offset` 加载后放在 `PresenterDefinition.PositionOffset`。`overrides.transform` 加载后放在组件 `PresenterInstanceTransformOverride`（`LocalPosition` / `LocalRotation` / `LocalScale` / `HasOverride`）。

### 3.2 谁在算

三条路径都会写世界位姿：

1. `PresenterEntityTransformSyncSystem`：同步属主位姿，并传播 `InheritParent` 与 `AttachedToParent`。
2. `PresenterEntityRuntime.PropagateParentDrivenTransforms`：父节点位姿变化后，向下传播 `InheritParent` 与 `AttachedToParent`。
3. `PresenterBehaviorSystem`：按行为槽执行挂载、贴地、样条。`BoneAttached` 只在这一条路径上重算。

`PresenterBehaviorSystem` 对单个 presenter 的顺序：

1. 来源已经是 `BoneAttached` 或 `AttachedToParent` 时，保持当前位姿，不再叠一次覆盖。覆盖只由挂载行为重算。挂载中途返回（父节点失效、骨骼取不到、provider 未注册）时，位姿停在上一帧，不会逐帧累加。
2. `InheritParent`：位置 = 父 `PresenterWorldPosition` + `PositionOffset`。这个锚点偏移直接加在世界坐标上，不随父旋转，也不乘父缩放。旋转取父旋转，缩放取父缩放。然后再叠 `overrides.transform`。
3. `EntityTransform`：位置 = 属主 `VisualTransform.Position` + `PositionOffset`。旋转和缩放取属主 `VisualTransform`。然后再叠 `overrides.transform`。
4. `SplineDriven` 与 `WorldFixed`：在当前位姿上叠覆盖。覆盖偏移按旋转转，不乘缩放。
5. `AssetBinding` 的 `LocalOffset` / `LocalRotation` / `LocalScale` 不进入上面的传播。它们只在发那一个资产的图元时使用，并且每个资产只消费一次（`PresenterLocalOffsetConsumption`）。
6. Grounding 在行为循环里按槽位顺序执行，不看 `TransformSource`。

无挂载时，`overrides.transform.localPosition` 按父旋转（或属主旋转）转，并且乘父缩放（或属主缩放）。有挂载时，同一项只按挂载旋转转，不乘缩放。把子 presenter 从 `InheritParent` 改成挂载之后，放大的父节点下这段偏移会变短。Parent 挂载自己的 `offset` 在 `inheritScale=true` 时仍然乘父缩放；覆盖偏移不乘。

### 3.3 缩放

| 来源 | 缩放 |
|------|------|
| InheritParent | 父 `PresenterWorldScale` × 覆盖缩放 |
| EntityTransform | 属主 `VisualTransform.Scale` × 覆盖缩放 |
| SplineDriven | 只有覆盖缩放；没有覆盖时是 `(1,1,1)` |
| AttachedToParent | `inheritScale` 时为父缩放 × 覆盖缩放，否则 `(1,1,1)` × 覆盖缩放 |
| BoneAttached | `inheritScale` 时为骨骼缩放 × 覆盖缩放，否则 `(1,1,1)` × 覆盖缩放 |
| WorldFixed | 只有覆盖缩放；没有覆盖时是 `(1,1,1)` |

没有覆盖时，覆盖缩放按 `(1,1,1)` 计。

### 3.4 旋转

| 来源 | 旋转 |
|------|------|
| InheritParent | 父旋转 × 覆盖旋转 |
| EntityTransform | 属主旋转 × 覆盖旋转 |
| SplineDriven | 见第 6 节。当前 Patrol 把朝向写成 0 |
| AttachedToParent | `(父旋转 × rotationOffset)` × 覆盖旋转 |
| BoneAttached | `(骨骼旋转 × rotationOffset)` × 覆盖旋转 |
| WorldFixed | 当前旋转 × 覆盖旋转 |

### 3.5 位置

`InheritParent` / `EntityTransform` 的锚点偏移是世界坐标相加。覆盖偏移再按基础旋转转，并乘基础缩放。

`AttachedToParent` 与 `BoneAttached` 的公式在第 5.5 节。`ResolveTransform` 对这两个来源原样返回当前位姿。

`SplineDriven`：覆盖偏移按当前旋转转，不乘缩放。`WorldFixed`：覆盖偏移按“当前旋转 × 覆盖旋转”转，不乘缩放。

## 4 Grounding（地面贴合）

### 4.1 Grounding 行为

贴地是独立的 Grounding 行为，不在 `AssetBindingConfig` 上。

```csharp
public enum GroundingMode : byte
{
    None = 0,
    SnapToGround = 1,
    AlignToSurface = 2,
}

public enum GroundingUpdatePolicy : byte
{
    Once = 0,
    EveryFrame = 1,
}

public struct GroundingConfig
{
    public GroundingMode Mode;
    public float Offset;
    public GroundingUpdatePolicy UpdatePolicy;
}
```

### 4.2 执行

贴地在 `PresenterBehaviorSystem` 的行为循环里执行，排在当帧 `ResolveTransform` 之后，具体次序等于行为槽在定义里的次序。

高度图接口是 `IContinuousHeightmap`。单点用 `TrySampleHeightCm`，坐标单位是厘米。`AlignToSurface` 走 `PresenterGroundingUtility.ResolveBatch` 里带 `rotations` 的重载，法线对齐用 `AlignUpToNormal`。

采样失败，或高度不是有限数时，Y 直接取 `Offset`。没有高度图时打一次警告：`UpdatePolicy=Once` 会延后到下一次；`EveryFrame` 同样把 Y 设成 `Offset`。

### 4.3 批量采样

```csharp
public static void ResolveBatch(
    Span<Vector3> positions,
    Span<GroundingMode> modes,
    Span<float> offsets,
    IContinuousHeightmap heightmap);

public static void ResolveBatch(
    Span<Vector3> positions,
    Span<Quaternion> rotations,
    Span<GroundingMode> modes,
    Span<float> offsets,
    IContinuousHeightmap heightmap);
```

`mode == None` 的项跳过。第二份重载只在 `AlignToSurface` 时改旋转。

### 4.4 归属

贴地由 `PresenterGroundingUtility` 和 `PresenterBehaviorSystem` 承担。高度图是地图拥有的 Core 服务（`IContinuousHeightmap`），贴地是 Core 的步骤，各个 adapter 消费同一份结果。

## 5 Attachment（挂载）

### 5.1 设计

挂载把子 presenter 挂到父 presenter 上，或挂到父 presenter 的一根骨骼上。

```csharp
public enum AttachmentTarget : byte
{
    Parent = 0,
    Bone = 1,
}

public struct AttachmentConfig
{
    public AttachmentTarget Target;   // Parent：挂父 presenter；Bone：挂父 presenter 的骨骼
    public int BoneId;                // 骨骼索引，只在 Target=Bone 时允许写，必须是正整数
    public Vector3 Offset;            // Parent：父空间偏移；Bone：骨骼空间偏移
    public Quaternion RotationOffset; // 右乘到父旋转或骨骼旋转上
    public bool InheritScale;         // Parent：是否继承父缩放；Bone：是否继承骨骼缩放。默认 false
}
```

`InheritScale=false` 时，缩放从 `(1,1,1)` 起，再乘覆盖缩放。Parent 且 `InheritScale=true` 时，`Offset` 先乘父缩放；Bone 的 `Offset` 不乘骨骼缩放。

### 5.2 变换计算

骨骼世界变换通过 `IBoneTransformProvider.TryGetBoneWorldTransform(parentStableId, boneId, …)` 读取。取不到时打一次警告并跳过，不用父位置顶替。

`InheritScale=false` 时缩放是 `(1,1,1)`，再乘覆盖缩放。`InheritScale=true` 时缩放是骨骼缩放（或父缩放）再乘覆盖缩放。

### 5.3 Bone Transform 回传

```csharp
public interface IBoneTransformProvider
{
    bool TryGetBoneWorldTransform(
        int stableId,
        int boneId,
        out Vector3 position,
        out Quaternion rotation,
        out Vector3 scale);
}
```

接口在 `src/Core/Presentation/Presenters/IBoneTransformProvider.cs`。Raylib 的实现是 `RaylibBoneTransformProvider`，由 `RaylibHostLoop` 注册到 `CoreServiceKeys.BoneTransformProvider`。仓库里的适配器是 Networking、Raylib、Web；Web 没有这个实现，骨骼挂载会警告一次并跳过。

Raylib 的 provider 读的是上一帧 `PresenterEmitSystem` 写入的蒙皮可视项。`PresenterBehaviorSystem` 排在动画时间推进和本帧发射之前，所以第 N 帧的骨骼挂件用的是第 N-1 帧的骨骼姿势和父世界位姿。CPU 蒙皮和 GPU 人群两条通道都是这样。要消掉这一帧，得把骨骼挂点挪到动画器推进之后、本帧发射之前，并用本帧父位姿重算挂点子树。这是表现管线顺序的改动，本页只记录，不在挂载合同的修补里改。

选帧只有 `RaylibSkinnedPlayback.ResolveRenderedPose` 一处，按父蒙皮可视项的渲染通道决定：

- `GpuSkinnedInstance`：先按动画器算精确帧，再按 `presentation.gpuSkinned.posePhaseBuckets` 量化到相位桶中心帧。桶数从绘制身体的渲染器读（`RaylibPrimitiveRenderer.GpuSkinnedPosePhaseBuckets`）。
- `SkinnedMesh`：逐项用精确帧。

身体和挂点走同一个选帧函数，量化误差两边一致。Knight 片段 11（40 帧，演示里 1 秒一圈）每桶 2.5 帧，最多差 1 帧（25 ms）。演示骑士实测：挂点 119/119 帧等于上一帧的手骨；量化到同一个桶时看不出，跨桶那一帧剑和手最多差 0.33 m（120 帧里 32 帧）。

### 5.4 挂载与 Grounding

`ApplyGrounding` 不看 `TransformSource`，加载器也不拒绝“挂载 + Grounding”同时存在。贴地会改已经算好的位置。挂在骨骼上的物体如果再开贴地，会被拉到地面高度。不要两套一起开。

### 5.5 挂载与子 presenter 位姿覆盖的叠加

子 presenter 上的 `overrides.transform` 成为 `PresenterInstanceTransformOverride`。同时有挂载时，先算挂载，再叠覆盖。覆盖只在 `HasOverride` 为真时生效。

Parent：

```
parentRot      = normalize(parentRotation)
parentScaleN   = normalize(parentScale)
scaledOffset   = inheritScale ? parentScaleN * offset : offset
attachRotation = normalize(parentRot * rotationOffset)
position       = parentPosition + rotate(scaledOffset, parentRot)
rotation       = attachRotation
scale          = inheritScale ? parentScaleN : (1,1,1)
```

Bone：把 `parentPosition` / `parentRotation` / `parentScale` 换成骨骼的世界位姿。`offset` 不乘骨骼缩放。`inheritScale=true` 时 `scale` 取骨骼缩放，否则取 `(1,1,1)`。

然后，若有覆盖：

```
position += rotate(override.localPosition, attachRotation)  // 不乘 scale
rotation  = attachRotation * override.localRotation
scale     = scale * override.localScale
```

同一段后半在这些调用点各算一次：`PresenterBehaviorSystem` 的单个挂载和 Parent 批量挂载、`PresenterEntityTransformSyncSystem` 的快速 Parent 挂载、`PresenterEntityRuntime.ApplyParentAttachmentTransform`。前半段（挂载自身的 offset / 旋转 / 缩放）仍是各处各写一份。

属主不进每帧同步时，创建当下会做一次初始化，之后不再重算：

- 定义里恰好一个活跃的 Parent 挂载，并且父位姿已经可读：写成 `AttachedToParent`。两个 Parent 挂载、Bone 挂载、或父位姿还读不到，都不走这条。
- 没有挂载、但子 presenter 带了位姿覆盖，并且父位姿可读：按 `InheritParent` 叠一次覆盖。
- 创建期只看定义上的 `behaviors`，不看 `instanceBehaviors` 追加的挂载。

加载期校验：

- 写了 `boneId` 而 `target` 不是 `Bone`，直接报错。`target` 是 `Bone` 时，`boneId` 必须是正整数；漏写、`0` 和负数报同一条错。schema 里 `boneId` 是 `integer`，`minimum` 为 1。
- 子定义声明了 `anchor.offset` 时，`children` 与 `instanceChildren` 里只要出现 `overrides.transform`（包括只写 `localScale` / `localRotation`，或空对象）就报错。用 `instanceBehaviors` 再加 Attachment 同样报错。判据是 `PresenterInstanceTransformOverride.HasOverride`，出现 `transform` 对象即为真。

## 6 Spline 驱动的变换

### 6.1 Patrol

`SplineUsage.Patrol = 2`。`PresenterBehaviorSystem.ApplySpline` 只做这些事：

1. 要求写了 `progressParamKey`，否则抛错。
2. `progress += dt * speed`。`speedParamKey` 缺省时速度是 0。`loop=true` 时取小数部分，否则夹到 0 到 1。
3. 把 progress 写回这个 float 参数，来源改成 `SplineDriven`。
4. 世界位置写成 `(progress, 原来的 Y, 0)`，朝向写成 0。

它不采样样条曲线。`splineAssetId`、`widthParamKey`、`colorParamKey`、`pingPong`、`waypointEventId` 会被加载器收下，Core 运行时没有读取。`waypointEventId` 因此不会触发任何表现事件。

### 6.2 SplineConfig

```csharp
public enum SplineUsage : byte
{
    Render = 1,
    Patrol = 2,
}

public struct SplineConfig
{
    public int SplineAssetId;
    public SplineUsage Usage;
    public int WidthParamKey;
    public int ColorParamKey;
    public int SpeedParamKey;
    public int ProgressParamKey;
    public bool Loop;
    public bool PingPong;
    public int WaypointEventId;
}
```

### 6.3 可见样条

道路、河流这类可见样条不是靠 Spline 行为发的。AssetBinding 在发射时组 `SplineRibbonRequest`（`P0`–`P3`、`Width`、`FillColor`、`BorderColor`、`BorderWidth`）。控制点来自 `SplineP0`–`SplineP3` 这些参数，宽度和颜色来自样条参数或该资产自己的缩放、颜色参数，不读 `SplineConfig.WidthParamKey` / `ColorParamKey`。

## 7 变换传播总结

| TransformSource | 位置 | 旋转 | 缩放 | Grounding |
|-----------------|------|------|------|-----------|
| InheritParent | 父位置 + 锚点偏移，再按父旋转和父缩放叠覆盖偏移 | 父旋转 × 覆盖旋转 | 父缩放 × 覆盖缩放 | 按槽位执行，不自动跳过 |
| EntityTransform | 属主位置 + 锚点偏移，再按属主旋转和属主缩放叠覆盖偏移 | 属主旋转 × 覆盖旋转 | 属主缩放 × 覆盖缩放 | 同上 |
| SplineDriven | 见 6.1，再叠不乘缩放的覆盖偏移 | 当前实现写成 0，再乘覆盖旋转 | 只有覆盖缩放 | 同上 |
| AttachedToParent | 见 5.5 Parent | 见 5.5 | 见 5.5 | 同上 |
| BoneAttached | 见 5.5 Bone | 见 5.5 | 见 5.5；offset 不乘骨骼缩放 | 同上 |
| WorldFixed | 当前世界位置上叠不乘缩放的覆盖偏移 | 当前旋转 × 覆盖旋转 | 只有覆盖缩放 | 同上 |

## 8 铁匠铺里的挂载

铁匠铺演示的定义在 `mods/showcases/presenter_blacksmith/PresenterBlacksmithShowcaseMod/assets/Presentation/presenters.json`。这里没有 `worker_hammer`。工具挂载的定义是 `blacksmith_dynamic_worker_tool_attachment`：`target` 为 Parent，偏移 `(0, 0.65, -0.35)`，`inheritScale` 为 false。它挂在父 presenter 上，不挂骨骼。

## 9 外观树与覆盖演示

`mods/showcases/presenter_tree_override` 把覆盖与挂载摆成能看的展台，镜头自动巡游。设计、场景和已知边界见该目录下的 `showcase-design.md`。逐案例验收是 `PresenterTreeOverrideShowcaseAcceptanceTests`。

摆放和创建时要注意：

- 地图摆放的 `PositionXCm` / `PositionYCm` 是这一份实例的位置，盖过模板里的默认 `WorldPositionCm`。单体摆放和批量摆放（写了 `PresenterParamOverrides` 的那条）都这样。两个轴必须一起写。再同时写 `Overrides.WorldPositionCm` 会在加载时报错。
- 实体子件挂到父实体上时，`AttachmentOps.ApplyAttachedPose` 按父位姿写出世界坐标，并补上 `VisualTransform`。模板子件因此不必自带 `WorldPositionCm`，`SourceHasVisualTransform` 也能给它建外观。
- Raylib 实机要有本地座位（`game.json` 的 `startupLocalSeats`，加上地图的 `Teams` / `Players`）。没有座位时可见性裁剪不会武装，挂在实体上的外观一直不画，加载不报错。
- 位姿覆盖和参数覆盖留在批量创建里。只有实例子树覆盖（`instanceChildren` / `instanceBehaviors`）和再往下长出的节点改为逐个创建。
- 属主带 `PresentationStaticTransform` 时，第 5.5 节的创建期初始化只发生一次。Bone 挂载和 `instanceBehaviors` 追加的挂载不在这次初始化里。
