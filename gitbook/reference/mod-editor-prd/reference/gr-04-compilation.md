# gr-06 reference · 编译与校验

> 现状参考。第一性需求见 [gr-05 PRD](../prd/gr-04-compilation.md)；配置说明见 [gr-05 配置说明](../config/gr-04-compilation.md)。

## 1. 现状快照

- 检查清单现状依序：头校验、节点 id 唯一、entry 存在、控制边三段必填+唯一、值边四段+唯一、寄存器分配、必需边（BranchBool 需 true/false+condition 等）、端口白名单、不可达检测（DFS）、未定义读 SSA、预算（单执行指令上限）、前缀 Jump、输出 schema。
- 诊断码 21 个：GASG0001-0021（缺图 id、缺 entry、重复节点 id、未知 op、缺节点引用、0008 不可达、0009 预算、0010 类型不匹配、0011 不支持 kind、0012 缺控制边、0013 意外控制边、0014 重复控制边、0015 缺值输入、0016 重复值边、0017 寄存器越界、0018 未定义读、0019 空图、0020 缺节点 id、0021 别名冲突）。
- 糖现状：BranchBool（Script/Effect）、SwitchInt/While/Until/Wait 均 Script-only、Wait=Yield 别名；糖名 SSOT 在 GraphAuthoringSugar；Linear/Query 链尾自动补 HaltReturnInt(A=0)；HaltReturnInt value 可缺省读 I[0]。
- 符号 patch 种类：Tag（QueryFilter*/SendEvent/HasTag）、Attribute（Load/Modify/Agg*/Self/Write）、EntityTemplate、EntityCollection key、EffectTemplate、TargetDispatchPreset、ConfigKey（黑板+LoadConfig）、InvokeBuiltin、关系四类（metric/reason/type/flag）；PatchFuncLib 换图 id 清 FuncLib 位；幂等 ConditionalWeakTable。
- 装载顺序末尾 GraphIdRegistry.Freeze()；装载先清后编译。

## 2. 代码锚点

| 机制 | 位置 |
|---|---|
| 检查清单 | src/Core/NodeLibraries/GASGraph/GraphControlFlowCompiler.cs:176-307 |
| HaltReturnInt 缺省读 I[0] | GraphControlFlowCompiler.cs:814-819 |
| 输出 schema 编译 | GraphControlFlowCompiler.cs:1905-2081 |
| 诊断码字典 | src/Core/NodeLibraries/GASGraph/GraphDiagnostics.cs:13-31 |
| 糖名 SSOT | src/Core/NodeLibraries/GASGraph/GraphAuthoringSugar.cs:12-16 |
| 链尾自动收尾 | src/Core/NodeLibraries/GASGraph/GraphControlFlowCompiler.Linear.cs:952-955 |
| 符号 patch 种类 | src/Core/NodeLibraries/GASGraph/Host/GraphProgramSymbolPatcher.cs:30-187 |
| PatchFuncLib | GraphProgramSymbolPatcher.cs:195-218 |
| 装载顺序与冻结 | src/Core/NodeLibraries/GASGraph/Host/GraphProgramConfigLoader.cs:47-145 |

**相关文档**：[gr-05 PRD](../prd/gr-04-compilation.md) · [gr-03 reference](gr-02-document.md) · [gr-08 reference](gr-06-funclib.md)
