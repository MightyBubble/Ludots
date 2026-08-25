# Entity TriggerGraph 聚落作用域设计

## 1. 概述

实体 attachment 能承载“聚落”这种实体域 aggregate，但它不是第二个 `World`、`MapSession` 或事件总线。聚落根实体仍属于当前地图；模板 children 通过现有物化和 attachment 管线形成一棵有向无环的实体树，根实体的多个 TriggerGraph 负责规则编排。

本片解决两个可验证的缺口：一个实体声明多个 TriggerGraph 时保持声明顺序；聚落根图只接收根实体或 attachment 子树产生的带实体事件，不误收同地图其他实体事件。

## 2. 结构

```text
MapSession (唯一模拟世界/事件总线)
└── EntityTemplate: settlement_root
    ├── EntityTriggerGraphAggregateRoot {}
    ├── TriggerGraphs: [lifecycle, population, economy]
    └── children[] -> MaterializeTemplate + AttachmentOps.Attach
        └── child entity may declare its own TriggerGraphs[]
```

`EntityTemplate.TriggerGraphs` 是多个 graph id 的有序列表。每个 graph 的 entries 仍按 graph 资产声明顺序挂载。聚落能力由图的连线和已有 atomic op 组合表达，不新增 preset 开关。

## 3. 详情

### 3.1 多图合同

- 列表顺序是挂载顺序，稳定用于诊断和同事件执行顺序。
- 重复 graph id 由模板加载校验拒绝；空白 id 也拒绝。
- 每个 graph 的每个 entry 仍是独立 mount；一个 graph 失败时整个实体物化失败，避免半棵规则树继续运行。
- graph 仍登记到实体所属 `MapSession` 的 `TriggerManager`，地图卸载统一回收。

### 3.2 聚落根与事件路由

模板根实体显式声明 `EntityTriggerGraphAggregateRoot` 组件后，其实体域 TriggerGraph 对带 `SourceEntity` 或 `TargetEntity` 的事件按 attachment 树判定归属：根实体自身或任意后代命中；树外实体被拒绝。未声明该组件的实体图只接收自身事件。没有实体载荷的地图节拍事件仍按既有地图节拍广播给存活实体图。

判定只读 `ChildOf` 向上链，沿用 attachment 的无环合同；不写结构、不分配、不创建第二事件总线。父实体死亡不会自动销毁子实体，仍遵循 attachment 的 orphan cleanup 语义；地图卸载先丢弃实体挂载，再清理实体。

### 3.3 生命周期与边界

- `EntitySpawned`: 实体自己的 entry 在物化当拍执行。
- `Gas.Event.*` / `Ability.*` / `Effect.*`: 通过现有地图事件桥进入；根图按 attachment 树过滤。
- `EntityDied`: 自身挂载在销毁当拍执行；子树聚合死亡统计使用带 source 的地图事件或显式图连线，不把父死隐式解释成子死。
- `MapId`: 仍是实体挂载注册前提；没有活动地图会 fail closed。
- 容量上限继续由 `ChildrenBuffer` 和现有 TriggerManager/VM 容量合同负责。

## 4. 域扩展合同

### 4.1 技能域

`GAS/abilities.json` 的 `triggerGraphs` 是技能定义拥有的有序 graph id 列表。地图加载时，技能图进入该地图已有 `TriggerManager`，每个 entry 仍是一个独立挂载；事件上下文中的 `MapTrigger.AbilityId` 必须与定义 id 相等才会执行。施法者来自 `MapTrigger.SourceEntity`，因此技能图不创建 caster 专属事件总线，也不在施法热路径注册/注销结构。

### 4.2 Mod 域

`mod.json` 的 `triggerGraphs` 是 Mod 生命周期图的有序列表。Mod 图注册到同一 `TriggerManager` 的全局事件索引，先注册后发出该 Mod 的 `ModLoaded`（上下文 `MapTriggerEventPayloadKeys.ModId`）；带 `ModId` 的事件只由同名 Mod 图接收，缺少或不匹配 `ModId` 的事件 fail closed。Mod 图不绑定实体或地图；挂起的 Mod 图由 `DeferredTriggerCollection` 固定步中的引擎级 `ModTriggerResume` 脉冲继续执行，卸载时先移除挂载再调用 Mod 的卸载生命周期。

### 4.3 跨地图路由

地图图挂载对象可以显式声明 `route: "global"`。默认 `local` 只匹配事件来源地图；`global` 通过同一 `TriggerManager` 的全局地图索引广播给已注册地图图，但保留原始 `ContextKeys.MapId`、SourceEntity、TargetEntity 和 AbilityId。实体域禁止 global route；跨地图是事件路由策略，不是第二个 `World`、`MapSession` 或 VM。地图卸载会同步移除该地图的 global mount。

### 4.4 统一生命周期

- `map` / `entity` / `ability` 图随所属地图注册和回收。
- `mod` 图随 Mod 加载、卸载回收。
- 一个实体、一个技能、一个 Mod 均可声明多张图；列表顺序稳定，重复 id fail-closed。
- 所有域复用 `TriggerManager`、`TriggerGraphMountTrigger` 和同一 Graph VM；未知域、未知路由、缺图、错误过滤器均抛明确错误。

## 5. 场景

1. 聚落根模板挂三张图：出生初始化、人口计数、资源规则。
2. `children[]` 生成居民、仓库和哨塔；每个子模板可以再挂自己的反应图。
3. 居民受到 GAS 命中事件时，根图可通过 attachment 作用域观察并更新地图变量；地图外实体的同名事件不会污染聚落状态。
4. 根实体被销毁时只执行根的死亡图；子实体是否销毁由显式 graph/lifecycle op 决定。

## 6. 边界

- 不创建独立 ECS `World`、`MapSession`、节拍或事件总线。
- 不为 attachment 增加 TriggerGraph 生命周期；attachment 只拥有关系、位姿、写权和成员身份。
- 不承诺每个实体独立 tick；实体图继续共享所属地图节拍。
- attachment 树必须无环；损坏关系在判定时 fail closed。
- “同事件跨图顺序”只保证实体模板列表顺序和图内 entry 顺序；不同实体之间不构成业务顺序合同。

## 7. UAT

```gherkin
Feature: EntityTemplate 聚落作用域的多 TriggerGraph

Scenario: 一个实体按声明顺序挂载多张图
  Given 一个实体模板声明 lifecycle、population、economy 三张 TriggerGraph
  When 该模板在活动地图中被物化
  Then 三张图都在同一实体上注册
  And 注册顺序与模板列表顺序一致
  And 任一图未注册成功时物化失败且不留下半套挂载

Scenario: 聚落根图观察 attachment 子实体事件
  Given 聚落根模板声明 EntityTriggerGraphAggregateRoot
  And 一个居民实体通过模板 children 挂接到该根实体
  When 居民产生一个带 SourceEntity 的 GAS 事件
  Then 根实体的聚落图收到该事件并更新聚落状态
  And 同地图未挂接实体产生相同事件时聚落状态不变

Scenario: 父实体死亡不隐式销毁子实体
  Given 聚落根实体与居民实体已通过 ChildOf 绑定
  When 根实体被销毁
  Then 根实体的 EntityDied 图执行一次
  And 居民实体的存活与否只由显式生命周期图决定

Scenario: 地图卸载收回整棵规则树
  Given 聚落根及其子实体已注册多个实体域图
  When 所属地图被卸载
  Then 所有实体域图挂载被移除
  And 卸载过程不再触发 EntityDied 图
```

## 8. UAT 计划与完成判据

### Slice A - 合同与作用域

- 多图顺序、重复 id、实体事件归属测试。
- `EntityTriggerGraphAggregateRoot` 组件进入模板 authoring 注册表。

### Slice B - 组合落地

- 使用现有 `MaterializeTemplate`、`AttachmentOps.Attach` 和图节点创建最小聚落。
- 增加 headless battle report、trace 和 path 资产。

### Slice C - Showcase

- 以真实 Mod/地图展示聚落根、居民事件、地图外对照和 HUD 状态。
- 通过 Agent Bridge 观察、驱动、验证，并保存真实截图。

完成判据：目标测试与构建通过；模板多图和 attachment 树事件路由有 headless 证据；运行时验收确认进程属于目标 Mod/地图，且未引入第二世界或 fallback。

### 域扩展 UAT

```gherkin
Feature: TriggerGraph 的技能、Mod 与跨地图域

Scenario: 一个技能声明多张图并按技能号过滤
  Given 一个技能定义声明 start、commit、finish 三张图
  And 两个实体在同一张地图上拥有不同技能
  When 其中一个实体施放该技能
  Then 三张图按声明顺序注册
  And 只有匹配 AbilityId 的图收到 Ability.CastStarted、Ability.CastCommitted 或 Ability.CastFinished
  And 另一个技能的事件不会触发这组图

Scenario: Mod 图只响应自己的 ModLoaded
  Given Mod A 声明一张 ModLoaded TriggerGraph
  And Mod B 也成功加载
  When Mod B 完成加载
  Then Mod A 的图不执行
  When Mod A 完成加载
  Then Mod A 的图执行一次并看到 ModId=A
  And 卸载 Mod A 后再次发出 ModLoaded 不再执行该图

Scenario: 跨地图图显式广播
  Given 地图一声明 route=global 的 TriggerGraph
  And 地图二声明同一事件的 local TriggerGraph
  When 地图二产生一个带来源地图和实体的事件
  Then 地图一的 global 图收到事件
  And local 图只在其所属地图收到事件
  And 卸载地图一后 global 图不再收到事件
```
