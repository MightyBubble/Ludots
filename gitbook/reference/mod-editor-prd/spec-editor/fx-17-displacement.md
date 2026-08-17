# fx-21 editor spec · 位移

> 编辑器实现任务书。编辑器需求见 [fx-20 UXD](../uxd/fx-17-displacement.md)；引擎侧见 [runtime spec](../spec-runtime/fx-17-displacement.md)。

## 1. 概述

Displacement 效果表单的位移子表单：模式联动、派生速度、替换语义提示。

## 2. 设计

- **模式联动**：directionMode 决定 fixedDirectionDeg 的可见与落盘（非 Fixed 不落盘），规则与 loader 的 Require/Absent 同源。
- **派生速度**：编辑器按 距离/时长 计算展示值，只读。
- **语义提示**：静态说明替换合同（同目标新段覆写旧段）。

## 3. 精确语义与不变量

- 落盘字段集与所选模式合法集一致；往返保存无损。
- 正数校验区间与 loader 一致。

## 4. 依赖接口与验收

- 消费：效果模板保存管线、热通道分级。
- 验收：ToTarget 切 Fixed 后角度必填红条；距离改 0 保存被拒。

**相关文档**：[fx-20 UXD](../uxd/fx-17-displacement.md) · [fx-20 runtime spec](../spec-runtime/fx-17-displacement.md)
