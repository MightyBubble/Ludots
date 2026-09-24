# RFC-0060 攻城关系与要塞运行时基建

## 1. 目标与非目标

### 1.1 目标

本 RFC 的目标不是把 `C:\001_AI\External` 的 React 原型逐帧搬进 Ludots，而是在 Ludots 现有 ECS / GAS / navigation / relation 基础上，抽出一套可正式落地的攻城共享基建，并据此承载后续攻城 Mod。

本 RFC 解决的核心问题：

- 为驻军、挂接、载员、壁面占位、墙段争夺、梯具投送、门侧夺取、地道运输、壕沟阻断提供统一数据语义。
- 在不重复造轮子的前提下，复用 Ludots 已有的 `ChildOf` / `ChildrenBuffer` / `RelationOps`、GAS effect/tag/rule、navigation graph、`FacingDirection` / `Rotation2D`。
- 将“结构化状态”与“过渡性状态”彻底分层：结构化状态进入 topology / attachment / occupancy 域，过渡性状态进入 GAS。
- 为正式攻城 Mod 提供可复用能力层，而不是继续堆叠 PR86 风格的 mod-local 运行时特判。
- 为地图编辑、存档转换、AI 调试侧栏建立明确的运行时契约，避免 UI 原型直接决定模拟层结构。

### 1.2 非目标

本 RFC 不做以下事情：

- 不定义具体兵种数值、城墙血量、爬墙耗时、云梯容量等平衡参数。
- 不把攻城原型的 React 组件结构迁移为 Ludots UI 结构。
- 不把拓扑连通性、墙体邻接、门内外侧、地道网络直接编码为 tag。
- 不把 presentation anchor 或 `PresentationStableId` 直接当作 gameplay attachment 主键。
- 不重做现有 road graph / path service / navigation2d 系统。

## 2. 外部原型能力盘点

### 2.1 关系与占位类机制

外部原型已经把以下机制作为统一问题处理，只是实现方式仍然是 JS 状态对象：

| 机制 | 外部原型实现 | 实质问题 |
|---|---|---|
| 建筑驻军 | `garrison.js` | 进入区、容量、允许单位类型、载员持有、出驻位置 |
| 云梯载员 | `siegeLadder.js` | 可移动载具的 garrison、装载上限、装载后投送 |
| 墙上交战 | `wallClimb.js` | 墙面占位、敌我混驻、超容量等待、胜利后归属转移 |
| 地道运输 | `tunnel.js` | 建筑内载员、网络出口选择、运输中状态、到站再驻军/出驻 |
| 壕沟捕获 | `tunnel.js` 中 trench 截获逻辑 | 经过型占位容器、容量、困陷 |

结论：

- 外部原型真正抽象的是“实体与宿主之间的关系语义 + 进入/退出规则 + 容量/占位”。
- 这不是单一 garrison 需求，而是一套通用 attachment / occupancy 问题。

### 2.2 拓扑与面位类机制

| 机制 | 外部原型实现 | 实质问题 |
|---|---|---|
| 墙体内外侧 | `garrison.js` / `wallClimb.js` | 墙段的 inner / outer face 语义 |
| 固定爬墙位 | `wallClimb.js` | 可占用 climb slot、approach slot |
| 城门内侧夺门 | `gateCapture.js` | gate inner node、可达性、占领通道 |
| 地道网络 | `tunnel.js` | topology node / edge、网络连通与路径 |
| 墙体邻接投送 | `siegeLadder.js` + `wallTraversal.js` | fortification graph 上的邻接搜索 |
| 壕沟阻断与填沟 | `trench.js` | 经过时交互、容量、阻断区 |

结论：

- 外部原型大量机制依赖“拓扑角色”和“面位语义”，而不是普通实体属性。
- 墙、门、地道、壕沟、梯具都需要 topology domain 支撑。

### 2.3 编辑器与存档工作流

| 能力 | 外部原型实现 | 含义 |
|---|---|---|
| 地图编辑器 | `GameCanvas.jsx` / `EditorToolbar.jsx` | 直接编辑 nodes / edges / buildings / terrain / presets |
| 存档 | `MapManager.jsx` + `base44/entities/GameMap.jsonc` | 把运行时地图序列化为 JSON 文件并经 Base44 保存 |
| AI 调试侧栏 | `AISidebar.jsx` / `AIDashboardFlow.jsx` | 面向调试的只读运行时可视化 |
| 策略代理 | `StrategyCommander.jsonc` | 面向 AI 的高层命令输入输出协议 |

结论：

- 编辑器和存档不是攻城规则本体，但它们决定了 topology 与 runtime state 的序列化边界。
- AI Dashboard 不是独立玩法系统，而是对运行时结构化数据的调试投影。

## 3. Ludots 现有能力盘点

### 3.1 已有且必须复用的能力

| 现有能力 | 位置 | 可直接复用结论 |
|---|---|---|
| 亲子关系底座 | `ChildOf.cs` / `ChildrenBuffer.cs` / `RelationOps.cs` | 作为 attachment spine 直接复用 |
| GAS 内 relation 变更 | `BuiltinHandlers.HandleApplyRelation` | 继续用于“进入/退出/挂接”触发 |
| GAS tag / rule / effect 管线 | Core GAS config + builtin handlers | 用于权限、状态窗口、成本、计时，不用于 topology 主表示 |
| 朝向基础 | `FacingDirection` / `Rotation2D` / create-unit facing | 直接复用为 orientation 基础字段 |
| 导航与图 | navigation2d、pathing bootstrap、road/nav graph | 继续承担可达性与图连通职责 |
| 运行时刷表现 | `WorldToVisualSyncSystem` + `VisualTransform` | 可消费 gameplay orientation，不应反向主导模拟 |
| 面板与调试 UI | Entity command panel / selection / presentation infra | 可作为编辑器与 AI 调试侧栏承载层 |

### 3.2 已有但当前使用方式仍偏 ad hoc 的能力

`mods/RtsDemoMod/Systems/RtsRelationRuntimeSystem.cs` 已经证明了：

- `ChildOf` 可承载 builder attach、morph attach、ungarrison。
- relation 变化后同步位置、同步可选中状态、完工 detach 这些流程可以在 runtime system 中完成。

但它也暴露了当前缺口：

- 关系只有“是否挂在父节点上”，没有共享的关系语义。
- 缺少槽位、容量、预约、锚点、进入朝向、挂接朝向、退出规则的共享数据。
- 逻辑仍依赖具体 tag 组合与特定 RTS map tag，无法覆盖攻城域。

### 3.3 不应误判为缺失的能力

以下能力不应再重复造轮子：

- pathing / graph / navigation service
- basic parent-child relation storage
- create-unit facing 与面向同步
- GAS tag gate、effect dispatch、timed tag
- 选择、命令面板、UI runtime

## 4. 真正缺失的共享基建

真正缺失的不是第二套关系系统，而是 relation spine 之上的共享语义层。

最小缺口如下：

| 缺口 | 说明 |
|---|---|
| 关系语义层 | 需要区分 garrison、builder-attached、docked、carried、wall-slot、ladder-slot、trench-trapped、tunnel-transit 等语义 |
| 槽位/锚点层 | 需要 gameplay anchor / slot id，而不是仅有 parent entity |
| 占位/容量/预约 | 需要宿主侧 slot capacity、occupancy、reservation，支持等待、排队、抢占失败 |
| 朝向策略层 | 需要进入朝向、驻留朝向、挂接朝向、投送朝向、目标朝向策略 |
| 要塞 topology 层 | 需要 wall segment、face、gate side、tunnel node、trench channel 的结构化数据 |
| 结构化 traversal runtime | 需要 climb、attach、capture、transport、fill 这类过程的共享 runtime shape |
| 编辑与存档契约 | 需要脱离 Base44 的中立 authoring DTO 与导入导出桥 |
| 调试快照契约 | 需要 attachment / topology / occupancy 的可观测接口，以及中立的 inspection-provider 契约，供 AI Dashboard 与 inspector 使用 |

## 5. 统一数据模型设计

### 5.1 设计原则

- `ChildOf` 继续作为唯一亲子关系主边。
- 所有“关系为什么成立”必须通过显式结构化元数据表达，不能仅靠 tag 猜测。
- topology 与 attachment 分层：topology 决定哪些宿主/面位/节点存在，attachment 决定哪个实体当前占了哪个槽位。
- GAS 只负责触发、计时、授权、成本、附加状态，不负责保存 topology 与 occupancy 主状态。

### 5.2 核心模型

#### A. 子实体侧：关系绑定

建议新增：

`AttachmentBinding`

字段建议：

- `Host`: `Entity`
- `Semantic`: `AttachmentSemantic`
- `SlotId`: `int`
- `AnchorId`: `int`
- `ReservationId`: `int`
- `EnterFacingPolicy`: `OrientationPolicy`
- `ResidentFacingPolicy`: `OrientationPolicy`
- `ExitPolicyId`: `int`

职责：

- 说明“这个 child 为什么挂在这个 host 上”。
- 说明它占用的是宿主哪个 gameplay slot / anchor。
- 说明驻留时朝向如何计算。

#### B. 宿主侧：槽位定义

建议新增：

`AttachmentSlotBuffer`

每个 slot 项至少包含：

- `SlotId`
- `SemanticMask`
- `AnchorId`
- `Capacity`
- `OccupancyCount`
- `ReservationCount`
- `FacingPolicy`
- `LocalOffsetCm`
- `LocalAngleRad`
- `TopologyRefType`
- `TopologyRefId`

职责：

- 描述宿主有哪些可挂接槽位。
- 区分 seat、ladder rung、wall segment slot、gate capture slot、trench trap slot。
- 为挂接后的定位与朝向提供宿主局部定义。

#### C. 宿主侧：占位状态

建议新增：

`AttachmentOccupancyBuffer`

每项记录：

- `SlotId`
- `Occupant`
- `ReservationId`
- `SinceTick`
- `Flags`

职责：

- 提供 slot 层面的已占用状态，而不是只看 `ChildrenBuffer.Count`。
- 支持等待、预约、释放、超时。

#### D. 通用枚举

建议新增：

`AttachmentSemantic`

建议至少覆盖：

- `Garrison`
- `BuilderAttach`
- `MorphAttach`
- `Docked`
- `Carried`
- `WallOccupant`
- `LadderOccupant`
- `TunnelOccupant`
- `TrenchCaptured`
- `SiegeDeviceCrew`

`OrientationPolicy`

建议至少覆盖：

- `PreserveCurrent`
- `MatchHost`
- `MatchSlot`
- `FaceHostCenter`
- `FaceTopologyForward`
- `FaceTopologyInward`
- `FaceTopologyOutward`
- `FaceTargetEntity`
- `FaceTargetPoint`

### 5.3 topology 模型

attachment 不能替代 topology。

建议新增独立要塞 topology 域：

`FortificationTopologyNode`

- `NodeId`
- `Kind`: wall segment / gate inner / gate outer / ladder dock / tunnel entrance / trench lane
- `OwnerEntity`
- `WorldPositionCm`
- `ForwardAngleRad`
- `Flags`

`FortificationTopologyEdge`

- `FromNodeId`
- `ToNodeId`
- `EdgeKind`: wall adjacency / gate pass / tunnel link / ladder bridge / trench crossing
- `Cost`
- `Flags`

`FortificationFaceDescriptor`

- `OwnerEntity`
- `FaceId`
- `FaceKind`: inner / outer / left / right / topological forward
- `NormalAngleRad`
- `EntryPolicy`

职责：

- 表示墙段相邻关系、门的内外侧、地道网络、梯具桥接关系。
- 为 pathing 验证、入口查找、朝向策略提供统一源数据。

### 5.4 traversal / capture / transport 运行时

以下过程不应内嵌进 attachment 主数据，应有独立 runtime state：

- `ClimbTraversalState`
- `LadderAttachState`
- `GateCaptureState`
- `TunnelTransitState`
- `TrenchFillState`

这些 runtime state 负责：

- 当前 phase
- 进度
- source / target topology ref
- reservation id
- cancel / fail reason

### 5.5 编辑与存档 DTO

建议新增中立 authoring DTO，而不是让 Base44 JSON 直接等于 runtime state。

建议：

`SiegeAuthoringMapDto`

- `FortificationNodes`
- `FortificationEdges`
- `Buildings`
- `Units`
- `TerrainZones`
- `AttachmentSlotAuthoring`
- `Metadata`

工作流：

- 外部 Base44 / Web editor 只读写 DTO。
- Ludots 导入器把 DTO 转为 map config + topology config。
- runtime occupancy / reservation / transient states 不进入静态地图存档。

## 6. relation / GAS / navigation / orientation 的职责边界

| 领域 | 应负责 | 不应负责 |
|---|---|---|
| relation | parent-child 主边、host-child 生命周期 | 槽位语义、容量、门内外侧、墙邻接 |
| attachment/occupancy | 槽位、占位、预约、挂接语义 | 图连通性、技能成本 |
| topology/navigation | 可达性、内外侧、邻接、网络路径 | 驻军状态窗口、成本、临时锁定 |
| GAS | 授权、成本、tag window、触发 relation/occupancy mutation | topology 主表示、slot ownership 主表示 |
| orientation | 进入朝向、驻留朝向、退出朝向、面位法向 | 决定谁可以进入哪个 slot |
| presentation | 读 runtime state 做显示与调试 | 作为 gameplay anchor 真值 |

边界原则：

- “能不能进入”先由 topology + occupancy 校验。
- “进入后给什么状态、花多少资源、播放多长窗口”由 GAS 决定。
- “进入后具体挂在哪、面向哪里”由 attachment + orientation 决定。

## 7. 面向机制的落地映射表

| 机制 | 现有可复用 | 必增共享基建 | 建议落点 |
|---|---|---|---|
| 驻军运行时 | `ChildOf`、`RelationOps`、GAS relation effect | `AttachmentBinding`、slot/capacity/reservation | Core shared infra + reusable capability mod |
| 城墙占位 / 墙段归属 | navigation graph、relation spine | fortification topology、wall slot occupancy | Core shared infra + fortification capability mod |
| 爬墙流程 | GAS state windows、pathing | climb traversal state、outer/inner face topology、slot reservation | 攻城专用 mod，依赖 shared infra |
| 云梯吸附 / 载员 / 投送 | garrison spine、orientation base | ladder dock slot、ladder carrier occupancy、deploy policy | reusable transport/attachment capability + siege mod |
| 城门内外侧与夺门 | navigation、GAS channel | gate side topology、capture slot、inside reach validation | fortification capability mod + siege mod |
| 地道网络与地下运输 | graph/pathing、relation spine | tunnel topology、transit runtime、entry/exit occupancy | fortification capability mod |
| 壕沟阻断 / 捕获 / 填沟 | occupancy concept、GAS transitions | trench lane topology、trap/fill slot、crossing policy | fortification capability mod + siege mod |
| 攻城器械交互规则 | GAS、selection、entity command panel | crew semantic、dock semantic、facing policy、deployment rule | siege mod |
| 地图编辑器与 Base44 存档 | UI runtime、selection、map config pipeline | neutral DTO、editor adapter、import/export bridge | editor/debug layer |
| AI Dashboard 调试侧栏 | panel/runtime UI infra、presentation ids | runtime debug snapshot contract | debug capability mod |
| 朝向 / rotation / target facing | `FacingDirection`、`Rotation2D`、spawn facing | orientation policy、slot-local facing、target facing resolver | Core shared infra |

## 8. 推荐的 mod / core 拆分

### 8.1 Core shared infra

进入 Core 的仅限真正跨题材复用的能力：

- `AttachmentBinding`
- `AttachmentSlotBuffer`
- `AttachmentOccupancyBuffer`
- `AttachmentSemantic`
- `OrientationPolicy`
- orientation resolver utilities
- reservation lifecycle helper
- topology-neutral attachment validation hook

### 8.2 可复用 capability mod

建议新增能力层：

`FortificationTopologyMod`

- 墙段、门侧、地道、壕沟、梯具 dock 节点与边
- fortification topology 查询服务

`AttachmentRuntimeMod`

- slot 校验、预约、占位同步、detach 策略
- inspector/debug 输出

`SiegeAuthoringBridgeMod`

- editor DTO
- Base44 / Web editor 适配
- Ludots map import/export

`RuntimeInspectorSiegeMod`

- occupancy、reservation、topology、orientation 调试卡片
- 基于中立 inspection-provider 契约聚合 attachment / topology / GAS / planner 的只读快照
- 为 AI Dashboard 与开发者侧栏提供统一只读数据

### 8.3 攻城专用 mod

保留在攻城 Mod 的只有题材规则：

- 哪些单位可以爬墙
- 哪些器械能吸附什么墙体
- 夺门时长与失败规则
- 云梯投送节奏
- 壕沟填充资源成本
- 攻城器械专属命令面板与表现

结论：

- “驻军 / 锚点 / 挂接 / 朝向 / 拓扑引用”应抽成共享基建。
- “云梯四秒吸附还是三秒吸附”这类规则不应进 Core。

## 9. 实施顺序

### Phase 1：最小共享闭包

目标：

- 把 PR86 的 mod-local relation runtime 提升为可复用 attachment runtime。

交付：

- `AttachmentBinding`
- `AttachmentSlotBuffer`
- `AttachmentOccupancyBuffer`
- `AttachmentSemantic`
- `OrientationPolicy`
- attachment validation / reservation helper

结果：

- 可先覆盖 builder attach、garrison、ungarrison、carrier load/unload。

### Phase 2：要塞 topology 能力层

目标：

- 让墙、门、地道、壕沟从“脚本约定”升级成结构化 topology。

交付：

- `FortificationTopologyNode/Edge`
- face descriptor
- gate side / wall adjacency / tunnel link 查询服务
- editor DTO 与导入桥

结果：

- 可正式支撑 wall climb、gate capture、tunnel transit、trench trap/fill。

### Phase 3：攻城专用玩法 Mod

目标：

- 在共享基建之上实现攻城题材规则与 UI。

交付：

- `SiegeAssaultMod`
- 攻城器械命令
- 攻城 UI / debug panel
- AI strategy adapter

结果：

- 攻城玩法成为正式 Mod，而不是原型拼装。

## 10. 关键风险与反模式

### 10.1 关键风险

- 把 slot identity 放进 tag，会导致 occupancy 无法审计。
- 把 gate side / wall face 放进 ability 配置，会让 topology 与技能脚本耦死。
- 继续让 relation runtime 只活在攻城 Mod 内，会把共享问题固化成题材特判。
- 用 presentation anchor 作为 gameplay anchor，会把视觉资源命名污染模拟层。
- 把 Base44 JSON 直接视为 runtime state，会让 transient 状态污染地图资产。

### 10.2 明确反模式

- 反模式一：`Tag = 当前占据哪个墙槽`
- 反模式二：`PresentationStableId = gameplay anchor id`
- 反模式三：`wall climb / gate capture / tunnel transit` 全部写成单个 GAS effect 链
- 反模式四：为每种载具、建筑、城防各自发明一套 attachment 结构
- 反模式五：忽略现有 `FacingDirection` / `Rotation2D`，再新造一套朝向字段

## 结论

这套攻城原型可以在 Ludots 现有基础上覆盖，但前提不是“继续往 PR86 的 mod-local relation runtime 上补特判”，而是先补齐共享 attachment / occupancy / topology / orientation 基建。

最小必须新增的基建是：

- 关系语义层
- 槽位/锚点/容量/预约层
- orientation policy 层
- fortification topology 层
- editor DTO、调试快照契约与 inspection-provider 接口

明确不应进入 GAS / tag rule 的内容是：

- 墙段邻接与归属拓扑
- 门的内外侧结构
- 地道网络连通性
- 壕沟几何阻断与捕获槽位
- 宿主 slot 的真实占位状态

GAS 在这套设计里的正确位置，是“围绕结构化状态执行授权、成本、计时、授予与剥离状态”，而不是成为 topology 与 occupancy 的主存储。只有先完成这一步，Ludots 才能把 React 原型翻译为正式攻城 Mod，而不是把原型脚本逻辑换个语言重写一遍。
