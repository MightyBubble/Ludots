# infra-01 editor spec · 引擎与物理配置

> 编辑器实现任务书。编辑器需求见 [infra-01 UXD](../uxd/infra-01-engine-physics.md)；引擎侧见 [runtime spec](../spec-runtime/infra-01-engine-physics.md)。

## 1. 概述

项目设置"引擎与物理"页实现：四文件表单、区间守卫、影响预览。

## 2. 设计

- **表单模型**：四个 DeepObject 文件的字段级投影；未覆盖字段显示继承值（引擎默认/依赖 mod），写时生成本 mod 局部键。
- **守卫**：区间与必填规则来自引擎校验合同的同源描述（单一出处，编辑器不自创界限）。
- **缺省对照**：读引擎缺省常量与实配文件双源，渲染差异提示（D2 缓解面）。
- **影响预览**：步频比与粗预算为编辑器侧派生计算。

## 3. 精确语义与不变量

- 表单守卫与引擎启动校验一致（同源规则）。
- 保存产物 = 本 mod 只写被改字段的 DeepObject 局部。

## 4. 依赖接口与验收

- 消费：四文件读写、引擎缺省常量投影、层注册表（发射层选择）。
- 验收：越界值不可保存；产物通过引擎启动校验；差异提示与实配/缺省一致。

**相关文档**：[infra-01 UXD](../uxd/infra-01-engine-physics.md) · [infra-01 runtime spec](../spec-runtime/infra-01-engine-physics.md)
