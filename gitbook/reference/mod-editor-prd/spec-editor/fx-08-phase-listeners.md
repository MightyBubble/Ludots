# fx-07 editor spec · 相位监听器

> 编辑器实现任务书。编辑器需求见 [fx-07 UXD](../uxd/fx-08-phase-listeners.md)；引擎侧见 [runtime spec](../spec-runtime/fx-08-phase-listeners.md)。

## 1. 概述

监听器编辑器：匹配构建器、动作双选、容量与宿主约束校验。

## 2. 设计

- 表单校验与 loader/计划双重校验同源；选择器数据来自效果注册表、图注册表、事件标签表。
- 宿主为 Instant 时入口拦截（与运行期抛错同语义，提前到编辑期）。
- 容量计数按宿主模板统计，与运行时缓冲上限同源。

## 3. 精确语义与不变量

- 表单合法 ⇔ 双重校验通过；通配语义（0=全听）在 UI 显式确认。
- 清单顺序即配置数组顺序，往返无损。

## 4. 依赖接口与验收

- 消费：效果注册表、图注册表、事件标签表、效果表加载校验。
- 验收：EssenceFlux 类三条监听可全程选择器化重建；Instant 宿主拦截有原因提示。

**相关文档**：[fx-07 UXD](../uxd/fx-08-phase-listeners.md) · [fx-07 runtime spec](../spec-runtime/fx-08-phase-listeners.md)
