# fx-05 editor spec · 八相位执行

> 编辑器实现任务书。编辑器需求见 [fx-05 UXD](../uxd/fx-05-phases.md)；引擎侧见 [runtime spec](../spec-runtime/fx-05-phases.md)。

## 1. 概述

相位编辑视图：八相位×三槽的绑定网格、回落提示、互斥校验。

## 2. 设计

- 网格为 phaseGraphs 的直接投影；图选择器按相位过滤 kind（提案列仅 Validation）。
- 空 main 槽显示回落目标（读 preset 默认处理器），skipMain 以开关表达。
- 绑定步计数与上限同源。

## 3. 精确语义与不变量

- 网格状态 ⇔ phaseGraphs 块往返无损；互斥判定与 loader 同源。

## 4. 依赖接口与验收

- 消费：效果模板加载产物、图注册表（按 kind 过滤）、preset 默认处理器表。
- 验收：拖图入格即时校验 kind 与互斥；保存后重编译提示正确。

**相关文档**：[fx-05 UXD](../uxd/fx-05-phases.md) · [fx-05 runtime spec](../spec-runtime/fx-05-phases.md)
