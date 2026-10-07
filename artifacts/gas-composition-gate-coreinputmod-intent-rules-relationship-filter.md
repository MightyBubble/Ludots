## GAS Composition Gate — Self Review

- **Task / Issue**: 删掉 CoreInputMod 时，把各展示的右键下令、集结点、技能键改成交互状态加触发图。为此动了三处共用能力：下令意图规则加“施令者身上不许有的标签”、路线加“落点不摊方阵”、`QueryFilterRelationship` 放开到脚本图和触发图。
- **Date**: 2026-09-29
- **Agent / Author**: cloud agent

### 1. Core judgment

新变体主要交付物是（A/B/C/D）: A

结论: PASS

一句话理由: 没有新增节点、没有新增枚举、没有新增 JSON 文件。按关系圈人只是把已有节点放到触发图可用；下令意图规则的两个新字段是已有规则表里的条件和路线属性，下一个 mod 想要同样行为，改的是自己那份规则数据。

### 2. Layer assignment

| 步骤/能力 | Layer (0/1/2/3) | 实现载体 |
|-----------|-----------------|----------|
| 触发图里只留敌对单位（火球 Q 键瞄敌人） | 2 | 已有 `QueryFilterRelationship`，图种掩码从线性四种扩到加上脚本图、触发图 |
| 施令者带某标签就不走这条规则（传送门形态的兵营不改集结点） | 2 | `command_intent_profiles.json` 规则的 `actor.noneTags`，和已有 `allTags` / `anyTags` 同一套判断 |
| 这条路线的落点按原样下发，不参与摊方阵 | 2 | 路线的 `exactGroundPoint`，由下令意图排空系统在摊方阵前跳过 |
| 按关系判定、按标签判定、摊方阵本身 | 0 | 既有 RelationshipRuntime、GameplayTagContainer、CommandIntentBufferDrainSystem |

### 3. Reuse list

- Handlers: HandleQueryFilterRelationship（原样复用，未改运行时代码）
- Queues / Systems: CommandIntentBufferDrainSystem、InputOrderActorAuthorization，无新队列
- Resolvers / Registries: CommandIntentProfileRegistry、RelationshipTypeRegistry、TagRegistry
- Existing presets / graphs: `ScreenPointToEntity`、`LoadEntityPosValid`、`InvokeGraph` + `QueryCollectAbilityHolders`、`SubmitCast`、`SubmitCommandIntent`

### 4. New Layer 0 ops (if any)

N/A

### 5. Transaction boundary

必须原子 rollback 的步骤: 无。触发图只往下令缓冲里写意图，排空时逐条派发，不进效果事务。

### 6. Config SSOT

行为配置落在: graph（各 mod `assets/GAS/graphs/`）与 catalog（各 mod `assets/Input/command_intent_profiles.json`、`interaction_context_profiles.json`）

是否新增 JSON schema: NO。`actor.noneTags` 与 `route.exactGroundPoint` 是已有下令意图规则表的字段。

关于 `exactGroundPoint` 为什么不做成图上的开关：一次右键会同时派给士兵（移动，要摊方阵）和兵营（改集结点，要落在点到的位置）。哪个施令者走哪条路线，是排空时按规则逐个判出来的，图提交的时候还不知道。所以“这条路线的落点不摊开”只能跟着路线走，放在图上的 `layout` 会一刀切到所有施令者。它只是路线自己的一个属性，没有引出新的路线种类或新的派发管线。

### 7. Red flag scan

- [x] 未新增 profile inherit/placement enum
- [x] 未新建与 spawn 平行的物化管线
- [x] 未把 placement 校验塞进 lifecycle op
- [x] 未添加「说不清的」默认 fallback（没点中敌人时 `SubmitCast` 的条件为假，不下令，也不去找最近的敌人）

### 8. Next variant test

「下一个 Mod 变体」将修改: graph 连线（换关系类型、换技能）与自己那份下令意图规则数据
