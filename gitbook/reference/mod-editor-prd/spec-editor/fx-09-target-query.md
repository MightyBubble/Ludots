# fx-08 editor spec · 目标查询

> 编辑器实现任务书。编辑器需求见 [fx-08 UXD](../uxd/fx-09-target-query.md)；引擎侧见 [runtime spec](../spec-runtime/fx-09-target-query.md)。

## 1. 概述

查询编辑区：形状表单、范围预览双向绑定、动态查询切换。

## 2. 设计

- 字段集切换按互斥矩阵驱动（与 loader 同源规则表）。
- 预览用俯视画布渲染形状与候选着色（敌对着色数据只读来自 fx-09 规则）。
- 查询块表单不出现任何过滤字段（与 E2 治理方向一致）。

## 3. 精确语义与不变量

- 表单值 ⇔ targetQuery 块往返无损；单位换算与地图 cellSize 同源。
- 预览手柄写回值经同一矩阵校验。

## 4. 依赖接口与验收

- 消费：效果表加载校验、图注册表（查询类）、地图 cellSize 常量。
- 验收：五形状拖拽与字段双向同步；违例值无法保存；GraphProgram 切换折叠空间字段。

**相关文档**：[fx-08 UXD](../uxd/fx-09-target-query.md) · [fx-08 runtime spec](../spec-runtime/fx-09-target-query.md)
