# input-04 runtime spec · 施法提交档案

> 引擎实现任务书。第一性需求见 [input-04 PRD](../prd/input-04-cast-commit.md)；现状见 [reference](../reference/input-04-cast-commit.md)。

## 1. 概述
提交合同：三操作序列、帧顶动作拦截、五级偏好解析、作用域锁。

## 2. 设计
- 档案形状保持三字段封死（id/onActivate/frameActions），loader 拒收其余键——无状态机 schema 不放宽。
- 帧内动作保持：仅压帧在顶期间由注册表按动作 id 拦截执行。
- 偏好解析保持：锁 > perSlot > perFormSet > perTemplate > global；被锁作用域 `TrySetPreference` 返回失败（不静默忽略）。
- 值源保持两值（cursorWorld/framePointer）；新增值源走注册表扩展，不改档案格式。

## 3. 精确语义与不变量
- 操作序列执行为声明序，无隐式排序。
- 提交订单的参数槽在提交时刻取值（值源惰性求值）。
- 空栈弹帧即失败；保留默认帧不可弹，越界弹在栈处 fail-fast。

## 4. 迁移与治理
现状即基线（根资产两空表）；无新增设计项。

## 变更记录
- v1（2026-08-15）：初版。

**相关文档**：[input-04 PRD](../prd/input-04-cast-commit.md) · [reference](../reference/input-04-cast-commit.md)
