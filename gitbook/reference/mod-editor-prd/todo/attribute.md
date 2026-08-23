# TODO · 属性域

> 卷 4（attr-01…attr-06）写作与审计沉淀的治理项，编号 A 系列；跨域总账见 [backlog](backlog.md)。spec 层以编号引用本表。严重度：高（误导用户/数据错误）· 中（易用性/体系缺口）· 低（打磨）。

| # | 严重度 | 问题（第一性） | 现状证据 | 方案建议 | 状态 |
|---|---|---|---|---|---|
| A1 | 高 | 修改器第 9 条静默丢失：模板加载不检查 modifiers.Add 返回值，与 configParams 溢出抛错不一致——作者以为生效 | src/Core/Gameplay/GAS/Config/EffectTemplateLoader.cs:267-271（对照 :1336-1339） | 溢出抛错，错误带效果 id 与序号 | 待立项 |
| A2 | 低 | GetBase 名不副实：clampToBase 下实返聚合 Cap，读者误当基线用 | src/Core/Gameplay/GAS/Components/AttributeBuffer.cs:83-98 | 拆分"读基线"与"读钳制上限"两个命名 | 待立项 |
| A3 | 低 | SetCurrentInternal 无条件置 DefinedMask：任何写路径都可能伪造定义位 | src/Core/Gameplay/GAS/Components/AttributeBuffer.cs:86 | 收敛为仅真实写入置位 | 待立项 |
| A4 | 中 | 聚合资格由 Buff 预设隐式推导：配置面无开关，新增预设默认不聚合易踩坑 | src/Core/Gameplay/GAS/Systems/EffectProposalProcessingSystem.cs:1692 | preset 面显式声明聚合资格，新预设强制显式 | 待立项 |
| A5 | 中 | 无 snapshot 实体首帧 OldValue=0 伪基线：变化事件 Old 失真 | src/Core/Gameplay/GAS/Systems/DeferredTriggerCollectionSystem.cs:115-136 | snapshot 补建对齐实体创建路径（模板创建与批量生成器已带） | 待立项 |
| A6 | 中 | 聚合器构造器允许图程序表为 null：派生绑定实体运行时才爆 | src/Core/Gameplay/GAS/Systems/AttributeAggregatorSystem.cs:28,77-81 | 构造期校验依赖完整，缺失即启动失败 | 待立项 |
| A7 | 中 | 派生属性图已建成未投产：生产零绑定、零 Derived 图，手册只能教学骨架 | assets 全域无 AttributeDerivedGraphBinding；GAS/graphs.json 无 Derived kind | 补 showcase 生产样例或显式标注实验特性 | 待立项（手册已标注） |
| A8 | 低 | 派生写作用域内只能 ModifyAttributeSet 不能 Add：不对称未文档化 | src/Core/NodeLibraries/GASGraph/Host/GasGraphRuntimeApi.cs:1071-1088 | 补 Add 入口或文档化禁令与理由 | 待立项 |
| A9 | 中 | Graph.EdgeCostOverlay sink 注册后零内容绑定：死配置，文档口径曾误记两个 sink | src/Core/Navigation/GraphSemantics/GAS/GraphAttributeSinks.cs:8-11；src/Core/Engine/GameEngine.cs:1306-1311；assets/GAS/attribute_bindings.json | 补内容消费或注销注册 | 待立项（文档已按三 sink 更正） |
| A10 | 中 | 相机行为双 reset 重叠：状态每帧全清与 resetPolicy 归零源属性并存，语义重叠 | src/Core/Gameplay/Camera/CameraBehaviorInputState.cs:71-85；src/Core/Gameplay/GAS/Bindings/CameraBehaviorInputSink.cs:37-40 | 收敛单一机制或文档化分工 | 待立项 |
| A11 | 中 | 两套同名 AttributeBinding 体系：GAS→sink 与 Input→属性，命名易混 | src/Core/Gameplay/GAS/Bindings/AttributeBindingLoader.cs；src/Core/Input/Systems/InputActionAttributeBindingSystem.cs | 命名拆分（如 SinkBinding / InputAttributeSource） | 待立项 |
| A12 | 低 | ForceInput2D reset 判定只看条目自身：同 channel 混合 resetPolicy 时顺序敏感无提示 | src/Core/Gameplay/GAS/Bindings/ForceInput2DSink.cs:28-95 | 同 channel 策略一致性校验（启动或编辑器侧先拦） | 待立项 |
| A13 | 高 | 属性→GameplayEvent 链路整体休眠：注册入口生产零调用、无配置文件，当前内容永不发布 | src/Core/Gameplay/GAS/Registry/AttributeEventTagRegistry.cs；唯一调用方 src/Tests/GasTests/GasCore/DeferredTriggerProcessEventTests.cs:23 | 补配置入口或显式标注实验特性 | 待立项 |
| A14 | 低 | 事件映射 Register 冻结后调用的错误信息不指向真因 | src/Core/Gameplay/GAS/Registry/AttributeEventTagRegistry.cs:5-24 | 错误信息带注册时机与正解 | 待立项 |
| A15 | 低 | 两条属性变化通知链（事件总线 vs 表现位）机制与 OldValue 语义不同，无官方对比 | src/Core/Gameplay/GAS/Systems/DeferredTriggerCollectionSystem.cs:110-161；src/Core/Presentation/Systems/GameplayPresentationProjectionSystem.cs:282-319 | 手册对比表已建（attr-06 配置说明）；架构文档同源化 | 文档已覆盖 |

备注：attr-01 spec 引用的 T16（扩展属性死链路）尚未入总账，待 attr-01 批次补记。
