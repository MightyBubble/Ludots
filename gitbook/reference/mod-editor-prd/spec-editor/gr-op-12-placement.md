# gr-op-12 editor spec · 节点：放置校验

> 编辑器实现任务书。编辑器需求见 [gr-op-12 UXD](../uxd/gr-op-12-placement.md)；引擎侧见 [runtime spec](../spec-runtime/gr-op-12-placement.md)。

## 1. 概述

放置件目录、集合键选择器与地图叠层预览；副作用徽标为静态标注。

## 2. 设计

- **目录条目**：描述符表扫描四行；非线性图置灰。
- **集合键选择器**：ConfigKeyRegistry 投影；`validOutput` 命名给自动建议（opId+`Valid`）。
- **叠层预览**：消费地图渲染接口；拉回圈由 a/b 参数画、吸附候选由集合内容画、边投影由图边数据画——全部只读投影。
- **副作用标注**：Clamp/两 Snap 的"改落点"徽标为编辑器静态映射，校验图首次保存触发一次性说明。

## 3. 精确语义与不变量

- 预览画出的校正结果与执行期 TargetPos 修改同源同参。
- 键选择器候选与注册表投影一致。

## 4. 依赖接口与验收

- 消费：描述符表、ConfigKeyRegistry、地图渲染与图边数据接口。
- 验收：四件叠层随参数刷新；校验图副作用说明首次保存出现；`validOutput` 往返无损。

**相关文档**：[gr-op-12 UXD](../uxd/gr-op-12-placement.md) · [gr-op-12 runtime spec](../spec-runtime/gr-op-12-placement.md)
