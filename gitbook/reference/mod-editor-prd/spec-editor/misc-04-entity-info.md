# misc-04 editor spec · 实体信息档案

> 编辑器实现任务书。编辑器需求见 [misc-04 UXD](../uxd/misc-04-entity-info.md)；引擎侧见 [runtime spec](../spec-runtime/misc-04-entity-info.md)。

## 1. 概述

信息面板设计器实现：模板归属锁、面板预览、槽位检查器三类联动。

## 2. 设计

- **归属模型**：模板→档案映射表（单射）；已归属模板在清单层锁定。
- **预览**：档案数据 + 样例实体属性快照驱动的编辑器侧渲染；token 经 pres-04 目录取当前语言模板。
- **检查器**：槽位参数化表单；token/属性/能力引用全下拉封闭，数据源为对应注册表投影。
- **能力 mod 检测**：工程依赖检查 EntityInfoPanelsMod，缺失时整页提示（D5 语义）。

## 3. 精确语义与不变量

- 预览取值逻辑与引擎面板渲染一致（同 token/属性读取语义）。
- 归属锁与引擎互斥校验同源。

## 4. 依赖接口与验收

- 消费：insight 档案目录、实体模板注册表、文本目录（pres-04）、AttributeRegistry、能力注册表、工程依赖清单。
- 验收：新建档案产物通过能力 mod 加载校验；预览数值与运行期面板一致。

**相关文档**：[misc-04 UXD](../uxd/misc-04-entity-info.md) · [misc-04 runtime spec](../spec-runtime/misc-04-entity-info.md)
