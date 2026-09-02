# 术语禁则与已知缺口

## "client" 一词的合法用法

仓库内 "client" 只保留两个精确义：

1. **client-local 前缀** = 本机（如 `ClientLocalSeatRegistry` 中的 Client）
2. **ReplicatedClient** = 网络客户端角色（#711 联机线已合入 main，是现状）

**禁止第四义**：client 不指机器。说机器就用 Machine/机器。

## 四条禁则

| # | 禁则 | 现状 |
|---|---|---|
| ① | client 不指机器（文档与代码标识符一律） | 守卫测试已合入 |
| ② | 设备实例只能由 Seat 持有；Adapter 不把设备句柄放 App 级服务容器 | `SyntheticInput` 单例为已知违反点，P3 收敛 |
| ③ | 没有跨宿主真实需求前，不新增 Machine/Client/Device/UISurface 空壳类型 | 守卫测试已合入 |
| ④ | 本机 I/O 概念（seatId/controlSchemeId/设备标识）不进存档与网络载荷 | `localSeats.controlSchemeId` 已裁决保留现状（未进生产阶段） |

## 命名禁则

禁止在代码标识符中 `Client` 与 `Machine` 并置（如 `MachineClient*` / `ClientMachine*`）——架构测试守卫已合入。

## 已知缺口（记录在案不修）

AgentBridge 单 discovery 目录 + 47921 起 16 端口探测。并行跑两组三进程验收会抢端口——"Mock 两台机器"目前不可达，属机器维度的寻址缺口。远端/CI 并行化时代处理。
