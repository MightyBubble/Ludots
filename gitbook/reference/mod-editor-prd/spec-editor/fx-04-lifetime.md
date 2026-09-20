# fx-04 editor spec · 生命周期与时长

> 编辑器实现任务书。编辑器需求见 [fx-04 UXD](../uxd/fx-04-lifetime.md)；引擎侧见 [runtime spec](../spec-runtime/fx-04-lifetime.md)。

## 1. 概述

寿命区表单：三选一联动、duration 矩阵校验、时间带示意。

## 2. 设计

- 矩阵显隐规则与 loader 的 duration 校验同源引用，不在表单重写。
- clockId 下拉数据源为 clock 表；首拍预估只显示散列区间承诺，不实现散列。
- durationTicks/periodTicks 挂热字段徽标，走工作台热替换。

## 3. 精确语义与不变量

- 表单矩阵判定 ⇔ loader 判定；Turn 等已移除时钟不进下拉。

## 4. 依赖接口与验收

- 消费：效果表加载校验、clock 表、热替换管线。
- 验收：三种寿命往返无损；全零块被即时拦截；热改时长下次施放生效。

**相关文档**：[fx-04 UXD](../uxd/fx-04-lifetime.md) · [fx-04 runtime spec](../spec-runtime/fx-04-lifetime.md)
