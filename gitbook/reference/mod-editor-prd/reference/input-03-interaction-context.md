# input-03 reference · 交互上下文档案

> 现状参考。第一性需求见 [input-03 PRD](../prd/input-03-interaction-context.md)；配置说明见 [input-03 配置说明](../config/input-03-interaction-context.md)。

## 1. 现状快照

- 档案形状：`profiles[].id` / `activeCollectionKey` / `activeEntityViewKey` / `filterProfileId`（可选，空=直通）/ `inputContextId`（可选）/ `commandIntentId`（可选）。
- 消费：`AbilityExecInteractionContextSystem` 在声明了 `interactionContextProfile` 的能力 exec 期间把帧压上 `InteractionContextStack`、结束后按 ContextEntity 回收；同实体去重跟踪。
- 能力侧声明：abilities 的 exec 段写 `interactionContextProfile`（非空串校验在能力加载期）；档案名不存在在**执行开始时**抛错（非启动期）。
- 命令意图联动：仲裁器读栈顶帧的意图 id，优先于控制方案默认。
- 根资产 `assets/Input/interaction_context_profiles.json` 现为空表（`profiles: []`）。

## 2. 代码锚点

| 机制 | 位置 |
|---|---|
| 档案字段形状 | src/Core/Input/Interaction/InteractionContextProfile.cs:17-35 |
| 压栈/回收系统（类注释） | src/Core/Input/Interaction/AbilityExecInteractionContextSystem.cs:7-19 |
| 执行期档案缺失报错 | AbilityExecInteractionContextSystem.cs:108-112 |
| 能力侧声明解析 | src/Core/Gameplay/GAS/Config/AbilityExecLoader.cs:182-193 |
| 栈顶意图优先 | src/Core/Input/Interaction/CommandIntentArbiter.cs:22-47 |
| 根资产 | assets/Input/interaction_context_profiles.json |

**相关文档**：[input-03 PRD](../prd/input-03-interaction-context.md) · [input-01 reference](input-01-command-intent.md)
