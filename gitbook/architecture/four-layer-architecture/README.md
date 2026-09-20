# 四层架构 Wiki：Machine · App · Seat · Device

Ludots 底层的正式分层模型——从一台机器到手柄按键，每一层是什么、归谁管、怎么协作。只讲已合入 main 的现状。

## 分层总览

- [Machine（机器）](machine.md) — 一台物理机或虚拟机；对应一个 AgentBridge discovery 目录
- [App（进程宿主）](app.md) — 一个游戏进程（Raylib / Web / DedicatedServer）；统一生命周期合同
- [Seat（席位）](seat.md) — App 内的玩家 I/O 槽位；占有、控制方案、交互栈、画面绑定
- [Device（设备）](device.md) — Seat 接入的输入设备；枚举、热插拔、绑定

## 全链路

- [一次输入的完整旅程](interaction-flow.md) — 从手柄 B 键到画面反馈，经过全部四层

## 治理

- [术语禁则与已知缺口](terminology.md) — 四条禁则 + AgentBridge 端口冲突已知缺口
