# Item / Equip / Backpack 架构

本文定义 Ludots 当前通用物品、装备、背包、仓储与交易子系统的正式挂靠方式。

目标：

* 用同一套 Core 基建覆盖 MOBA、ARPG、搜打撤离、枪支弹药、配件插槽与仓储交易。
* 保持“一切皆 Mod”“不造平行 runtime”“GAS / ECS / UI 单一真相”。
* 允许内容层通过配置自定义槽位、形状、格子、容器层级、被动、授能与交易规则。

## 1 总体结论

当前正式方案采用三层结构：

1. `Items/*` 配置层
   * 通过 `ConfigPipeline` + `config_catalog.json` 合并 `Items/shapes.json`、`Items/layouts.json`、`Items/definitions.json`。
2. Core runtime 层
   * 物品实例、容器实例、放置关系是 ECS 实体与组件。
   * 复杂布局校验、移动、拆分、旋转、交易由 `InventoryRuntimeService` 统一执行。
3. GAS / UI 联动层
   * 装备产生的属性、标签、buff 通过正式 GAS effect 生效与撤销。
   * 装备授能能力槽通过单独的 item-granted slot 层接入 `AbilitySlotResolver`。
   * 展示层统一走 `UiScene` / `ReactivePage`，不新增第二套 UI runtime。

## 2 复用挂靠点

复用基建：

* Registry
  * `TagRegistry`：物品分类、装备位语义、套装标签、槽位接受规则。
  * `EffectTemplateRegistry`：装备、配件、套装、消耗品产生的被动或触发效果。
  * `AbilityDefinitionRegistry`：物品授能能力定义。
  * `StringIntRegistry`：容器挂点、命名槽位等轻量字符串 ID。
* Pipeline
  * `ConfigPipeline.MergeFromCatalog(...)`：合并 `Items/*` 内容配置。
  * GAS effect pipeline：装备生效与失效不绕过 `EffectRequestQueue` / `EffectLifetimeSystem`。
* System
  * `SystemGroup.InputCollection`：同步 item-granted slot 与装备脏状态。
  * `SystemGroup.EffectProcessing`：装备被动 effect 正式进入 GAS 生命周期。
  * `SystemGroup.AttributeCalculation`：装备带来的属性修正继续经 `AttributeAggregatorSystem` / sink 落地。
* Mod
  * `UiScene` / `ReactivePage` 现有宿主模式：showcase 复用统一 UI runtime。
  * `EntityCommandPanelMod` 的单宿主思路：多面板展示不为每个窗口单独起 runtime。

## 3 数据模型

### 3.1 物品定义

`ItemDefinitionRegistry` 负责记录：

* 物品基础元数据：`id`、显示名、类别、堆叠上限
* 静态标签：例如 `Item.Weapon.Rifle`、`Equip.Slot.Head`、`Ammo.556`
* 网格形状：引用 `ItemShapeRegistry`
* 装备行为
  * 允许放入哪些命名槽位
  * 装备后附带哪些 GAS effect
  * 装备后授能哪些 ability slot
* 子容器挂点
  * 背包自身打开的储物格
  * 武器的 `magwell`、`optic`、`muzzle`、`underbarrel`

### 3.2 容器定义

`ItemLayoutRegistry` 负责记录：

* 网格尺寸与阻塞格
* 命名槽位
* 槽位接受规则
  * required-all tags
  * blocked-any tags
  * 是否只接受单件
* 展示锚点与 UI 布局元数据

容器本身也是实体。角色纸娃娃、背包、仓库、枪械插槽、弹匣、保险箱都只是不同布局的容器实例。

### 3.3 运行时实例

运行时的单一真相位于 ECS：

* `ItemInstanceCm`
  * `DefinitionId`
  * `StackCount`
  * `Charges`
  * `Durability`
* `ItemLocationCm`
  * 当前所在容器
  * 命名槽位或网格坐标
  * 朝向 / 旋转
* `ItemContainerCm`
  * `LayoutId`
  * 容器拥有者引用
  * 容器类型标记
* `MountedContainerOwnerCm`
  * 指向承载该子容器的物品实例与挂点 ID
* `ItemGrantedSlotBuffer`
  * 装备物品授能得到的能力槽覆盖

`InventoryRuntimeService` 只做校验、规划与结构变更回放，不额外保存第二份容器真相。

## 4 统一语义：装备只是移动

本子系统不单独发明“装备 runtime”。

统一规则：

* 把物品放入角色装备容器的命名槽位 = 装备
* 把物品放入背包容器的网格 = 入包
* 把物品放入仓库存储容器 = 存仓
* 把物品放入武器挂载容器 = 装配件 / 装弹匣

因此：

* 枪械配件槽与角色纸娃娃槽是同一种布局概念。
* 背包、胸挂、保险箱、仓库页是同一种容器概念。
* 掉落拾取、交易转移、本地仓储搬运都复用同一套 `TryMove / TryTransfer / TrySplit / TryRotate`。

## 5 GAS 联动

### 5.1 装备被动

装备不直接改属性。

正式路径：

1. `InventoryEquipmentGrantSyncSystem` 找出某个角色当前应该生效的装备物品。
2. 对每个装备物品的 `equipEffects`：
   * 缺失时，发布 `EffectRequest`
     * `Source = owner`
     * `Target = owner`
     * `TargetContext = item entity`
   * 多余时，取消对应 active effect entity
3. 该 effect 继续走 `OnApply / OnPeriod / OnExpire / OnRemove`。

这样：

* 属性修正仍由 `AttributeAggregatorSystem` 聚合。
* granted tags 仍由 `EffectGrantedTags` / `TagOps` 管理。
* 套装与状态联动仍通过 tag / effect / listener 表达。

### 5.2 物品授能能力槽

现有 `GrantedSlotBuffer` 主要服务临时 buff / 状态授能，不适合直接塞入装备层。

正式方案：

* 新增 `ItemGrantedSlotBuffer`
* `AbilitySlotResolver` 优先级变为：
  * transient `GrantedSlotBuffer`
  * `ItemGrantedSlotBuffer`
  * `AbilityFormSlotBuffer`
  * `AbilityStateBuffer`

这样装备换装不会与临时 buff 授能互相覆盖。

## 6 交易与搜打撤离语义

交易不是专用子系统，而是容器间的受规则约束转移：

* 卖给商人：玩家容器 -> 商人容器 / 结算池
* 买入物资：商人容器 -> 玩家仓储容器
* 撤离存入：战局背包容器 -> 藏身处仓储容器

价格、货币、声望、黑名单等规则由内容层定义，执行仍落在 `InventoryRuntimeService` 的原子操作上。

## 7 Showcase 设计

正式 showcase mod 需要同时证明以下场景：

* MOBA
  * 鞋子、神话装、主动道具、套装标签、授能技能槽
* ARPG
  * 头胸手脚、戒指护符、护身符仓位、词缀 buff
* 搜打撤离
  * 藏身处仓储、战术背包、保险箱、交易
* 枪械系统
  * 武器本体、弹匣、子弹、瞄具、枪口、下挂
  * 装填、退弹、开火消耗、附件改写能力或属性

showcase 必须提供：

* 可玩的 UI
* 可见的世界 / HUD 反馈
* 自动化 trace / battle-report / path
* Raylib 实机截图

当前 `ItemSystemShowcaseMod` 将玩家路径拆成四个短房间，而不是一个说明墙：

* `item_system_showcase_loadout_garage`
  * 纸娃娃、装备槽、背包格、item-granted ability、被动效果联动。
* `item_system_showcase_weapon_bench`
  * 武器配件槽、弹匣、共享弹药堆、装填与开火反馈。
* `item_system_showcase_forge_socket_lab`
  * 合成配方卡、材料仓格、宝石镶嵌槽、嵌入后 GAS 被动刷新。
  * 配方执行要求输出槽位可落位；扣料后若产物落位失败，必须原路回滚，不允许吞材料。
* `item_system_showcase_raid_loop`
  * 背包、保险箱、仓库、商人交易与弹药拆分。
  * 商人库存面板是只读浏览面板；买卖必须走显式交易动作，不能通过通用格子搬运绕过价格规则。

对应的玩家视角截图由 `scripts/acceptance/capture-item-system-showcase-rooms.ps1` 产出到
`artifacts/acceptance/item-system-showcase/room-screenshots/`。
`scripts/acceptance/run-item-system-showcase-acceptance.ps1` 默认会一并触发这些房间截图，保证主截图与分房间证据同步刷新。

为避免“大而全但无法单项验收”，当前交付采用“共享运行时 + 聚焦入口 mod”拆分：

* 共享运行时仍由 `mods/showcases/item_system/ItemSystemShowcaseMod/` 提供。
  * 这里承载 item/equip/backpack 的统一 ECS + GAS + UI 联动、四个房间的运行时逻辑，以及聚焦模式识别。
* 四个入口 mod 只负责设置启动房间与焦点包，不复制任何库存/装备运行时：
  * `mods/showcases/item_system/ItemLoadoutShowcaseMod/`
  * `mods/showcases/item_system/WeaponBenchShowcaseMod/`
  * `mods/showcases/item_system/ForgeSocketShowcaseMod/`
  * `mods/showcases/item_system/RaidLoopShowcaseMod/`
* 聚焦模式下会隐藏跨房间导航，只保留当前玩家任务所需的板块与操作，便于单 demo 验收。

独立入口的验收与截图脚本如下：

* `scripts/acceptance/run-item-loadout-showcase-acceptance.ps1`
  * 产出到 `artifacts/acceptance/item-loadout-showcase/`
* `scripts/acceptance/run-weapon-bench-showcase-acceptance.ps1`
  * 产出到 `artifacts/acceptance/weapon-bench-showcase/`
* `scripts/acceptance/run-forge-socket-showcase-acceptance.ps1`
  * 产出到 `artifacts/acceptance/forge-socket-showcase/`
* `scripts/acceptance/run-raid-loop-showcase-acceptance.ps1`
  * 产出到 `artifacts/acceptance/raid-loop-showcase/`

对应的 focused acceptance 用例位于
`src/Tests/GasTests/Production/ItemSystemShowcasePlayableAcceptanceTests.cs`：

* `ItemLoadoutShowcaseMod_StartsInLoadoutGarageWithoutCrossRoomNavigation`
* `WeaponBenchShowcaseMod_StartsInWeaponBenchWithoutCrossRoomNavigation`
* `ForgeSocketShowcaseMod_StartsInForgeLabWithoutCrossRoomNavigation`
* `RaidLoopShowcaseMod_StartsInRaidLoopWithoutCrossRoomNavigation`

## 8 约束

* 不创建第二套 buff / stat runtime；装备增益必须落到 GAS。
* 不创建第二套 UI runtime；背包、仓储、交易面板必须落到 `UiScene`。
* 不把网格校验写死在某个 demo mod；布局与形状来自配置注册表。
* 不把“枪械、背包、仓库、纸娃娃”拆成四套存储模型；统一是容器实例。

## 9 相关路径

* `src/Core/Gameplay/Items/`
* `assets/Configs/config_catalog.json`
* `docs/architecture/gas_layered_architecture.md`
* `docs/architecture/ui_runtime_architecture.md`
* `docs/architecture/runtime_entity_spawn_flow.md`
* `mods/showcases/item_system/ItemSystemShowcaseMod/`
* `mods/showcases/item_system/ItemLoadoutShowcaseMod/`
* `mods/showcases/item_system/WeaponBenchShowcaseMod/`
* `mods/showcases/item_system/ForgeSocketShowcaseMod/`
* `mods/showcases/item_system/RaidLoopShowcaseMod/`
