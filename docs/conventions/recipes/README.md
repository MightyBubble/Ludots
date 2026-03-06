# 任务 Recipes

## 按场景找 Recipe

先确定你要做什么，再按顺序执行对应的原子 Recipe。

| 我要做什么 | 按顺序执行 | 说明 |
|-----------|-----------|------|
| **做一个新英雄/角色** | [new_component] → [new_ability] → [new_input] → [new_order] → [new_presenter] | 属性组件 → 技能定义 → 按键绑定 → 命令映射 → 视觉表现 |
| **做一张新地图** | [new_map] → [new_trigger] → [new_config] | 地图 JSON → 加载事件 → 地图专属配置 |
| **加一种新操作方式** | [new_input] → [new_order] | 按键绑定 → 命令类型注册 |
| **加一个 gameplay 机制** | [new_system] → [new_component] → [new_config] | System 逻辑 → 数据组件 → 配置驱动 |
| **加一个视觉特效/UI** | [new_presenter] | Performer 定义（JSON 或代码） |
| **做一个独立功能模块** | [new_mod] → 按需组合其他 Recipe | 先建 Mod 骨架，再填充功能 |
| **加一种新配置数据** | [new_config] | 接入 ConfigPipeline |
| **响应一个生命周期事件** | [new_trigger] | OnEvent 回调 |
| **加一个新技能（已有英雄）** | [new_ability] | 纯 JSON，不需要写 C# |

## 原子 Recipe 索引

每篇 Recipe 是一个不可再拆的最小任务单元。

| Recipe | 产出物 | 核心挂靠点 |
|--------|-------|-----------|
| [new_mod](new_mod.md) | Mod 骨架（mod.json + Entry） | `SystemFactoryRegistry`、`TriggerManager` |
| [new_ability](new_ability.md) | GAS 技能（JSON） | `AbilityDefinitionRegistry`、`EffectTemplateRegistry` |
| [new_system](new_system.md) | ECS System | `SystemGroup`、`SystemFactoryRegistry` |
| [new_component](new_component.md) | ECS 组件（Cm/Tag/Event） | Arch ECS |
| [new_order](new_order.md) | 交互/命令类型 | `OrderTypeRegistry`、`InputOrderMappingSystem` |
| [new_presenter](new_presenter.md) | 表现/UI Performer | `PerformerDefinitionRegistry` |
| [new_config](new_config.md) | 配置类型 | `ConfigCatalog`、`ConfigPipeline` |
| [new_trigger](new_trigger.md) | 事件触发器 | `TriggerManager` |
| [new_map](new_map.md) | 地图 | `MapManager`、`ConfigPipeline` |
| [new_input](new_input.md) | 输入动作/按键 | `PlayerInputHandler`、`InputConfigPipelineLoader` |

## 使用原则

*   场景中的 Recipe 顺序不是绝对的，但**依赖关系是确定的**（如 order 依赖 input）
*   如果场景中只需要部分 Recipe，跳过不需要的即可
*   每个 Recipe 都可以独立执行——它们是原子的
*   如果你的场景不在上表中，按"我需要什么产出物"在原子索引中查找
