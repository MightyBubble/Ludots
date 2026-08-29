# input-03 reference · 交互上下文档案

> 现状参考。第一性需求见 [input-03 PRD](../prd/input-03-interaction-context.md)；配置说明见 [input-03 配置说明](../config/input-03-interaction-context.md)。

## 1. 现状快照

- 档案形状：`profiles[].id` / `activeCollectionKey` / `activeEntityViewKey` / `filterProfileId`（可选，空=直通）/ `inputContextId`（可选）/ `commandIntentId`（可选）。
- 消费：`AbilityExecInteractionContextSystem` 在声明了 `interactionContextProfile` 的能力 exec 期间把帧压上 `InteractionContextStack`、结束后按 ContextEntity 回收；同实体去重跟踪。
- 能力侧声明：abilities 的 exec 段写 `interactionContextProfile`（非空串校验在能力加载期）；档案名不存在在**执行开始时**抛错（非启动期）。
- 命令意图联动：仲裁器读栈顶帧的意图 id，优先于控制方案默认。
- 输入上下文联动：帧不再直接联动 IMC。`InputContextProjectionSystem` 每 tick 把帧的 `inputContextId` 需求并入对应座位的 (seatId, contextId, op) diff 命令流；帧归属哪张座位，由帧的 context entity 经 `ControlDomainQuery` 解析出的控制域代表决定，解析不到本机座位的帧不投影（#1306 路线④第一步，`InteractionContextInputContextBridge` 已删除）。
- 根资产 `assets/Input/interaction_context_profiles.json` 现为空表（`profiles: []`）。

## 2. 代码锚点

| 机制 | 位置 |
|---|---|
| 档案字段形状 | src/Core/Input/Interaction/InteractionContextProfile.cs:17-35 |
| 压栈/回收系统（类注释） | src/Core/Input/Interaction/AbilityExecInteractionContextSystem.cs:7-19 |
| 执行期档案缺失报错 | AbilityExecInteractionContextSystem.cs:108-112 |
| 能力侧声明解析 | src/Core/Gameplay/GAS/Config/AbilityExecLoader.cs:182-193 |
| 栈顶意图优先 | src/Core/Input/Interaction/CommandIntentArbiter.cs:22-47 |
| 帧 → IMC 投影（含座位归属） | src/Core/Input/Systems/InputContextProjectionSystem.cs |
| 根资产 | assets/Input/interaction_context_profiles.json |

## 3. 栈退役路线图（#1306 路线④）

`InteractionContextStack` 保留交互状态机职责（帧生命周期、ownerToken、每帧 commandIntentId、集合/过滤路由），按 #1306 目标模型分步退役。本页记录剩余步骤：

1. 帧状态实体化：帧携带的 contextId / activeCollectionKey / filterProfileId / commandIntentId / contextEntity 升级为实体挂载的稀疏交互状态组件（照 `InteractionMode`、`CommandPref` 先例，graph op 写入、随存档携带）；`AbilityExecInteractionContextSystem` 改写组件而非压栈。
2. 消费方迁移到实体读取：`CommandIntentArbiter` 与 `InputOrderMappingSystem` 的 DEC-14 仲裁链（栈顶意图 / `ActiveCollectionKeyId` / `OwnerToken` 作 `CastDispatchContext.GroupKey`）、`ContextBoundCollectionWriter` 的帧过滤与集合路由、`InputInteractionContextAccessor` 与 `LocalOrderSourceHelper` 的命令源 owner 解析。
3. 注册表搬迁：`FilterProfileIdRegistry` / `CommandIntentProfileIdRegistry` / `ContextIdRegistry` / `EntityViewKeyRegistry` / `InputContextIdRegistry` 的所有权离开栈本体。
4. `CastCommitProfileRegistry` 的 `pushFrame` / `popFrame` op 改写实体状态（该内核目前无生产驱动，迁移随接线一并做）。
5. 默认帧退役：引擎启动压入的保留帧（`interaction.context.default`，steady-state 命令源路由锚）改为数据声明的默认档案。
6. 展示 mod 的帧身份检查（superweapon_context、interaction showcase 面板）迁移后，删除栈与 OwnerToken。

**相关文档**：[input-03 PRD](../prd/input-03-interaction-context.md) · [input-01 reference](input-01-command-intent.md)
