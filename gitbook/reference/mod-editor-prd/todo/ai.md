# TODO · AI 行为层

> 卷 9（ai-01…ai-11）写作过程中沉淀的 AI 行为层治理项。条目模型同 [backlog](README.md)：`编号 | 严重度 | 问题（第一性）| 现状证据 | 方案建议 | 状态`。

| # | 严重度 | 问题（第一性） | 现状证据 | 方案建议 | 状态 |
|---|---|---|---|---|---|
| I1 | 中 | Constant 输入只吃整数：效用基线要 0.5 这类小数只能绕 GraphScore，作者不知道 | AiConfigLoader.cs:604（TryReadInt） | Value 补 float 通道（TryReadFloat），或文档化绕行并编辑器提示 | 待立项 |
| I2 | 中 | 同目录两套大小写规则：inputs 的 Kind 不区分大小写，BT/HFSM 的 kind/leaf/predicate 区分——作者踩坑无提示 | AiConfigLoader.cs:601-643 vs GraphBehaviorDefinitionLoader.cs:402-443（ignoreCase:false） | 统一为单一规则；短期在编辑器下拉固定拼写兜底 | 待立项 |
| I3 | 高 | 三层连续区间约束（Tasks/Decisions/DecisionMakers）对跨 mod 分片是隐性限制：分片拆同一决策者的引用易触发 contiguous 报错 | AiConfigLoader.cs:912-943,966-997,1040-1071 | 文档化"同一决策者须由一方整体提供"；或改区间为显式 id 列表（结构改动） | 待立项 |
| I4 | 低 | HasAllTags 的 IntB 恒 0：编译端固定传 0、运行时 priorityBucket+=op.IntB 恒加 0——死字段未接线 | AiConfigLoader.cs:546-550；UtilityAiRuntimeEvaluator.cs:431 | 接通权重通道（如 BucketBonus 字段）或删掉累加行 | 待立项 |
| I5 | 高 | 组合任务 Kind 命名误导：Sequence 是 no-op、Parallel/ParallelComplete 只置 requiredAny 不做事，三种行为近乎等价——作者以为有编排 | UtilityAiRuntimeEvaluator.cs:184-210 | 实现真实编排语义，或收窄 Kind 枚举并文档声明 SubmitOrder 单发 | 待立项 |
| I6 | 高 | stance 是半成品：编译了但无系统消费（仅 AIInspectorMod 打印长度）；UtilityAiStanceState 无读写；DefaultStanceId 只在测试出现——作者以为姿态生效 | AiConfigLoader.cs:791-815；UtilityAiRuntimeComponents.cs:65-68；mods/AIInspectorMod/Triggers/PrintAiConfigTrigger.cs:58 | 立项消费系统（索敌/反击/追击并入过滤器或就绪）或冻结声明；编辑器先标半成品 | 待立项 |
| I7 | 低 | 空 [] 占位文件：两个 showcase 的 stances/actuators 永远为空，示范价值为零还暗示已接线 | mods/showcases/utility_autocast/.../assets/AI/stances.json、actuators.json | 随 I6 一并处置：要么给真例要么删文件 | 待立项 |
| I8 | 中 | HFSM 平级转移后定义者胜：与"先声明优先"直觉相反，作者排错困难 | HfsmWorld.cs:156（tr.Priority >= bestPriority） | 编辑器前置标注平局胜者；行为改严格大于需独立立项 | 待立项 |
| I9 | 低 | AiConfigModels.cs 全部 9 个 POCO 死代码：loader 全程 JsonObject 手工解析，POCO 无任何消费方 | src/Core/Gameplay/AI/Config/AiConfigModels.cs | 删除，或改为真正反序列化目标（与 I10 schema 联动） | 待立项 |
| I10 | 中 | utility 十表无 schema：仅 BT/HFSM 有 schema 且不参与流水线校验——编辑器无结构提示，字段拼错到启动期才报 | assets/AI/behavior_trees.schema.json、hfsm.schema.json；utility 族无 | 十表补 schema 并决定是否挂流水线校验；与 I9 反序列化联动 | 待立项 |
