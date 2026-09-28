# CoreInputMod 退役设计

状态：待评审，未开发。

依据：`origin/cursor/tw-showcase-intent-members-de53` 分支（`ec16d97743`）和 main（`0cc88ce375`）的代码，以及 Case E 输入宪法（`mods/showcases/case_e_selection/CaseESelectionMod/docs/input-config-constitution.html`）。关系那一半见 `2026-09-28-relationship-graph-control-plane-design.md`，两份在"下令授权"处交叉。

## 1. 概述

CoreInputMod 是游戏开始时由 C# 一次性装上的一批输入和界面系统：技能栏、Tab 切目标、视角模式、瞄准预览、移动路线预览、老的"按键→下令"映射。每个系统里都写死了东西：动作名（`TabTarget`、`ViewModeNext`）、下令类型（`castAbility`、`moveTo`、`stop`）、集合名（`collection.command.source`）、文件路径（`assets/Input/input_order_mappings.json`）、"只能有一个 mod 带下令映射""只有一个本地玩家"这类规矩，还有十几个全局表的键。

Case E 和 TW 已经证明这些事可以不写 C#：按键绑到交互状态，交互状态挂边沿图，图读写玩家实体上的集合、提交下令、开关面板，表现层按集合挂 presenter。

退役后：

- CoreInputMod 整个删掉，没有替代它的新 C# mod。
- 每个 showcase 自己带交互状态、图、面板、presenter 配置，和 TW 一样。
- 引擎只补 Case E 零件里确实缺的几个节点（见 3.9），不补"通用输入系统"。

玩家能感到的变化：交互展示、MOBA 两个 mod 挂的"选中指示器"现在从来不出现，退役后改成 presenter 订阅选中集合，真的会出现；Tab 切目标、技能栏在每个 showcase 里的键位和样式由配置决定，不再是全局默认；移动路线预览这次去掉。

## 2. 结构

CoreInputMod 每一块的去向：

| 部件 | 现在做什么 | 去向 | 分类 |
| --- | --- | --- | --- |
| 已选中回调列表 | 给 mod 挂"选中单位时"的反馈 | 删；选中指示器以后由 presenter 订阅选中集合 | 死代码 |
| 视角模式显示开关 `CoreInputMod.ViewModeHudEnabled` | 3 个 showcase 在写 | 删，连同写它的代码 | 死代码（没人读） |
| 技能输入应答 `GasInputResponseSystem` + 技能里"等玩家"的两种步骤 `InputGate`、`TargetCollectionGate` | 技能执行到一半等玩家点目标 / 选一组目标 | 删；没有任何技能数据用到这两种步骤 | 死功能 |
| 空的 `interaction_context_profiles.json` | 无 | 删 | 空文件 |
| 81 个 mod 的 `mod.json` 依赖 | 大多没用它的代码，但都靠它拿到 Core 功能的默认按键（3.11） | 按键归位后再删 | 不是空依赖 |
| Core 里写死的动作名：`InteractionActionBindings`、`MinimapPresentationSystem`、响应连锁系统 | Core 功能按固定名字找动作，默认按键由 CoreInputMod 提供 | 动作名写进各功能的配置，默认按键由开这个功能的 mod 带（3.11） | Core 硬编码 |
| 老下令映射（`LocalOrderSource*`、`AutoInstalledLocalOrderSourceSystem`、Core 的 `InputOrderMappingSystem` 和 `input_order_mappings.json`） | 按键直接翻成下令 | 12 个 mod 改成交互状态 + 下令图 + `SubmitCommandIntent` / `SubmitCast` | 有现成做法 |
| 集合名 `collection.command.source` | 写死的"选中单位"集合 | 改成各 mod 选中图自己写的集合，和 Case E 的 `selected` 一样 | 有现成做法 |
| 小地图焦点 | C# 注册，焦点固定取 `collection.command.source` | 小地图配置里写看哪个集合 | 有现成做法 |
| 技能栏 `SkillBarOverlaySystem` | 画当前单位的技能槽 | 面板 + 图（`QueryCollectAbilitySlots`），照 TW 的 `tw.panel.*` | 零件都在 |
| Tab 切目标 `TabTargetCycleSystem` | 按 Tab 轮流选附近敌人 | 交互状态绑 Tab + 边沿图，结果写集合 | 零件基本都在，排序要核实 |
| 瞄准预览 `AbilityAimPresentationProjectionSystem` | 瞄准时画范围圈 | 删；指示器由 presenter 读实体参数画（3.7） | 零件都在，缺各 mod 的配置 |
| 持续瞄准 `AbilityExecAimSyncSystem` | 引导技能期间跟着鼠标改落点 | 删；指针经交互状态写进实体参数，技能效果图自己读（3.7） | 零件都在 |
| 视角模式 `ViewMode*` + `viewmodes.json` | 切镜头 + 切施法方式 + 开关技能栏 | 每个模式一个交互状态，切换走 `ActivateContext` | 缺切镜头节点 |
| 移动路线预览 `CommandActorMovePathPresentationSystem` | 画选中单位的移动线和路点 | 删；以后走 presenter，按参与者座位显示（3.10） | 这次不做替代 |
| 默认按键 `default_input.json`、presenter `presenters.json` | 全局默认动作和瞄准 / 路线 presenter | 搬进真正用它们的 mod，或随上面各项删掉 | 跟随 |

## 3. 详情

### 3.1 直接删

**已选中回调。** CoreInputMod 开局建一个空列表，摄像机验收、交互展示、MOBA 三个 mod 往里挂回调。全仓库没有任何地方调用这个列表，挂上去的反馈从来不会执行。删掉列表和三处挂回调的代码，行为不变。

- 摄像机验收另有自己的点击系统 `CameraAcceptanceProjectionClickSystem` 直接处理点击，不靠这个回调。
- 交互展示和 MOBA 挂的是选中指示器，一直没显示过。补回来的做法照 Case E：presenter 规则订阅选中集合的成员事件，放在 3.3 换集合名那一片做。

**视角模式显示开关。** `EntityCommandPanelShowcaseMod`、`InteractionShowcaseMod`、`SuperweaponContextShowcaseMod` 在写 `CoreInputMod.ViewModeHudEnabled`，没有任何代码读它。删常量和六处写入。

**技能里"等玩家"的两种步骤。** 技能执行项 `InputGate`（停下来等玩家点目标）和 `TargetCollectionGate`（停下来等玩家确认一组目标）都往同一条请求队列里放请求，由 `GasInputResponseSystem` 在玩家按确认时回答。技能数据里一个都没有，只有加载器、执行器和几个测试在用。删掉这两种执行项、`GasInputResponseSystem`、技能请求队列 `AbilityInputRequestQueue` 和应答缓冲 `InputResponseBuffer`。响应连锁用的是另一条请求队列 `InputRequestQueue`，保留。

连带要改的是技能"使用条件"的延后判断：现在技能如果在产生效果前有等玩家的步骤，使用条件推迟到选完目标再判断。两种步骤删掉以后，这条延后路径也没有了，使用条件一律在开始时判断。

以后技能要中途选目标，走宪法 §12 的瞄准交互状态：先瞄准选好目标，再 `SubmitCast`。

这一项改 Core 技能执行，开发时先按 `ludots-gas-composition-gate` 做自审。

### 3.2 下令：老映射改成 Case E 下令图

带 `input_order_mappings.json` 的 12 个 mod：`BrowserRtsProductionShowcaseMod`、`CapabilityStandardCrowdPhysicsArenaMod`、`CapabilityStandardMassNavigationLargeWorld10kMod`、`ChampionSkillSandboxMod`、`EastAsiaBordersLandSeaDemoMod`、`FormationCapabilityShowcaseMod`、`InteractionShowcaseMod`、`MobaDemoMod`、`FireballSharedMod`、`RoadNetworkShowcaseMod`、`RtsDemoMod`、`UxPrototypeMod`。

每个 mod 改成和 `tw.rts.command` 一样：

```text
交互状态 bindings 里有下令键 → triggers 挂下令图
下令图：LoadCaster → 指针落地 → 按命中分支 → QueryFromCollection(本 mod 的选中集合)
      → SubmitCommandIntent / SubmitCast / SubmitEngageBatch（orderTypeKey 写在节点上）
```

下令类型写在图节点上，`castAbility` / `moveTo` / `stop` 不再在 C# 里查表。"只能有一个 mod 带映射"这条规矩随之消失：每个 mod 管自己的交互状态。

三个 mod 在 C# 里改写下令（`FormationOrderPolicySystem`、`MobaInputModeSystem`、`RoadNetworkOrderPolicySystem`）。它们的逻辑要改成下令图里的分支；改不动的部分要逐个列出来，不能保留 C# 钩子。

全部迁完后删：`LocalOrderSource*`、`AutoInstalledLocalOrderSourceSystem`、`LocalOrderSourceHelper`（它是 `ControlDomainQuery` 的调用方之一，和关系那份设计对上）、Core 的 `InputOrderMappingSystem`、`InputOrderMappingLoader` 和所有 `input_order_mappings.json` / `local_order_source.json`。

### 3.3 选中集合：不再有写死的名字

`collection.command.source` 在 CoreInputMod 外有 98 个文件引用。写它的只有两张图：`LudotsCoreMod` 的 `graph.core.select_commit.json` 和 `RtsDemoMod` 的 `graph.rts.select_commit.json`，其余是 showcase 的 C# 直写、presenter 规则、镜头跟随配置和测试。

做法：集合名由写它的选中图定，读它的配置写同一个名字，和 Case E 的 `selected` 一样。引擎和共享 mod 里不再有这个常量。showcase 的 C# 直写要改成图写；这一项要逐个 mod 看，工作量最大，放在最后一片。

### 3.4 小地图焦点

现在 CoreInputMod 开局在 C# 里注册焦点来源，固定读本地玩家实体上的 `collection.command.source`。改成小地图配置里写"焦点看哪个集合"，presenter 按集合挂的做法也是这样。没写就不显示焦点，不猜。

### 3.5 技能栏

照 TW 的面板做法：面板模板写在 `Panels/panel_templates.json`，开局图 `CreatePanel`，数值图用 `QueryCollectAbilitySlots` 读当前单位的技能槽写进面板。键位文字写在面板模板里。视角模式里的"技能栏开 / 关"改成 `ShowPanel` / `HidePanel`。

### 3.6 Tab 切目标

```text
交互状态 bindings 里有 Tab / Shift+Tab → 挂 tab 图
tab 图：LoadCaster → QueryRadius → 按敌对过滤（QueryFilterRelationship 或 QueryFilterTeam）
      → 排序 → ReadBlackboardInt(下标) → TargetListGet → WriteCollection(tab_target, replace)
      → WriteBlackboardInt(下标 + 1 或 − 1)
presenter 按 tab_target 集合画目标环
```

半径、敌对怎么判、集合名都写在图和配置里。现在读 `CoreServiceKeys.TabTargetEntity` 的地方改成读集合。

待核实：`QuerySortStable` 是不是按距离排序。不是的话，缺一个"按离某点距离排序"的节点。

### 3.7 瞄准和持续瞄准

五层，各管各的：

| 层 | 管什么 | 配置 / 节点 |
| --- | --- | --- |
| 设备 | 鼠标、键盘、手柄 | `default_input.json` 的绑定 |
| 输入语义 | "指针位置""确认""取消"这些动作和它的轴值 | `default_input.json` 的动作 |
| 实体参数 | 原始指针轴值写进玩家实体的属性；瞄准图把它换算成地面落点，写进玩家实体的黑板 | 属性：`action_attribute_bindings.json`，和 Case E 取指针一样。黑板：瞄准交互状态的持续图 `LoadAttribute` → `ScreenPointToGround` → `ClampTargetToRange` → `WriteBlackboard*` |
| 技能效果 | 落点从黑板取 | 技能效果图 `ReadBlackboard*` |
| 指示器 | 范围圈、扇形、落点标记 | presenter 读同一份黑板 |

属性只放按键轴值这类每帧都会被覆盖的原始值；换算后的落点放黑板，因为它是这次瞄准的结果，技能效果和指示器都要读同一份。

瞄准本身按宪法 §12：技能按下图读 `InteractionPref` 分支，智能施法直接 `SubmitCast`；否则 `ActivateContext(瞄准)`，确认图 `SubmitCast`，取消图 `DeactivateContext`。指针轴值只在瞄准交互状态活着时绑定。

引导技能期间跟着鼠标改落点，不需要单独的系统，也不需要新节点：瞄准图持续把最新落点写进黑板，技能效果图每次生效都从黑板取。

要做的是给用到瞄准的 mod 写这些配置，把 CoreInputMod `presenters.json` 里的 `core_input.ability_aim.*` 搬进这些 mod。`AbilityExecAimSyncSystem`、`AbilityAimPresentationProjectionSystem`、Core 的 `AbilityExecAimSync` 组件都删掉。`ChampionSkillSandboxMod` 模板里的 `AbilityExecAimSync` 改成上面的配置。

### 3.8 视角模式

一个视角模式现在管三件事：切镜头、切施法方式、开关技能栏。改成每个模式一个交互状态，切换由按键图 `ActivateContext` 做，交互状态的 `onActivated` 图做这三件事：

| 事 | 节点 |
| --- | --- |
| 切施法方式 | `SetInteractionMode`（已有） |
| 开关技能栏 | `ShowPanel` / `HidePanel`（已有） |
| 切镜头 | 没有，见 3.9 |

`CameraProfilesMod` 的 `viewmodes.json`（战术 / 跟随 / 观察三个模式）改成三个交互状态。TW 经 `CameraProfilesMod` 间接依赖 CoreInputMod，这一步做完才断得开。

### 3.9 引擎要补的节点

| 编号 | 缺什么 | 谁要 | 备注 |
| --- | --- | --- | --- |
| C1 | 图里切镜头：激活某个虚拟镜头，可带跟随目标 | 视角模式 | 镜头配置已在 `Camera/virtual_cameras.json`，节点只按 id 激活 |
| C2 | 按离某点距离排序 | Tab 切目标 | 先核实 `QuerySortStable`，是按距离排就不补 |

### 3.10 移动路线预览：先删

现在的系统读选中单位（最多 4 个）的下令队列，画移动线和路点。集合名、4 个单位上限、画面分层编号都写死在 C# 里。

这次整块删掉：`CommandActorMovePathPresentationSystem`、`core_input.move_path.*` presenter、7 个 mod 配置里的 `movePathPreviewOrderTypeKeys`。

以后要做时，走 presenter 那套：presenter 声明画什么，按参与者座位决定谁能看到（只有下令的那个玩家看到自己单位的路线）。开发这一片时把这条待办登记到 `gitbook/architecture/graph-capability-status.md`。

### 3.11 Core 里写死的动作名和默认按键

几个 Core 功能按固定名字找动作：

| 功能 | 写死的动作名 | 位置 |
| --- | --- | --- |
| 交互默认动作 | `Select.Begin`（确认）、`Cancel`、`Command`、`PointerPos` | `InteractionActionBindings` |
| 响应连锁 | `ResponseChainPass`、`ResponseChainNegate`、`ResponseChainActivate` | `InteractionActionBindings`、`ResponseChainHumanOrderSourceSystem`、`ResponseChainUiSyncSystem` |
| 小地图 | `Minimap.Toggle`、`Minimap.Zoom`、`Minimap.Pan` 等 8 个 | `MinimapPresentationSystem` |

这些动作的声明和默认按键（左键框选、右键下令、Esc、M、PageUp/PageDown、空格 / 1 / N 等）只在 CoreInputMod 的 `default_input.json` 里。根目录 `assets/Input/default_input.json` 声明了 `Default_Gameplay` 上下文，CoreInputMod 往里追加这些绑定。`LudotsCoreMod` 的 `game.json` 默认开 `Default_Gameplay`，所以每个依赖 CoreInputMod 的 mod 都拿到了这批按键。删掉依赖，这些 mod 就没有左键框选、右键下令和小地图按键了。

做法：

- 动作名不写在 C# 里。小地图、响应连锁各自的配置写"用哪个动作做什么"；交互的确认 / 取消 / 下令由交互状态的 `bindings` 和边沿图决定，不需要引擎默认名。
- 默认按键由开这个功能的 mod 带：用小地图的 mod 带小地图按键，用响应连锁的 mod 带连锁按键，用框选的 mod 带框选按键。
- 做完这一步，才逐个删 `mod.json` 里的 CoreInputMod 依赖。删之前跑这个 mod 的验收。

开发前要先逐个查小地图、响应连锁现有的配置入口，确认动作名放在哪个配置里。

## 4. 场景

| 场景 | 玩家看到的 | 靠什么 |
| --- | --- | --- |
| RTS 右键下令 | 框选几个兵，右键地面，这几个兵走过去；右键敌人，这几个兵去打 | 交互状态 + 下令图 `SubmitCommandIntent` |
| MOBA 放技能 | 按 Q，智能施法直接放；非智能施法出现范围圈，左键确认放出，右键取消 | 技能按下图读 `InteractionPref`，瞄准交互状态 + presenter |
| Tab 切目标 | 按 Tab，离我最近的敌人头上出现目标环；再按跳到下一个；Shift+Tab 往回跳 | tab 图写 `tab_target`，presenter 画环 |
| 技能栏 | 选中英雄后屏幕下方出现他的技能格子，冷却中的变灰 | 面板 + `QueryCollectAbilitySlots` |
| 切视角 | 按键从战术视角切到跟随视角，镜头跟到我的英雄身上，施法方式和技能栏跟着模式变 | 视角交互状态的 `onActivated` 图 |
| 选中指示器 | MOBA 里选中英雄，脚下出现选中环（现在不出现） | presenter 订阅选中集合 |

## 5. 边界

- 不建新的通用输入 mod，不把 CoreInputMod 的 C# 换个地方放。
- 共享 mod 和引擎里不出现任何动作名、集合名、下令类型名。
- 删 `InputGate` 以后，技能配置里再写它，加载时报"未知执行项"并点名技能。`AbilityExecAimSync` 同理。
- 移动路线预览删掉后，选中单位下令时不再有路线线条，这是这次有意去掉的。
- 没声明焦点集合的小地图不显示焦点；没挂 tab 图的 mod 按 Tab 没反应。都是配置决定的，不是故障。
- 三个 C# 下令改写钩子迁不进图的部分，列出来单独评审，不留钩子。
- 片与片之间 main 要能跑：每片只删已经没人用的东西。
- 删掉依赖后，这个 mod 开局就不会再装 CoreInputMod 的那批系统，也不再拿到它的默认按键。所以删依赖放在按键归位之后，删之前逐个跑这个 mod 的验收。
- 分片顺序：
  1. 删死代码：已选中回调、视角模式显示开关（3.1）。
  2. 删技能里"等玩家"的两种步骤和应答系统（3.1），先做 GAS 自审。
  3. Core 动作名写进配置，默认按键归位到功能所属 mod，然后删 `mod.json` 依赖（3.11）。
  4. 补 C1、C2，删移动路线预览（3.10）。
  5. 12 个 mod 的下令改图（3.2），同时去掉 `LocalOrderSourceHelper` 对 `ControlDomainQuery` 的调用。
  6. 技能栏、Tab、瞄准、视角模式、小地图改配置（3.4–3.8）。
  7. `collection.command.source` 全部换掉，showcase 的 C# 直写改图写，选中指示器改 presenter 订阅（3.3）。
  8. 删 CoreInputMod 目录和 Core 里只为它存在的类型，架构测试禁止这些名字再出现。

## 6. UAT

```gherkin
Feature: 退役后 RTS 下令照常

  Scenario: 右键地面移动
    Given 我在 RTS 演示里框选了 3 个步兵
    When 我右键点地面
    Then 这 3 个步兵走到点击处
    And 没被选中的兵不动

  Scenario: 右键敌人攻击
    Given 我选中了 3 个步兵
    When 我右键点一个敌方坦克
    Then 这 3 个步兵朝坦克开火

Feature: 技能瞄准

  Scenario: 非智能施法先瞄准
    Given 我的施法方式是"先瞄准"
    When 我按 Q
    Then 英雄脚下出现 Q 的范围圈，圈跟着鼠标走
    When 我左键
    Then 技能在鼠标处放出，范围圈消失

  Scenario: 引导技能跟着鼠标走
    Given 我在放一个引导 3 秒的激光
    When 我在这 3 秒里移动鼠标
    Then 激光落点跟着鼠标走，超出射程时停在射程边上

  Scenario: 右键取消瞄准
    Given 我按 Q 进入瞄准
    When 我右键
    Then 范围圈消失，技能没放，蓝没扣

Feature: Tab 切目标

  Scenario: 按距离轮流
    Given 我附近有 3 个敌人，距离从近到远是甲、乙、丙
    When 我按 Tab
    Then 甲头上出现目标环
    When 我再按 Tab
    Then 目标环移到乙头上
    When 我按 Shift+Tab
    Then 目标环回到甲头上

  Scenario: 附近没有敌人
    Given 我附近没有敌人
    When 我按 Tab
    Then 什么都不出现

Feature: 技能栏

  Scenario: 选中英雄显示技能格子
    When 我选中英雄
    Then 屏幕下方出现他的 4 个技能格子，写着 Q W E R
    When 我放了 Q
    Then Q 格子变灰，冷却结束后恢复

Feature: 切视角

  Scenario: 战术切跟随
    Given 我在战术视角
    When 我按跟随视角键
    Then 镜头移到我的英雄身后并跟着他走

Feature: 选中指示器真的出现

  Scenario: MOBA 里选中英雄
    Given 我在 MOBA 演示里
    When 我点选我的英雄
    Then 英雄脚下出现选中环
    When 我点选另一个单位
    Then 选中环移到新单位脚下

Feature: 旧写法启动即失败

  Scenario: 技能里写了等玩家输入
    Given 某个技能配置里有一步 InputGate 或 TargetCollectionGate
    When 游戏加载
    Then 加载失败，报错写明是哪个技能的哪一步

Feature: 删掉依赖后按键不丢

  Scenario: 小地图按键
    Given 一个用小地图的 showcase 已经不依赖 CoreInputMod
    When 我按 M
    Then 小地图照常开关
```

## 附录 A：删依赖的前提

81 个 mod 在 `mod.json` 里依赖 CoreInputMod。按 main（`0cc88ce375`）逐个查，其中 38 个既不引用它的 C#，也不用它的配置约定（视角模式文件、下令映射、`collection.command.source`、技能栏开关、`core_input.*` presenter、它声明的动作）。但这 38 个也都经 `Default_Gameplay` 拿到它追加的默认按键，所以都要等 3.11 做完才能删依赖。

TW 删了直接依赖后，还经 `CameraProfilesMod` 间接依赖，要等 3.8 做完。`VisualTerrainEditorMod` 的项目文件里还有一条对 CoreInputMod 的引用，没用上，放在同一片删。
