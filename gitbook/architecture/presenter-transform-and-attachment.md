# Presenter Transform、Grounding 与 Attachment

本文定义 Presenter 树中变换（position/rotation/scale）的传播规则、地面贴合（grounding）策略、以及骨骼挂载（attachment）的完整语义。

## 1 问题域

一个铁匠铺 presenter 树中，变换传播涉及多种场景：

- root presenter 跟随 gameplay entity 的 `WorldPositionCm` → 子 presenter 需要继承
- 工房 mesh 有 `LocalOffset` 相对于 root → 需要组合父变换
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

### 3.1 PresenterInstance 的变换字段

```csharp
public struct PresenterInstance
{
    // ...其他字段...

    // ── 变换 ──
    public Vector3 WorldPosition;        // 最终世界位置（每帧计算）
    public Quaternion WorldRotation;     // 最终世界旋转
    public Vector3 WorldScale;           // 最终世界缩放

    // ── 变换来源 ──
    public TransformSource TransformSource;
}

public enum TransformSource : byte
{
    InheritParent = 0,   // 从父 presenter 继承（默认）
    EntityTransform = 1, // 从 Owner entity 的 VisualTransform 读取
    SplineDriven = 2,    // 由 Spline behavior 驱动
    BoneAttached = 3,    // 由 Attachment behavior 驱动（骨骼挂载）
    WorldFixed = 4,      // 固定世界坐标（不继承、不跟随）
}
```

### 3.2 变换计算流程

每帧由 `PresenterBehaviorSystem` 在行为驱动阶段计算：

```
1. 确定 TransformSource
2. 按 source 获取基础变换：
   - InheritParent: 读父 presenter 的 WorldPosition/Rotation/Scale
   - EntityTransform: 读 Owner entity 的 VisualTransform
   - SplineDriven: 从 Spline behavior 计算当前位置/朝向
   - BoneAttached: 从 Attachment behavior 读骨骼世界变换
   - WorldFixed: 使用创建时的固定坐标
3. 叠加 AssetBindingConfig.LocalOffset/LocalRotation/LocalScale
4. 若该子实例配置了 `overrides.transform`，再叠加实例位姿（位置相加、旋转相乘、缩放相乘）
5. 应用 Grounding（如果启用）
6. 写入 WorldPosition/WorldRotation/WorldScale
```

### 3.3 缩放传播规则

| 场景 | 父 Scale | 子 LocalScale | 最终 Scale | 说明 |
|------|----------|--------------|-----------|------|
| 普通子 presenter | (2,2,2) | (1,1,1) | (2,2,2) | 继承父缩放 |
| 有自身缩放的子 | (2,2,2) | (0.5,0.5,0.5) | (1,1,1) | 乘法组合 |
| 骨骼挂载 | 忽略 | (1,1,1) | (1,1,1) | 骨骼空间，不继承父缩放 |
| 世界固定 | 忽略 | (1,1,1) | (1,1,1) | 不继承 |

规则：**InheritParent 和 EntityTransform 模式下，缩放乘法组合。BoneAttached 和 WorldFixed 模式下，不继承父缩放。**

### 3.4 旋转传播规则

| 场景 | 父 Rotation | 子 LocalRotation | 最终 Rotation | 说明 |
|------|------------|-----------------|--------------|------|
| 普通子 presenter | Yaw90° | Identity | Yaw90° | 继承父旋转 |
| 有自身旋转的子 | Yaw90° | Yaw45° | Yaw135° | 四元数乘法组合 |
| 样条驱动 | 忽略 | 样条切线方向 | 样条切线 | 朝向由样条决定 |
| 骨骼挂载 | 忽略 | 骨骼旋转 + Offset | 骨骼空间 | 不继承父旋转 |

### 3.5 位置传播规则

```csharp
Vector3 ComputeWorldPosition(PresenterInstance self, PresenterInstance parent,
    AssetBindingConfig config)
{
    switch (self.TransformSource)
    {
        case TransformSource.InheritParent:
            // 父位置 + 父旋转 * (父缩放 * 子局部偏移)
            return parent.WorldPosition
                + Vector3.Transform(parent.WorldScale * config.LocalOffset,
                                    parent.WorldRotation);

        case TransformSource.EntityTransform:
            // entity 位置 + 局部偏移（不受父变换影响）
            var entityPos = GetEntityVisualTransform(self.Owner).Position;
            return entityPos + config.LocalOffset;

        case TransformSource.SplineDriven:
            // 由 Spline behavior 每帧写入，LocalOffset 作为样条法线偏移
            return self.WorldPosition + config.LocalOffset;

        case TransformSource.BoneAttached:
            // 由 Attachment behavior 每帧写入，LocalOffset 在骨骼空间
            return self.WorldPosition; // 已含 bone transform + offset

        case TransformSource.WorldFixed:
            return self.WorldPosition; // 创建时设定，不变
    }
}
```

## 4 Grounding（地面贴合）

### 4.1 Grounding 策略

不是所有 presenter 都需要 grounding。策略由 `AssetBindingConfig` 声明：

```csharp
public enum GroundingMode : byte
{
    None = 0,           // 不贴合（飞行 VFX、UI 元素）
    SnapToGround = 1,   // 贴合地面高度（建筑、地面单位）
    AlignToSurface = 2, // 贴合地面高度 + 法线对齐（贴花、道路）
}
```

`AssetBindingConfig` 新增字段：

```csharp
public struct AssetBindingConfig
{
    // ...现有字段...
    public GroundingMode Grounding;      // 默认 None
    public float GroundingOffset;        // 贴合后的额外 Y 偏移（如建筑地基高度）
}
```

### 4.2 Grounding 执行时机

在变换计算流程的第 4 步：

```
WorldPosition = ComputeWorldPosition(...)
if (config.Grounding != None)
{
    float groundHeight = visualHeightmap.SampleHeight(WorldPosition.X, WorldPosition.Z);
    WorldPosition.Y = groundHeight + config.GroundingOffset;
    if (config.Grounding == AlignToSurface)
    {
        WorldRotation = AlignToSurfaceNormal(visualHeightmap, WorldPosition);
    }
}
```

### 4.3 批量 Grounding

当一个 presenter 树有多个需要 grounding 的子 presenter 时，应批量采样高度图而非逐个查询。复用现有 `PrefabGroundingUtility` 的批量采样逻辑，但简化接口：

```csharp
public static class PresenterGroundingUtility
{
    public static void ResolveBatch(
        Span<Vector3> positions,        // in/out: 输入 XZ，输出 XYZ
        Span<GroundingMode> modes,      // in: 每个 presenter 的 grounding 模式
        Span<float> offsets,            // in: 每个 presenter 的 Y 偏移
        IVisualHeightmap heightmap)
    {
        // 批量采样，只处理 mode != None 的项
    }
}
```

### 4.4 与现有 Grounding 基建的关系

现有 `PrefabGroundingUtility` 和 `PrefabGroundingBatchContext` 随 Prefab 系统一起删除。新架构中 grounding 由 `PresenterGroundingUtility` 承载，接口更简洁，但核心原则不变（详见 [Prefab Grounding 与 Visual Height](prefab-grounding-and-visual-height.md)）：

- visual height 是 map-owned 的 Core service（`IVisualHeightmap`）
- grounding 是 Core-owned 的 lowering 步骤，不是 adapter 私活
- 所有 adapter 消费同一份 grounding 结果

## 5 Attachment（骨骼挂载）

### 5.1 设计

Attachment behavior 将子 presenter 挂载到父 presenter 的骨骼上。

```csharp
public struct AttachmentConfig
{
    public int BoneId;                   // 骨骼 ID（由 adapter 解析）
    public Vector3 Offset;              // 骨骼空间偏移
    public Quaternion RotationOffset;   // 骨骼空间旋转偏移
    public bool InheritScale;           // 是否继承骨骼缩放（默认 false）
}
```

### 5.2 Attachment 变换计算

```
1. 从父 presenter 的 AnimatorPackedState 获取骨骼世界变换
   （需要 adapter 提供 bone transform 回传接口）
2. 在骨骼空间应用 Offset 和 RotationOffset
3. 写入子 presenter 的 WorldPosition/WorldRotation
4. 如果 InheritScale=true，乘以骨骼缩放；否则使用自身 LocalScale
```

### 5.3 Bone Transform 回传

当前 adapter 只消费 `AnimatorPackedState`，不回传骨骼变换。需要新增：

```csharp
public interface IBoneTransformProvider
{
    bool TryGetBoneWorldTransform(int stableId, int boneId,
        out Vector3 position, out Quaternion rotation, out Vector3 scale);
}
```

- Raylib adapter：从 Raylib 的骨骼动画系统读取
- UE5 adapter：从 Skeletal Mesh Component 的 bone space 读取

这是 attachment 功能的 adapter 侧前置条件。

### 5.4 Attachment 与 Grounding 的交互

骨骼挂载的 presenter **不做 grounding**。即使 `GroundingMode != None`，当 `TransformSource == BoneAttached` 时跳过 grounding。骨骼位置已经包含了角色的地面贴合信息。

## 6 Spline 驱动的变换

### 6.1 Spline Patrol 模式

工人沿样条巡逻时：

```
1. Spline behavior 每帧推进 t 值（基于 speed param）
2. 从样条曲线采样 position 和 tangent
3. 写入 presenter 的 WorldPosition 和 WorldRotation（朝向切线方向）
4. 到达 waypoint 时可触发 PresentationEvent（如播放动画）
```

### 6.2 SplineConfig 扩展

```csharp
public struct SplineConfig
{
    public int SplineAssetId;
    public SplineUsage Usage;            // Render / Patrol
    public int SpeedParamKey;            // 移动速度（float lane）
    public int ProgressParamKey;         // 当前进度 0~1 输出（float lane）
    public bool Loop;                    // 是否循环
    public bool PingPong;               // 是否往返
    public int WaypointEventId;          // 到达 waypoint 时触发的事件 ID（0=不触发）
}
```

### 6.3 Spline Render 模式

道路/河流等可见样条：

```
1. Spline behavior 读取样条控制点
2. 发射 SplineRibbonRequest 到 adapter（复用现有 SplineRibbonRequest）
3. 宽度和颜色由 param 驱动
```

## 7 变换传播总结

| TransformSource | 位置来源 | 旋转来源 | 缩放来源 | Grounding |
|----------------|---------|---------|---------|-----------|
| InheritParent | 父位置 + 父旋转×局部偏移 | 父旋转×局部旋转 | 父缩放×局部缩放 | 可选 |
| EntityTransform | entity 位置 + 局部偏移 | entity 旋转×局部旋转 | 局部缩放 | 可选 |
| SplineDriven | 样条采样 + 法线偏移 | 样条切线 | 局部缩放 | 可选 |
| BoneAttached | 骨骼位置 + 骨骼空间偏移 | 骨骼旋转×偏移旋转 | 可选继承骨骼缩放 | 跳过 |
| WorldFixed | 创建时固定 | 创建时固定 | 局部缩放 | 可选 |

## 8 铁匠铺 UAT 中的变换场景

| Presenter | TransformSource | Grounding | 说明 |
|-----------|----------------|-----------|------|
| blacksmith_root | EntityTransform | SnapToGround | 跟随 entity 位置，贴合地面 |
| workshop_1 | InheritParent | SnapToGround | 相对 root 偏移 (2,0,0)，独立贴合 |
| workshop_2 | InheritParent | SnapToGround | 相对 root 偏移 (-2,0,0) |
| furnace | InheritParent | SnapToGround | 相对 root 偏移 (0,0,2) |
| smoke | InheritParent | None | 相对 furnace 偏移 (0,5,0)，不贴合 |
| worker | SplineDriven | SnapToGround | 样条巡逻，贴合地面 |
| worker_hammer（如有） | BoneAttached | 跳过 | 挂载到工人右手骨骼 |
