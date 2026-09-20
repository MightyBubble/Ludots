# attr-05 runtime spec · 属性绑定与 Sink

> 引擎实现任务书。第一性需求见 [attr-05 PRD](../prd/attr-05-bindings.md)；现状见 [reference](../reference/attr-05-bindings.md)。

## 1. 概述

绑定表全显式合同、折叠成组的应用序，与 sink 注册封闭语义。

## 2. 设计

- 加载保持：七字段 RequireExplicit、sink 查注册表未知抛、channel 显式并交 sink 复核、合并后按 id 排序遍历再按 (sink, 声明序) 折叠成组；绑定系统在聚合重算后逐组 Apply，Override 替换、Add 累加（bool 通道 OR），脉冲策略消费后源属性归零。
- **治理项 A9**：Graph.EdgeCostOverlay sink 注册后零内容绑定（死配置）——补内容消费或注销注册；文档口径统一为三个内置 sink。
- **治理项 A10**：相机行为双 reset 重叠（状态每帧全清+脉冲归零源属性）——收敛单一机制或文档化分工。
- **治理项 A11**：两套同名 AttributeBinding 体系（GAS→sink 与 Input→属性）——类型与文档命名拆分。
- **治理项 A12**：ForceInput2D 的 reset 判定只看条目自身——同 channel 混合 resetPolicy 时顺序敏感，加一致性校验（启动侧）。

## 3. 精确语义与不变量

- sink 注册表启动注册后冻结，绑定只可引用已注册 sink；折叠分组遍历序确定（id 序加载、(sink, 声明序) 序应用），同帧可复现。

## 4. 迁移与治理

现状即基线；A9-A12 见 todo/attribute.md。A9 先做内容决策；A12 可与编辑器侧拦截（attr-05 editor spec）同步落地。

## 变更记录

- v1（2026-08-17）：初版。

**相关文档**：[attr-05 PRD](../prd/attr-05-bindings.md) · [attr-03 runtime spec](attr-03-aggregation.md)
