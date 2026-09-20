# gr-op-04 reference · 节点：属性与配置

> 现状参考。第一性需求见 [gr-op-04 PRD](../prd/gr-op-04-attributes.md)；配置说明见 [gr-op-04 配置说明](../config/gr-op-04-attributes.md)。

## 1. 现状快照

- LoadAttribute（:86，L+SC，source+属性符号→Float）；LoadSelfAttribute（:140，L+SC，无 source）。
- WriteSelfAttribute（:141，Effect+Derived，value+属性符号）：描述符表唯一 `derivedWrite=true` 行，直写 SetCurrent 绕过修改器。
- LoadConfigFloat/Int/EffectId（:134-136，LinearAll，configKey 符号）：`listenerOwner=true`，监听图禁用（无 owner 模板上下文）。
- 键经 ConfigKeyRegistry；属性经属性注册表（上限见事实页）。

## 2. 代码锚点

| 机制 | 位置 |
|---|---|
| LoadConfig 三件 | src/Core/NodeLibraries/GASGraph/GraphOpDescriptorTable.Data.cs:134-136 |
| LoadAttribute | GraphOpDescriptorTable.Data.cs:86 |
| LoadSelfAttribute / WriteSelfAttribute | GraphOpDescriptorTable.Data.cs:140-141 |
| 配置键注册表 | src/Core/Gameplay/GAS/Registry/ConfigKeyRegistry.cs:5 |

**相关文档**：[gr-op-04 PRD](../prd/gr-op-04-attributes.md) · [attr-01 reference](attr-01-definition.md)
