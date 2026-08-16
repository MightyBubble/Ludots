# TODO · 效果卷

> 卷 5（fx）写作与审计中沉淀的第一性问题，源自效果系统事实报告逐条入账（源列 P 编号即报告原编号，fx 编号即分篇出处）。条目模型同 [总账](README.md)；spec 篇治理项以本表 E 编号引用。

| # | 源 | 严重度 | 问题（第一性） | 现状证据 | 方案建议 | 状态 |
|---|---|---|---|---|---|---|
| E1 | P1 | 中 | 内建 preset 无核心资产示范：16 种 preset 绝大多数在核心 effects.json 无消费者，作者照 preset_types 写效果却找不到官方样例 | assets/GAS/effects.json 现仅 1 条（Effect.Preset.ApplyForce2D）；assets/GAS/preset_types.json 共 16 条 | 核心（或演示底座）资产为每 preset 补至少一条示范条目；补齐前手册各 preset 篇标注"核心无示例、见 mod 示例" | 待立项 |
| E2 | P4 | 中 | 空间查询描述符残留四个过滤字段：loader 双路径遗留、从不填充，作者写在查询块会被静默忽略 | src/Core/Gameplay/GAS/EffectTemplateRegistry.cs:126-129（RelationFilter/ExcludeSource/MaxTargets/LayerMask 四字段） | 删除描述符四字段；过滤参数一律走 targetFilter 块（fx-09） | 待立项 |
| E3 | P6 | 高 | 堆叠 limit 无正值校验：写 0/负值常意指"不可堆叠"，现状按无上限意外放行 | src/Core/Gameplay/GAS/Config/EffectTemplateLoader.cs:2038-2070（全必填但无正值校验）；EffectStack.cs:46-59（Limit>0 才判上限） | loader 拒绝 limit≤0；编辑器同源校验并提示 | 待立项 |
| E4 | P7 | 低 | grantedTags 图公式在 loader 直接拒绝，其后图解析分支为死代码 | src/Core/Gameplay/GAS/Config/EffectTemplateLoader.cs:1988-1992 | 删除死分支（接通图公式属能力扩张，须先评审） | 待立项 |
| E5 | P9 | 高 | 响应链动态图路径无消费点：ResponseGraphIds>0 的约定槽位在 Collect 阶段从不读取，只生效静态值——作者写图不生效且无报错 | src/Core/Gameplay/GAS/Components/ResponseChainComponents.cs:45-124；Systems/EffectProposalProcessingSystem.cs:526-576 | 接通图路径或移除字段；接通前编辑器对图槽标注"未生效" | 待立项 |
| E6 | P10 | 低 | 监听器收集注释称"超预算截断丢弃"，实现是 dropped>0 即抛——注释与行为不一致 | src/Core/Gameplay/GAS/Systems/EffectProposalProcessingSystem.cs:511-515 | 修正注释为抛错语义（与 fail-closed 全库取向一致） | 待立项 |
| E11 | fx-12 | 中 | grantedTags GraphProgram 公式 loader 直接拒绝后，其后的参数处理与图解析为不可达死代码（与 E4 同一问题，自本条起合并跟踪） | src/Core/Gameplay/GAS/Config/EffectTemplateLoader.cs:1988-2024 | 同 E4：删除死分支；接通 tag 贡献图评估器属能力扩张须先评审 | 并入 E4 |
| E12 | fx-13 | 高 | CallerParams 合并容量满静默丢弃：模板默认 + caller 覆盖的追加路径在参数容量满时无声跳过——施放侧数值悄悄失效，无计数无日志 | src/Core/Gameplay/GAS/Components/EffectConfigParams.cs:193-221（MergeFrom 仅 Count<MAX 才追加） | 改为可观测：计入预算指标或抛错；编辑器容量条预警（fx-13 spec 已列） | 待立项 |
| E13 | fx-17/fx-19 | 高 | 原子域"可配置不可执行"：Relation 的 RemoveParent/EnsureLink 与 Exchange 处理器实现完整但注册 Unsupported，计划编译 fail-closed——能写出合法 JSON 却无法通过启动 FinalizeAll | src/Core/Gameplay/GAS/BuiltinHandlers.cs:72-79,601-617；EffectExecutionPlan.cs:600-603；EffectExecutionPlanTests.cs:133-160 | 二选一收口：认证原子域（staged 化入事务）或 loader 前置拒绝并说明；编辑器同源警示 | 待立项 |
| E14 | fx-18 | 高 | 视野揭示全链路死配置：revealArea 字段全部可写可校验，但 RevealArea/DecayRevealArea 注册 Unsupported(Vision) 且全库无调用点——任何挂载该块的模板都无法通过计划编译 | src/Core/Gameplay/GAS/BuiltinHandlers.cs:77-78,619-671；src/Core/Vision/KnowledgeAreaRevealRuntime.cs:11；唯一 JSON 实例在 src/Tests/GasTests/Integration/CoreHeroSkillInfraTests.cs:236-258 | 接通（认证 Vision 原子域 + 相位图调用链）或 loader 拒块；接通前编辑器常驻警示（fx-18 spec 已列） | 待立项 |
| E15 | fx-22 | 高 | DeployConsumeSource 预设无法通过启动计划编译：默认图六个生命周期内建全部注册 Unsupported(Lifecycle)，现有测试绕过 FinalizeAll 直连执行器——手册须如实标注"预设在计划编译下不可用，原子链经测试直连验证" | src/Core/Gameplay/Lifecycle/EntityLifecycleBuiltinHandlers.cs:11-16；assets/GAS/preset_types.json:141-148；src/Tests/GasTests/Integration/LifecycleArchitectureTests.cs:44-320 | 认证 Lifecycle 原子域（六步整体作为单一外部原子操作）或提供经认证组合预设；验收 = 部署效果走完启动编译 + 运行 | 待立项 |
