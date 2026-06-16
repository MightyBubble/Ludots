# 空间几何与静态障碍 SSOT

本页定义 Physics2D / Navigation2D 对运行时阻挡体的正式数据归属。目标是让大型静态地图障碍在加载或显式 dirty 时付费，而不是在每个仿真步反复桥接、重建和排序。

## 1 SSOT

- Authoring SSOT：`ManifestationObstacleIntent2D` 表达 sink 目标，`ManifestationObstaclePolygon2D` 表达单多边形，`ObstacleGeometryProfile2D` 表达单逻辑实体上的多 piece 几何。
- Shape SSOT：`ShapeDataStorage2D` 保存已注册的 circle / box / polygon shape data。Physics 和 Navigation compound runtime 都引用同一组 shape index。
- Physics static SSOT：`Mass2D.IsStatic` 决定 body lane；`Physics2DStaticBodyState` 表示已进入静态缓存；`Physics2DStaticBodyDirty` 是静态缓存更新入口。
- Bridge SSOT：`ManifestationObstacleBridge2DState` 记录 shape / pose / sink signature；`ManifestationObstacleBridge2DDirty` 是 authoring 变更后的桥接入口。

## 2 几何合同

`ObstacleGeometryProfile2D` 是 Core-owned generic contract，用于替代“一个逻辑障碍拆成多个隐藏 child entity”的做法。

- 一个 logical entity 最多 author `ObstacleGeometryProfile2D.MaxPieces` 个 piece。
- 单个 polygon piece 最多 author `ObstacleGeometryProfile2D.MaxPolygonVertices` 个顶点。
- 超出限制、缺失必须字段、未知 shape、错误大小写都必须 fail fast。
- 支持的 piece shape 为 `Circle`、`Box`、`Polygon`；不做近似 fallback。

## 3 Materialization

`ManifestationObstacleBridge2DSystem` 只处理三类 entity：

- 新增：有 `ManifestationObstacleIntent2D` 但没有 `ManifestationObstacleBridge2DState`。
- 显式 dirty：带 `ManifestationObstacleBridge2DDirty`。
- 运行时移动：带 `ManifestationMotion2D`。

静态 obstacle 首次桥接后会写入 `Position2D`、`Mass2D.Static`、`Velocity2D.Zero`、`Collider2D` 或 `CompoundCollider2D`，并标记 `Physics2DStaticBodyDirty`。之后如果 authoring、sink、world position 或 rotation 改变，修改方必须显式添加 dirty 组件；不得依赖每帧全量扫描。

## 4 Broadphase

`BuildPhysicsWorldSystem2D` 将 body 拆成 dynamic 和 static 两个 lane：

- dynamic lane 每个 physics step 从未缓存的 dynamic body 构建 descriptor。
- static lane 只在新 static body 或 `Physics2DStaticBodyDirty` 出现时更新缓存。
- compound collider 会为每个 piece 生成一个 broadphase body handle，但仍保留同一个 logical entity。

`SortAndSweepStrategy` 维护独立 dynamic/static endpoint cache：

- dynamic endpoints 每步排序。
- static endpoints 只在 static body version 改变时重建排序。
- 默认只生成 dynamic-dynamic 与 dynamic-static pair，不生成 static-static pair。

`CollisionPair` 使用 body handle + collider snapshot 作为 pair key，因此一个 dynamic body 可以同时与同一 logical static entity 的多个 piece 产生 contact pair。

## 5 Navigation

`NavObstacle2D` 对应单 shape，`NavCompoundObstacle2D` 对应 geometry profile。Navigation compound obstacle 与 Physics compound collider 共用 `ShapeDataStorage2D` shape index。Flow obstacle stamping 可以消费两种 runtime component；静态 obstacle 的 authoring/pose 变更仍由 bridge dirty 规则进入 runtime component 更新。

## 6 Diagnostics

`Physics2DPerfStats` 必须区分：

- `StaticBodyCount`
- `DynamicBodyCount`
- `DirtyStaticBodyCount`
- `StaticMaterializationMs`
- `DynamicBuildMs`
- existing pair/contact stats

这些指标用于验证大规模静态地图在 steady state 下没有持续 materialization 和 static broadphase rebuild 成本。
