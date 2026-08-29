# input-03 reference · 交互上下文档案

> 现状参考。第一性需求见 [input-03 PRD](../prd/input-03-interaction-context.md)；配置说明见 [input-03 配置说明](../config/input-03-interaction-context.md)。

## 1. 现状快照

- 档案形状：`profiles[].id` / `activeCollectionKey` / `activeEntityViewKey` / `filterProfileId`（可选，空=直通）/ `inputContextId`（可选）/ `commandIntentId`（可选）。
- 消费：`AbilityExecInteractionContextSystem` 在声明了 `interactionContextProfile` 的能力 exec 期间把帧压上 `InteractionContextStack`、结束后按 ContextEntity 回收；同实体去重跟踪。
- 能力侧声明：abilities 的 exec 段写 `interactionContextProfile`（非空串校验在能力加载期）；档案名不存在在**执行开始时**抛错（非启动期）。
- 命令意图联动：仲裁器读实体挂载交互状态（`ActiveInteractionContext` 稀疏组件，挂控制域 representative）：活动上下文显式意图优先，其次玩家默认（CommandPref），挂载且零意图不路由不冒泡（DEC-14）。该组件由 `AbilityExecInteractionContextSystem` 在帧生命周期对账时按控制域投影（每域最顶层帧胜出；栈仍是生命周期正本，组件是读面）。命令源 owner 解析与 superweapon 帧身份同样读该组件的 contextEntity/contextId。
- 输入上下文联动：帧不再直接联动 IMC。`InputContextProjectionSystem` 每 tick 把帧的 `inputContextId` 需求并入对应座位的 (seatId, contextId, op) diff 命令流；帧归属哪张座位，由帧的 context entity 经 `ControlDomainQuery` 解析出的控制域代表决定，解析不到本机座位的帧不投影（#1306 路线④第一步，`InteractionContextInputContextBridge` 已删除）。
- 根资产 `assets/Input/interaction_context_profiles.json` 现为空表（`profiles: []`）。

## 2. 代码锚点

| 机制 | 位置 |
|---|---|
| 档案字段形状 | src/Core/Input/Interaction/InteractionContextProfile.cs:17-35 |
| 压栈/回收系统（类注释） | src/Core/Input/Interaction/AbilityExecInteractionContextSystem.cs:9-26 |
| 执行期档案缺失报错 | AbilityExecInteractionContextSystem.cs:124-129 |
| 能力侧声明解析 | src/Core/Gameplay/GAS/Config/AbilityExecLoader.cs:182-193 |
| 实体交互状态（DEC-14 读面） | src/Core/Input/Interaction/ActiveInteractionContext.cs |
| 意图解析与 owner 解析 | src/Core/Input/Interaction/CommandIntentArbiter.cs:23-45 · mods/CoreInputMod/Systems/InputInteractionContextAccessor.cs |
| 帧 → IMC 投影（含座位归属） | src/Core/Input/Systems/InputContextProjectionSystem.cs |
| 根资产 | assets/Input/interaction_context_profiles.json |

## 3. 栈退役路线图（#1306 路线④）

`InteractionContextStack` 保留交互状态机职责（帧生命周期、ownerToken、每帧 commandIntentId、集合/过滤路由），按 #1306 目标模型分步退役。本页记录剩余步骤：

1. 帧状态实体化：帧携带的 contextId / activeCollectionKey / filterProfileId / commandIntentId / contextEntity 升级为实体挂载的稀疏交互状态组件（照 `InteractionMode`、`CommandPref` 先例，graph op 写入、随存档携带）；`AbilityExecInteractionContextSystem` 改写组件而非压栈。**读面先行一步**：`ActiveInteractionContext` 稀疏组件（contextId / contextEntity / commandIntentProfileId）已由该系统对账写入，DEC-14 仲裁与命令源 owner 解析已改读组件；写侧仍是栈，压栈→组件的 SSOT 翻转与 collectionKey/filterProfileId 的实体化仍归本步。
2. 消费方迁移到实体读取：`CommandIntentArbiter` 与 `InputOrderMappingSystem` 的 DEC-14 仲裁链（栈顶意图 / `ActiveCollectionKeyId` / `OwnerToken` 作 `CastDispatchContext.GroupKey`）、`ContextBoundCollectionWriter` 的帧过滤与集合路由、`InputInteractionContextAccessor` 与 `LocalOrderSourceHelper` 的命令源 owner 解析。**已迁**：DEC-14 意图链、命令源 owner 解析（含 superweapon 帧身份）。**未迁**：`ActiveCollectionKeyId` / `OwnerToken` 消费面与 `ContextBoundCollectionWriter`。
3. 注册表搬迁：`FilterProfileIdRegistry` / `CommandIntentProfileIdRegistry` / `ContextIdRegistry` / `EntityViewKeyRegistry` / `InputContextIdRegistry` 的所有权离开栈本体。
4. `CastCommitProfileRegistry` 的 `pushFrame` / `popFrame` op 改写实体状态（该内核目前无生产驱动，迁移随接线一并做）。
5. 默认帧退役：引擎启动压入的保留帧（`interaction.context.default`，steady-state 命令源路由锚）改为数据声明的默认档案。DEC-14 侧已不依赖该帧（实体读面的缺席即 steady state）；本步收命令源集合路由锚。
6. 展示 mod 的帧身份检查迁移后，删除栈与 OwnerToken（superweapon_context 的运行时帧身份已改读实体状态；interaction showcase 面板仍读栈帧）。

**相关文档**：[input-03 PRD](../prd/input-03-interaction-context.md) · [input-01 reference](input-01-command-intent.md)
