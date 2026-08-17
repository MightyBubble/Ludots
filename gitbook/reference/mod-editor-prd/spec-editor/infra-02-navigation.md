# infra-02 editor spec · 导航配置

> 编辑器实现任务书。编辑器需求见 [infra-02 UXD](../uxd/infra-02-navigation.md)；引擎侧见 [runtime spec](../spec-runtime/infra-02-navigation.md)。

## 1. 概述

导航设置页实现：三表表单、面积代价联动、地图内试烘视图。

## 2. 设计

- **表单模型**：agent_profiles（数组编辑）、pathing（根对象下 agentTypes 列表）、navmesh（DeepObject 局部覆盖）三类写回。
- **联动**：profile/area 下拉数据源为对应注册表投影；引用失效即时标红。
- **试烘**：调用引擎烘焙器（离线参数 + 当前地图数据），结果渲染为地图覆盖层；覆盖率统计为编辑器侧派生。
- **守卫**：空档案表、空 agentTypes 在保存层拦截（与引擎校验同源）。

## 3. 精确语义与不变量

- 表单写回产物与手写 JSON 等价（agentTypes 恒在 pathing 根对象内）。
- 覆盖层渲染所用烘焙参数与保存参数一致。

## 4. 依赖接口与验收

- 消费：档案/类型注册表投影、navmesh areas/layers、烘焙器入口、地图数据。
- 验收：新体型 + 类型 + 烘焙组的产物通过启动校验；试烘覆盖与重启后实际可通行区域一致。

**相关文档**：[infra-02 UXD](../uxd/infra-02-navigation.md) · [infra-02 runtime spec](../spec-runtime/infra-02-navigation.md)
