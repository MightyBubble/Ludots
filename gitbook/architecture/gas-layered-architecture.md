# GAS 分层架构

本页是 Ability、Effect 与 GAS Graph 组合关系的正式入口。目标是让玩法变化停留在 Mod 数据中，让 Core 只提供稳定、可复用的执行能力。

## 1. 概述

GAS 的组合顺序是：

`Graph -> 可选 PresetType -> EffectTemplate`

- Graph 描述“怎么做”：操作顺序、分支、阶段与可复用行为。
- PresetType 是可选的编写简写，只提供稳定的默认阶段处理器。
- EffectTemplate 是具体内容实例，拥有持续时间、标签、参数和阶段 Graph 引用。

运行时执行的是已经编译的 EffectTemplate。PresetType 不能成为英雄、技能、地图或玩法模式的目录。

Ability 时间线只能通过 `EffectClip` 或 `EffectSignal` 提交 EffectTemplate。玩法 Graph 必须由 EffectTemplate 的 `phaseGraphs` 引用并进入统一的校验、预算和事务执行计划；Ability 不提供直接执行 Effect Graph 的时间线条目。

## 2. 结构

### Graph

Graph 组合通用原子操作。原子操作只能表达不可再拆的引擎能力，例如修改属性、查询目标、派发 Effect 或生成实体。

Graph 配置必须声明受支持的 `kind`，通过严格 JSON 加载和完整的 opcode 元数据检查。无效配置在 Mod 加载阶段失败，不能进入游戏后再采用默认行为。

### PresetType

PresetType 可以为常见写法提供默认 Main 处理器，例如普通伤害或周期属性修改。它不拥有具体玩法参数，也不阻止模板替换 Main。

新增玩法变体不得新增 Core `EffectPresetType`。只有跨多个游戏长期稳定、且确实减少重复编写的通用简写，才允许经过 GAS 组合门禁后进入 preset 目录。

### EffectTemplate

EffectTemplate 是内容 SSOT，负责：

- lifetime、duration、period 与 clock；
- 标签、modifier、目标查询与派发参数；
- `phaseGraphs` 的 Pre、Main、Post；
- 具体能力参数，例如 modifier 数值、事件标签和目标派发参数。

当 `phaseGraphs.<phase>.main` 存在时，它替换 preset 的默认 Main。没有 Main 且没有 `skipMain` 时，才使用 preset 默认值。`main` 与 `skipMain=true` 同时出现属于配置错误。

## 3. 详情

Effect 的阶段顺序为：

`OnPropose -> OnCalculate -> OnResolve -> OnHit -> OnApply -> OnPeriod -> OnExpire -> OnRemove`

每个阶段按 `Pre -> Main -> Post` 执行。模板 Main 的优先级高于 preset 默认 Main，因此 Graph 是行为组合的最终控制面。

宏观执行顺序由 `SystemGroup` 固定：

- `AbilityActivation`
- `EffectProcessing`
- `AttributeCalculation`
- `DeferredTriggerCollection`

瞬时 Effect 与持久 Effect 都必须在提交前完成容量和依赖检查。阶段中产生的属性、队列、生成和表现写入属于同一事务；失败时不能留下部分结果。

## 4. 场景

### 带事件的伤害变体

Core 提供属性修改、事件发布与 Effect 派发等原子操作。Mod Graph 负责组合伤害与命中事件，EffectTemplate 提供具体 modifier、事件标签和阶段引用。新技能只新增或复用 Graph 与模板数据。

尚未纳入 Effect 事务或独占原子执行计划的能力不得发布为正式 Effect Graph。当前 `RevealArea` 与 `DecayRevealArea` 仍属于未认证能力，不能作为 EffectTemplate 的阶段 Graph 使用。

### 普通伤害的特殊变体

模板可以采用 `InstantDamage` 的默认 Main，也可以在 `phaseGraphs.OnApply.main` 指向自定义 Graph，完全替换默认伤害步骤。替换不需要新增 preset 或修改 loader 分支。

### 生命周期部署

`Graph.Lifecycle.DeployConsumeSource` 组合多个生命周期原子操作。PresetType 只作为可选简写引用该 Graph，不承载部署规则本身。

## 5. 边界

- 禁止用 Core enum 表达英雄、技能、地图或模式变体。
- 禁止在 `EffectTemplateLoader` 增加只服务某个具体玩法名称的分支。
- 禁止未知 JSON 字段、未知 Graph kind、未知 opcode 或未写验证结果被默认接受。
- 禁止 registry 重复注册后采用最后一次写入。
- 禁止固定容量溢出时静默丢弃、截断或在热路径扩容。
- 禁止阶段执行直接绕过事务写入外部系统。

## 6. UAT

```gherkin
Feature: 用数据组合新的技能效果

  Scenario: Mod 作者创建带命中事件的伤害技能
    Given Core 已提供属性修改与事件发布原子操作
    And Mod 中有组合伤害与命中事件的 Graph
    And EffectTemplate 配置了 modifier、事件标签与阶段 Graph
    When 玩家释放该技能
    Then 目标按模板参数受到一次伤害
    And 命中事件与伤害在同一事务中发布
    And Core 不需要新增 EffectPresetType

  Scenario: 模板替换 preset 的默认行为
    Given EffectTemplate 选择了一个通用 preset
    And 该模板为 OnApply 声明了 main Graph
    When Effect 执行 OnApply
    Then 只执行模板声明的 main Graph
    And 不执行 preset 的默认 Main

  Scenario: 无效 Graph 阻止 Mod 启动
    Given Mod 中的 Graph 使用未知字段、未知 kind 或未知 opcode
    When 游戏加载该 Mod
    Then 加载失败并指出具体资产和合同错误
    And 游戏不会使用默认行为继续运行
```

相关入口：

- [GAS、订单与输入运行时合同](gas-order-input-runtime-contract.md)
- [实体生命周期原子操作](entity-lifecycle-atomic-ops.md)
- [Graph Query Services](../reference/graph-query-services.md)
