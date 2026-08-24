# Seat（席位）

App 内的玩家 I/O 槽位。拥有设备、ControlScheme、交互栈、占有关系、画面绑定。

## 定义

ClientLocalSeat 是"本机 I/O 席位"——一台机器上谁在玩、用什么方案玩、占有谁、看到什么。它不是"玩家属于哪台客户端"（那是远端概念），而是本进程内的玩家入口。

## 四分职责（禁止混写）

| 概念 | 管什么 | 有无画面 |
|---|---|---|
| Participant | 世界身份（化身 entity） | 无关 |
| Possession | 谁在驾驶该身份 | 无关 |
| LogicView | 纯逻辑镜头权威 | 无关 |
| ClientLocalSeat | 本机 I/O（设备/方案/交互栈） | 有设备才有 |
| PresentBinding | Seat → LogicView 的呈现面 | 仅要画时 |

## 代码

| 类型 | 位置 | 职责 |
|---|---|---|
| `ClientLocalSeatRegistry` | `Core/Client/ClientLocalSeatRegistry.cs` | 座位表：注册/占有/绑定/查询 |
| `ClientLocalSeat` | 同上 | SeatId + ControlSchemeId + PossessedPlayerId + PossessedRep + PresentBinding |
| `ParticipantBindingResolver` | `Core/Gameplay/Teams/ParticipantBindingResolver.cs` | 进图时发布座位（含激活链） |
| `PresentBinding` | `Core/Client/PresentBinding.cs` | Seat→LogicView 的屏幕矩形 + 分辨率 |

## 座位配置三层 SSOT

| 层 | 字段 | 角色 |
|---|---|---|
| 冷启动默认 | `GameConfig.startupLocalSeats[]` | 默认怎么坐 |
| 本次进图 | `MapLaunchContext.LocalSeats[]` | 进图座位表 SSOT |
| 运行时 | `ClientLocalSeatRegistry` | 开局后可变 |

## 控制方案激活链（P2.5）

唯一 seat 声明的 `controlSchemeId` 在进图发布时激活：
- 声明的方案优先于偏好存储
- 方案未安装 / 被允许集拒绝 → fail-fast
- 多 seat 不激活任何声明（P3 范围）

## 与其他层的关系

- Seat 挂在 App 进程上
- Device 归 Seat 所有（`ClientLocalSeatDeviceBinding`）
- 换地图 / 换 App 不搬家——只改箭头
