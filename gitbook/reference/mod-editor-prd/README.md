# MOD 编辑器 PRD · 总篇

> 状态：结构已定，分篇撰写中（目录见第 6 节，状态列同步更新）。进度与决策 SSOT：仓库 issue #986。
> 视角：**MOD 编辑器产品需求文档**。每个原子功能点（命题）六件套——`prd/` 第一性需求、`config/` 配置说明、`uxd/` 编辑器需求、`spec-runtime/` 引擎实现任务书、`spec-editor/` 编辑器实现任务书、`reference/` 现状参考。
> 三层合同共同约束手册与编辑器：不发明 schema、不新增配置类型；字段最终依据是代码，行为正本在 `gitbook/architecture/`。

## 1. 读者与用法

| 读者 | 读哪层 |
|---|---|
| MOD 作者 | `prd/` 各篇：配置怎么写、承诺的行为是什么，读完即可动手 |
| MOD 编辑器开发 | `prd/` 给需求承诺，`config/` 给字段合同，`uxd/` 给界面需求，`spec-runtime/` 与 `spec-editor/` 给理想实现，`reference/` 给当下的事实 |
| 引擎开发 | `spec/` 是设计目标，`reference/` 是现行实现的事实清单 |
| 编辑器产品 | 卷 10 的 `prd/` 篇是需求清单本体 |

目录结构：

```
gitbook/reference/mod-editor-prd/
  README.md             总篇（本页）
  facts.md              事实与取值表（脚本生成，勿手改）
  prd/<编号>-<名>.md      第一性需求——定稿后冻结，永不修改
  config/<编号>-<名>.md   配置说明——字段表、示例、书写通则（随 schema 演进）
  uxd/<编号>-<名>.md      编辑器需求——界面功能、数据存储、易用性（逐命题成套）
  spec-runtime/<编号>-<名>.md  引擎实现任务书——可变更，篇内变更记录跟踪
  spec-editor/<编号>-<名>.md   编辑器实现任务书——可变更，篇内变更记录跟踪
  reference/<编号>-<名>.md 现状参考——如实记录现状与代码锚点
```

编号规则：`卷前缀-序号`（`fx-05` = 效果卷第 5 篇）。引用格式：`见 fx-05`（指 prd 篇）、`见 fx-05 config`、`见 fx-05 uxd`、`见 fx-05 spec-runtime`、`见 fx-05 spec-editor`、`见 fx-05 reference`。

## 2. 演示场景与示例纪律

本手册的主角是 **mod 编辑器的能力**。为了把能力讲实，全部命题共用一个贯穿示例场景：围绕一个 C&C 风格 RTS 底座，长出一族 mod——官方 RA2 / Dune 皮肤、社区 War3 换皮、皇室战争玩法，以及战役地图包、地图难度修正、强化兵种。**每个 mod 对应演示编辑器的一项核心能力**：

| 场景 mod | 演示的编辑器能力 |
|---|---|
| C&C RTS 底座 | 从零创建完整 mod 项目：实体、技能效果、订单、生产的全流程编辑 |
| RA2 / Dune 皮肤 | 皮肤资产替换与一键切换：资产浏览器、表现覆盖、启动预设 |
| War3 换皮 | 社区 mod 导入与兼容：依赖管理、命名空间、覆盖审计 |
| 战役地图包 | 地图编辑器：地形、敌军布局、剧情触发器 |
| 地图难度修正 | 已有内容的修饰：合并预览、地图级跨 mod 覆盖 |
| 强化兵种 | 数值热调：表单编辑、下次施放生效的热应用 |
| 皇室战争玩法 | 玩法创新组合：技能/效果/图编辑器把卡牌、圣水、塔拼出来 |

示例纪律：cfg-01 起各命题的示例统一取自本场景的真实文件（现阶段锚定仓库 `mods/showcases/rts_red_alert_like/`，治理拆分后切换到 mods/handbook/（规划中目录，尚未创建） 族），禁止零散杜撰；已有实现与文档合同冲突时，**治理实现**，不顺着错误实现写文档。

## 3. 概念模型

```
输入映射 ─→ Order（命令意图：admission → 实体队列 → 终态）
              │ castAbility
              ▼
           Ability（激活门 → 执行时间轴：Clip/Signal/Gate/End）
              │ EffectSignal/EffectClip
              ▼
           Effect（提案窗口[验证图→响应链→纯计算] → 实体化/堆叠 → 应用[8 相位] → 周期 → 过期）
              │                                    │
              ▼                                    ▼
        Attribute（Base/Cap/Current → 聚合 → 派生图）   Tag（规则事务/层数/定时）
              │                                            │
              ▼                                            ▼
        属性绑定 Sink（物理/相机）              延迟触发 → 事件 → Reaction/EventGate（下一帧）
```

Graph 不是第六个框，而是**贯穿 Effect、Ability、AI 的可编程面**：效果相位图、能力前置图、派生属性图、订单校验图、AI 打分图、BT/HFSM 叶子——八个挂接点见 `gr-08`；新图节点与效果处理器可由 mod 代码在加载窗口注册（扩展面，见 cfg-01）。

配置资产与加载顺序——谁必须先注册，谁才能引用：

```
target_dispatch_presets → clock → attribute_constraints → graphs → preset_types → order_types
→ effects → abilities → ability_form_sets → tag_rules → func_lib → action_lib
→ context_groups → attribute_bindings      （+ AI/*.json、Input/input_order_mappings；此链是引用许可序）
```

跨 mod 合并：同名文件按 `config_catalog.json` 声明的策略合并，主力是"同 id 深合并"——后加载的 mod 赢，但只赢它写到的字段；`__delete:true` 可删条目。详见 `cfg-05`。

## 4. 六层模板

每个配置命题六件：**第一性 PRD、配置说明、UXD、runtime spec、editor spec、reference**（全部与命题同名成套、逐层同构跳转）。

**PRD 篇**——第一性需求，`prd/<编号>-<名>.md`：

> 生命周期：定稿即冻结。只写承诺：是什么、保证什么、边界与失败语义。**零字段表、零 JSON 示例、零编辑器界面。**

| 节 | 回答的问题 |
|---|---|
| 1. 定位 | 这个东西是什么 |
| 2. 产品承诺 | 逐条承诺，每条一句话可验收 |
| 3. 运行行为 | 承诺的时序与不变量 |
| 4. 异常承诺 | 哪些情况一律拒绝、报什么 |

**配置说明篇**——给照着写的人，`config/<编号>-<名>.md`：

> 生命周期：随 schema 演进。示例一律取自演示场景真实文件。

| 节 | 回答的问题 |
|---|---|
| 1. 示例配置 | 场景真实文件 + 读法 + 最小骨架 |
| 2. 字段与行为 | 每字段（或规则）"这样配会产生什么效果" |
| 3. 文件结构 | 工程目录树中的位置、命名约定、发现根 |
| 4. 运行时加载效果 | 代码/数据被加载时发生什么链路 |
| 5. 异常处理 | 异常情形与系统响应 |
| 6. 实例 | 仓库真实路径 |

**UXD 篇**——编辑器需求（高保真规格），`uxd/<编号>-<名>.md`（逐命题成套）：

> 生命周期：随产品迭代。固定六节：**界面定位 / 布局线框（文字线框图）/ 控件与数据源（每控件的取值来源，数值引用事实页）/ 关键交互流（逐步含校验）/ 状态设计（空·错误·冲突·待生效）/ 易用性验收口径（可测）**。

**Runtime Spec 篇**——引擎实现任务书，`spec-runtime/<编号>-<名>.md`：

> 生命周期：可变更，篇末变更记录跟踪。实现由独立的引擎任务执行，落地后回写 reference。

| 节 | 回答的问题 |
|---|---|
| 1. 概述 | 目标机制一句话 |
| 2. 设计 | 机制、数据形状、执行路径、兼容代码清除清单 |
| 3. 精确语义与不变量 | 策略集合、比较规则、失败模式——作为实现验收标准 |
| 4. 迁移与治理 | 迁移步骤、风险、验收口径 |

**Editor Spec 篇**——编辑器实现任务书，`spec-editor/<编号>-<名>.md`：

> 生命周期：可变更，篇末变更记录跟踪。面向编辑器工程：消费哪些引擎接口、编辑器侧建什么模型、验收口径。

| 节 | 回答的问题 |
|---|---|
| 1. 概述 | 编辑器侧实现目标一句话 |
| 2. 设计 | 表单/视图模型、保存管线、消费的引擎接口 |
| 3. 精确语义与不变量 | 编辑器判定与引擎判定同源、往返无损等 |
| 4. 依赖接口与验收 | 消费的引擎接口清单、可测验收 |

**Reference 篇**——现状参考，`reference/<编号>-<名>.md`：

> 生命周期：现状变化时更新。只记事实——现状是什么、代码在哪；**不与 spec 对照、不做评价**，对错好坏读者自见。

| 节 | 回答的问题 |
|---|---|
| 1. 现状快照 | 当前实际行为 |
| 2. 代码锚点 | 现行实现的位置（文件:行） |

写作规则：

- **标题只用简短名词短语，不带括号补充说明**；补充说明放该节第一句。
- PRD 零字段表、零示例、零界面；配置说明零编辑器叙事、示例只取演示场景；UXD 零配置字段表；Spec 零产品叙事、零现行代码位置；Reference 零对照、零评价、零展望。
- 每篇开头一行互链其余各层。
- **数值纪律**：一切计数/默认值/上限不手抄，引用 [事实与取值表](facts.md)（`python scripts/generate-prd-facts.py` 再生成）。
- 热应用级别标注在配置说明与 UXD：下次施放生效、重进地图生效、重启生效。
- 目录状态列"已写"= 六件齐备（UXD 页按承载命题合并计）。

## 5. 阅读路线

| 我想做… | 读这几篇的 prd 层 |
|---|---|
| 搞懂 mod 的组成与启动 | 按卷 1 顺序通读：cfg-01 → cfg-02 → cfg-03 → cfg-04 → cfg-05 → cfg-06 → cfg-07 → cfg-08 |
| 有一张能进游戏的地图 | cfg-06（启动地图）→ map-01 → map-02 → ent-01（卷 2） |
| 一个伤害或治疗技能 | cfg-05 → fx-01/02/03 → ab-01/02/04 → fx-08/09/10 |
| Buff、Debuff 或状态流 | tag-01/02 → fx-11/12 → fx-04 → attr-02/03 |
| 自定义伤害公式或复杂逻辑 | gr-01/02/03 → gr-op-02/04/10 → fx-05/06 |
| 弹道、召唤或位移技能 | fx-14/15/16 → ab-02 → fx-13 |
| AI 自动施法 | ai-01 到 ai-08（第二期）+ ab-04/06 |
| 单位生产、建造或经济 | fx-15/20/21 → ab-09 → attr-01 |
| 理解运行时为什么这样跑 | rt-01 到 rt-05 |
| 配引擎与平台基建（物理/导航/视野/界面） | infra-01 到 infra-04（卷 13） |
| 编辑器该做成什么样 | 全目录 prd 层 + ed-01 到 ed-03 |

## 6. 分篇目录

共 119 个命题，分 14 卷，每个命题一套 prd / spec / reference 三件（下表文件名为三层共用编号名）。优先级 P1 为第一期，P2 为第二期。

### 卷 1 · 配置基础

| 文件 | 篇名 | 范围 | 优先级 | 状态 |
|---|---|---|---|---|
| `cfg-01-mod-manifest.md` | mod 数据 | mod.json 全字段与行为、发现与装配、依赖与顺序 | P1 | **已写** |
| `cfg-02-vfs.md` | 虚拟文件系统 | URI 文法、挂载点来源、路径安全 | P1 | **已写** |
| `cfg-03-launch-graph.md` | 启动计划 | launch graph 与 runtime bootstrap、指纹校验、顺序 SSOT | P1 | **已写** |
| `cfg-04-config-tables.md` | 配置表体系 | 表清单与总览、声明与加载原理、JSON 书写通则、新增表与加载器 | P1 | **已写** |
| `cfg-05-config-pipeline.md` | 配置管线与跨 mod 合并 | 合并规则、加载顺序、命名空间 | P1 | **已写** |
| `cfg-06-game-config.md` | 游戏配置 | game.json 全字段、深合并特例、常量表 | P1 | **已写** |
| `cfg-07-merge-rules.md` | 合并规则案例集 | 十种意图的写法与结果、危险数组清单、删除时序 | P1 | **已写** |
| `cfg-08-mod-extensions.md` | mod 代码扩展面 | 四类扩展注册、加载窗口与枢纽冻结、与配置编译的时序 | P1 | **已写** |

### 卷 2 · 地图与触发器

| 文件 | 篇名 | 范围 | 优先级 | 状态 |
|---|---|---|---|---|
| `map-01-definition.md` | 地图定义 | Maps/*.json、地图资产管线、跨 mod 地图合并 | P1 | **已写** |
| `map-02-triggers.md` | 地图触发器 | 地图内触发器与棋盘、剧情与胜负判定 | P1 | **已写** |
| `ent-01-templates.md` | 实体模板 | Entities/templates.json、组件与初始值、出生效果 | P1 | **已写** |

### 卷 3 · Tag

| 文件 | 篇名 | 范围 | 优先级 | 状态 |
|---|---|---|---|---|
| `tag-01-basics.md` | Tag 表示与状态 | 位图、层数、定时、快照、有效缓存、惰性注册 | P1 | **已写** |
| `tag-02-rules.md` | Tag 规则 | 六类规则、事务与预算、热替换边界 | P1 | **已写** |
| `tag-03-changed-events.md` | Tag 变化与事件 | 延迟触发器、变化事件、一帧延迟语义 | P1 | **已写** |

### 卷 4 · Attribute

| 文件 | 篇名 | 范围 | 优先级 | 状态 |
|---|---|---|---|---|
| `attr-01-definition.md` | 属性定义与约束 | attribute_constraints.json、Base/Cap/Current、clampToBase、64 上限 | P1 | **已写** |
| `attr-02-modifiers.md` | 修改器 | Add/Multiply/Override、聚合与即时的区别、写入权威 | P1 | **已写** |
| `attr-03-aggregation.md` | 聚合管线 | Buff 重聚合、持久 Current、Cap 取聚合值 | P1 | **已写** |
| `attr-04-derived.md` | 派生属性图 | Derived 图、实体绑定、原子提交 | P1 | **已写** |
| `attr-05-bindings.md` | 属性绑定与 Sink | attribute_bindings.json、物理力与相机通道、脉冲式清零 | P1 | **已写** |
| `attr-06-events.md` | 属性事件 | 属性变化发布事件、变化位、表现层读取 | P1 | **已写** |

### 卷 5 · Effect

| 文件 | 篇名 | 范围 | 优先级 | 状态 |
|---|---|---|---|---|
| `fx-01-pipeline.md` | 效果执行管线总览 | 从提案到移除的端到端时序、三个事务边界 | P1 | **已写** |
| `fx-02-template.md` | 效果模板骨架 | effects.json 顶层、presetType 与 lifetime 合法组合 | P1 | **已写** |
| `fx-03-preset-types.md` | Preset 类型系统 | preset_types.json、16 种 preset 全表、默认相位处理器 | P1 | **已写** |
| `fx-04-lifetime.md` | 生命周期与时长 | Instant/After/Infinite、duration 块、周期、过期条件 | P1 | **已写** |
| `fx-05-phases.md` | 八相位执行 | 相位顺序、Pre/Main/Post 三槽、skipMain、纯相位限制 | P1 | **已写** |
| `fx-06-proposal-window.md` | 提案窗口与 Instant 内联 | OnPropose 验证图、OnCalculate、外部原子独占律 | P1 | **已写** |
| `fx-07-response-chain.md` | 响应链 | Hook/Modify/Chain/PromptInput、窗口深度、fan-out 预算 | P1 | **已写** |
| `fx-08-phase-listeners.md` | 相位监听器 | phaseListeners、Source 与 Target 视角、通配、随宿主清理 | P1 | **已写** |
| `fx-09-target-query.md` | 目标查询 | 五种空间形状、origin、GraphProgram 动态查询 | P1 | **已写** |
| `fx-10-target-filter.md` | 目标过滤 | 敌我关系、排除源、数量上限、层掩码 | P1 | **已写** |
| `fx-11-target-dispatch.md` | 目标派发 | preset 与 contextMapping、payloadEffect、FanOut 链 | P1 | **已写** |
| `fx-12-stack.md` | 堆叠 | 三种策略、两种溢出处理、无 stack 时的独立实体 | P1 | **已写** |
| `fx-13-granted-tags.md` | 效果授予 Tag | Fixed/Linear/LinearPlusBase 公式、层数回收 | P1 | **已写** |
| `fx-14-config-params.md` | 参数化 | configParams 七类型、`_ep.` 保留键全表、CallerParams 覆盖 | P1 | **已写** |
| `fx-15-projectile.md` | 弹道 | projectile 全字段、直射与追踪、命中与落点子效果 | P1 | **已写** |
| `fx-16-unit-creation.md` | 造单位 | placement 与 facing、onSpawnEffect、父与玩家归属 | P1 | **已写** |
| `fx-17-displacement.md` | 位移 | 四种朝向模式、导航接管、叠加即替换 | P1 | **已写** |
| `fx-18-relation.md` | 关系操作 | SetParent、RemoveParent、EnsureLink | P1 | **已写** |
| `fx-19-vision.md` | 视野揭示 | revealArea、scope/layers、记忆时长、探测强度 | P1 | **已写** |
| `fx-20-exchange.md` | 兑换 | exchange 块、exchangeOperationId 参数 | P1 | **已写** |
| `fx-21-progression.md` | 进度完成 | id 与 scope、level 与 delta 互斥 | P1 | **已写** |
| `fx-22-submit-order.md` | 出生下单 | submitOrderFromBlackboard、五个黑板键、提交模式 | P1 | **已写** |
| `fx-23-lifecycle-atomic.md` | 生命周期原子操作 | DeployConsumeSource 链、六个生命周期内建操作 | P1 | **已写** |

### 卷 6 · Ability

| 文件 | 篇名 | 范围 | 优先级 | 状态 |
|---|---|---|---|---|
| `ab-01-definition.md` | 技能定义骨架 | abilities.json 顶层、presentation 与本地化、input 声明、禁止字段 | P1 | **已写** |
| `ab-02-exec-timeline.md` | 执行时间轴 | 11 种 item 全表、独立时钟、推进与打断、终态 | P1 | **已写** |
| `ab-03-caller-params.md` | CallerParams 参数池 | 最多四组、与 configParams 的合并规则 | P1 | **已写** |
| `ab-04-cooldown.md` | 冷却三件套 | cooldown 数据契约、TagClip 与 blockTags 闭环、AI 就绪判定 | P1 | **已写** |
| `ab-05-activation-gates.md` | 激活门 | 校验顺序、blockTags、前置校验图、进度需求、toggle 先关 | P1 | **已写** |
| `ab-06-slots.md` | 槽位系统 | 8 槽、四层解析、按来源回收 | P1 | **已写** |
| `ab-07-form-sets.md` | 形态路由 | ability_form_sets.json、route 匹配、优先级 | P1 | **已写** |
| `ab-08-toggle.md` | Toggle 技能 | toggleSpec、activeEffects、deactivateExec | P1 | **已写** |
| `ab-09-targeting.md` | Targeting 与组合命令 | castRangeCm、超射程自动走近、投影排队 | P1 | **已写** |
| `ab-10-context-groups.md` | 上下文组 | context_groups.json、candidate 评分、两张图 | P1 | **已写** |

### 卷 7 · Order 与输入

| 文件 | 篇名 | 范围 | 优先级 | 状态 |
|---|---|---|---|---|
| `ord-01-types.md` | 订单类型 | order_types.json 三段结构、全字段表、语义 key 分配 | P1 | **已写** |
| `ord-02-rules.md` | 订单规则与打断 | orderRules、阻止与打断、同类型与满队策略 | P1 | **已写** |
| `ord-03-pipeline.md` | 订单流水 | 全局队列、准入、实体缓冲、终态、17 种失败原因 | P1 | **已写** |
| `ord-04-blackboard.md` | 黑板 | 四种 buffer、内置键、persistentStoredTarget 五键 | P1 | **已写** |
| `ord-05-input-protocol.md` | 输入协议 | InputRequest 与 Response、三种 Gate 的等待与改写目标 | P1 | **已写** |
| `ord-06-input-mappings.md` | 输入映射 | input_order_mappings.json、argsTemplate、路由候选、用户覆写 | P1 | **已写** |
| `input-01-command-intent.md` | 命令意图档案 | command_intent_profiles.json、指针命令意图路由 | P2 | **已写** |
| `input-02-cast-dispatch.md` | 施法派发档案 | cast_dispatch_profiles.json、目标收集与派发策略 | P2 | **已写** |
| `input-03-interaction-context.md` | 交互上下文档案 | interaction_context_profiles.json、交互模式与上下文 | P2 | **已写** |
| `input-04-cast-commit.md` | 施法提交档案 | cast_commit_profiles 与 locks、提交确认与锁 | P2 | **已写** |
| `input-05-filters-and-schemes.md` | 过滤与输入方案 | default_input、filter_profiles、control_schemes、动作属性绑定 | P2 | **已写** |

### 卷 8 · Graph

| 文件 | 篇名 | 范围 | 优先级 | 状态 |
|---|---|---|---|---|
| `gr-01-model.md` | 图编程模型 | L0/L1/L2 分层、编译流水线、寄存器机、限额总表 | P1 | **已写** |
| `gr-02-document.md` | 图文档格式 | 顶层字段、节点字段全表、端口、边表强制、next 禁用 | P1 | **已写** |
| `gr-03-kinds.md` | 六种 Kind | 返回约定、创作白名单与执行闸、预设寄存器 | P1 | **已写** |
| `gr-04-compilation.md` | 编译与校验 | 编译期检查全清单、符号解析、注册终态与热替换边界 | P1 | **已写** |
| `gr-05-execution.md` | 执行模型 | Run-to-Halt 与切片、Yield 宿主政策、步数预算、零分配 | P1 | **已写** |
| `gr-06-funclib.md` | FuncLib | func_lib.json、纯度闭包校验、跨图调用 | P1 | **已写** |
| `gr-07-actionlib.md` | ActionLib | action_lib.json、四种 host、yield 政策 | P1 | **已写** |
| `gr-08-mount-points.md` | 挂接点总表 | 八个挂点与各自要求的 kind | P1 | **已写** |
| `gr-09-outputs.md` | Query 图输出 | outputs 声明、实体集合与摘要标量、槽位清理 | P1 | **已写** |
| `gr-op-01-context.md` | 节点：常量与上下文 | Const 三件、LoadCaster/Target/Viewer、Context 三件、EventPayload、TargetPos | P1 | **已写** |
| `gr-op-02-math.md` | 节点：数学与比较 | 四则、clamp、abs、random、比较、SelectEntity | P1 | **已写** |
| `gr-op-03-tags.md` | 节点：标签 | HasTag；查表一律走通用用户表（ADR #876）；"纯读选 tag"节点按 ADR 活口可重立（输入绑通用 tag 集/用户表，禁绑专表） | P1 | **已写** |
| `gr-op-04-attributes.md` | 节点：属性与配置 | LoadAttribute、LoadSelfAttribute、WriteSelfAttribute、LoadConfig 三件 | P1 | **已写** |
| `gr-op-05-blackboard.md` | 节点：黑板 | Read 与 Write × Float/Int/Entity | P1 | **已写** |
| `gr-op-06-spatial.md` | 节点：空间查询 | Circle/Cone/Rectangle/Line/Hex、Sort/Limit/Filter | P1 | **已写** |
| `gr-op-07-entityset.md` | 节点：实体集查询 | 全图、复制集合、五种过滤、按属性排序与聚合 | P1 | **已写** |
| `gr-op-08-relationship.md` | 节点：关系系统 | 读、写、查询管线三组 | P1 | **已写** |
| `gr-op-09-aggregate.md` | 节点：聚合与迭代 | AggCount、AggMinByDistance、TargetListGet | P1 | **已写** |
| `gr-op-10-effect-actions.md` | 节点：效果与事件动作 | ApplyEffect 四件、RemoveEffect、FanOutDispatch 两件、ModifyAttributeAdd、SendEvent | P1 | **已写** |
| `gr-op-11-lifecycle-builtin.md` | 节点：生命周期与内建 | BeginLifecycleTransaction、InvokeBuiltin 加 20 个内建全表 | P1 | **已写** |
| `gr-op-12-placement.md` | 节点：放置校验 | ClampTargetToRange、IsPointInCircle、两种吸附 | P1 | **已写** |
| `gr-op-13-topology.md` | 节点：拓扑谓词 | 控制域解析与可控性、知识投影 | P1 | **已写** |
| `gr-op-14-control-flow.md` | 节点：Script 控制流 | Jump、Call、Return、Yield、HaltReturnInt、InvokeScript、MoveInt、作者糖 | P1 | **已写** |
| `rel-01-catalog.md` | 关系目录 | Relationships/catalog.json、类型/度量/旗标/姿态（图节点关系族依赖） | P2 | **已写** |

### 卷 9 · AI 行为层

| 文件 | 篇名 | 范围 | 优先级 | 状态 |
|---|---|---|---|---|
| `ai-01-utility-overview.md` | Utility AI 总论 | 十表关系、与 GAS 和 Graph 的三个接缝 | P2 | **已写** |
| `ai-02-inputs.md` | 打分输入 | 8 种输入、GraphScore 接 Score 图 | P2 | **已写** |
| `ai-03-norm-curves.md` | 归一化与曲线 | Identity/Range/RangeInverse × Linear/Power/Inverse | P2 | **已写** |
| `ai-04-decisions.md` | 决策 | 四种聚合、节流、autocast | P2 | **已写** |
| `ai-05-dm-profiles.md` | 决策者与档案 | UtilityScore 与 FixedPriority、决策间隔 | P2 | **已写** |
| `ai-06-target-filters.md` | 目标过滤 | 9 种操作 | P2 | **已写** |
| `ai-07-tasks.md` | 任务 | SubmitOrder 落到 Order 系统 | P2 | **已写** |
| `ai-08-stances-actuators.md` | 姿态与执行器 | stance 与 actuator | P2 | **已写** |
| `ai-09-behavior-trees.md` | 行为树 | behavior_trees.json、ScriptSlice、跨波次挂起 | P2 | **已写** |
| `ai-10-hfsm.md` | 层次状态机 | hfsm.json、三个生命周期钩子、转移谓词 | P2 | **已写** |
| `ai-11-goap-htn.md` | GOAP 与 HTN | 旧栈五文件、256 位世界状态 | P2 | **已写** |

### 卷 10 · 运行时横切

| 文件 | 篇名 | 范围 | 优先级 | 状态 |
|---|---|---|---|---|
| `rt-01-clocks.md` | 时钟系统 | 三种时钟域、Step 策略、实体级变速、tick 换算 | P1 | **已写** |
| `rt-02-budgets.md` | 预算与容量 | 帧预算、单根 fan-out 上限、容量总表、报错语义 | P1 | **已写** |
| `rt-03-diagnostics.md` | 诊断与错误码 | 9 域 21 指标、错误码字典 | P1 | **已写** |
| `rt-04-presentation.md` | 表现事件 | 施法与效果九种事件、属性增量、溢出报错 | P1 | **已写** |
| `rt-05-events.md` | 事件总线与帧延迟 | 双缓冲、一帧延迟语义、Reaction 消费 | P1 | **已写** |

### 卷 11 · 编辑器能力与需求

| 文件 | 篇名 | 范围 | 优先级 | 状态 |
|---|---|---|---|---|
| `ed-01-workbench-base.md` | 实时技能工作台编辑基座 | 会话、三段式流水线、四级应用分级、安全帧回滚 | P1 | **已写** |
| `ed-02-hot-apply.md` | 热应用白名单与边界 | 热字段清单、重载与重启边界、身份扩张禁热 | P1 | **已写** |
| `ed-03-gap-roadmap.md` | 编辑器缺口与路线图 | 文档投影源、撤销重做、图编辑器、冷编辑流 | P1 | **已写** |

### 卷 12 · 表现资产

| 文件 | 篇名 | 范围 | 优先级 | 状态 |
|---|---|---|---|---|
| `pres-01-performers.md` | 表现器档案 | presenters（含内联 behaviors）、分片 | P2 | **已写** |
| `pres-02-asset-registry.md` | 表现资产清单 | mesh/material/host 资产、instanced_batches（现零数据）、lod/vfx | P2 | **已写** |
| `pres-03-animation.md` | 动画配置 | animator_controllers、animation_clips、animation_profiles | P2 | **已写** |
| `pres-04-localization.md` | 本地化 | text_tokens、text_locales | P2 | **已写** |

### 卷 13 · 引擎与平台基建

| 文件 | 篇名 | 范围 | 优先级 | 状态 |
|---|---|---|---|---|
| `infra-01-engine-physics.md` | 引擎与物理配置 | Engine/clock、Physics2D 的时钟/求解器/运动学 | P2 | **已写** |
| `infra-02-navigation.md` | 导航配置 | agent_profiles、pathing、navmesh 烘焙参数 | P2 | **已写** |
| `infra-03-vision-camera.md` | 视野与相机 | fog_layers、virtual_cameras | P2 | **已写** |
| `infra-04-ui-profiles.md` | 界面档案 | 命令甲板、生产总览、技能聚合三张 UI 档案 | P2 | **已写** |

### 卷 14 · 其余域

| 文件 | 篇名 | 范围 | 优先级 | 状态 |
|---|---|---|---|---|
| `misc-01-progression.md` | 进度域 | scopes、progressions、requirements 三张表 | P3 | **已写** |
| `misc-02-items-exchange.md` | 物品与兑换 | shapes、layouts、definitions、operations | P3 | **已写** |
| `misc-03-narrative.md` | 叙事与任务 | variables、quests、dialogues、cinematics | P3 | **已写** |
| `misc-04-entity-info.md` | 实体信息档案 | insight_profiles | P3 | **已写** |

## 7. 写作顺序与纪律

- 顺序：卷 1 → 2 → 3 → 4 → 5 → 6 → 7 → 9 → 10 → 8。效果卷先写 fx-00 总览再写分块。每个命题三件同一次治理节点交付。
- 纪律一，防幻觉：reference 的每张锚点表逐项对照代码；prd 的每个 JSON 骨架必须能在仓库找到同构实例或通过 loader 校验逻辑推演；未接线的实现显式标注状态。
- 纪律二，分层：PRD 冻结产品预期，Spec 是可跟踪的实现任务书，Reference 只记现状；规则见第 4 节。
- 纪律三，职责边界：本手册只改文档；引擎与编辑器代码变更由独立任务执行，spec 篇即其任务书，落地后回写 reference。
- 与现有文档关系：`architecture/` 各契约文档是行为合同正本，分篇链接它们而不复述全文；`reference/graph-node-op-wiki/` 是玩家视角节点短剧，节点家族篇互链它作为"这个节点玩起来是什么样"。
- 治理红线：手册与编辑器都不得催生新 JSON schema、新 preset 枚举或平行 loader（GAS 组合门禁）。

## 8. 开放决策

1. 节点文档粒度：当前方案是 14 个家族命题，篇内逐节点表格。若需要逐节点一个命题（约 120 对），扩展点在卷 7，不影响其他卷。
2. AI 卷目前排第二期。若编辑器第一期就要做 AI 面板，可以提前。
3. 挂载位置：当前在参考资料章下，门户已为手册开了一级 tab。若希望 SUMMARY 也逐篇列出，只需调整导航。
4. id 命名空间治理：配置 id 为全局扁平命名空间，撞名行为当前不一致（技能注册 last-wins、效果注册抛错）；治理方向待产品评审，评审前 cfg-05 只作事实陈述。
5. 配置重载专文：重载机制为预留触发器且当前无调用方；工具接线需求出现时立项专文讲组语义与触发器协议。
