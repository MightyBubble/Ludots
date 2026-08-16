# attr-04 runtime spec · 派生属性图

> 引擎实现任务书。第一性需求见 [attr-04 PRD](../prd/attr-04-derived.md)；现状见 [reference](../reference/attr-04-derived.md)。

## 1. 概述

派生绑定与写作用域的语义合同，及生产落地路径。

## 2. 设计

- 绑定结构保持：定长图程序 id 数组+Count，Add 校验 id 有效与容量；配置面只收图名，数字 id 拒绝。
- 执行校验链保持：Count 越界、图程序表缺失、api 未实现派生接口、逐绑定程序缺失/非 Derived kind——逐层抛错。
- 写作用域保持：进入时拷贝宿主缓冲进暂存、重复进入抛错；作用域内读走暂存、写仅限 ModifyAttributeSet 且 caster==target==owner；退出整体写回、异常不落半程。
- 副作用禁令保持：加属性、发事件等一律拒绝（GAS.GRAPH.ERR.DerivedAttributeSideEffectForbidden）。
- **治理项 A7/A8**：已建成未投产——补 showcase 生产样例或特性门禁后显式标注实验；作用域内只能 Set 不能 Add 的不对称——补 Add 写口或文档化禁令与理由。

## 3. 精确语义与不变量

- 派生执行点唯一：聚合重算末段（attr-03 步序③）；派生写过的属性位当帧不恢复持久值；图 kind 强制 Derived。

## 4. 迁移与治理

现状零投产即基线；A7/A8 见 todo/attribute.md。A7 决策（投产或标注）先于编辑器面板排期。

## 变更记录

- v1（2026-08-17）：初版。

**相关文档**：[attr-04 PRD](../prd/attr-04-derived.md) · [attr-03 runtime spec](attr-03-aggregation.md)
