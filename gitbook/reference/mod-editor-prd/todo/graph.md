# TODO · 图系统卷

> 卷 8 写作中沉淀的图系统治理项。条目模型同 [backlog](README.md)；立项后链到 issue。

| # | 严重度 | 问题（第一性） | 现状证据 | 方案建议 | 状态 |
|---|---|---|---|---|---|
| G1 | 低 | kind 不匹配错误文案中英混杂：整库错误均为英文，唯此处中文（含「」引号与全角标点），诊断呈现与翻译管线不一致 | src/Core/GraphRuntime/GraphProgramRegistry.cs:197-198 | 统一错误文案语言（随 cfg-04 id 统一任务定调） | 待立项 |
| G2 | 高 | 程序缓冲溢出静默丢弃：编译超 128 指令不报错，尾部指令无声消失——图行为错误且无诊断 | src/Core/GraphRuntime/GraphProgramBuffer.cs:21（`if ((uint)Count >= CAPACITY) return;`） | Add 溢出改为报错（GASG0009 预算族），或缓冲上限与编译预算同源化 | 待立项 |
| G3 | 中 | HaltReturnInt 缺省 value 读 I[0]：与宿主 ABI 槽（Script→I[0]）同寄存器的"环境约定"只活在代码注释里，作者无从得知，误用即静默读到返回槽 | src/Core/NodeLibraries/GASGraph/GraphControlFlowCompiler.cs:814-819（注释自认 ambient register contract）；GraphRegisterFile.cs:73-76 | 成文（手册+编辑器提示）或禁用缺省、要求显式连边 | 待立项 |
| G4 | 中 | 图装载先清后编译：LoadIdsAndCompile 开头清空注册表再编译，任一片段编译抛出时注册表已空——失败现场丢失、重试语义不明 | src/Core/NodeLibraries/GASGraph/Host/GraphProgramConfigLoader.cs:47-56（Clear 在前）、:145（Freeze 在后） | 先在暂存区编译全量成功后原子交换，失败保留旧注册表 | 待立项 |
| G5 | 中 | FuncLib 注册层死路径：GraphFunctionCatalog.Register 接受 Script/Validation/Score 三 kind，但 loader 只喂 Script——Validation/Score 分支不可达，注释（deferred）与实现失真 | src/Core/GraphRuntime/GraphFunctionCatalog.cs:22；GraphFunctionCatalogLoader.cs:93-98 | Register 收敛为仅 Script，或删除延迟注释并补 invoke 通道设计 | 待立项 |
| G6 | 高 | Query 物化链路空转：outputs 声明、编译 schema、回写器、槽池、清理系统五件全套实现，但主线资产零 Query 图零 outputs，回写器仅注册为服务、Core 层零调用——读者按手册配 outputs 无任何可见效果 | GraphReturnWriter.cs:34-181（仅注册为服务）；assets/GAS/graphs.json（无 Query kind）；GraphOutputValueStore.cs:24-128 | 接通消费方（瞄准预览挂点实测）或在手册/编辑器显式标注实验态 | 待立项 |
| G7 | 中 | 事实页图程序上限数字错误：facts.md 生成脚本把 GraphIdRegistry 上限记为 0（误读 InvalidId=0），实际 MaxGraphs=4095——手册数值纪律的源头失真 | gitbook/reference/mod-editor-prd/facts.md「图程序上限 = 0」；src/Core/NodeLibraries/GASGraph/Host/GraphIdRegistry.cs:6 | 修 generate-prd-facts.py 抽取逻辑并再生成（可与 T13 CI 门禁合并） | 待立项 |
| G8 | 中 | "纯读选 tag id"节点空档：图里无法把 tag 名变成可查表的 id，状态栏 curState 类场景无一等节点；ADR #876 删 SelectTagInMask/LookupTagDisplayToken 时留了"可另单保留"活口无人兑现（同总账 T8） | ADR #876 决策表；全库无此二 op，仅表现层 TagDisplayTable 残名 | 重立 op：输入绑通用 tag 集/用户表，禁绑专表；同步清理 TagDisplayTable 残名 | 待提案（gr-op-03 spec） |
| G9 | 低 | op 枚举死注释失真：opcode 110 处注释称 QueryFilterTeam 已删、用 QueryFilterRelationship 替代——实际 QueryFilterTeam 已以新 opcode 重生且与后者并存（团队过滤与关系过滤并存是产品语义，不是重复） | src/Graph/Ludots.Graph.Abstractions/GraphOps.cs:52 与 :119；描述符表 :161/:112 | 删除死注释；"现存 op"以描述符表为唯一 SSOT | 待清理（gr-op-06 spec） |
| G10 | 低 | 事件载荷槽位边界无编译期诊断：Int 0..1、Float 0..3 的范围只存在于描述符 imm 语义，越界报错不带槽位号 | GraphOpDescriptorTable.Data.cs:186-187 | 补一条带槽位号的边界错误信息 | 待立项（gr-op-01 spec） |
| G11 | 中 | Int 与 Float 无换算节点：跨类型公式只能两端手工改写，伤害公式处频繁撞墙 | GraphOpDescriptorTable.Data.cs:87-103（全族无转换件） | 若补节点走新 op 注册，禁止隐式转换；先收集实场景 | 观察（gr-op-02 spec） |
| G12 | 低 | 双路写入无冲突提示：同图同属性既有修改器又有 WriteSelfAttribute 直写时静默并存 | WriteSelfAttribute :141（derivedWrite） | 编译期 lint：同图同属性双路写入告警 | 待立项（gr-op-04 spec） |
| G13 | 低 | 黑板 Read 掩码不含 Query：Query 图无黑板读入口，按记忆筛选暂无实场景 | ReadBlackboard×3 :128-130（L+SC） | 有实场景再扩掩码，不加平行节点 | 观察（gr-op-05 spec） |
| G14 | 低 | TeamIdSource 旗标语义无文档正本：QueryFilterTeam 的 teamId 取值来源（立即值 vs 按 source 取队伍）只在描述符旗标里 | QueryFilterTeam :161 | 归属 rel-01 落地时补队伍语义段落 | 随 rel-01（gr-op-07 spec） |
| G15 | 低 | Mutual 与 BetweenPair 差异无用户面文档：点对间链集 vs 双向链两语义只在代码里 | RelationshipQueryMutual/BetweenPair :151-152 | rel-01 落地时补对比表 | 随 rel-01（gr-op-08 spec） |
| G16 | 低 | TargetListGet 不进 Query 图：Query 图按下标取元素要绕道 gr-op-07 聚合 | TargetListGet :115（L+SC） | 有实场景再扩掩码 | 观察（gr-op-09 spec） |
| G17 | 中 | 生命周期事务无编译期前驱检查：InvokeBuiltin 的事务前驱可达性靠运行期管线隐式状态，写错图要到运行才炸 | BeginLifecycleTransaction/InvokeBuiltin :177-178 | 编译期可达性检查：InvokeBuiltin 须在 BeginLifecycleTransaction 可达下游 | 待立项（gr-op-11 spec） |
| G18 | 低 | Script 图落点校验空档：放置校验四件掩码不含 Script，行为树叶子校验落点无件可用 | ClampTargetToRange 等 :181-184（LinearAll） | 有实场景再扩掩码 | 观察（gr-op-12 spec） |
| G19 | 低 | Query 管线无拓扑筛：按控制域/知情投影筛实体无对应 Query 件，"只看看得见的敌人"要出图判定 | ControlDomain×2/KnowledgeHasProjection :188-190 | 有实场景考虑 Query 管线扩展而非扩三件掩码 | 观察（gr-op-13 spec） |
| G20 | 中 | 糖循环变量钉槽无 lint：While/Until 内被写的未钉槽 Int 会被暂存复用冲掉，静默错值 | GraphAuthoringSugar.cs:12-16；节点画廊 JumpIfFalse.json 依赖手写 pinRegister | 编译期 lint：糖循环内被写的未钉槽 Int 建议钉槽 | 待立项（gr-op-14 spec） |
