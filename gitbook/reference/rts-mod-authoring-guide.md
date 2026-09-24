# RTS Mod 作者指南：复刻 RA2 / War3 / SC2 风格单位

这篇写给第一次接触 Ludots 的 Mod 作者。你的目标不是先理解引擎内部怎么跑，而是先做出一个玩家能理解的 RTS 单位：

- 玩家能看见它。
- 玩家能左键点选或框选它。
- 玩家能右键地面让它移动。
- 选中后能看见脚下 marker、血条、命令按钮。
- 你能换它的阵营、速度、半径、外观、技能和生产方式。

玩家不需要输入 template id、performer id、selection key、order key 或 profile id。这些只属于 Mod 作者的配置文件。

## 先跑已有样板

仓库里现在有两类样板，先分清它们：

| 样板 | 适合学什么 | 不要误解成什么 |
| --- | --- | --- |
| `RtsDemoMod` 和 RTS training showcases | RA2 / War3 / SC2 风格单位、建筑、命令卡、右键命令、QWER 技能。 | 它不是 MassNavigation 大规模 agent 样板。 |
| `MassNavigationMod` 和 `MassNavigationTotalWarEntryMod` | 大规模寻路、避障、agent profile、performer 规则、Total War-like 方阵和士兵。 | Total War 方阵业务不是所有 RTS 单位都必须套的模型。 |

先跑普通 RTS 训练场：

```powershell
.\scripts\run-mod-launcher.cmd cli launch RtsSc2TrainingShowcaseMod --adapter raylib --build auto
.\scripts\run-mod-launcher.cmd cli launch RtsWar3TrainingShowcaseMod --adapter raylib --build auto
.\scripts\run-mod-launcher.cmd cli launch RtsCncTrainingShowcaseMod --adapter raylib --build auto
```

再跑 MassNavigation 大规模样板：

```powershell
.\scripts\run-mod-launcher.cmd cli launch MassNavigationTotalWarEntryMod --adapter raylib --build auto
```

## 你要先认识的四件事

| 你想做的事 | 配置入口 | 人话解释 |
| --- | --- | --- |
| 定义一个单位 | `assets/Entities/templates.json` | 单位蓝图。它叫什么、属于哪队、有多少血、能不能被选中、能不能接命令。 |
| 把单位放到地图上 | `assets/Maps/*.json` | 地图出生点。用哪个模板，出生在哪，是否覆盖名字、队伍、初始属性。 |
| 让玩家看见单位 | `assets/Presentation/performers.json` | 外观演员。单位本身负责规则和数值，performer 负责画出来。 |
| 让右键变成移动 | `assets/Input/default_input.json` 和 `assets/Input/input_order_mappings.json` | 输入映射。右键不是直接改坐标，而是给当前选中的单位提交一个移动命令。 |

如果你只做几十到几百个普通 RTS 单位，先按 `RtsDemoMod` 的单位、输入、命令卡方式做。

如果你要做上万 agent、方阵、全图持续避障、镜头裁剪表现单位，再接入 `MassNavigationMod`。

## 文件阅读顺序

第一次写自己的 RTS Mod，按这个顺序看文件：

| 顺序 | 文件 | 看什么 |
| --- | --- | --- |
| 1 | `mods/RtsDemoMod/assets/Entities/templates.json` | 普通 RTS 单位和建筑模板。 |
| 2 | `mods/RtsDemoMod/assets/Input/default_input.json` | 左键、右键、QWER、S 键绑定。 |
| 3 | `mods/RtsDemoMod/assets/Input/input_order_mappings.json` | 右键怎么变成 `moveTo`，S 怎么变成 `stop`。 |
| 4 | `mods/RtsDemoMod/assets/GAS/abilities.json` | 命令按钮和技能定义。 |
| 5 | `mods/RtsDemoMod/assets/GAS/ability_form_sets.json` | 命令卡布局。 |
| 6 | `mods/RtsDemoMod/assets/Presentation/performers.json` | 普通 RTS 样板外观。 |
| 7 | `mods/showcases/rts_training_sc2/RtsSc2TrainingShowcaseMod/assets/Maps/rts_sc2_training.json` | SC2 风格地图怎么刷单位。 |
| 8 | `mods/showcases/rts_training_war3/RtsWar3TrainingShowcaseMod/assets/Maps/rts_war3_training.json` | War3 风格地图怎么刷单位。 |
| 9 | `mods/showcases/rts_training_cnc/RtsCncTrainingShowcaseMod/assets/Maps/rts_cnc_training.json` | RA2 / C&C 风格地图怎么刷单位。 |
| 10 | `mods/capabilities/navigation/MassNavigationMod/assets/MassNavigationConfig.json` | MassNavigation profile、世界、避障、相机驻留。 |
| 11 | `mods/capabilities/navigation/MassNavigationMod/assets/Presentation/performers.json` | 生产级 selection marker、血条、小地图 marker 的 performer 规则。 |
| 12 | `mods/showcases/mass_navigation_total_war_entry/MassNavigationTotalWarEntryMod/assets/TotalWarShowcaseConfig.json` | 方阵生成士兵、slot 排列、轮廓、障碍 overlay。 |

## 配方一：做一个普通 RTS 单位

先从一个 SC2-like zealot 开始。模板片段来自 `RtsDemoMod`，你可以复制后改 id、名字、队伍和属性：

```json
{
  "id": "my_sc2_zealot",
  "components": {
    "Name": { "Value": "Zealot" },
    "Team": { "Id": 3 },
    "SelectionSelectableTag": {},
    "SelectionSelectableState": { "IsEnabled": true },
    "WorldPositionCm": { "Value": { "X": 0, "Y": 0 } },
    "AttributeBuffer": {
      "base": {
        "Health": 500,
        "Shield": 500,
        "MoveSpeed": 620
      }
    },
    "AbilityStateBuffer": {
      "abilityIds": [
        "Ability.Rts.Strategy.Shared.Hold"
      ]
    },
    "GameplayTagContainer": {},
    "TagCountContainer": {},
    "TimedTagBuffer": {},
    "OrderBuffer": {},
    "BlackboardSpatialBuffer": {},
    "BlackboardEntityBuffer": {},
    "BlackboardIntBuffer": {}
  }
}
```

字段怎么读：

| 字段 | 作者要知道什么 |
| --- | --- |
| `id` | 这个单位模板的名字。地图刷单位、performer 规则都会用它。 |
| `Name` | 玩家看到的名字。 |
| `Team` | 阵营。选择过滤、敌我关系、表现颜色都可以围绕队伍设计。 |
| `SelectionSelectableTag` | 允许它进入玩家选择。没有这个，点选和框选不会把它当作可选单位。 |
| `SelectionSelectableState` | 当前是否可被选中。建造中、被装载、临时隐藏时可以关掉。 |
| `WorldPositionCm` | 地图坐标，单位是厘米。 |
| `AttributeBuffer` | 血量、护盾、移动速度、资源等数值。建议显式写 `MoveSpeed`，不要依赖运行时默认速度。 |
| `AbilityStateBuffer` | 命令卡里有哪些按钮。 |
| `OrderBuffer` | 命令队列。没有它，右键移动命令不会落到这个单位上。 |
| `BlackboardSpatialBuffer` | 移动点、施法点这类位置参数。 |
| `BlackboardEntityBuffer` | 目标单位、目标建筑这类实体参数。 |
| `BlackboardIntBuffer` | 技能槽位、模式等整数参数。 |

## 配方二：把单位放进地图

地图文件用 `Template` 选择单位模板，用 `Overrides` 改出生点、显示名、队伍等差异。

```json
{
  "Template": "my_sc2_zealot",
  "Overrides": {
    "WorldPositionCm": { "Value": { "X": 8280, "Y": 6880 } },
    "Name": { "Value": "Frontline Vanguard" },
    "Team": { "Id": 3 }
  }
}
```

现有参考：

- SC2 训练地图：`mods/showcases/rts_training_sc2/RtsSc2TrainingShowcaseMod/assets/Maps/rts_sc2_training.json`
- War3 训练地图：`mods/showcases/rts_training_war3/RtsWar3TrainingShowcaseMod/assets/Maps/rts_war3_training.json`
- C&C / RA2 训练地图：`mods/showcases/rts_training_cnc/RtsCncTrainingShowcaseMod/assets/Maps/rts_cnc_training.json`

## 配方三：让右键地面变成移动

输入分两层：

| 文件 | 作用 |
| --- | --- |
| `assets/Input/default_input.json` | 把鼠标和键盘变成动作。比如左键是 `Select`，右键是 `Command`。 |
| `assets/Input/input_order_mappings.json` | 把动作变成订单。比如 `Command` 变成 `moveTo`。 |

右键移动配置片段：

```json
{
  "actionId": "Command",
  "trigger": "PressedThisFrame",
  "orderTypeKey": "moveTo",
  "argsTemplate": {},
  "requireSelection": true,
  "selectionType": "Position",
  "isSkillMapping": false,
  "modifierBehavior": "QueueOnModifier"
}
```

人话解释：

- 玩家按右键。
- 输入系统发现这是 `Command`。
- 映射系统发现 `Command` 应该提交 `moveTo`。
- `requireSelection: true` 表示必须有当前选中的单位。
- `selectionType: "Position"` 表示这次命令需要一个地面点。
- 命令进入单位的 `OrderBuffer`。
- 移动系统消费订单并更新单位位置。

不要在自己的 Mod 里写一套私有右键系统。先复用这条输入到订单的正式链路。

## 配方四：给单位不同表现

`performer` 可以先理解成外观演员。一个单位可以有身体、脚下圈、血条、文字、小地图 marker、选中 marker。它们都应该由配置和 performer rule 创建，不要在业务 tick 里手写“如果选中了就画一个圈”。

一个最小身体 performer：

```json
{
  "id": "my_sc2_zealot_body",
  "defaultColor": [0.38, 0.72, 1.0, 1.0],
  "rules": [
    {
      "event": { "kind": "EntitySpawned", "key": "my_sc2_zealot" },
      "condition": { "inline": "SourceHasVisualTransform" },
      "command": {
        "kind": "CreatePerformer",
        "definitionId": "my_sc2_zealot_body",
        "scopeSource": "EventPayloadA"
      }
    },
    {
      "event": { "kind": "EntityDestroyed", "key": "my_sc2_zealot" },
      "command": {
        "kind": "DestroyPerformerScope",
        "scopeSource": "EventPayloadA"
      }
    }
  ],
  "behaviors": [
    {
      "slot": "body",
      "kind": "AssetBinding",
      "activeByDefault": true,
      "assetBinding": {
        "assetKind": "SkinnedMesh",
        "assetId": "mass_navigation.agent.soldier",
        "materialId": "default_surface",
        "renderPath": "GpuSkinnedInstance",
        "mobility": "Movable",
        "localScale": [0.45, 0.45, 0.45]
      }
    },
    {
      "slot": "grounding",
      "kind": "Grounding",
      "activeByDefault": true,
      "grounding": {
        "mode": "SnapToGround",
        "offset": 0.0,
        "updatePolicy": "EveryFrame"
      }
    }
  ]
}
```

同一种规则可以做不同阵营表现：

```json
{
  "id": "my_terran_marine_body",
  "extends": "my_sc2_zealot_body",
  "defaultColor": [0.28, 0.58, 1.0, 1.0]
}
```

```json
{
  "id": "my_zergling_body",
  "extends": "my_sc2_zealot_body",
  "defaultColor": [1.0, 0.34, 0.28, 1.0],
  "behaviors": [
    {
      "slot": "body",
      "kind": "AssetBinding",
      "activeByDefault": true,
      "assetBinding": {
        "assetKind": "SkinnedMesh",
        "assetId": "mass_navigation.agent.soldier",
        "materialId": "default_surface",
        "renderPath": "GpuSkinnedInstance",
        "mobility": "Movable",
        "localScale": [0.34, 0.34, 0.34]
      }
    }
  ]
}
```

当前稳定做法是：不同模板触发不同 performer definition，或者让 performer 绑定 entity color / 属性参数。

如果你想“同一个模板根据阵营自动换完全不同模型”，先确认 performer rule 条件是否已有正式能力；没有就补正式 performer 条件能力，不要在某个 showcase system 里硬编码阵营到模型的映射。

## 配方五：选中 marker 和血条

选中 marker 的生命周期应该跟选择事件走：

```json
{
  "event": { "kind": "SelectionMemberAdded", "key": "selection.live.primary" },
  "command": {
    "kind": "CreatePerformer",
    "definitionId": "my_unit_selection_marker",
    "scopeSource": "SourceStableId"
  }
}
```

```json
{
  "event": { "kind": "SelectionMemberRemoved", "key": "selection.live.primary" },
  "command": {
    "kind": "DestroyScopedPerformer",
    "definitionId": "my_unit_selection_marker",
    "scopeSource": "SourceStableId"
  }
}
```

这里的 `selection.live.primary` 不是玩家要输入的东西。它只表示“玩家当前主选择”。Mod 作者只会在 performer 规则里看到它。

血条可以绑定属性：

```json
{
  "id": "my_unit_health_hud",
  "defaultColor": [0.12, 0.92, 0.30, 0.96],
  "behaviors": [
    {
      "slot": "body",
      "kind": "AssetBinding",
      "activeByDefault": true,
      "assetBinding": {
        "assetKind": "WorldHud",
        "materialParamKey": "my.unit.health.ratio",
        "renderPath": "None",
        "mobility": "Movable",
        "localScale": [42.0, 5.0, 1.0]
      }
    }
  ],
  "bindings": [
    {
      "paramKey": "my.unit.health.ratio",
      "source": "attributeRatio",
      "attributeId": "Health"
    }
  ],
  "paramDefaults": [
    {
      "paramKey": "my.unit.health.ratio",
      "lane": "Float",
      "floatValue": 1.0
    }
  ]
}
```

不要写一个每帧扫描 selection 的业务 system 来创建 marker。创建和清理都走 performer rule，取消选中和实体销毁才能自然清干净。

## 配方六：接入 MassNavigation

当你需要这些能力时，再接入 MassNavigation：

- 同屏几千到几万个 agent。
- 单位之间必须持续避障和碰撞。
- 逻辑单位和表演单位运行在不同世界。
- 镜头离开后，表现单位可以保留一段时间再释放。
- 方阵、兵群、载具群等业务层级需要自己发射下层 agent。

一个可控 MassNavigation 单位模板应该包含这些能力块：

```json
{
  "id": "my_massnav_infantry",
  "components": {
    "Name": { "Value": "MassNav Infantry" },
    "Team": { "Id": 1 },
    "WorldPositionCm": { "Value": { "X": 0, "Y": 0 } },
    "VisualHeightmapSampleState": {},
    "FacingDirection": { "AngleRad": 0.0 },
    "OrderBuffer": {},
    "SelectionSelectableTag": {},
    "SelectionSelectableState": { "IsEnabled": true },
    "AttributeBuffer": {
      "base": { "Health": 100 },
      "current": { "Health": 100 }
    },
    "GameplayTagContainer": {},
    "TagCountContainer": {},
    "MassNavigationAgentTag": {},
    "EntityLayer": {
      "category": [ "myGame.infantry" ],
      "mask": [ "myGame.infantry", "myGame.vehicle" ]
    },
    "MassNavigationControllable": {}
  }
}
```

MassNavigation 专属字段：

| 字段 | 作者要知道什么 |
| --- | --- |
| `VisualHeightmapSampleState` | 让表现贴地。地形起伏明显时必须有。 |
| `FacingDirection` | 单位朝向。移动和朝向不要偷偷耦合，除非你显式做了朝向策略。 |
| `MassNavigationAgentTag` | 让它进入 MassNavigation agent 系统。 |
| `MassNavigationControllable` | 允许正式移动订单控制它。 |
| `EntityLayer.category` | 它属于哪些碰撞/避障层。 |
| `EntityLayer.mask` | 它需要和哪些层互相处理。 |

profile 写在 `assets/MassNavigationConfig.json`：

```json
{
  "agentProfiles": {
    "defaultProfileId": "infantry",
    "profiles": [
      {
        "id": "infantry",
        "heavy": false,
        "navMass": 1.0,
        "visualScale": 0.22,
        "bodyRadiusCm": 20.0,
        "speedCmPerSecond": 920.0,
        "everyNth": 0,
        "nthOffset": 0
      },
      {
        "id": "vehicle",
        "heavy": true,
        "navMass": 6.0,
        "visualScale": 0.75,
        "bodyRadiusCm": 120.0,
        "speedCmPerSecond": 520.0,
        "everyNth": 0,
        "nthOffset": 0
      }
    ]
  }
}
```

profile 是移动身体：

- `bodyRadiusCm` 决定占地半径。
- `speedCmPerSecond` 决定最大速度。
- `navMass` 决定推挤和避让里的质量感。
- `heavy` 表示它在避让里更像重单位。
- `visualScale` 给表现层使用。

模板负责“这个单位有什么能力”，profile 负责“这个单位在导航世界里是什么身体”。不要把 profile id 硬塞进普通 entity template，当前 Total War showcase 是在业务配置里把 `templateId` 和 `profileId` 配成一对。

## 普通 RTS 与 MassNavigation 怎么选

| 需求 | 推荐路径 |
| --- | --- |
| 一个 Footman、Rhino Tank、Zealot 能选中、能右键移动、能放技能 | 先用 `RtsDemoMod` 路线。 |
| 几百个普通单位，需要基本停靠和避让 | 用普通 RTS 订单链路，再按需要打开 `Navigation2D`。 |
| 上万单位、长时间全图仿真、多世界表现驻留 | 用 `MassNavigationMod`。 |
| Total War-like 方阵控制，方阵可选，士兵跟随且士兵也参与避障 | 用 `MassNavigationTotalWarEntryMod` 的业务结构做参考，但把业务名字换成你的游戏语言。 |
| 纯表现单位，不需要碰撞、避障、命令 | 可以只做 performer，但不要把它伪装成需要导航的 gameplay unit。 |

## 方阵、编队、生产和技能放哪里

这些是你的游戏规则，应该放在你的游戏 Mod：

- barracks 训练 footman。
- gateway 训练 zealot。
- construction yard 放建筑。
- 方阵生成士兵。
- 方阵把 slot 目标同步给士兵。
- 载具发射乘员表现单位。
- 相机附近的表演单位驻留多久。

这些不是 MassNavigation foundation 的职责：

- 私有 selection runtime。
- 私有 order runtime。
- 私有 performer runtime。
- 私有 JSON loader。
- Total War 方阵业务。

如果两个以上 Mod 都需要同一种能力，再提炼成正式可复用基建。只服务你这个玩法的规则，留在你的业务 Mod。

## 选择和命令不要绑死在 PlayerOwner

RTS 选择不是“只能选自己拥有的单位”这么窄的概念。正式思路是：

- 单位用 `Team`、关系、tag 或其他数据表达它是什么。
- 选择系统用配置里的过滤规则决定当前视角能选什么。
- 命令系统根据订单、选择结果和业务规则决定能不能执行。

现有 MassNavigation Total War 样板在 `assets/game.json` 里使用：

```json
{
  "selection": {
    "targetFilter": { "relationFilter": "Friendly" },
    "movePathPreviewOrderTypeKeys": [ "massNavigationMove" ]
  }
}
```

这表达的是“这个样板默认框选友方目标”。如果你的产品需要观察敌军、裁判视角、回放视角、AI 视角、调试视角，就应该改选择过滤配置或视角上下文，而不是发明一套只认本地玩家拥有者的私有系统。

## 数字和字符串怎么写

用字符串表达语义：

- template id。
- performer id。
- order type key。
- profile id。
- map id。
- selection event key。
- parameter key。
- EntityLayer 名称。

用数字表达真实调参，并写清单位：

- cm。
- cm/s。
- 秒。
- Hz。
- capacity。
- radius。
- scale。
- color。
- line width。

不要把运行时内部的数字 handle 写进作者配置。作者应该写 `massNavigationMove`、`my_sc2_zealot`、`myGame.infantry` 这种可读名字；运行时热路径可以把这些名字编译成数字。

## 常见问题排查

| 现象 | 先检查什么 |
| --- | --- |
| 地图上看不见单位 | map 的 `Template` 是否存在；template 是否有 `WorldPositionCm`；performer 是否有 `EntitySpawned` 规则；asset id 是否存在。 |
| 点不中或框不中 | template 是否有 `SelectionSelectableTag` 和 enabled 的 `SelectionSelectableState`；当前 `selection.targetFilter` 是否允许选它。 |
| 右键没有反应 | 单位是否有 `OrderBuffer`；`input_order_mappings.json` 的 `Command` 是否映射到正确 order；当前是否真的有选中单位。 |
| 单位移动但速度不对 | `AttributeBuffer.base.MoveSpeed` 或 MassNavigation profile 的 `speedCmPerSecond` 是否显式配置。 |
| 选中 marker 残留 | 是否同时配置了 `SelectionMemberAdded` 和 `SelectionMemberRemoved`；销毁实体时是否有 `EntityDestroyed -> DestroyPerformerScope`。 |
| 血条不动 | `AttributeBuffer` 是否有对应属性；performer binding 的 `attributeId` 是否和属性名一致。 |
| MassNavigation agent 不避障 | template 是否有 `MassNavigationAgentTag`；profile 是否存在；`EntityLayer.category/mask` 是否允许它和目标层互相处理。 |
| 士兵追不上方阵 | 士兵 profile 速度是否大于方阵；士兵目标是否来自方阵当前位置和朝向，而不是只复制最初订单点。 |
| 外观插进地面 | template 是否有 `VisualHeightmapSampleState`；performer 是否有 `Grounding`；地图是否配置了 visual heightmap。 |

## 不要做

- 不要让玩家输入 template id、performer id、selection key、order key。
- 不要复制一套 selection runtime。
- 不要复制一套 right-click order runtime。
- 不要在 MassNavigation system 里写 selection marker 生命周期。
- 不要每帧扫描 selection 来手动画 marker。
- 不要把 Total War 方阵业务塞进 `MassNavigationMod`。
- 不要把移动和朝向偷偷耦合。需要自动朝向时，做成显式策略。
- 不要用大小写宽容解析。`Grid` 和 `grid` 应该是两个不同输入，其中一个错了就失败。
- 不要在缺模板、缺 performer、缺 mesh、缺 heightmap 时静默画替代物。
- 不要把 `PlayerOwner` 当成 RTS 选择和命令的唯一解释。

## 上线前验收清单

- 玩家打开地图能看见单位。
- 单位能被左键点选和框选。
- 选中后 marker 跟随单位，取消选中后 marker 消失。
- 右键地面后单位移动。
- S 或 stop 命令能停止单位。
- 命令卡按钮能显示并触发对应能力。
- 血条、护盾、文字等表现和属性一致。
- 不同阵营或单位类型能显示不同 performer。
- 普通 RTS 单位没有被强迫理解 MassNavigation 方阵业务。
- MassNavigation agent 的 template、profile、layer、performer 都来自显式配置。
- 方阵、生产、士兵跟随、驻留策略这些玩法规则只在业务 Mod 里。
- 所有 id 使用语义字符串，所有调参数字有单位和产品含义。

## 继续阅读

- `gitbook/reference/mass-navigation-user-book.md`：Total War-like 方阵和士兵的大规模导航教学。
- `gitbook/reference/mass-navigation-formal-chain.md`：MassNavigation 正式链路和职责边界。
- `gitbook/architecture/performer-as-actor-architecture.md`：performer-as-actor 架构。
- `gitbook/architecture/performer-transform-and-attachment.md`：performer transform、grounding、attachment。
- `mods/RtsDemoMod/`：普通 RTS 单位、建筑、命令卡和输入样板。
- `mods/capabilities/navigation/MassNavigationMod/`：大规模寻路 foundation。
