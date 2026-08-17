# fx-09 editor spec · 提案窗口与 Instant 内联

> 编辑器实现任务书。编辑器需求见 [fx-08 UXD](../uxd/fx-06-proposal-window.md)；引擎侧见 [runtime spec](../spec-runtime/fx-06-proposal-window.md)。

## 1. 概述

提案窗口检查器：窗口判定展示、纯图离线模拟、独占律冲突面板。

## 2. 设计

- 判定行与冲突面板消费执行计划编译产物，不自建规则副本。
- 纯图离线模拟复用图 VM（纯图无副作用，可安全试跑）；验证寄存器终值透出。
- 独占律冲突项与编译错误码一一映射，点击跳对应块。

## 3. 精确语义与不变量

- 模拟结果与运行期同配置执行一致（同一图 VM）。
- 冲突集合与编译期检查同源，无编辑器私设。

## 4. 依赖接口与验收

- 消费：执行计划四窗口编译结果、图 VM 试运行入口、错误码字典。
- 验收：拒绝用例可在编辑器内复现寄存器终值；独占冲突可视并可跳转。

**相关文档**：[fx-08 UXD](../uxd/fx-06-proposal-window.md) · [fx-08 runtime spec](../spec-runtime/fx-06-proposal-window.md)
