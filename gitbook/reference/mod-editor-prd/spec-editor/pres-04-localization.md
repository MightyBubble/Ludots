# pres-04 editor spec · 本地化

> 编辑器实现任务书。编辑器需求见 [pres-04 UXD](../uxd/pres-04-localization.md)；引擎侧见 [runtime spec](../spec-runtime/pres-04-localization.md)。

## 1. 概述

文案工作台实现：token×语言矩阵、漏译分析、预览渲染。

## 2. 设计

- **矩阵模型**：tokens 与 locales 的联结视图；单元格值区分继承（依赖 mod）/本 mod 两层，保存只写本 mod 层。
- **漏译分析**：扫描矩阵生成缺失/参数不符清单；与引擎加载校验同一判定。
- **预览**：编辑器侧模板格式化（位次参数 + 样例实参），不依赖运行时。
- **写回**：token 行写 `text_tokens.json`（追加数组项），语言值写 `text_locales.json` 的对应 locale 键（DeepObject 局部）。

## 3. 精确语义与不变量

- 矩阵视图 = 引擎两表合并后的投影（同源合并器）。
- 参数一致性判定与引擎 argCount 语义同源。

## 4. 依赖接口与验收

- 消费：token 目录投影、locale 键集合、能力表现的 token 引用清单。
- 验收：补语言产物通过引擎加载与能力文案校验；漏译清单与实际缺失一一对应。

**相关文档**：[pres-04 UXD](../uxd/pres-04-localization.md) · [pres-04 runtime spec](../spec-runtime/pres-04-localization.md)
