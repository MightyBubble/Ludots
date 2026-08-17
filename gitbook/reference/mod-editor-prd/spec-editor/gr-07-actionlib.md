# gr-09 editor spec · 动作库 ActionLib

> 编辑器实现任务书。编辑器需求见 [gr-09 UXD](../uxd/gr-07-actionlib.md)；引擎侧见 [runtime spec](../spec-runtime/gr-07-actionlib.md)。

## 1. 概述

动作库面板实现：宿主分组视图、入库向导、政策预检。

## 2. 设计

- 政策预检复用引擎挂起可达性校验器（与 FuncLib 纯度校验同源），入库前给出路径级结果。
- 撞名判定与双目录命名空间规则同源；挂点跳转消费 gr-09 挂接点表投影。

## 3. 精确语义与不变量

- 编辑器政策判定与装载校验结论一致。
- 入库产物与手写 action_lib 条目等价（同 schema）。

## 4. 依赖接口与验收

- 消费：动作目录、宿主政策表、挂起可达校验器、函数目录（撞名）、挂接点表。
- 验收：政策违规/撞名/悬空三例编辑器全拦；四宿主各入一例装载零诊断。

**相关文档**：[gr-09 UXD](../uxd/gr-07-actionlib.md) · [gr-09 runtime spec](../spec-runtime/gr-07-actionlib.md)
