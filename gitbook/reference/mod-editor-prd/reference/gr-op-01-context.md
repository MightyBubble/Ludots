# gr-op-01 reference · 节点：常量与上下文

> 现状参考。第一性需求见 [gr-op-01 PRD](../prd/gr-op-01-context.md)；配置说明见 [gr-op-01 配置说明](../config/gr-op-01-context.md)。

## 1. 现状快照

- 13 个 op：常量三件（ConstBool/ConstInt/ConstFloat）、实体加载（LoadCaster→E0、LoadExplicitTarget→E1、LoadViewer→E2）、上下文三件（Source/Target/TargetContext）、事件载荷两件（imm 槽位 Int 0..1、Float 0..3）、落点坐标两件（Int 厘米）。
- ConstInt 支持 `pinRegister` 钉槽；ConstFloat 与 LoadCaster 覆盖 Query/Script，其余 Load 系为线性四类或 L+SC。
- E0/E1/E2 在寄存器文件创建时按 EntityPreset 理由 Reserve，scratch 分配避让。
- HaltReturnInt 缺省 `value` 读 I[0]（环境槽），同槽见 Script Host ABI。

## 2. 代码锚点

| 机制 | 位置 |
|---|---|
| 常量与实体加载描述符 | src/Core/NodeLibraries/GASGraph/GraphOpDescriptorTable.Data.cs:79-83 |
| 上下文三件 | GraphOpDescriptorTable.Data.cs:137-139 |
| 落点坐标 | GraphOpDescriptorTable.Data.cs:179-180 |
| 观众与事件载荷 | GraphOpDescriptorTable.Data.cs:185-187 |
| E0/E1/E2 保留 | src/Core/NodeLibraries/GASGraph/GraphRegisterFile.cs:61-63 |
| Reserve/scratch 避让 | GraphRegisterFile.cs:222-226 |
| HaltReturnInt 缺省 I[0] | src/Core/NodeLibraries/GASGraph/GraphControlFlowCompiler.cs:814-819 |

**相关文档**：[gr-op-01 PRD](../prd/gr-op-01-context.md) · [gr-op-02 reference](gr-op-02-math.md)
