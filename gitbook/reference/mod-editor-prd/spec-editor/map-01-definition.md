# map-01 editor spec · 地图定义

> 编辑器实现任务书。编辑器需求见 [map-01 UXD](../uxd/map-01-definition.md)；引擎侧见 [runtime spec](../spec-runtime/map-01-definition.md)。

## 1. 概述

地图编辑器实现：棋盘渲染、布阵编辑、片段视图。画布是合并结果的投影，编辑只写本 mod 片段。

## 2. 设计

- **画布渲染**：消费合并后地图（棋盘几何 + 实体位置 + 队伍色）；片段来源条切换投影范围。
- **布阵写回**：拖放/属性编辑生成 Entities 条目写入本 mod 片段；InstanceId 由编辑器保证唯一。
- **覆盖表单**：schema 来自模板组件清单（ent-01 投影）。
- **试玩**：调启动链路（cfg-03 editor spec），运行配置自动带本地图。

## 3. 精确语义与不变量

- 画布显示与引擎加载结果同源（同一合并器输出），不实现第二套地图合并。
- 写回片段必须被地图管线原样接受。

## 4. 依赖接口与验收

- 消费：地图合并器、实体模板注册表、启动链路。
- 验收：摆单位→保存→试玩开局可见；合并视图布阵与引擎加载一致。

**相关文档**：[map-01 UXD](../uxd/map-01-definition.md) · [map-01 runtime spec](../spec-runtime/map-01-definition.md)
