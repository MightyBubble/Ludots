# gr-02 reference · 六种 Kind

> 现状参考。第一性需求见 [gr-02 PRD](../prd/gr-03-kinds.md)；配置说明见 [gr-02 配置说明](../config/gr-03-kinds.md)。

## 1. 现状快照

- 返回约定现状：Script 的 HaltReturnInt 写 I[A] 入 ReturnInt（宿主 ABI 槽 I[0]）；Score 写 F[0]；Validation 执行前清零 B[0]；Effect 无返回；Query 写 TargetList 并按 schema 物化；Derived 由 WriteSelfAttribute 直写自身属性 SetCurrent。
- 白名单现状：ScriptOnly 节点仅 Script；Effect 全放行；Script 仅 Pure；其余 kind 需 Pure，唯 Derived 叠加 DerivedAttributeWrite（唯一 WriteSelfAttribute）。
- 监听相容现状：InvokeBuiltin 拒；RequiresListenerOwnerContext 的图拒 LoadConfig*；纯相位须 Pure、非纯相位须 Pure+GasTransactional。
- 预设寄存器：E0/E1/E2 编译期 Reserve 且 scratch-protected；E2 来源 TargetContext/Viewer/PreviewTarget；宿主 ABI 槽保护 Validation→B[0]、Score→F[0]、Script→I[0]。
- 程序校验含必含 HaltReturnInt；策略错误码八值。

## 2. 代码锚点

| 机制 | 位置 |
|---|---|
| Script 返回写回 | src/Core/NodeLibraries/GASGraph/GasGraphOpHandlerTable.cs:512-525 |
| ABI 槽 I[0] | src/Core/NodeLibraries/GASGraph/GraphRegisterFile.cs:73-76 |
| Score→F[0] / Validation 清零 | src/Core/NodeLibraries/GASGraph/GraphExecutor.cs:448,394,411 |
| Query 物化写回 | src/Core/NodeLibraries/GASGraph/GraphReturnWriter.cs:92,150-154 |
| Derived 直写 | GraphOps.cs:98 |
| 程序校验四件与错误码 | src/Core/NodeLibraries/GASGraph/GraphKindOperationPolicy.cs:41-48,108-119 |
| 白名单四规则 | src/Core/NodeLibraries/GASGraph/GraphOpDescriptorTable.cs:63-87 |
| 监听相容 | GraphKindOperationPolicy.cs:319-335 |
| E0-E2 与槽保护 | GraphRegisterFile.cs:61-63,65-76,222-231 |
| E2 三种来源 | src/Core/NodeLibraries/GASGraph/GraphFrame.cs:8-14 |
| 实例（Score/Validation/Derived） | mods/showcases/capability_standard/CapabilityStandardGraphScoreShowcaseMod 等（gr-02 config 第 6 节） |

**相关文档**：[gr-02 PRD](../prd/gr-03-kinds.md) · [gr-00 reference](gr-01-model.md) · [gr-07 reference](gr-08-mount-points.md)
