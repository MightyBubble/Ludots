# input-01 reference · 命令意图档案

> 现状参考。第一性需求见 [input-01 PRD](../prd/input-01-command-intent.md)；配置说明见 [input-01 配置说明](../config/input-01-command-intent.md)。

## 1. 现状快照

- 档案形状：`profiles[].id` / `groupPolicy.kind`（内置 independent，mod 代码可注册新 kind，未知 kind 注册报错）/ `rules[].priority` + `actor{hasAbilityWithCategory, allTags, anyTags}` + `target{allTags, anyTags, stance, hasEntity 三态}` + `route{orderTypeKey | slot("byAbilityCategory:<category>" | "contextGroup:<id>")}`。
- 安装：引擎装配期注册并校验路由引用，随装 KnowledgeCommandTargetGate（目标条件走知识投影）。
- 消费：InputOrderMappingSystem 每帧经 `CommandIntentArbiter.ResolveActiveCommandIntent` 解析意图 id（实体挂载交互状态显式 > 玩家默认 CommandPref > 0 不路由），再逐演员过规则。
- 根资产 `assets/Input/command_intent_profiles.json`：`intent.command.default` 两规则（hasEntity true/false 均 → moveTo）。

## 2. 代码锚点

| 机制 | 位置 |
|---|---|
| 档案字段形状 | src/Core/Input/Interaction/CommandIntentProfile.cs:73-132 |
| 加载器 | src/Core/Input/Interaction/CommandIntentProfileConfigLoader.cs:31 |
| 编组策略注册与未知 kind 报错 | src/Core/Input/Interaction/CommandIntentProfileRegistry.cs:68-93,359-362 |
| 意图解析链（读实体交互状态） | src/Core/Input/Interaction/CommandIntentArbiter.cs:23-45 |
| 实体交互状态组件与对账写入 | src/Core/Input/Interaction/ActiveInteractionContext.cs · src/Core/Input/Interaction/AbilityExecInteractionContextSystem.cs |
| 安装点（含知识门） | src/Core/Engine/GameEngine.cs:1557-1568 |
| 消费调用 | src/Core/Input/Orders/InputOrderMappingSystem.cs（经 Arbiter 解析交互状态） |
| 根资产 | assets/Input/command_intent_profiles.json |

**相关文档**：[input-01 PRD](../prd/input-01-command-intent.md) · [ord-06 reference](ord-06-input-mappings.md)
