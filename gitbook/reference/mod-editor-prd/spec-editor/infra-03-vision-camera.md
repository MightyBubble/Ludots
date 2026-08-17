# infra-03 editor spec · 视野与相机

> 编辑器实现任务书。编辑器需求见 [infra-03 UXD](../uxd/infra-03-vision-camera.md)；引擎侧见 [runtime spec](../spec-runtime/infra-03-vision-camera.md)。

## 1. 概述

相机实验室实现：预设表单 + 实时视口联动 + 迷雾层表单。

## 2. 设计

- **表单模型**：预设字段级投影，区分继承（依赖）/本 mod 覆盖两层；保存只写覆盖层。
- **视口联动**：参数变更即时驱动视口相机（同 rig 求值逻辑的编辑器侧副本或引擎只读实例）。
- **交互试操作**：视口按当前预设的交互参数处理作者输入（平移/缩放/跟随同感）。
- **守卫**：枚举封闭、边界成对校验、迷雾正数——与引擎校验同源。

## 3. 精确语义与不变量

- 视口渲染位姿与引擎按该预设求得的位姿一致（同参数同结果）。
- 覆盖层产物 = 本 mod 只写改动字段的 ArrayById 条目。

## 4. 依赖接口与验收

- 消费：相机/迷雾注册表投影、rigKind 与交互枚举、渲染视口。
- 验收：调参产物通过启动校验；视口试操作手感与运行期一致。

**相关文档**：[infra-03 UXD](../uxd/infra-03-vision-camera.md) · [infra-03 runtime spec](../spec-runtime/infra-03-vision-camera.md)
