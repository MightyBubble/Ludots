# fx-17 editor spec · 效果授予 Tag

> 编辑器实现任务书。编辑器需求见 [fx-16 UXD](../uxd/fx-13-granted-tags.md)；引擎侧见 [runtime spec](../spec-runtime/fx-13-granted-tags.md)。

## 1. 概述

效果表单的授予 Tag 子表单：公式联动、层数试算、容量预警。

## 2. 设计

- **子表单**：tag 选择器 + 公式单选 + amount/base；公式切换控制 base 可见性，规则与 loader 的必填/禁写判定同源。
- **层数试算**：复用引擎 Compute 语义（同源包或逐行复刻并加一致性测试），预览层 1..5 贡献。
- **容量预警**：授予条数与计数上限引用事实页常量，不手抄。

## 3. 精确语义与不变量

- 表单校验集合与 loader 一一对应（缺 base、多余 base、超条数；GraphProgram 不入选项）。
- 试算输出与引擎 Compute 一致。

## 4. 依赖接口与验收

- 消费：tag 注册表枚举、效果模板保存管线。
- 验收：Linear amount=2 时层 2→3 试算从 4 变 6；LinearPlusBase 缺 base 保存被拒并指明字段。

**相关文档**：[fx-16 UXD](../uxd/fx-13-granted-tags.md) · [fx-16 runtime spec](../spec-runtime/fx-13-granted-tags.md)
