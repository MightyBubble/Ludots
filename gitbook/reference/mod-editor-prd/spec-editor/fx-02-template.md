# fx-05 editor spec · 效果模板骨架

> 编辑器实现任务书。编辑器需求见 [fx-04 UXD](../uxd/fx-02-template.md)；引擎侧见 [runtime spec](../spec-runtime/fx-02-template.md)。

## 1. 概述

效果模板表单：presetType 驱动的动态骨架、块级校验、热改边界标注。

## 2. 设计

- 表单模型按 17 组件块分片，合法块集由 preset 类型注册表投影驱动，切换原型即时重算。
- 校验与 loader 规则同源复用：不在编辑器重写组合规则。
- 热改判定与工作台热替换白名单同源；白名单外字段编辑标"重启生效"。

## 3. 精确语义与不变量

- 表单合法 ⇔ loader 接受；往返（表单→JSON→表单）无损。
- 灰色块集合只由 presetType 决定，与手写 JSON 等价。

## 4. 依赖接口与验收

- 消费：preset 类型注册表枚举、效果表加载校验入口、工作台热替换管线。
- 验收：新建 DoT 全程无查文档；非法组合输入即报；热字段改动下次施放生效。

**相关文档**：[fx-04 UXD](../uxd/fx-02-template.md) · [fx-04 runtime spec](../spec-runtime/fx-02-template.md)
