# fx-13 editor spec · 参数化

> 编辑器实现任务书。编辑器需求见 [fx-13 UXD](../uxd/fx-14-config-params.md)；引擎侧见 [runtime spec](../spec-runtime/fx-14-config-params.md)。

## 1. 概述

效果表单的参数子表单：类型化值控件、保留键语义表、覆盖预览。

## 2. 设计

- **参数表**：键 + 类型下拉 + 类型化值控件；引用类型走对应注册表选择器，与 loader 的解析依赖同源。
- **保留键表**：编辑器内置 `_ep.` 键→消费域注释的映射表，随 EffectParamKeys 常量同步生成。
- **覆盖预览**：以样例 CallerParams 走与引擎 MergeFrom 同源的合并实现，展示同键覆盖与异键追加结果。

## 3. 精确语义与不变量

- 表单允许的类型集合、保留键类型约束与 loader 一致；往返保存无损。
- 预览合并结果与运行期合并一致（同源实现或一致性测试）。

## 4. 依赖接口与验收

- 消费：ConfigKeyRegistry、各引用注册表枚举、EffectParamKeys 常量、效果保存管线。
- 验收：EffectTemplate 引用选未注册名即拒；caller 同键覆盖预览显示新值与新类型。

**相关文档**：[fx-13 UXD](../uxd/fx-14-config-params.md) · [fx-13 runtime spec](../spec-runtime/fx-14-config-params.md)
