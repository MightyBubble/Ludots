# ab-05 editor spec · 激活门

> 编辑器实现任务书。编辑器需求见 [ab-05 UXD](../uxd/ab-05-activation-gates.md)；引擎侧见 [runtime spec](../spec-runtime/ab-05-activation-gates.md)。

## 1. 概述

激活门检查器实现：关卡链视图、状态沙盒、同源干跑。

## 2. 设计

- **关卡链**：技能定义三门块 + 固定判序投影为关卡组件；每关内嵌对应选择器（tag/图/进度需求注册表）。
- **状态沙盒**：编辑器侧单位模型（tag 集、属性、进度状态），干跑调用与引擎同一评估器（tag 门评估器、前置图评估器、进度需求评估），无副作用。
- **失败原因映射**：干跑结果按引擎失败原因码渲染，与表现层置灰原因同码。

## 3. 精确语义与不变量

- 干跑判序 = 订单起播判序；直接激活入口差异只作只读对照展示。
- 沙盒判定的 tag 门/图/进度结果与引擎逐字一致（同一判定件）。

## 4. 依赖接口与验收
- 消费：AbilityActivationBlockTagEvaluator、前置图评估入口、进度需求评估入口、失败原因枚举。
- 验收：构造六关各拦一例，沙盒结果与测试局实测一致。

**相关文档**：[ab-05 UXD](../uxd/ab-05-activation-gates.md) · [ab-05 runtime spec](../spec-runtime/ab-05-activation-gates.md)
