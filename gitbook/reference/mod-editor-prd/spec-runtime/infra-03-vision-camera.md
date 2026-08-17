# infra-03 runtime spec · 视野与相机

> 引擎实现任务书。第一性需求见 [infra-03 PRD](../prd/infra-03-vision-camera.md)；现状见 [reference](../reference/infra-03-vision-camera.md)。

## 1. 概述

迷雾分层与虚拟相机预设的加载、校验与运行消费合同。

## 2. 设计

- 加载合同保持：两表 ArrayById；迷雾层正数校验；相机 rigKind/交互枚举封闭、边界与 edgePanMarginPx 校验。
- 运行合同保持：相机运行系统按预设求位姿并路由输入；迷雾层按 updateHz 周期重算。
- **治理项**：叙事运行时按 cameraId 激活预设（dialogues/cinematics 引用），相机 id 的交叉域引用无启动期对账——评估在 Narrative 加载后对 cameraId 做一次解析校验（未注册即抛）。

## 3. 精确语义与不变量

- 预设字段跨 mod 深合并只赢写到的字段；min ≤ max 恒成立。
- 迷雾层 id 在效果侧 revealArea 引用（fx-22 scope/layers）。
- 运行期预设切换不改表；表变更重启生效。

## 4. 迁移与治理

现状即基线；叙事相机 id 对账入 TODO。

## 变更记录

- v1（2026-08-15）：初版。

**相关文档**：[infra-03 PRD](../prd/infra-03-vision-camera.md) · [reference](../reference/infra-03-vision-camera.md)
