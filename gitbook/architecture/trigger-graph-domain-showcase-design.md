# TriggerGraph 域扩展示 showcase 设计

## 一句话与目标用户

让新玩家看见同一次火球施法如何同时驱动技能状态、英雄实体状态、Mod 覆盖和跨地图观察。

## 主循环

玩家在现有火球 arena 选择目标并施法。火球命中后，技能域图按施法者记录施法次数，英雄实体图记录效果事件；玩家切换到夜袭 override 入口后，地图覆盖把击杀阈值改为 2，并由 Mod 图和 global route 图继续接收生命周期事件。惊喜时刻是同一个火球动作同时改变技能与实体的两条状态，而地图外事件不会污染实体状态。

## 消融对照

| A | B |
|---|---|
| 只启用基底 FireballSharedMod | 同时启用 `Graph.Fireball.Ability.Moments` 与 `Graph.Fireball.Entity.Moments` |
| 夜袭不加载 `NightRaidOverrideMod`，阈值为 5 | 加载 override，阈值为 2，并启用 Mod 图与 global route |

两组使用同一张地图、同一套能力和同一套面板；差异来自声明的挂载图，不复制运行时规则。

## 解释层

- 火球 arena 现有状态面板继续显示生命、法力和攻击力。
- 实体域状态写入火球地图变量：`entity_ready`、`entity_effect_count`。
- 技能域状态写入同一张地图的变量：`ability_cast_count`、`ability_last_finished`。
- 夜袭面板显示 `kill_threshold`、`override_active`、`override_seen`；颜色和文案沿用现有夜袭面板，不新增第二份状态源。

## 旋钮清单

| 旋钮 | 运行时范围 | 玩家问题 |
|---|---:|---|
| 目标切换 | 英雄/目标 | 技能图是否只跟随当前施法者 |
| 施法次数 | 0..N | 每次 CastStarted 是否只增加一次 |
| 命中效果次数 | 0..N | Entity-domain 是否只接收自身事件 |
| 夜袭击杀阈值 | 2..5 | Mod 覆盖是否改变波次推进 |
| 地图焦点 | strategic/battle | global route 是否跨地图接收、local 是否隔离 |

## 场景结构

- 主演示：火球 arena，首屏提示“选择目标并施法，观察面板和实体/技能状态”。
- 子场景：夜袭基底、夜袭 override、跨地图 global route。
- 首屏引导：启动器进入 `panel_fireball_shared` 相关入口后，按现有火球快捷键施法；进入 `night_raid_override` 后按现有击杀工具并观察阈值差异。

## 门户资产

- 注册入口：`showcase.registry.json` 的 `night_raid_override`；火球入口沿用现有 FireballSharedMod 及其皮肤入口。
- 数据同源：技能图、实体图、Mod 图和地图 route 全部从各自正式 JSON 读取；不生成第二份预览数据。
- 运行取证：真实运行后把 AgentBridge 截图、状态快照和 Cucumber UAT 结果写入 `artifacts/agent-bridge/` 与本目录验收资产。

## 反向 API 审计

| 需求 | 归属 | 状态 |
|---|---|---|
| AbilityId 过滤和施法者 scope | Core TriggerGraph mount | 已实现 |
| Entity attachment 子树归属 | Core EntityTriggerGraphMounts | 已实现 |
| ModId 生命周期隔离 | Core TriggerManager/ModLoader | 已实现 |
| global route 广播和卸载回收 | Core TriggerManager | 已实现 |
| 把火球地图变量数值直接显示到既有 HUD | Fireball presenter/panel 数据绑定 | 待运行验收；不以日志冒充 HUD |
| 运行时切换四个旋钮 | Showcase 交互层 | 待补入口；没有入口前不能称可玩交付完成 |

## 交付边界与完成判据

本轮交付正式 TriggerGraph 域基建、火球技能/实体图声明、夜袭 Mod 图与 global route 声明、注册表和文档。状态为“已实现，待运行验收”：还缺带 AgentBridgeMod 的真实进程、两次 `pumpCount` 增长、玩家操作前后状态、截图以及旋钮控件的人验闭环；在这些证据补齐前，不宣称 showcase 可玩交付完成。

```gherkin
Feature: 玩家能看懂 TriggerGraph 域扩展

  Scenario: 火球同时改变技能与实体状态
    Given 我进入火球 arena 并看到英雄和目标
    When 我对目标施放火球
    Then 目标受到真实伤害
    And 英雄的技能施法计数增加
    And 英雄的实体效果计数增加

  Scenario: Mod 覆盖改变夜袭节奏
    Given 我从启动器进入夜袭 override
    When 我击杀两名第一波敌人
    Then 夜袭进入下一阶段
    And 面板显示阈值为 2

  Scenario: 跨地图图不污染实体状态
    Given strategic 和 battle 两张地图同时存在
    When battle 发出 global 观察事件
    Then global 图收到保留来源地图的事件
    And strategic 的实体域状态不被改写
```
