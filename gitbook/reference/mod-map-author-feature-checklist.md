# Ludots 能力总览：Mod 作者 / 地图作者版（说人话 + 自检清单）

> 面向对象：想做新玩法、新单位、新技能、新地图、新 UI 的人。
> 本页只讲"你能做什么、文件放哪、怎么开始、错了会怎样"，不深入引擎内部。
> 每个主题都附一个可以直接抄的 showcase 例子和一条"跑起来"命令。

---

## 1. 一分钟总览（先看这段）

Ludots 是一个"一切皆 Mod"的游戏框架：**连内置玩法都是 Mod**。你写的东西 = 一堆 JSON 配置文件（数据/规则）+ 可选的一点点 C#（新逻辑）。

核心思想只有三句话：

1. **数据说话**：技能、单位、AI、地图、表现全是 JSON。改数据就能出新玩法，不用碰引擎。
2. **组合不重造**：引擎提供"原子积木"（读属性、查目标、放效果、生成实体、移动、播放表现……），你在图（Graph）里把它们连起来。新变体 = 新连线，不是改引擎。
3. **错了就当场报错**：配置写错、文件缺、名字对不上，启动就失败并告诉你哪里错——**绝不静默兜底、绝不留一个"假装正常"的半成品**。这既是纪律也是保护：你永远不会上线一个自己没注意到的坏配置。

你能做的八类事（后面每节展开）：

| # | 你能做什么 | 一句话 |
|---|---|---|
| 1 | 做技能和效果 | 用"图"组合原子操作：扣血、加状态、放弹道、触发事件，全部 JSON |
| 2 | 做 AI | 效用打分 AI + 行为树 + 状态机，AI 只"想"不下手，动手全走正式命令 |
| 3 | 做移动与寻路 | 集群方阵移动、寻路、障碍物、道路/运输网络 |
| 4 | 做表现 | 实体长什么样、血条面板、小地图、粒子、动画，纯 JSON |
| 5 | 做地图与关卡 | 摆单位、设阵营/玩家、写关卡触发器（进圈开袭、清场过波） |
| 6 | 做输入与命令 | 右键点哪、选中谁，路由到不同命令（移动/建造/集结） |
| 7 | 做随机 | 确定性掉率表、抽卡，同种子必同结果，可回放可存档 |
| 8 | 做存档 | 一整套存/读档基础设施，你的新组件写对了就自动进存档 |

---

## 2. 基础概念（快速对齐说法）

- **Mod**：一个目录 + `mod.json`。可以是纯资源（只放 JSON/模型/贴图），也可以带 C# 程序集。
- **VFS（虚拟文件系统）**：Mod 里的文件用 `ModId:路径/文件名` 访问，比如 `MyMod:assets/Entities/templates.json`。所有 Mod 文件互相可见，互不干扰。
- **配置目录（config_catalog.json）**：引擎有一个"配置文件总账"。一个正式配置文件在总账里登记"合并策略、是否允许空、分片目录"；Mod 可以**分片**——往一个既有配置文件里追加自己的条目，而不覆盖别人的。
- **图（Graph）**：Ludots 的"脚本"。由节点（原子操作）和连线组成，写 JSON。分几类：`Script`（普通流程）、`Effect`（技能阶段）、`Query`（查目标）、`Validation`（校验）、`Score`（AI 打分）、`TriggerGraph`（地图触发器）、行为树 / 状态机（粗行为）。
- **实体（Entity）**：游戏里任何"东西"（单位、建筑、甚至纯逻辑的"阵营代表"）。实体 = 组件集合，由模板（templates.json）定义，地图里摆实例。
- **EffectTemplate / 技能**：具体技能内容（多少伤害、持续多久、带什么标签、走哪个图）。Effect 有生命周期阶段：`OnPropose(提议) → OnCalculate(计算) → OnResolve(判定) → OnHit(命中) → OnApply(生效) → OnPeriod(周期) → OnExpire(结束) → OnRemove(移除)`，每阶段还能分 `Pre / Main / Post` 三个步骤，图可以替换其中任何一步。

---

## 3. 能力地图（按主题）

### 3.1 做技能和效果（GAS + 图）

**你能做的**：任意技能组合——伤害、治疗、Buff/Debuff、范围 AOE、弹道、召唤、变形/吞噬（DeployConsumeSource）、反击/吸血（监听器）、技能冷却/共用冷却（GCD）、条件释放（射程/落点校验）。

**怎么写**：

- 文件都在 Mod 的 `assets/GAS/` 下：
  - `effects.json` —— 技能效果实例（数值、时长、标签、阶段图引用）
  - `graphs.json` —— 图定义（节点连线）
  - `abilities.json` —— 技能（槽位、前置条件、能力定义）
  - `preset_types.json` —— 预设类型（常用写法的简写，可被模板替换）
  - `func_lib.json` —— 函数库（可复用的子流程）
  - `action_lib.json` —— 动作库（行为树叶子）
- 图节点全集见 `gitbook/reference/graph-node-op-wiki/`（100+ 个节点，每个都有单页讲解）。

**不变量（硬规则）**：

- 新技能变体 = 新图连线/新 effect 步骤；**不新增引擎枚举、不新增预设开关**。
- 纯计算阶段（提议/计算）不能偷偷发事件、改属性。
- 技能事务要么全成要么全不成，失败不留半次结算。

**示例**：`mods/showcases/capability_standard/` 下有 130+ 个"单个图节点"展示 + 技能沙盘（`CapabilityStandardAbilityGraphSandboxMod`）。

**跑起来**：`.\scripts\run-mod-launcher.cmd cli launch preset:Capability Standard Ability Graph Sandbox Raylib --adapter raylib`

### 3.2 做 AI

**你能做的**：给单位配 AI——效用打分（Utility AI，多目标里"此刻哪个最有价值"）、行为树、分层状态机（HFSM）、战斗姿态（待机/反击/防御/主动攻击）。

**怎么写**：

- `assets/AI/profiles.json` —— AI 档案（默认姿态等）
- `assets/AI/behavior_trees.json`、`assets/AI/hfsm.json` —— 行为树/状态机（L2 行为调度）
- `assets/AI/actuators.json`、`assets/AI/inputs.json` —— 执行器与感知输入
- 阵营/姿态等业务行为包：`mods/CombatStanceBehaviorMod`（自带 attackMove / guard / patrol / setCombatStance / scatter 等命令）

**不变量（硬规则）**：

- AI 只输出"命令意图"，**不直接扣血、不放效果、不改世界**——动手全走正式命令管线。
- 普通攻击 = 一种带自动施放策略的技能，不是特殊系统。
- 配置引用必须存在：未知 AI 档案/姿态/执行器，加载期直接报错。

**示例**：`mods/showcases/utility_autocast/`（Utility AI 自动施放）、`mods/showcases/combat_stance/`、`CapabilityStandardBehaviorTreeArenaMod` / `CapabilityStandardHfsmSentryArenaMod`。

### 3.3 做移动与寻路

**你能做的**：单位寻路（NavMesh）、集群方阵移动（一个"锚点"带一队兵整体移动、避障后恢复队形）、静态/动态障碍物（圆/盒/多边形）、道路与运输网络（道路/铁路/水路的拓扑与寻路）。

**怎么写**：

- `assets/MassNavigationConfig.json` —— 集群移动求解器、Agent 档案（heavy 重装 / light 轻装）、容量
- 障碍物是**实体组件**（不是单独配置文件）：`ManifestationObstacleIntent2D`（圆/盒/多边形）+ `CompoundObstacle2D`（多件组合）；`sinkNavigationObstacle: true` 参与寻路烘焙，`sinkPhysicsCollider: true` 参与物理碰撞
- `assets/TransportNetwork/transport_network.json` —— 道路/铁路/水路的唯一来源：一份文件同时产出寻路图和视觉缎带（路面），**不允许再写第二份 .graph 源文件**

**不变量（硬规则）**：

- 容量必须显式配置，容量不足就明确失败，**不扩容、不丢兵**。
- 所有障碍/容量/尺度数字必须写对；大小写敏感，无别名。

**示例**：`mods/showcases/formation_capability/FormationCapabilityShowcaseMod`（方阵移动）、`mods/showcases/road_network/`、`mods/showcases/static_obstacle_physics/`。

**跑起来**：`.\scripts\run-mod-launcher.cmd cli launch mass_navigation --adapter raylib`

### 3.4 做表现（Presenter / 面板 / 小地图 / 粒子）

**你能做的**：让实体"看得见"——模型、颜色、动画、血条面板、小地图圆点、粒子特效、贴花；全部**纯 JSON，零 C#**。

**怎么写**：

- `assets/Presentation/presenters.json` —— 表现定义（长什么样）+ 出生规则（什么事件出现/消失）
- `assets/Entities/templates.json` —— 实体模板（表现监听的是实体出生事件，模板 id 就是事件 key）
- `assets/Presentation/mesh_assets.json` + `host_assets.json` —— 你自己的模型/贴图注册（用 `MyMod:assets/...` 虚拟路径）
- `assets/Presentation/particle_vfx.json` —— Quarks 粒子（Once/Loop 发射，Billboard/拉伸/图元/尾迹四种渲染）
- `assets/Panels/panel_templates.json` —— 面板模板（血条、资源栏、科技树……数据来自图输出）
- `game.json` 里 `panelSkin` / `panelTheme` —— 一键换皮换主题（皮：default/markup/compose/reactive/web；主题：水墨/fantasy/极简）

**内置免费资产**：LudotsCoreMod 自带 `cube` / `sphere` 图元和 `default_surface` 材质，最小演示一个模型文件都不用带。

**不变量（硬规则）**：

- 配置错不静默：字段拼错、材质不存在、车道不匹配，装载即抛错。
- 小地图只认 `MinimapMarker` 行为显式声明；"想让它显示就声明，不想就删声明"，没有偷偷推断。
- 渲染车道要配对：静态模型走 `InstancedStaticMesh`，带骨骼动画的走 `GpuSkinnedInstance`。

**示例**：`mods/showcases/raylib_client_parity/RaylibClientParityShowcaseMod`（红方块最小例）、`mods/showcases/presenter_blacksmith/`（大世界表现）、`mods/showcases/panel_fireball_shared/`（10 分钟血条）、`mods/showcases/vfx_forge_raylib/`（9 种粒子）。

**跑起来**：`.\scripts\run-mod-launcher.cmd cli launch preset:Raylib Client Parity --adapter raylib`

### 3.5 做地图与关卡

**你能做的**：摆单位、定阵营/玩家/外交关系、写关卡触发器（进圈开袭、清场过波、Boss 死亡翻阶段）、设置进图座位（本机操控谁）、地图继承。

**怎么写**：Mod 的 `assets/Maps/<地图名>.json`：

- `Entities[]` —— 摆实体（`Template` 引用模板 id，`Overrides` 改位置/属性）
- `Teams[]` / `Players[]` / `ParticipantRelationships[]` —— 阵营/玩家/关系，各自绑定一个**代表实体**（`RepresentativeInstanceId`）
- `MapTriggerGraphs[]` —— 挂关卡触发器图（TriggerGraph），图在"思考波"上逐拍续跑
- 进图座位：`MapLaunchContext.LocalSeats[]`（启动时经 `GameConfig.startupLocalSeats` 或命令行）

**不变量（硬规则）**：

- 玩家/阵营真相必须挂在地图实体上（可以是纯逻辑实体，不带坐标），**没有第二套容器**。
- `InstanceId` 写了就必须非空、唯一；代表实体必须能解析，否则加载报错。
- 世界单位：厘米。默认 1 格 = 100cm，hex 边长 400cm，256 格一个宏块；写地图尺寸时用宏块数。

**示例**：`mods/showcases/map_trigger_night_raid/`（夜袭三波关卡流）、`mods/showcases/rts_training_*`、`mods/showcases/ownership_cascade/`。

### 3.6 做输入与命令

**你能做的**：定义"玩家点什么 → 发生什么"——右键移动、右键集结、按技能槽训练单位、不同单位右键同一种操作但行为不同（农民建造 vs 士兵移动）。

**怎么写**：

- `assets/Input/input_order_mappings.json` —— 输入动作到命令的映射；`actorOrderRouting` 按选中单位把同一个输入路由到不同命令
- `assets/Input/command_intent_profiles.json` —— 命令意图路由
- `assets/GAS/order_types.json` —— 注册命令类型；`instantComplete: true` + `persistentStoredTarget` 做"集结地/生产目标"这类即时命令（把点/实体存进黑名单，之后让新单位找它）

**不变量（硬规则）**：

- 路由不匹配就跳过，**不注入默认命令**；核心不硬编码任何业务命令名。

**示例**：`mods/showcases/interaction/`、`mods/showcases/superweapon_context/`、`mods/RtsDemoMod`。

### 3.7 做随机（确定性 RNG）

**你能做的**：掉率表、抽卡、随机事件——**同种子必同结果**，可回放、可存档、可调试。

**怎么写**：`assets/Rng/distributions.json` —— 分布表（条目、权重、启用/锁定、调制）；图里用 `WeightedPick` 节点按分布名抽取。

**不变量（硬规则）**：未声明流、未知分布、负权重——全报错；没有隐式回退。

**示例**：`mods/showcases/rng/RngShowcaseMod`（自动抽取 + 旋钮调权重 + 一键重放证明）。

**跑起来**：`.\scripts\run-mod-launcher.cmd cli launch --selector rng_showcase --mod AgentBridgeMod`

### 3.8 做存档

**你能做的**：存/读档，`saves/{manual|autosave}/{名字}.ldsave` 单文件容器，含完整性校验（改坏了就拒读，不迁移不兜底）。

**怎么写**（对作者几乎透明）：

- 你的 ECS 组件**只要是不含托管引用的值类型，自动进存档**，什么都不用写。
- 含字符串/数组等引用的组件：手写一个 formatter 并注册（有模板可抄）。
- 非实体状态（计时器、会话等）：实现 `ISaveParticipant` 挂进现有 domain，不新建平行存档。

**不变量（硬规则）**：读档先过三道闸门（schema 版本 / Mod 集合哈希 / 注册表指纹），不匹配就拒读。

### 3.9 多人 / 回放（概要）

- 确定性模拟 + 确定性随机是多人/回放的地基：同样输入序列必产出同样世界。
- 现有验收展示：`mods/showcases/persistence_online_replay/`（存档、回放、联机追回）。
- 网络/运输拓扑：`mods/showcases/rts_multiplayer_frontline/`、`mods/showcases/diplomacy_trade_gate/`、`mods/showcases/gold_market/`。

### 3.10 调试与验证（你最好的朋友）

- **AgentBridge**：运行中的游戏开一个本机调试口（HTTP JSON-RPC `127.0.0.1:47921`），可以查实体/日志/触发事件/模拟输入/截图——验收和取证都靠它。
- **GM 控制台 / 诊断覆盖层 / AI Inspector**：游戏内直接看状态。
- **可视化编辑器**（React Web）：地图编辑与调试。
- **验收证据**：每个能力都有自动验收（截图 + 统计），门户站聚合展示。

---

## 4. 自检清单（Checklist）

### 4.1 通用铁律（每个 Mod 开工前过一遍）

- [ ] 先搜仓库有没有现成能力，能复用就不重造（配置管线、图、Registry、管线都是共享基建）
- [ ] 新玩法变体 = 数据组合，不新增引擎枚举/开关
- [ ] 两个以上 Mod 都要用的逻辑，放 Core 或公共基础设施；只服务本 Mod 的放本 Mod；完整独立功能拆独立 Mod
- [ ] 配置文件名/字段名与总账 `config_catalog.json` 登记一致
- [ ] 图名、模板 id、事件 key、分布名、AI 档案名……所有名字大小写一致、可解析
- [ ] 未知字段/未知名字/缺文件 → 应报错，不靠"默认值"混过去
- [ ] 写注释只在命名表达不了意图时写；代码里不写 issue 号

### 4.2 新建一个 Mod

- [ ] 有 `mod.json`（id、依赖、入口）
- [ ] 资源走 `ModId:路径` 虚拟路径
- [ ] 带 C# 时实现 `IMod.OnLoad(IModContext)`，所有注册（效果处理器/图操作/表现命令/表现行为）只在 OnLoad 里做
- [ ] 扩展 key 用 `MyMod.名字` 命名空间（只归你所有，别人可引用不可注册）
- [ ] 纯资源 Mod 可以没有程序集

### 4.3 技能 / 效果

- [ ] 数值、时长、标签、阶段图都在 `effects.json` / `graphs.json` 里，不写死在代码
- [ ] 新行为要么用现有图节点连，要么用 `RegisterBuiltinHandler` / `RegisterGraphOp` 注册代码处理器
- [ ] 模板的 `phaseGraphs.<阶段>.main` 存在时，它替换预设默认；`main` 与 `skipMain` 不同时出现
- [ ] 提议/计算阶段只算不写（不发事件、不改属性）
- [ ] 监听器（反击/吸血等）只走正式副作用事务，不提前发事件
- [ ] 固定容量（目标数/事件数）配置足够，溢出必须显式失败

### 4.4 AI

- [ ] AI 只产出命令意图，不直接改世界（不扣血、不放效果、不写锁定）
- [ ] 命令引用用 `OrderTypeKey` 字符串（或确认注册过的 `OrderTypeId`），不写 `OrderTagId`
- [ ] profile / stance / bucket / actuator 全部写字符串 key，不写数字字段
- [ ] 普通攻击用"带自动施放策略的技能"表达，不建特殊系统
- [ ] 共享技能冷却用 GAS 的共享锁定标签，不在 AI 里做

### 4.5 移动 / 寻路

- [ ] Agent 档案（heavy/light 等）显式配置
- [ ] 容量（成员数/总展开数/路由状态）显式配置且够用
- [ ] 障碍物用实体组件（`ManifestationObstacleIntent2D` / `CompoundObstacle2D`），不用废弃的 sidecar 配置文件
- [ ] 每个下沉到导航的障碍件 `navRadiusCm > 0`
- [ ] 道路/运输网络只写 `transport_network.json` 一份源文件
- [ ] 尺度和分辨率引用统一常量，不内联 256/64/100 魔数

### 4.6 表现 / UI

- [ ] Presenter 定义 + 出生规则齐备；事件 key 与模板 id 完全一致
- [ ] 渲染车道与资产类型匹配（静态=InstancedStaticMesh，骨骼动画=GpuSkinnedInstance）
- [ ] 模型/贴图在 `mesh_assets.json` + `host_assets.json` 注册（`ModName:assets/...` URI）
- [ ] 小地图：需要显示的实体显式声明 `MinimapMarker` 行为
- [ ] 面板：pins 的 key 与图 outputs 的 key 一致；模板 `graph` 字段与图 id 一致
- [ ] 粒子：版本 `quarks.ludots.v1`；贴图（flipbook）真实落盘，缺图会报错

### 4.7 地图

- [ ] 摆的实体都引用已存在的模板 id
- [ ] 阵营/玩家绑定真实存在的代表实体（`InstanceId` 唯一、已解析）
- [ ] player 引用了所属 team；本地座位引用已绑定的 player
- [ ] 关系表（team-team / player-player / player-team）类型有效
- [ ] 关卡触发器图挂在正确的作用域（地图 / 实体模板 / 技能 / Mod）
- [ ] 世界尺寸用宏块数 × 每格 cm 心算对得上

### 4.8 存档

- [ ] 新组件：不含托管引用 → 自动进存档（补 round-trip 测试）
- [ ] 新组件：含字符串/数组 → 手写 formatter 并注册（缺 formatter 会 fail-fast，不能回退）
- [ ] 含实体引用的组件 → 登记引用有效性校验 + WorldId 归一化
- [ ] 非实体状态 → 实现 `ISaveParticipant` 挂进唯一 domain
- [ ] 不该存档的瞬时实体 → 挂 `SaveExcludedTag`

### 4.9 上线 / 交付前

- [ ] 用 `.\scripts\run-mod-launcher.cmd` 启动你的 preset，肉眼验收一遍
- [ ] 配置错误路径验证过：故意写错一个名字，确认启动报错而不是静默
- [ ] 用 AgentBridge 取证（日志 / 实体状态 / 截图），证据落盘
- [ ] 改了行为树/状态机/技能 → 确认无效引用会拦截（不能进对局才发现）
- [ ] 提交信息描述变更本身；代码/文档里不写 issue 号、修复历史

---

## 5. 怎么跑起来（命令）

```powershell
# 列 preset
.\scripts\run-mod-launcher.cmd cli list

# 跑一个 preset（推荐入口）
.\scripts\run-mod-launcher.cmd cli launch preset:<Preset 名称> --adapter raylib

# 按 selector 跑（需要 AgentBridge 调试时）
.\scripts\run-mod-launcher.cmd cli launch --selector <selector> --mod AgentBridgeMod
```

预设名用引号包住含空格的名称，例如：
`.\scripts\run-mod-launcher.cmd cli launch "preset:Capability Standard Ability Graph Sandbox Raylib" --adapter raylib`

常用示例：

- 方阵移动：`cli launch mass_navigation --adapter raylib`
- 夜袭三波（地图触发器图）：`cli launch preset:Map Trigger Night Raid Raylib --adapter raylib`
- 粒子：`cli launch preset:Raylib VFX Forge --adapter raylib`
- 面板/血条：`cli launch preset:Panel Fireball Shared Raylib --adapter raylib`

---

## 6. 详细文档索引（想深挖时按这里走）

- 快速开始：`gitbook/quick-start.md`
- Mod 架构：`gitbook/architecture/mod-architecture.md`
- 运行时扩展点（分片/处理器/图操作/表现扩展）：`gitbook/architecture/mod-extensible-runtime.md`
- GAS 分层：`gitbook/architecture/gas-layered-architecture.md`
- 图分层（Flow/Script/行为调度）：`gitbook/architecture/graph-layering-flow-and-behavior.md`
- 图节点全集：`gitbook/reference/graph-node-op-wiki/`
- 集群移动上手书：`gitbook/reference/mass-navigation-user-book.md`
- 障碍物写作：`gitbook/reference/obstacle-authoring.md`
- 运输网络：`gitbook/architecture/transport-network-ssot.md`
- 表现 10 分钟上手：`gitbook/architecture/presenter-quickstart.md`
- 面板 10 分钟上手：`gitbook/architecture/panel-quickstart.md`（35 个现成面板设计：`panel-case-designs.md`）
- 粒子：`gitbook/architecture/quarks-particle-schema.md`
- 小地图：`gitbook/architecture/core-minimap-authoring.md`
- 地图参与方契约：`gitbook/architecture/map-owned-participant-contract.md`
- 输入与命令：`gitbook/architecture/input-order-and-spawn-target.md`
- 确定性随机：`gitbook/architecture/deterministic-rng.md`
- 时间体系：`gitbook/architecture/time-system.md`
- 存档：`gitbook/architecture/save-system.md`
- 实体生命周期原子操作：`gitbook/architecture/entity-lifecycle-atomic-ops.md`
- AI：`gitbook/architecture/ai-utility-autocast-contract.md`
- 空间尺度：`gitbook/architecture/spatial-scale-and-resolution-ssot.md`（短表：`gitbook/reference/spatial-scale-configuration.md`）
- 渲染能力总览：`gitbook/architecture/raylib-engine-capabilities.md`
- 全部可玩 showcase 与验收：门户站 `https://mightybubble.github.io/Ludots/` + 仓库根 `showcase.registry.json`
