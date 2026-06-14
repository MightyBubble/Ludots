# MassNavigation RTS 上手书

这份文档写给第一次接触 Ludots 的 Mod 作者：你想做一个类似《全面战争》的 RTS 战场，玩家选择的是方阵，方阵有血量、能接移动命令；方阵里的士兵也是 MassNavigation agent，但士兵由方阵驱动，不能被玩家单独框选。

先把三个视角分清：

- 玩家：只玩游戏。框选方阵、右键移动、看血条、看 marker、移动相机。
- Mod 作者：改 JSON 配置和自己的业务 runtime。你配置模板、方阵、士兵、表现、地图、障碍和调参。
- 引擎开发者：维护 Core、Selection、Order、Performer、MassNavigation 基建。

玩家不需要输入 `selection.live.primary`、`SelectionSetKeys.LivePrimary`、template id、performer id 或 order blackboard key。看到文档让玩家输入这些，就是文档写错了。

## 先跑起来

当前参考战场是：

`mods/showcases/mass_navigation_total_war_entry/MassNavigationTotalWarEntryMod/`

从仓库根目录启动：

```powershell
.\scripts\run-mod-launcher.cmd cli launch MassNavigationTotalWarEntryMod --adapter raylib --build auto
```

Raylib launch graph 在：

- `src/Apps/Raylib/Ludots.App.Raylib/launcher.mass-navigation-total-war.runtime.json`
- `src/Apps/Raylib/Ludots.App.Raylib/raylib.mass-navigation-total-war.launch.graph.json`

玩家视角应该看到：

- Shu / Wei 两边方阵在战场上。
- 只有方阵能被选中。
- 选中方阵后，脚下有 selection marker，界面有血量表现。
- 右键地面后，方阵移动。
- 士兵跟随所属方阵移动，但不是玩家直接操作对象。
- 障碍物可见。
- 方阵轮廓贴地，不插进地形。
- 相机移动影响表现裁剪和驻留，不会让逻辑方阵消失。

## 你真正要改哪些文件

Total War 示例的文件入口：

| 文件 | 你在这里做什么 |
| --- | --- |
| `mods/showcases/mass_navigation_total_war_entry/MassNavigationTotalWarEntryMod/assets/game.json` | 配启动地图、presentation capacity、相机裁剪距离、小地图、selection 路径预览订单。 |
| `mods/showcases/mass_navigation_total_war_entry/MassNavigationTotalWarEntryMod/assets/Maps/mass_navigation_total_war.json` | 配地图 id、visual heightmap、board/world 数据。 |
| `mods/showcases/mass_navigation_total_war_entry/MassNavigationTotalWarEntryMod/assets/MassNavigationConfig.json` | 配导航世界、solver、agent profiles、障碍、cadence、arrival、avoidance、camera profiles、view residency。 |
| `mods/showcases/mass_navigation_total_war_entry/MassNavigationTotalWarEntryMod/assets/TotalWarShowcaseConfig.json` | 配业务战场：方阵、士兵模板、slot 排列、轮廓、障碍物 overlay、初始选中。 |
| `mods/showcases/mass_navigation_total_war_entry/MassNavigationTotalWarEntryMod/assets/Entities/templates.json` | 配方阵、士兵、障碍物 overlay 的 entity template。 |
| `mods/showcases/mass_navigation_total_war_entry/MassNavigationTotalWarEntryMod/assets/Presentation/performers.json` | 配模型、marker、血条、小地图 marker、performer 生命周期规则。 |
| `mods/showcases/mass_navigation_total_war_entry/MassNavigationTotalWarEntryMod/assets/Configs/config_catalog.json` | 告诉 ConfigPipeline 加载哪些 showcase 配置。 |

业务 runtime 入口：

| 文件 | 职责 |
| --- | --- |
| `MassNavigationTotalWarEntryModEntry.cs` | 注册 showcase runtime 和组件。 |
| `Runtime/TotalWarShowcaseConfig.cs` | `TotalWarShowcaseConfig.json` 的强类型配置。 |
| `Runtime/TotalWarShowcaseRuntime.cs` | 建方阵计划、士兵计划、障碍 overlay 计划、spawn 请求、士兵目标同步。 |
| `Runtime/TotalWarSpawnReceiptRuntime.cs` | 记录 showcase 的 spawn receipt 绑定。 |
| `Runtime/TotalWarSpawnReceiptBindingSystem.cs` | 把 spawn 出来的实体绑定回方阵、士兵和障碍 runtime。 |
| `Runtime/TotalWarFormationRuntimeSystem.cs` | 跑方阵业务状态。 |
| `Runtime/TotalWarFormationOutlinePresentationSystem.cs` | 发射贴地的方阵轮廓表现。 |
| `Runtime/TotalWarObstacleOverlayPresentationSystem.cs` | 发射障碍物 overlay 表现。 |
| `Runtime/TotalWarFormationComponents.cs` | showcase 专属组件，以及严格大小写的 layout/outline 名称。 |

通用基建入口：

`mods/capabilities/navigation/MassNavigationMod/`

这里放的是可复用导航能力，不放“蜀国左翼方阵”“方阵拥有士兵”这种产品业务。

## 模块上手路线

第一次做自己的 RTS，不要从 Core 源码开始啃。按这个顺序看：

| 模块 | 你要怎么用 |
| --- | --- |
| `LudotsCoreMod` | 提供 entity template、spawn、selection、order、presentation、minimap 等基础链路。先复用，不要复制。 |
| `CoreInputMod` | 提供玩家框选、右键命令等输入入口。你的 Mod 通常只配置可选实体和订单类型。 |
| `CameraProfilesMod` | 提供相机 profile。你的 Mod 通过配置选择或扩展 profile。 |
| `MassNavigationMod` | 提供大规模导航基建。你配置 profiles、cadence、avoidance、order 和 agent contract。 |
| `MassNavigationTotalWarEntryMod` | Total War-like 业务示例。重点看方阵如何生成士兵、士兵如何跟随方阵、轮廓和障碍如何表现。 |

如果你要做自己的游戏，通常复制的是 `MassNavigationTotalWarEntryMod` 的结构，然后换成你的业务命名和配置；`MassNavigationMod` 是被依赖的基建，不是放游戏规则的地方。

## 玩家模型

玩家只理解这些：

- 选择方阵。
- 右键地面移动。
- 用游戏提供的旋转按钮或快捷键调整朝向。
- 看血条、选中 marker、方阵轮廓、小地图。
- 切相机观察不同战区。

玩家不理解、也不应该被要求理解：

- runtime selection set
- performer scope
- spawn receipt channel
- MassFlow solver index
- entity template id
- order blackboard key

## Mod 作者模型

Mod 作者描述内容和规则：

1. 地图和视觉高度图。
2. 导航 profile 和 solver 调参。
3. 方阵业务配置。
4. entity template。
5. performer 表现。
6. 必要时写自己的业务 runtime。

Mod 作者不新建这些基础设施：

- selection runtime
- order runtime
- spawn queue
- performer runtime
- minimap runtime
- JSON loader

缺能力时先补正式基建，不在 showcase 里绕一条私有链。

## 方阵怎么配置

方阵在 `mods/showcases/mass_navigation_total_war_entry/MassNavigationTotalWarEntryMod/assets/TotalWarShowcaseConfig.json` 的 `formations[]` 里。

示例：

```json
{
  "id": "shu_left_vanguard",
  "label": "Shu Left Vanguard",
  "teamId": 1,
  "soldierAgent": {
    "templateId": "mass_navigation_total_war_soldier_azure_light",
    "profileId": "light"
  },
  "centerXCm": -2600,
  "centerYCm": -2200,
  "facingDeg": 78,
  "slots": {
    "layout": "Grid",
    "grid": { "columns": 20, "rows": 12, "spacingXCm": 46, "spacingYCm": 50 }
  },
  "outline": {
    "shape": "Rectangle",
    "rectangle": { "widthCm": 1100, "depthCm": 760, "edgeLineWidthCm": 18 }
  }
}
```

字段怎么读：

- `id`：方阵稳定配置 id。
- `teamId`：队伍归属。
- `soldierAgent.templateId`：这个方阵生成哪种士兵模板。
- `soldierAgent.profileId`：士兵使用哪个 MassNavigation profile。
- `centerXCm` / `centerYCm`：初始位置，单位是厘米。
- `facingDeg`：初始朝向，单位是角度。
- `slots.layout`：士兵排列方式。当前支持 `Grid` 和 `Disc`。
- `outline.shape`：方阵轮廓。当前支持 `Rectangle` 和 `Circle`。

大小写必须严格。`Grid` 不是 `grid`，`Rectangle` 不是 `rectangle`。

## Agent Profile 怎么配

agent profile 在 `mods/showcases/mass_navigation_total_war_entry/MassNavigationTotalWarEntryMod/assets/MassNavigationConfig.json` 的 `agentProfiles.profiles[]`。

当前示例有三类：

- `formation`：方阵 agent。半径大、质量高、速度慢。
- `heavy`：重装士兵。比方阵快。
- `light`：轻装士兵。比重装士兵更快。

现在的配置里：

- formation speed 是 `360` cm/s。
- heavy soldier speed 是 `780` cm/s。
- light soldier speed 是 `920` cm/s。

产品规则很直接：士兵速度必须大于方阵速度，否则方阵被推挤、绕障、转移战区时，士兵会追不上。

这些数字是调参值，不是 magic id。速度、半径、Hz、容量、颜色、线宽、秒数都应该继续显式写数字。

## Entity Template 怎么配

模板在 `mods/showcases/mass_navigation_total_war_entry/MassNavigationTotalWarEntryMod/assets/Entities/templates.json`。

方阵模板当前包含：

- `WorldPositionCm`
- `VisualHeightmapSampleState`
- `FacingDirection`
- `OrderBuffer`
- `SelectionSelectableTag`
- `SelectionSelectableState`
- `AttributeBuffer`
- `GameplayTagContainer`
- `TagCountContainer`
- `MassNavigationAgentTag`
- `EntityLayer`
- `MassNavigationControllable`

这表示方阵能被选中、能接订单、有血量、能进入 MassNavigation。

士兵模板当前包含：

- `WorldPositionCm`
- `VisualHeightmapSampleState`
- `FacingDirection`
- `EntityLayer`
- `MassNavigationAgentTag`

这表示士兵也是 MassNavigation agent，但玩家不能直接选中或下令。士兵由方阵业务 runtime 生成并同步目标。

## Performer 怎么配

表现写在 `mods/showcases/mass_navigation_total_war_entry/MassNavigationTotalWarEntryMod/assets/Presentation/performers.json`。

当前 showcase 的生命周期规则是：

- `EntitySpawned` 创建方阵或士兵 performer。
- `EntityDestroyed` 销毁 performer scope。
- `SelectionMemberAdded` 创建方阵 selection marker。
- `SelectionMemberRemoved` 销毁方阵 selection marker。

选中 marker 的规则类似：

```json
{
  "event": { "kind": "SelectionMemberAdded", "key": "selection.live.primary" },
  "command": {
    "kind": "CreatePerformer",
    "definitionId": "mass_navigation_total_war_formation_selection_marker",
    "scopeSource": "SourceStableId"
  }
}
```

给 Mod 作者的人话解释是：

“当一个方阵进入玩家主选择时，为这个方阵创建这个 marker performer。”

清理规则类似：

```json
{
  "event": { "kind": "SelectionMemberRemoved", "key": "selection.live.primary" },
  "command": {
    "kind": "DestroyScopedPerformer",
    "definitionId": "mass_navigation_total_war_formation_selection_marker",
    "scopeSource": "SourceStableId"
  }
}
```

这说明 marker 生命周期属于通用 Selection -> PresentationEvent -> PerformerRule -> PerformerRuntime 链路，不属于 MassNavigation 私有 system。

## selection.live.primary 到底是什么

只有一个“玩家当前主选择”概念。

| 视角 | 名字 | 含义 |
| --- | --- | --- |
| 玩家 | 没有名字 | 玩家只是框选和取消选中。 |
| Mod 作者 | `selection.live.primary` | performer 规则里写的事件 key，表示玩家主选择流。 |
| 引擎开发者 | `SelectionSetKeys.LivePrimary` | 代码里的 canonical key。 |

这不是两套选择集，也不是兼容 alias。配置字符串和代码常量必须解析到同一个正式概念。不要再加第二个拼法。

## 订单和移动

右键移动链路：

```text
Local input
  -> SelectionRuntime
  -> OrderBuffer(massNavigationMove)
  -> MassNavigationOrderIngestionSystem
  -> MassNavigationGroupRuntime
  -> MassFlowSimulationState
  -> WorldPositionCm / FacingDirection
  -> performer transform sync
```

订单类型用语义字符串配置，不在 JSON 里写数字 id。

移动和朝向是两件事。方阵移动不应该自动改朝向，除非你显式配置了 auto-facing 策略。玩家要旋转方阵，就提供旋转命令或按钮。

## 方阵和士兵是什么关系

在这个 showcase 里，业务 runtime 会创建：

- 方阵 MassNavigation agent。
- 归属这个方阵的士兵 MassNavigation agents。

士兵目标来自方阵当前 carried position 和方阵朝向。这样方阵因为避障、推挤产生的被动位移，也会同步影响士兵的目标。

这个“方阵拥有士兵”的规则是业务逻辑，应该放在 `MassNavigationTotalWarEntryMod` 或你的游戏 Mod。它不属于 MassNavigation foundation。

## 障碍物

障碍在 `mods/showcases/mass_navigation_total_war_entry/MassNavigationTotalWarEntryMod/assets/MassNavigationConfig.json` 的 `world.obstacles[]`。

示例：

```json
{ "id": "central_blocker", "localXCm": 5000.0, "localYCm": 5000.0, "radiusCm": 300.0 }
```

玩家要看见障碍，所以 showcase 还配置了：

- `mods/showcases/mass_navigation_total_war_entry/MassNavigationTotalWarEntryMod/assets/Entities/templates.json` 里的 obstacle overlay template。
- `mods/showcases/mass_navigation_total_war_entry/MassNavigationTotalWarEntryMod/assets/TotalWarShowcaseConfig.json` 里的 `obstacleOverlay` 外观参数。
- `Runtime/TotalWarObstacleOverlayPresentationSystem.cs` 发射 overlay 表现。

不要用隐藏 debug draw 假装障碍可见。玩家要看的东西必须走明确表现链路。

## 相机和裁剪

相机相关配置分两处：

- `mods/showcases/mass_navigation_total_war_entry/MassNavigationTotalWarEntryMod/assets/game.json`：presentation culling distance 和 capacity。
- `mods/showcases/mass_navigation_total_war_entry/MassNavigationTotalWarEntryMod/assets/MassNavigationConfig.json`：`cameraProfiles` 和 `viewResidency`。

当前 `viewResidency.mode` 是 `Probe`，并且有 `retainSeconds` 和 `cameraProbes`。这对应产品需求：镜头离开一个地区后，表演单位可以保留一段时间；超过配置时间，再按表现预算处理。

逻辑方阵不应该因为镜头离开而消失。表现驻留不是 gameplay 存活。

## Visual Heightmap

Total War 地图引用：

`mods/showcases/mass_navigation_total_war_entry/MassNavigationTotalWarEntryMod/assets/terrain/mass_navigation_total_war_relief.vhtm`

地图文件：

`mods/showcases/mass_navigation_total_war_entry/MassNavigationTotalWarEntryMod/assets/Maps/mass_navigation_total_war.json`

士兵、方阵轮廓、marker、障碍 overlay 都应该贴这个 visual heightmap。缺 heightmap service 应该 fail-fast，不应该静默退回平面。

## 数字和语义字符串

继续用数字配置真实调参：

- cm
- 秒
- Hz
- capacity
- radius
- speed
- color
- line width
- sample count

用字符串配置语义 id：

- template id
- performer id
- order key
- parameter key
- selection event key
- profile id
- map id
- spawn receipt channel key

运行时可以把字符串编译成 int 走热路径。作者侧不要暴露不透明数字 handle。

## 做自己的 RTS Mod

复制结构，不要复制业务名字：

1. 新建你的 entry mod。
2. 给 Raylib launch graph 加你的 mod。
3. 配地图和 visual heightmap。
4. 配 `MassNavigationConfig.json`：profiles、cadence、obstacles、camera probes、view residency。
5. 配你的业务 config：军团、方阵、 squad 或你产品里的控制单位。
6. 配 selectable control unit 模板。
7. 配 lower-level agent 模板。
8. 配 performer definitions 和 performer rules。
9. 只有真正属于你游戏的规则才写业务 runtime，比如“方阵生成士兵并同步 slot 目标”。
10. Selection、Order、Spawn、Performer、Minimap、ConfigPipeline 都复用现有链路。

## 不要做

- 不要让玩家输入 runtime key。
- 不要新建第二套 selection runtime。
- 不要为 selection marker 写 MassNavigation 私有生命周期 system。
- 如果士兵需要避障和碰撞，不要把士兵做成非 MassNavigation 对象。
- 不要把 Total War 方阵业务塞进 MassNavigation core。
- 不要在 JSON 里写 order blackboard 数字 id 或 performer param 数字 id。
- 不要给缺失模板、performer、mesh、material、map、heightmap 加 fallback。
- 不要做大小写宽容解析。
- 不要把移动和朝向偷偷耦合。

## 验收清单

上线前至少检查：

- launch graph 包含 Total War entry mod 和 MassNavigation foundation。
- `game.json` 启动目标 map。
- map 引用目标 visual heightmap。
- 所有 template id 存在。
- 所有 performer id 存在。
- 方阵可选中、可下令、有血量。
- 士兵是 MassNavigation agent，但不能直接选中。
- 士兵 profile 速度大于方阵 profile 速度。
- selection marker 的创建和销毁由 performer rule 驱动。
- 取消选中或销毁实体后 marker 不残留。
- 障碍物可见并参与导航。
- 相机裁剪和驻留都在文件里配置。
- layout/outline 名称大小写错误会失败，而不是静默兼容。
