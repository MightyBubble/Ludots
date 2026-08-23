# attr-05 editor spec · 属性绑定与 Sink

> 编辑器实现任务书。编辑器需求见 [attr-05 UXD](../uxd/attr-05-bindings.md)；引擎侧见 [runtime spec](../spec-runtime/attr-05-bindings.md)。

## 1. 概述

绑定面板实现：全字段表单、sink 通道枚举、通道占用全景。

## 2. 设计

- **投影**：绑定表条目 ↔ 七字段表单；序列化全字段显式输出，不产缺省。
- **数据源**：sink 下拉取启动冻结的注册表投影；通道合法域随 sink 联动；属性选择器与 attr-01 面板同源。
- **同源不变量**：通道域与 sink 复核同判；折叠分组视图与运行时 (sink, 声明序) 一致。
- **一致性拦截**：同 sink 同 channel 混合 resetPolicy 在保存前警示（A12 编辑器侧）。

## 3. 精确语义与不变量

- 零绑定 sink 在全景灰显并标注（死配置候选，A9）；绑定增删改属表结构变更，标注重启生效。

## 4. 依赖接口与验收

- 消费：sink 注册表枚举（含通道域）、属性注册表枚举、绑定表模型。
- 验收：非法通道/未知 sink 在表单层不可达；保存产物通过加载器校验；全景与折叠分组一致。

**相关文档**：[attr-05 UXD](../uxd/attr-05-bindings.md) · [attr-05 runtime spec](../spec-runtime/attr-05-bindings.md)
