## 背景

对当前 Input → Interaction → Order → Ability 配置链做了一次横向审计。整体的数据驱动方向是正确的，但目前的 authoring model 基本直接暴露 runtime plumbing，导致一个 InputOrderMapping 同时承担输入触发、施法交互、目标解析、命令路由、参数 ABI、批量单位布局和用户覆盖等职责。

结果是：配置虽然“可数据驱动”，但领域所有权不清晰，多个位置可以描述同一语义，effective value 只能通过阅读运行时分支顺序才能确定；编辑器也只能照搬深层嵌套结构，难以提供类型安全和上下文感知的配置体验。

本 issue 关注 **bounded context / SSOT / authoring contract 收敛**，不重复 #275 的 fail-fast 修复，也不重复 #708 的 OrderQueue/Order payload 内部实现审计。

## 审计发现

### 1. InputOrderMapping 已成为跨领域聚合根

单条 mapping 当前同时包含：

- 输入语义：`actionId / trigger / doubleTapWindowSeconds / heldPolicy`
- Order 选择：`orderTypeKey / actorOrderRouting`
- 底层参数：`argsTemplate.i0..i3/f0..f3`
- actor/target 来源：`actorCollectionKey / targetCollectionKey / requireTarget / targetType`
- 提交策略：`modifierBehavior`
- 施法交互：`isSkillMapping / castModeOverride`
- 自动选目标：`autoTargetPolicy / cursorTargetPolicy / ranges`

根配置又拥有全局 `interactionMode`、`groupMoveTargetLayout` 和 `userOverrides`。

这些字段属于至少五个不同领域：Input Binding、Cast Interaction、Command Intent、Order Contract、Formation/Target Layout。它们聚合在一处后，任何新增 interaction mode、target policy 或 order argument 都会继续扩大 mapping。

### 2. Ability.input 与 InputOrderMapping 存在双重所有权

Ability loader 可再次声明并编译：

- `input.trigger`
- `input.heldPolicy`
- `input.castModeOverride`
- `input.autoTargetPolicy / autoTargetRangeCm`

运行时再通过 skill mapping override provider 将 Ability slot 上的值覆盖到 mapping；同时 cast mode 还受根级 `interactionMode`、mapping override 和 client preference/profile 链影响。

这造成两个问题：

1. “按键如何触发”同时属于 Input mapping 和 Ability definition；
2. effective policy 的优先级是运行时控制流知识，而不是一个显式、可验证的领域契约。

建议先给出一张 canonical ownership + resolution matrix。至少应区分：

- device/action trigger：Input Binding 所有；
- ability cast requirements/default interaction：Ability 或 CastInteractionPolicy 所有；
- player preference：ClientCastPreference 所有；
- context temporary override：InteractionContext 所有。

### 3. isSkillMapping 是技术分支，不是领域概念

`isSkillMapping` 不引用 Ability，也不表达一种稳定业务类型；它只是决定 mapping 是否进入 InteractionMode/aim/ability override 分支。

实际 Ability 选择通常由另一条隐含协议完成：

- `orderTypeKey = castAbility`
- `argsTemplate.i0 = abilitySlotIndex`

因此“这是 Skill”与“施放哪个 Ability”由一个 bool 和一个无名整数槽拼起来。代码和配置中同时使用 Skill / Ability 术语，也容易形成新的 ubiquitous language 漂移。

建议替换为显式语义，例如：

- `activationKind: Direct | CastInteraction`；或
- `interactionPolicyRef`；或
- 由 OrderTypeContract 声明该 order 需要 cast interaction，加载期派生，不再手填 bool。

产品和领域层统一使用 Ability；“Skill”仅在具体游戏 UI 词汇需要时出现。

### 4. argsTemplate 是 runtime ABI 泄漏

`OrderArgsTemplate` 直接暴露 `i0..i3 / f0..f3`。不同 order/runtime resolver 再自行解释槽位，例如 castAbility/ContextScored 等路径把 `I0` 解释为 ability slot index。

这保留了紧凑 runtime representation，但不是合格的 authoring contract：

- JSON 无法说明 `i0` 的业务语义；
- loader 无法基于 order type 完整验证参数名、类型和范围；
- 编辑器无法生成具名、上下文相关的字段；
- order handler 与配置作者依赖口头约定共享 ABI。

建议由 Order Type 注册表提供参数 schema，例如：

`castAbility { abilitySlot: int }`

加载期将具名参数编译为内部 `OrderArgs` 槽位。槽位可以继续存在于 runtime，但不应是 authored JSON 的公共模型。

### 5. actorOrderRouting 与 CommandIntent 是竞争的路由模型

二者都会根据 actor 能力/标签和目标上下文选择 order：

- `actorOrderRouting`：逐 actor 匹配 tags、ability slot/id，取最高 priority candidate；
- `CommandIntentProfile`：按 actor/target predicates 和 priority 产生 route，并支持 group/dispatch 流程。

更关键的是，当前 mapping 主循环先判断 `IsCommandAction(actionId)` 并直接进入 `SubmitCommandIntentOrder`，之后才可能执行 `ActorOrderRouting`。因此同一 mapping 同时配置两套语义时，实际使用哪一套取决于 command action wiring；该 precedence 并未由配置类型表达。

示例配置中已经存在 `Command + actorOrderRouting`，这使 author 很难从 JSON 判断它是否会被 CommandIntent 分支短路。

建议：

- 将 per-actor capability routing 合并进 CommandIntent 的统一 rule model；
- Input mapping 只引用 intent/profile，不再内嵌另一套 candidate DSL；
- 若确需两种模式，使用显式 discriminated union，加载期保证二选一，而不是依靠 if 顺序决定。

相关：#493 已覆盖 ActorOrderRouting/AbilitySlotResolver 的局部 SSOT 问题；本 issue 处理其上层领域归属。

### 6. groupMoveTargetLayout 属于 Formation/Target Layout，不属于 Input Mapping

当前根配置仅支持 `None | Grid`，再用 `orderTypeKeys` 白名单和 `spacingCm` 控制。运行时仅对多 actor、Position target、non-skill mapping 应用偏移。

这是具体的 group target layout/formation policy，却被放入 InputOrderMappingConfig，并且 Grid 是 Core 中唯一枚举实现。未来 Line/Wedge/Circle/formation capability 会继续扩大 Input 配置和 Core 分支。

建议独立为可引用策略：

`targetLayoutProfileRef -> None | Grid | Formation | ...`

由 CommandIntent route、Order contract 或 ControlScheme 选择；具体 planner 属于 Formation/Target Layout bounded context。

### 7. 字符串引用与深层嵌套缺少统一 contract SSOT

`actionId / orderTypeKey / collectionKey / profileId / abilityIdKey` 等引用散落在多个 loader 中独立校验。当前大量嵌套对象虽然映射了 runtime 数据结构，却没有统一、可发现的 authoring schema，因此：

- registry 是 runtime SSOT，但不是 editor/JSON authoring SSOT；
- enum、引用候选、条件可见性和参数含义容易在 C#、JSON、文档、编辑器之间漂移；
- 每个 loader 各自实现局部 validation，无法自动生成完整编辑契约。

建议从 C# authoring contracts/registries 生成或导出机器可读 schema（JSON Schema 或等价 contract metadata），供 loader validation、文档和编辑器共同消费。

## 建议的领域边界

### Input Binding

只负责：device path/context → Action，以及 Action 的 trigger semantics。

### Cast Interaction

负责：aim/target acquisition/cast mode/commit policy；通过显式 profile/ref 组合 Ability default、context override 和 player preference。

### Command Intent

负责：actor + target facts → typed route；统一当前 CommandIntent 与 actorOrderRouting。

### Order Contract

负责：order type、具名参数 schema、target requirement、submit capabilities；加载期编译到紧凑 runtime OrderArgs。

### Formation / Target Layout

负责：一个语义目标如何展开为多 actor 空间目标；不由 Input Mapping 枚举具体 Grid 实现。

### Ability

负责 Ability 自身激活/执行/目标需求，不持有设备按键触发细节；若需要交互默认值，引用 CastInteractionPolicy，而不是复制 Input mapping 字段。

## 建议迁移顺序

1. **先写 ownership ADR**：列出 trigger、held、cast mode、target policy、order route、argument、layout 的 canonical owner 与 override 顺序。
2. **引入 OrderTypeContract**：先让 `castAbility` 使用具名 `abilitySlot`，内部仍编译到 I0，保留兼容 loader。
3. **移除 isSkillMapping authoring bool**：改为显式 interaction kind/ref 或从 contract 派生。
4. **合并 actorOrderRouting → CommandIntent rules**：为旧 JSON 提供迁移诊断；禁止同一 action 隐式进入两条竞争路由。
5. **抽离 TargetLayoutProfile**：保持现有 Grid planner 行为不变，仅调整所有权。
6. **生成 editor/schema metadata**：让枚举、引用、参数和条件可见性来自同一 contract。
7. **最后压平 authored JSON**：runtime model 可以深，但 authoring model 应按引用组合 bounded contexts，而不是继续嵌套 runtime object graph。

## 验收标准

- [ ] 有明确 ADR/ownership matrix，任一语义只有一个 canonical authoring owner；覆盖所有 override precedence。
- [ ] authored JSON 不再暴露 `i0..i3/f0..f3`；至少 castAbility 使用具名 `abilitySlot` 并在加载期编译。
- [ ] `isSkillMapping` 从 authoring contract 移除，替换为明确领域语义或由 contract 派生。
- [ ] Command action 不再同时存在两套隐式竞争路由；配置类型在加载期保证唯一决策路径。
- [ ] `groupMoveTargetLayout` 从 InputOrderMappingConfig 移出，现有 Grid 行为和确定性测试保持不变。
- [ ] Ability 与 Input Mapping 不再重复持有 trigger/held 等输入绑定语义；剩余 override 有单一、可测试的 resolution service。
- [ ] loader、文档和编辑器消费同一份机器可读 contract metadata，至少覆盖 enum、registry ref、typed args 和 conditional fields。
- [ ] 旧 showcase 配置有兼容迁移路径，运行结果与当前基线一致。

## Related

- #275 Input/Order 解析 fail-fast 与魔法数
- #493 ActorOrderRouting / AbilitySlotResolver 局部 SSOT
- #708 OrderQueue / Order payload 边界审计
- #436 Input → Order 确定性
