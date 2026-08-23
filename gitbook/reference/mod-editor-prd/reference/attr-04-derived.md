# attr-04 reference · 派生属性图

> 现状参考。第一性需求见 [attr-04 PRD](../prd/attr-04-derived.md)；配置说明见 [attr-04 配置说明](../config/attr-04-derived.md)。

## 1. 现状快照

- 绑定组件：MAX_BINDINGS=8，定长 int 数组+Count；Add 校验 id>0 与容量。
- 执行点在聚合器 ExecuteDerivedGraphs，重算步序③调用。校验链：Count 越界抛→图程序表/图 api 缺失抛→api 须实现派生接口→逐绑定 programId<=0 抛/程序缺失抛/RequireKind(Derived)。
- 提交语义：BeginDerivedAttributeWrites 把宿主属性缓冲拷入暂存并进作用域（重复进入抛 GAS.GRAPH.ERR.DerivedAttributeWriteScopeAlreadyActive）；作用域内宿主属性读走暂存，写仅 ModifyAttributeSet 且 caster==target==owner；EndDerivedAttributeWrites 整体写回，finally 退作用域。
- 副作用禁止：ModifyAttributeAdd/SendEvent 等被拒（GAS.GRAPH.ERR.DerivedAttributeSideEffectForbidden）。图执行强制 Derived kind。
- 配置面：实体模板组件只接受 `{"graphs": ["图名", ...]}`；显式拒绝 graphProgramIds/graphProgramId 数字（"internal only; author graphs by name"）；图名经注册表解析，未知抛。
- 现状生产零实例：assets 全域无绑定、graphs.json 无 Derived kind 图；唯一使用方为测试。

## 2. 代码锚点

| 机制 | 位置 |
|---|---|
| 绑定结构与容量 | src/Core/Gameplay/GAS/Components/AttributeDerivedGraphBinding.cs:9-30 |
| 执行点与校验链 | src/Core/Gameplay/GAS/Systems/AttributeAggregatorSystem.cs:63-122,174 |
| 作用域进入/退出 | src/Core/NodeLibraries/GASGraph/Host/GasGraphRuntimeApi.cs:234-272 |
| 作用域内读写限制 | GasGraphRuntimeApi.cs:599-603,1071-1088 |
| 副作用禁令 | GasGraphRuntimeApi.cs:279-286,1052-1054 |
| 图名绑定与数字拒绝 | src/Core/Config/ComponentRegistry.cs:987-1039 |
| 执行闸 kind 强制 | src/Core/NodeLibraries/GASGraph/GraphExecutor.cs:126-135 |
| 唯一样例（测试） | src/Tests/GasTests/GasCore/AttributeDerivedGraphTests.cs:216-247 |
| 图表现状 | assets/GAS/graphs.json:4,78 |

**相关文档**：[attr-04 PRD](../prd/attr-04-derived.md) · [attr-03 reference](attr-03-aggregation.md)
