# 10K MassNavigation showcase：生产级配置审计（"开箱即用"标准）

审计对象：`mods/showcases/capability_standard/CapabilityStandardMassNavigationLargeWorld10kMod`

验收标准（用户口径，对齐 SC2 编辑器）：**放一个陆战队就有物理/碰撞/寻路/队伍/队色/血条/动画状态机；
放一个建筑物就影响寻路；出生点即位置。全部是数据。若还需要 C#，就是有缺陷。**

## 1 现状：**不合格。396 行 C# 在承担本该是数据的东西**

| 文件 | 行数 | 性质 |
|---|---|---|
| `...ModEntry.cs` | 88 | 注册两个 system + 启动时改 minimap 运行时状态 |
| `Systems/MassNavigationObserverVisibilityBindingSystem.cs` | 209 | **每帧**为 10006 个 agent 写 knowledge 记录 |
| `Systems/MassNavigationLargeWorldLocalOrderSourceSystem.cs` | 80 | 建 input mapping、绑 seat actor |
| `CapabilityStandardMassNavigationLargeWorld10kMapFocus.cs` | 19 | 判断"当前地图是不是启动图" |
| **合计** | **396** | |

而 `assets/Maps/mass_navigation.json` 只有 **10 行**：一个相机 + `Id`。**零实体、零队伍、零出生点。**
10K 单位全部来自 `MassNavigationConfig.json` 的 `scenario` 块，由 core 的
`MassNavigationScenarioBootstrap` 程序化 spawn（`QuadrantSpread`，seed 12648430）。

### 逐项违反你的标准

1. **出生点不是数据**：`scenario.spawnLayout` 是**程序化布局算法**（按队切象限 + 网格铺 + 随机 seed），
   不是"我写在哪里就生在哪里"。改一个单位位置要改算法，不是改配置。
2. **队伍/队色不是地图数据**：队伍在 `MassNavigationConfig.json` 的 `presentation.teams`
   与 `scenario.teams`，不在 map instance 的 `Teams`/`Players`（那两个字段**当前完全没用**）。
3. **"放建筑物影响寻路"没有数据通路**：地图里没有障碍实体；核心有
   `MassNavigationBlockerProfile` / `ManifestationObstacleIntent2D` 通路，但本 mod 零声明。
4. **重复实现**：`MassNavigationObserverVisibilityBindingSystem`（209 行）做的
   "按绑定把可见实体写进 `EntityCollectionStore` + `KnowledgeProjectionStore`"，
   **core 里已经有 `DynamicParticipantVisibilityPublisher`** 在做同一件事，
   且它是**声明式绑定**（`DynamicParticipantVisibilityBinding`：viewer/source/query/collection/
   presence/position/mask 全是参数）。本 mod 不是复用，是第三份实现
   （另两份在 `CapabilityStandardParticipantViewsMod`、`RtsMultiplayerFrontlineMod`）。
   → 违反 `AGENTS.md` §3「防重复造轮子条款」。
5. **mod entry 在运行时改表现层状态**：`runtime.Visible = true; SetRotateWithCamera(false);
   UseRtsFullMapPreset();` —— 这些应是 game.json / minimap 配置里的字段。
6. **`MapFocus` 这类"判断是不是启动图"** 是框架关心的事，不该由 showcase 自己写。

## 2 结论：可以做到纯数据，且框架已具备全部能力

已确认可用的数据通路（**无需新基建**）：

| 需要 | 现成数据通路 |
|---|---|
| 静态出生点 | map `Entities[]` + `Template` + `Overrides.WorldPositionCm` |
| 队伍绑定 | map `Teams[]`（`TeamId` + `RepresentativeInstanceId`） |
| 玩家/座位 | map `Players[]` + game.json `startupLocalSeats` |
| 队色 | presenter `style.color`（4 队色**已存在**：blue/red/amber/emerald） |
| 血条/文本 | presenter `AssetBinding` + `WorldText`（**已存在**） |
| 动画状态机 | `animator_controllers.json` + `animation_profiles.json`（**已存在**） |
| 物理/碰撞/寻路 | `MassNavigationAgent` 组件（template 里**已存在**） |
| 障碍影响寻路 | `ManifestationObstacleIntent2D` / blocker template + map entity |
| 可见性/knowledge | `DynamicParticipantVisibilityPublisher` + 声明式 binding（core 已有） |

⇒ **不需要写任何新 C#**。需要的是：把 10K 实体**离线生成**进 map instance 的 `Entities[]`，
把队伍/座位写进 map 的 `Teams`/`Players`，把可见性改成 core publisher 的声明式 binding。

## 3 需要框架补的三个缺口（这才是"有问题"的真身）

这三条是**框架级**的，不是 showcase 能自己解决的；按 `AGENTS.md` §4.1 应先出方案：

1. **`DynamicParticipantVisibilityPublisher` 没有配置加载器**。
   它只接受构造函数传入的 `DynamicParticipantVisibilityBinding[]`，全仓三处调用都是
   **C# 里手搓 binding**（含本 mod 的重复实现）。要"开箱即用"，需要一个
   `participant_visibility.json` 之类的声明式 catalog，让地图/mod 用数据声明
   "这个 viewer 能看到哪些实体、以什么 presence/position/mask"。
2. **10K 静态实体的 authoring 方式**。map `Entities[]` 能表达静态实体，但
   10K 条手写 JSON 不现实；需要一个**离线生成工具**产出 map instance（用户已同意此路线）。
   同时要确认 map entity 走的绑定路径与 `MassNavigationScenarioBootstrap` 等价
   （agent index 分配、spatial 注册、presenter 绑定、knowledge 可见性四条都要对齐）。
3. **地图级"场景声明"缺位**。当前 10K 场景的语义（几队、每队多少、哪队什么色、出生布局）
   横跨两个 mod 的两个文件，且其中一半是算法。应有 map instance 承载。

## 4 本轮动作（数据化改造）

1. ✅ 模型换成 mannequin（红蓝人）：`mass_navigation_agent_mannequin.glb`
   （6 mesh / 9148 tri / 23 joints / 4 clips，替代 Knight 的 15 mesh / 615 骨槽）。
2. ✅ 修好 animator 状态索引（原 41/42 是按 Knight 的 80 clips 编的，mannequin 只有 4）。
3. ⏳ 离线生成 10K 静态 map instance（`Entities[]` + `Teams`/`Players`）。
4. ⏳ 用 core 的声明式可见性替换 209 行重复实现（依赖缺口 1）。
5. ⏳ 删掉 mod entry 的运行时 minimap 改动，改为配置字段。
