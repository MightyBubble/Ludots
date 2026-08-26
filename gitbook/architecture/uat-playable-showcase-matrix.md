# UAT 可玩 Showcase 矩阵

本页给现有正式 Mod 分配玩家可观察的验收职责，不新建 UAT 私有 runtime。

## 矩阵

| UAT | Showcase | 地图 | 玩家 feature | 核心证据 |
| --- | --- | --- | --- | --- |
| UAT-1 | 未分配，等待 #643 | 无 | 精确物理车道尚未承诺 | 不虚构入口 |
| UAT-2A | `CapabilityStandardMassNavigationLargeWorld10kMod` | `mass_navigation` | 万级单位选择、移动、避障、到达 | MassNavigationFlow、presenter、10K evidence |
| UAT-2B | `FormationCapabilityShowcaseMod` | `formation_capability_showcase` | 选择方阵 anchor，右键后成员整体移动/重聚 | Command Router cluster fan-out、GAS/MovePlan/Mass typed chain |
| UAT-2C | `RoadNetworkShowcaseMod` | `road_network_showcase` | 道路规划与逐实体 MovePlan 执行 | `MovePlanExecutionMode.Individual` |
| UAT-3 | `RelationshipShowcaseMod` | `relationship_showcase` | 场景卡、状态变化、artifact | battle report、trace、path evidence |
| UAT-4 | `InteractionShowcaseMod` | `interaction_showcase_hub` | 统一输入、collection、HUD、entity info | 单一 command/selection truth |
| UAT-DataSchema | `ConfigurableDataSchemaSharedMod` (+ Native/Web) | `configurable_data_schema_workbench` | 作者改草稿后看见面板与校验变化 | `ConfigurableDataSchemaShowcaseAcceptanceTests`；设计见 `configurable-data-schema-showcase-design.md` |

## Formation 玩家场景

```gherkin
Feature: 玩家以方阵为操作单位

  Scenario: 选择方阵并移动
    Given 玩家进入 Formation Capability Showcase
    And 玩家选择一个或多个方阵 anchor
    When 玩家右键点击地面
    Then Command Router 在 CastDispatch 后展开每个 anchor 的 live members
    And 每个 anchor 的成员订单以原子 batch 激活
    And 成员通过通用 GAS Order 和 typed MovePlan 进入 MassNavigation
    And 玩家看到成员整体移动并在避障后重聚
```

通过标准：

- anchor 没有 `OrderBuffer` 或 `MassNavigationAgent`；
- members 有 `OrderBuffer`、`MassNavigationAgent` 和 MovePlan intent/result；
- 展开顺序由 slot 决定，suspended member 不参与；
- 任一 member 无效、重复、阻塞或容量不足时，不产生部分激活；
- 多 anchor 使用配置的 `groupMoveTargetLayout`，不会塌到同一目标；
- MassNavigation 不读取或反写 Order；
- `Arrived` 完成 order，`Failed` 取消 order 且不触发 continuation；
- 不验收已删除的 Q/E 假旋转。

## MassNavigation 与 Road 隔离

Formation/GAS 使用 `MovePlanExecutionMode.CommandGroup`；Road 使用 `MovePlanExecutionMode.Individual`。两个执行器必须互斥，未声明的 `None` 不得被静默执行。

## 推荐验证顺序

1. Formation command expansion 与原子 admission。
2. Formation lifecycle playable test。
3. MassNavigation typed command-group execution。
4. Road individual MovePlan regression。
5. 10K production path 与 presenter build。

## 禁止事项

- 不用源码字符串扫描代替行为验收；
- 不直接实例化已删除的 Order ingestion consumer 证明错误链路；
- 不把文档固定句、私有字段名或测试数量当作架构证据；
- 不恢复 Core Formation、专用 Formation order 或 UAT 私有 launcher。
