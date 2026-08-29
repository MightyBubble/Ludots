# Activity 作用域与多玩家投票——研究简报

## 背景（已定案，不可推翻）

Ludots 引擎的 Activity（活动）是"把一次拍板摆到决策者面前"的内容机器。经架构裁定：

1. **两层分离**：GAS 的 Effect Response Chain（效应提案窗口：Hook/Modify/Chain/PromptInput，`src/Core/Gameplay/GAS/Systems/EffectProposalProcessingSystem.cs`）是**技能作用域**的机器，活动**不骑它**。活动是 **representative entity / player 作用域**的决策层。
2. 活动的条件=Validation 图、结算=Script 图、派发=TriggerGraph `OfferActivity` op、建任务=`CreateTask` op；配置结构见 `.epic-briefs/activity-v4-spec.md`（v4 稿，先读它）。

## 用户刚提出的新需求（本研究的问题源）

- 活动的作用域不只是"某个实体"：多数时候是 **representative entity（玩家的代表实体）/ player 作用域**——王廷决议、势力国策，归属"谁在替谁拍板"。
- 存在**全局事件**：跨玩家的世界级事件，需要**多个 player 投票**表决，也由 Activity 负责。

## 当前引擎事实（供绑定设计）

- 玩家/队伍无特殊地位，全部物化为实体；**Representative Entity** 是一个玩家的唯一控制入口实体（`commandSystem.md`：ControlPlane 从 Representative 经 query graph 得控制集）。
- 地图配置已有 `Players: [{PlayerId, TeamId, RepresentativeInstanceId}]` 绑定与 `startupLocalSeats`（seat→player）——玩家↔代表实体的映射已是现成数据。
- 活动现状：scope host=单实体；准入键=(定义id, scopeKey)；面板 topic 可按 ownerScope 过滤；确认命令 `activity.confirm` 无 seat 维度。
- 四层架构 Machine/App/Seat/Device（`gitbook/architecture/four-layer-architecture/`）；联机/多 seat 的会话语义在 mapSessions。

## 研究问题

1. **作用域分类学**：活动的"归属"怎么表达——per-representative（=player 的物化，优雅）/ per-team / per-faction / global（scope=null？map？）。全局活动的准入键、呈现与去重语义和实体作用域有何不同？"给每个 player 各发一份"（fan-out）与"全体共用一份"（shared instance）在 schema 上怎么区分？
2. **投票语义**：一个实例、N 个投票者（seats/玩家/派系），每人提交选项偏好。需要定义：投票者集合怎么声明（全部 seat？按 team？按派系权重？）；决议规则（多数/加权多数/一致/主席独裁/超时默认）；平局；投票可见性（公开计票/密投）；投票可否撤改；决议后才跑 settle。P社参照：EU4 联机事件表决、CK3 联机同意弹层、Vic3 法律制定的利益集团投票、HOI4 国策的限时完成。
3. **Schema 提案**：上述落在 v4 结构上的形状——`scope` 字段？`resolve` 块（`resolve: "chair" | { vote: {voters, rule, quorum, timeout} }`）？options/settle 与决议的执行时序？
4. **引擎缺口清单**：投票收集存储（instance+seat 键）、带超时的决议系统、per-seat 面板 topic、测试/AgentBridge 钩子——逐项列出，标注"已有可复用/需新增"，禁止发明与既有注册表/图/队列重复的轮子。

## 输出要求

- 中文 markdown；给出 v5 schema 增量草案（只加不改 v4 既有字段）+ 待裁定点清单；
- 每个设计断言给出依据（引擎文件/文档路径，或 P社机制先例的机制层转述——禁止抄原文文案）；
- 只读仓库，禁止修改任何文件。
