# Device（设备）

Seat 接入的输入设备：键鼠、手柄、触控。设备参数在映射层被解释为语义后上传。

## 定义

Device 是 Seat 域内的一等概念。每个设备有稳定标识、类型和归属座位。Adapter 负责枚举和热插拔检测，Seat 拥有设备绑定表。

## 代码

| 类型 | 位置 | 职责 |
|---|---|---|
| `InputDeviceDescriptor` | `Platform.Abstractions/Input/InputDeviceDescriptor.cs` | DeviceId + Kind + DisplayName + SeatSlot |
| `IInputDeviceWatcher` | 同上 | Adapter 侧合同：枚举 + DeviceChanged 事件 |
| `ClientLocalSeatDeviceBinding` | `Core/Client/ClientLocalSeatDeviceBinding.cs` | Seat 域绑定：谁归哪个座位 |
| `RaylibInputDeviceWatcher` | `Adapters/Raylib/RaylibInputDeviceWatcher.cs` | 键鼠常在 + 每帧 diff 手柄 |
| `SyntheticInputDevice.WatchAsDeviceWatcher()` | `Core/Input/Runtime/SyntheticInputDevice.cs` | AgentBridge Mock 设备也可枚举 |

## 热插拔路由

- 手柄插入时，恰好只有一个 seat → 自动归座
- 多 seat 时不自动绑定，须显式 `BindDevice`
- 设备断开时从绑定表移除

## 设备参数在映射层消亡

设备原始参数（灵敏度、按压时长、摇杆死区）在 `InputConfig` → `PlayerInputHandler` 的 Input Action Mapping 层被解释为语义（如 `Action:Jump`）。往上传的是语义，不是参数。

## 禁则验证

设备 Connect/Disconnect 全程不触碰 seat 的 `PossessedPlayerId` / `PossessedRep` / `PresentBinding`——设备事件只停留在设备-座位关系内（测试 `DeviceChanges_NeverTouchSeatPossession` 守卫）。

## 与其他层的关系

- Device 归 Seat 所有（术语治理定案）
- Adapter 枚举设备，Seat 持有绑定
- AgentBridge 可通过 `SyntheticInputDevice` Mock 任意设备
