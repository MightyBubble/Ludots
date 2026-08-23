# TODO · Order 与输入卷（卷 7）

> 卷 7（ord-01…06、input-01…05）撰写过程中沉淀的治理项。严重度：高（误导用户/数据错误）· 中（易用性/体系缺口）· 低（打磨）。

| # | 严重度 | 问题（第一性） | 现状证据 | 方案建议 | 状态 |
|---|---|---|---|---|---|
| O1 | 低 | 订单类型类默认值不可达且不一致：加载器逐字段显式赋值，类默认永不生效，但 `QueuedModeMaxSize=16` 超加载器上限 8、黑板键默认非 -1——读代码者会误信默认合法 | src/Core/Gameplay/GAS/Orders/OrderTypeConfig.cs:23-43 | 类默认改为与加载器一致的合法缺省，或删默认改构造必填 | 待立项 |
| O2 | 中 | 语义 orderTypeId 只能自引用（须与条目 key 逐字相同），表面像"别名机制"实非——作者会误以为能映射外部 key | src/Core/Gameplay/GAS/Orders/OrderTypeConfigLoader.cs:356-369 | 文档已收口（ord-01 spec）；如需真别名另立显式映射表 | 文档已覆盖 |
| O3 | 中 | 空间 payload 所有权硬守卫正确但易踩：所有清理路径必须先 Release，漏放即泄漏且无诊断 | src/Core/Gameplay/GAS/Components/OrderBuffer.cs:179-189 等 RequiresOwner 族 | 调试期断言 + 泄漏诊断计数（ord-03 spec 任务） | 待立项 |
| O4 | 中 | 提交结果到失败原因的映射对"接受态"输入抛异常，调用方须先排除——接口把合同藏在调用约定里 | src/Core/Gameplay/GAS/Orders/OrderAdmissionResults.cs:50-66（:62-63 抛） | 接受态返回显式哨兵或拆分接口（ord-03 spec 任务） | 待立项 |
| O5 | 低 | 黑板四缓冲操作面不对称：Float 仅 TryGet/Set 无 Remove，其余三种可移除——清理路径对 Float 只能覆盖 | src/Core/Gameplay/GAS/Components/BlackboardFloatBuffer.cs（无 Remove） | Float 补齐移除接口（ord-04 spec 任务） | 待立项 |
| O6 | 低 | `Cast.AbilityId`（113）死键：定义注册但核心无读取方——内置键表里的暗桩 | src/Core/Gameplay/GAS/Orders/OrderBlackboardKeys.cs:26-87 | 接通消费方或从内置表移除（ord-04 spec 任务） | 待立项 |
| O7 | 高 | 输入映射文件全部由 mod 携带且缺失时仅日志跳过：新 mod"按键无效却无错误"，排障成本高 | 根 assets/Input 无 input_order_mappings.json；InputOrderMappingLoader 缺文件日志跳过 | mod 清单声明携带则缺失 fail-fast，未声明静默（ord-06 spec 任务） | 待立项 |
| O8 | 高 | 施法派发 cycle 的推进入口生产零调用（仅测试）：`one_by_one` 档案退化为永远第一位演员——配置承诺与运行事实背离 | src/Core/Input/Interaction/CastDispatchProfileRegistry.cs:163-176 | 订单接受回执处按 advanceOn 推进轮转游标（input-02 spec 任务） | 待立项 |
| O9 | 高 | 根默认输入的关键动作（Hotkey1-9、PrimaryClick 等）只绑在 Physics2D_Playground，Default_Gameplay 未绑——默认玩法上下文不可触发，依赖 mod 补充 | assets/Input/default_input.json 两上下文绑定对照 | 补根绑定或"玩法上下文必绑清单"校验（input-05 spec 任务） | 待立项 |

**相关文档**：[backlog 总账](backlog.md) · [卷 7 目录](../README.md)
