# TODO · Ability 技能域

> 写卷 6 技能系统手册（ab-01…ab-10 六件套）中沉淀的第一性问题；各篇 spec 治理项以本表编号引用。严重度：高（误导用户/数据错误）· 中（易用性/体系缺口）· 低（打磨）。

| # | 严重度 | 问题（第一性） | 现状证据 | 方案建议 | 状态 |
|---|---|---|---|---|---|
| AB1 | 中 | TagSignal/TagSignalTarget 的增/删语义藏于 payloadA 整数，JSON 无枚举名——作者无从知道 0/1 是加/删 | AbilityExecComponents.cs:29（PayloadA: 0=add,1=remove）；FireTagSignal AbilityExecSystem.cs:1259-1274 | 加载器增加具名写法（如 `mode: add\|remove`），移除 payloadA 裸整数路径 | 待立项 |
| AB2 | 低 | Committed 死枚举值：运行状态六值之一全库无赋值点 | AbilityExecComponents.cs:65-79（Committed=2）；全库零赋值 | 移除枚举值或接通其语义 | 待立项 |
| AB3 | 中 | CallerParams 空间参数追加失败整技能报 PreconditionFailed，错误不指明是池余位不足——作者无法定位根因 | AbilityExecSystem.cs:1185-1212 | 拆独立失败原因（容量不足）并携带池状态 | 待立项 |
| AB4 | 高 | cooldown 块是零使用配置面：编译通过但无任何系统据此实现冷却——作者以为写了就冷却 | 全 mods abilities.json grep "cooldown"=0；AbilityCooldown 全 Core 无写入系统（仅 loader 编译 + AI 就绪读取） | 二选一：接通（起播自动挂 tag/写属性）或降级为 AI 查询面并在文档言明；编辑器向导默认生成 TagClip+blockTags 闭环 | 待立项 |
| AB5 | 中 | 形态路由同分无加载期校验：运行期平分先出现者静默胜出，作者误当 priority 是全序 | AbilityFormRoutingSystem.cs:28-93（严格更大才替换）；AbilityFormSetConfigLoader.cs 无同分检测 | 加载期同分可同时匹配检测告警，或文档化平分规则为合同 | 待立项 |
| AB6 | 中 | 缺 AbilityFormSlotBuffer 的 actor 静默无形态路由：模板漏配组件无任何信号 | AbilityFormRoutingSystem.cs:28-93（查询仅命中持全三组件者） | 启动期/模板编辑器诊断"路由条件涉及的单位缺组件" | 待立项 |
| AB7 | 中 | 上下文组候选图 kind 运行期才校验：错 kind 图过启动、迟到爆发在打分消费 | ContextGroupConfigLoader.cs:24-152（仅解析不查 kind）；ContextScoredOrderResolver.cs 消费时要求 Validation/Score | kind 校验前移到加载期（注册表已含 kind） | 待立项 |
| AB8 | 中 | 临时授予槽层无生产写入口：GrantedSlotBuffer 与按来源回收 API 完备但仅测试使用——"buff 授予技能"能力未落地 | AbilityStateBuffer.cs:76-174；src/Core 无 Grant 生产调用方 | 接通效果处理器（按来源 tag 授予/回收技能）或标注预留层 | 待立项 |
| AB9 | 低 | exec.callerParams 参数池全仓无真实使用者：能力已接通、演示场景无用例，配置说明只能给教学骨架 | 全仓 JSON grep "callerParams"=0；AbilityExecLoader.cs:437-489 | 演示场景落地首个参数化技能后回填 config 实例节 | 待立项 |
