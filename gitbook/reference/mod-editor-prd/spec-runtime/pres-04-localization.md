# pres-04 runtime spec · 本地化

> 引擎实现任务书。第一性需求见 [pres-04 PRD](../prd/pres-04-localization.md)；现状见 [reference](../reference/pres-04-localization.md)。

## 1. 概述

token 目录与 locale 映射的加载、选择与消费合同；能力文案的启动校验。

## 2. 设计

- 加载合同保持：tokens ArrayById、locales DeepObject 合并；加载后产出目录与 locale 选择器。
- 校验合同保持：默认语言存在时对已注册能力做 token 校验（requireTokensOnAllPresentations 分级）。
- **治理项**：模板位次参数数与 argCount 的一致性目前靠作者自律——补加载期检查（数 `{n}` 位次，越界/缺位报错）。
- **治理项**：token 目录无"被谁引用"反向索引，编辑器漏译/孤儿分析只能全配置扫描——评估在目录侧暴露只读引用集。

## 3. 精确语义与不变量

- token id 全局命名；同一 token 多语言模板位次参数数必须一致。
- locale 选择在加载期完成，运行期切语言 = 重启（现状）。

## 4. 迁移与治理

现状即基线；位次一致性检查与引用索引入 TODO。

## 变更记录

- v1（2026-08-15）：初版。

**相关文档**：[pres-04 PRD](../prd/pres-04-localization.md) · [reference](../reference/pres-04-localization.md)
