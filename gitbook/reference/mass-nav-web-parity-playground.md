# Mass Nav Web Parity Playground 说明

这份文档讲的是 `MassNavWebParityMod` 目前已经落地的实现。它不是未来方案图，也不是理想设计稿，而是当前仓库里真的在跑的版本。

目标只有两个：

1. 先把 external 参考项目里那种“大规模群体移动 playground”搬进 Ludots。
2. 全程走 Ludots 现有正式基建，尤其是 UI、selection、presentation 这几条主链，不再单独造一套临时外壳。

## 这套东西现在是什么

`MassNavWebParityMod` 是一个基于 Raylib runtime 的 mass navigation playground。

它现在已经接好了这些能力：

- 使用 Ludots 的 `selection` 基建做框选
- 使用 Ludots 的 `presentation` 路径画 agent primitive
- 使用 Ludots 的 `UIRoot + ReactivePage` 挂右上角调试面板
- 支持右键下达移动命令
- 支持编队模式和 `Q / E` 旋转
- 支持场景 reset、镜头 reset、agent 数量切换
- 支持运行时热调 budget / slice / physics hz / navigation hz / flow 参数
- 支持 `Arrival Fallback`：卡太久的 unit target 会超时停住，被推开后有限次重试

对应入口在这些文件：

- `mods/showcases/navigation/MassNavWebParityMod/MassNavWebParityModEntry.cs`
- `mods/showcases/navigation/MassNavWebParityMod/Runtime/MassNavPlaygroundRuntime.cs`
- `mods/showcases/navigation/MassNavWebParityMod/Systems/`
- `mods/showcases/navigation/MassNavWebParityMod/UI/`

## 总体架构怎么分

这套实现刻意分成 4 层。

### 1. Mod runtime 层

职责是“把系统装进去，把地图、镜头、panel、scenario 接起来”。

核心文件：

- `Runtime/MassNavPlaygroundRuntime.cs`

它负责：

- 在 `GameStart` 时安装 system 和 presentation system
- 在 `MapLoaded / MapResumed` 时确保 playground 状态可玩
- 在 `MapUnloaded` 时清掉自己持有的 panel
- 通过 Ludots 正式服务拿 `SelectionRuntime`、`PresentationMeshAssetRegistry`、`UIRoot`、camera request

这层不做具体模拟算法，只做装配。

### 2. 交互与编排层

职责是“把 Ludots 输入、选择、命令，翻译成 mass-nav playground 的内部操作”。

核心文件：

- `Systems/MassNavSelectionSyncSystem.cs`
- `Systems/MassNavCommandBridgeSystem.cs`
- `Systems/MassNavFormationSystem.cs`
- `Systems/MassNavPanelPresentationSystem.cs`

这层做的事：

- 把 selection 基建中的选中集合同步到 simulation runtime
- 把右键命令翻译成 team target 或 formation move
- 每帧更新 formation target
- 每帧刷新右上角 panel

这一层的重点是“桥接”，不是自己重新实现 selection / UI / input。

### 3. 运行时状态层

职责是“保存 playground 运行态和调参状态”。

核心文件：

- `Runtime/MassNavSimulationRuntime.cs`
- `Runtime/MassNavFlowTuning.cs`
- `Runtime/MassNavArrivalTuning.cs`
- `Runtime/MassNavFormationRuntime.cs`
- `Runtime/MassNavAgentState.cs`

可以把这一层理解成 playground 的内存中控台。

它保存：

- 当前 agent 数量
- 当前选中集
- 当前编队模式
- flow 调参
- arrival fallback 调参
- 本帧性能观测值

### 4. SoA 模拟层

职责是“真的去推进 agent 的位置和速度”。

核心文件：

- `Runtime/MassNavWebParitySimState.cs`

这层是整个 playground 的核心。当前实现走的是 SoA 数组，而不是把每个 agent 的临时仿真状态都塞回 ECS component 里逐个读写。

它内部维护了这些主要数组：

- position
- velocity
- team
- unit target
- selected flag
- separation hash
- hard-resolve hash
- obstacle cache
- flow field
- arrival fallback 相关状态

## 一帧是怎么跑的

如果把一帧拆成人话，流程大概是这样：

1. Ludots 输入系统先跑，selection 基建更新框选结果。
2. `MassNavSelectionSyncSystem` 把当前选中单位同步进 `MassNavSimulationRuntime`。
3. `MassNavCommandBridgeSystem` 读取右键命令。
4. 如果当前有选中单位，就下 formation move。
5. 如果当前没有选中单位，就改 team target。
6. `MassNavFormationSystem` 根据当前编队状态更新每个 unit 的局部目标。
7. `MassNavWebParitySimState.Step` 用 SoA 数组推进速度和位置。
8. 仿真结果同步回实体的 `VisualTransform` 和 `WorldPositionCm`。
9. presentation system 把 primitive 发给渲染层。
10. panel presentation system 刷新右上角面板。

## 移动算法现在怎么做

当前移动不是直接用引擎主线的 `Navigation2D` agent 求解器来跑 20k crowd，而是保持了参考项目那种“单独 crowd sim core”的路线，再通过 Ludots 正式链路接输入、UI、presentation。

具体分 4 段。

### 1. flow field

`MassNavWebParitySimState` 维护了两张 team flow field。

- team 0 一张
- team 1 一张

flow 的生成方式比较直接：

- 先按障碍物写 cost
- 然后从每个 cell 指向 team target 的方向
- 再叠一层障碍物附近的避让偏移

这部分的重点不是“高精度导航”，而是给大团运动一个稳定的大方向。

### 2. local steering

每个 agent 每帧会组合几类速度来源：

- 朝目标走的方向
- 邻居 separation
- 障碍物推开
- 临近目标时的减速

最后再做速度混合和限速。

### 3. hard resolve

light steering 不够的时候，agent 还是会挤进重叠。

所以还有一层更硬的 penetration resolve：

- 先建 hard-resolve spatial hash
- 找近邻重叠对
- 直接把两个 agent 沿法线推出去
- 如果撞到圆障碍物，再做 obstacle penetration resolve

这层很粗暴，但对 playground 很重要。没有它，群体很快就会压成一团。

### 4. arrival fallback

这是后来为了解决“到不了还一直抖”的问题加的。

当前逻辑是：

- 只对 `unit target` 这条链启用
- 如果单位有目标，但在一段时间里几乎没有实质进展，就判定它卡住
- 卡住后进入 settled 状态，当前位置视为暂时落点，停止继续死冲
- 如果之后被别人推离 settled 位置足够远，允许重试
- 重试次数有上限
- 一旦用户重新下命令，会重置这套恢复状态

它解决的不是“让所有单位都完美到达”，而是先解决“明显已经到不了，还在无穷尝试”这个体验问题。

## 编队这层怎么做

编队不直接操作 ECS world，而是先在 `MassNavFormationRuntime` 里维护 group。

一个 group 里保存：

- 成员索引
- 基础 offset
- 旋转后的 offset
- 目标点
- 当前中心
- 当前旋转角

当玩家右键下命令时：

- 如果是 `None` 或者只选中 1 个单位，就直接给 unit target
- 如果是 `Line / Square / Circle / Wedge`，就先建 group
- group 记录目标中心和每个成员的 offset
- 每帧 `UpdateTargets` 用 group 当前中心和目标中心重新计算每个单位的局部目标

`Q / E` 旋转本质上改的是 group 的旋转角，再把 offset 重新算一遍。

## 面板为什么之前老是看不到

这是这次实现里一个很低级但很真实的坑。

Ludots 当前的 `UIRoot` 只有一个 `Scene`。谁最后 `MountScene`，谁就把前一个 UI scene 顶掉。

所以问题不是“panel 代码没写”，而是“panel 虽然在跑，但 scene 被别的 UI 路径顶掉了”。

后来的处理方式是：

- 把 panel 刷新路径对齐到仓里已经可工作的 `Navigation2DPlaygroundRuntime.RefreshPanel`
- map 不匹配时主动 `ClearIfOwned`
- 刷 panel 时强制保持 `DrawSkiaUi = true`

这属于典型的 presentation 挂载问题，不是算法问题。

## 右上角面板现在能干什么

当前面板能做两类事。

### 1. 看状态

现在面板会显示：

- render fps
- render frame ms
- primitive render ms
- logic hz
- simulation budget / slice
- physics hz / max steps
- navigation hz / max steps
- flow 当前参数
- arrival fallback 当前参数
- settled 数量
- 各阶段观测耗时
- 选中数量、编队数量、镜头状态

### 2. 热调参数

现在可以直接热调：

- `SimulationBudgetMsPerFrame`
- `SimulationMaxSlicesPerLogicFrame`
- `PhysicsHz`
- `Physics MaxStepsPerFixedTick`
- `NavigationHz`
- `Navigation MaxStepsPerFixedTick`
- `Flow Enabled`
- `Flow Iterations`
- `Flow StepInterval`
- `Flow CrowdStampInterval`
- `Flow ObstacleStampInterval`
- `Arrival Enabled`
- `Arrival Timeout`
- `Arrival ProgressDistance`
- `Arrival WakePushDistance`
- `Arrival MaxRetries`

当前只有 `LogicHz` 是只读展示，因为它走的是 engine fixed tick 配置路径，现有正式基建里没有安全的运行时热改链路。

## 性能上做了哪些现实选择

当前版本为了能在大规模数量下跑起来，做了这些非常务实的选择：

- 仿真主数据使用 SoA 数组
- separation 和 hard resolve 都走 spatial hash
- 大规模时启用 candidate gating，减少 hard resolve 工作量
- 优先在数组里推进，再批量同步回 ECS
- panel 的性能采样不是每帧全量刷新，而是节流刷新
- 尽量把真正重的工作留在仿真数组里，不把热路径拆成大量零碎 ECS 查询

这套选择的核心思想是：

“让 ECS 负责系统组织、正式管线接入、外部交互；让 crowd sim 热路径留在连续数组里。”

## 这次踩过的典型坑

这里单列出来，方便以后快速避坑。

### 1. 以为 UI 没写，实际上是 scene 被顶掉

这是 presentation 链问题，不是业务逻辑问题。

### 2. 以为右键不动是导航问题，实际上是命令桥、组件同步、system 注册链某个环节没接上

这类问题必须顺着“输入 -> selection -> command bridge -> runtime target -> sim -> entity sync -> presentation”整条链排。

### 3. 只修底层避障，不修上层目标分配

如果目标点本身不合理，比如直接塞到障碍物里，底层再努力也只会抖。

### 4. 只看单个系统，不看正式基建接入

在 Ludots 里，很多问题不是“代码没有”，而是“没有走正式入口”，最后就会出现能跑一点、但和主线系统脱节。

## 当前已知限制

这部分很重要。下面这些问题在当前版本里还没有算彻底解决：

- 障碍物附近的目标点分配还不够聪明，仍然可能出现目标不理想
- 大团队共享目标时，上层离散落点分配还不够完整
- 20k 规模下性能仍然没有稳定达到理想目标
- 这套 crowd sim 仍然与引擎主线 `Navigation2D` runtime 有职责重叠，后续需要继续收敛

所以现在的状态更准确地说是：

“playground 已经可玩、可调、可测，也能稳定复现问题，但离最终主线化方案还有距离。”

## 如果以后要继续收敛，优先顺序是什么

建议按下面顺序继续做。

1. 先把目标分配层补好，别再让明显错误的 target 直接进入 sim。
2. 再把大团队 group slot 分配补强，减少共享单点目标。
3. 再决定哪些 crowd-specific 逻辑该沉到正式 navigation / physics / spatial 基建里。
4. 最后才考虑进一步的主线化整合，而不是一边修 bug 一边大拆迁。

这顺序的原因很简单：

现在很多肉眼可见的问题，本质上是“目标分配错了”，不是“resolve 力还不够大”。

## 代码索引

如果要从代码里读实现，建议按这个顺序看：

1. `mods/showcases/navigation/MassNavWebParityMod/MassNavWebParityModEntry.cs`
2. `mods/showcases/navigation/MassNavWebParityMod/Runtime/MassNavPlaygroundRuntime.cs`
3. `mods/showcases/navigation/MassNavWebParityMod/Systems/MassNavCommandBridgeSystem.cs`
4. `mods/showcases/navigation/MassNavWebParityMod/Runtime/MassNavSimulationRuntime.cs`
5. `mods/showcases/navigation/MassNavWebParityMod/Runtime/MassNavFormationRuntime.cs`
6. `mods/showcases/navigation/MassNavWebParityMod/Runtime/MassNavWebParitySimState.cs`
7. `mods/showcases/navigation/MassNavWebParityMod/UI/MassNavPlaygroundPanelController.cs`

这样读，比较容易先看清装配关系，再看具体算法。
