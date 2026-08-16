# cfg-04 · 配置表体系

> 产品承诺 · 已冻结。理想实现见 [cfg-04 spec](../spec-runtime/cfg-04-config-tables.md)；现状见 [cfg-04 reference](../reference/cfg-04-config-tables.md)。

## 1. 定位

引擎的全部玩法数据都放在一张张**配置表**里（效果表、技能表、图程序表……）。这一篇回答四个问题：世界上有哪些表、一张表怎么被声明和加载、表里的 JSON 怎么写、以及怎么新增一张表和它的加载器。表与表之间的覆盖合并规则见 cfg-05；各张表自己的字段合同见后续各卷。

## 2. 示例配置

一张表由"目录登记 + 表文件"两半构成。目录登记（配置目录里的一行）：

```json
{ "Path": "GAS/effects.json", "Policy": "ArrayById", "IdField": "id" }
```

表文件（你的 mod 里的 `{mod名}:assets/GAS/effects.json`，路径与登记的 Path 对应）：

```json
[ { "id": "Effect.MyMod.Poison", "presetType": "DoT", "lifetime": "After",
    "participatesInResponse": false,
    "duration": { "durationTicks": 60, "periodTicks": 10, "clockId": "FixedFrame" } } ]
```

一张表可以拆成小文件写。目录声明 `"ShardDirectories": ["GAS/abilities"]` 后，`GAS/abilities.json` 仍是逻辑入口，`GAS/abilities/` 目录下一个小文件放一条：

```json
[ { "id": "Ability.MyMod.EmberBolt", "exec": { "clockId": "FixedFrame", "items": [ … ] } } ]
```

一个技能一个文件、一个效果一个文件，启动时与主文件合成一份——大 JSON 不再是唯一形态。

### JSON 书写通则

全部配置表共同遵守：

- 文件是 UTF-8 编码的 JSON 数组或对象；**不允许注释、不允许尾逗号**。
- 字段名**小驼峰**且**大小写敏感**——`presetType` 写成 `PresetType` 或 `presettype` 都是错。
- **未知字段即错**：表里出现该表 schema 之外的字段，启动失败并指出字段名。没有"宽松忽略"。
- 枚举值**精确匹配**：合法值拼错一个字母就是启动失败，不做模糊纠正。
- 语义字符串（名字、id）不允许首尾空白。
- 布尔写规范 `true` / `false`。

## 3. 字段与效果

目录条目的字段（声明一张表时的全部输入）：

| 字段 | 类型 | 必填 | 这样配会产生什么效果 |
|---|---|---|---|
| `Path` | string | 是 | 表的相对路径——引擎以此找到各来源的同名片段；加载器也以此查询自己的表 |
| `Policy` | string | 是 | 这张表的合并策略，五个合法值：`Replace` / `DeepObject` / `ArrayReplace` / `ArrayAppend` / `ArrayById`（条目级合并的主力，语义见 cfg-05） |
| `IdField` | string | 否 | 条目去重字段，默认 `id`；少数表用 `name`（函数库两表） |
| `ArrayAppendFields` | string[] | 否 | 条目内部按追加而非替换合并的数组字段名；当前无任何条目使用 |

条目还支持两个分片相关字段：

| 字段 | 类型 | 必填 | 这样配会产生什么效果 |
|---|---|---|---|
| `ShardDirectories` | string[] | 否 | 声明分片目录（如 `["GAS/abilities"]`）：目录下每个 json 文件是一个分片，schema 与正式条目完全相同；启动时每个来源先收主文件、再按稳定顺序收全部分片，汇入同一合并管线 |
| `AllowEmpty` | bool | 否 | 允许这张表**一条内容都不存在**（主文件与分片全空）——用于有意留空的扩展点；不声明时零片段即启动失败 |

条目只允许上述六个字段，出现其他字段启动失败。

## 4. 文件结构

- 目录正本：`Core:Configs/config_catalog.json`；目录自身也跨 mod 合并，mod 可追加条目。
- 表文件：引擎默认在 `Core:Configs/` 下；mod 内两个合法位置任选——`{mod名}:assets/` 根下或 `{mod名}:assets/Configs/` 下，与登记的 Path 拼接（地址文法见 cfg-02）。
- 分片：登记了分片目录的表，可在 `{mod名}:assets/GAS/abilities/` 这类目录下一条一个文件；分片目录同样适用两个合法根。

## 5. 声明与加载原理

一张表从声明到可用走五步，全部发生在启动期：

1. **登记**：目录先于一切表加载，形成"世界上有哪些表"的清单。
2. **收集**：按登记的 Path，从引擎默认和各 mod（按启动计划顺序、mod 内两槽）收齐全部片段；声明了分片目录的表，每个来源先收主文件、再按稳定顺序收分片。
3. **合并**：按登记的 Policy 合并成一份（规则见 cfg-05）。
4. **编译**：这张表的加载器校验每个条目（字段、类型、互斥、枚举），解析条目里的引用。
5. **注册**：通过校验的条目进这张表自己的注册表——名字换成整数 id，之后的表和代码都按 id 引用。编译发生在扩展枢纽冻结之后：mod 代码注册的扩展键（cfg-01 第 5 节）先就位，配置再编译引用。

三条由此而来的规则：

- **加载顺序 = 注册顺序 = 引用许可**。表按固定链加载（目标派发预设 → 时钟 → 属性约束 → 图程序 → 函数库 → 预设类型 → 订单类型 → 效果 → 技能 → 形态集 → Tag 规则 → 上下文组 → 属性绑定，另有 AI 与输入各表）。同表内引用后注册的 id 合法；跨表只能引用已加载表里的名字。
- **名字进注册表后就是整数**。条目名换成整数 id，运行期的效果执行、图执行、AI 决策全部按 id 工作，不在热路径做字符串查找；编辑器一律按区分大小写处理名字。
- **注册表以启动期为权威**。表在加载链上填充完毕即为权威范围；个别基础设施键（如实体集合键）仍保留运行期注册入口，属 spec 统一约定的治理项。

### 全部配置表总览

按域分组（以目录为准；各表字段合同见对应卷）：

| 域 | 表 | 管什么 | 详见 |
|---|---|---|---|
| GAS | effects / abilities / graphs / func_lib / action_lib / preset_types / order_types / tag_rules / attribute_bindings / attribute_constraints / clock / target_dispatch_presets / ability_form_sets / context_groups（14 张） | 效果、技能、图程序、函数与动作库、预设类型、订单、Tag 规则、属性绑定与约束、时钟、派发预设、形态、上下文 | 卷 4–7、卷 9 |
| AI | atoms / projection / utility / goap_actions / goap_goals / htn_domain / behavior_trees / hfsm（8 张） | 世界状态原子、规划、行为树与状态机 | 卷 8 |
| 实体 | Entities/templates | 单位模板（组件与初始值） | 卷 11 |
| 输入 | default_input / input_order_mappings / filter_profiles / command_intent_profiles / cast_dispatch_profiles / interaction_context_profiles / cast_commit_profiles / cast_commit_locks / control_schemes（9 张） | 输入映射与施法路由 | 卷 6 |
| 表现 | performers / mesh_assets / material_assets / host_assets / prefabs / instanced_batches / presentation_behaviors / animator_controllers / animation_clips / animation_profiles / text_tokens / text_locales（12 张） | 表现器、资产、动画、本地化 | 卷 12 |
| 导航 | agent_profiles / pathing / navmesh | 寻路参数与烘焙 | 卷 13 |
| 物理与引擎 | Engine/clock、Physics2D/clock、solver、kinematic | 引擎与物理时钟、求解器 | 卷 13、rt-01 |
| 进度 | scopes / progressions / requirements | 进度域与需求 | 卷 14 |
| 物品与兑换 | shapes / layouts / definitions、Exchange/operations | 物品与兑换操作 | 卷 14 |
| 其余 | Vision/fog_layers、Camera/virtual_cameras、UI 三表、叙事四表、EntityInfo | 视野、相机、界面、叙事、实体信息 | 卷 13、卷 14 |

AI 的十张决策表（输入、归一化、曲线、决策等）走引擎代码内的独立登记，不经配置目录——总览与各自字段见卷 8。

## 6. 新增一张表与加载器

**先过治理门禁**：新表是最后手段。多数"想要新表"的需求，用已有表的组合（新条目、新图、新 preset 组合）就能表达——这是组合优先红线，新增 schema 必须走审批。

确认要加之后，四步交付（引擎侧工作，mod 作者不能加加载器——加载器是引擎代码）：

1. **目录登记**：在目录加一行（Path / Policy / IdField），决定路径与合并方式。
2. **写加载器**：消费合并产物，逐条目校验（未知字段、类型、互斥、枚举精确匹配），把通过的名字注册进新注册表。加载器必须对未登记路径 fail-closed，对单条错误给出条目定位。
3. **挂进加载链**：在引擎初始化里排在依赖它的表之后——新表引用谁，谁就必须先加载。
4. **验收**：未登记路径查询失败、未知字段失败、引用缺失失败、空表合法（表可以没有条目）。

编辑器侧对应"新类型审批流"：目录条目 + 加载器挂接声明 + 策略选择三件同批交付（见第 9 节）。

**新图节点、效果处理器、表现扩展不走这条路**：mod 代码经扩展注册面（cfg-01 第 5 节）在加载窗口注册即可，不必新增配置 schema、不占治理审批。治理门禁只针对"新表"；加载器本身仍属引擎侧。

## 7. 预期反馈

- **启动期**：目录先加载；每张表合并、编译、注册一次完成，失败即启动失败。
- **运行期**：注册表只读；一切查询按 id 进行。
- **编辑器内**：表清单视图即第 5 节总览的投影——每张表的登记信息、来源片段、条目计数。

## 8. 异常处理

| 异常情形 | 系统响应 |
|---|---|
| 条目缺 `Path` 或 `Policy`、策略名拼错、出现未知字段 | 启动失败 |
| 加载器查询未登记的路径 | 启动失败（没登记的 JSON 不是配置） |
| 表文件语法错误、未知字段、枚举拼错 | 启动失败，指出文件与条目 |
| 条目缺 id 或 id 非字符串 | 启动失败 |
| 条目引用的 id 不在已加载的表里 | 启动失败，指明引用方与目标 |

## 9. 编辑器要点

- **表清单视图**：全部已登记表的分组浏览（域、路径、策略、条目数），是"我能配什么"的完整答案。
- **新类型审批流**：按第 6 节四步生成审批件，审批前引导用现有表组合表达。
- **引用检查**：mod 里每个表文件路径对得上登记、条目引用对得上已加载注册表，编辑期即时报错。

## 10. 实例

- 目录正本：`assets/Configs/config_catalog.json`
- 一张表的两个来源对照：`Core:Configs/GAS/graphs.json`（引擎默认）与 `MobaDemoMod:assets/Configs/GAS/graphs.json`（mod 追加）

**相关文档**：[cfg-04 spec](../spec-runtime/cfg-04-config-tables.md) · [cfg-04 reference](../reference/cfg-04-config-tables.md) · [cfg-05](cfg-05-config-pipeline.md)（表间合并规则）· [cfg-06](cfg-06-game-config.md)（不走目录的特例）
