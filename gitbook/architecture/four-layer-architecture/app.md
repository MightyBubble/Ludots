# App（进程宿主）

一个游戏进程。Raylib / Web / DedicatedServer / 未来 UE 都是 App。部署与 Mock 的最小单元。

## 定义

App 是一个实现了 `IAppHost` 接口的进程宿主，拥有统一的生命周期状态机。

## 生命周期

```text
Created → Configuring → Initialized → Running ⇄ Suspending → ShuttingDown → Terminated
```

- **逐级前进**：不允许跳级（Created 直接到 Running 会被拒绝）
- **暂停恢复**：Running 和 Suspending 可互转
- **关停入口**：Running 或 Suspending 状态可直接进入 ShuttingDown

## 代码

| 类型 | 位置 | 职责 |
|---|---|---|
| `IAppHost` | `Platform.Abstractions/Hosting/IAppHost.cs` | 宿主合同：Initialize / Run / RequestShutdown |
| `AppHostLifecycle` | 同上 | 共享相位状态机（各 Adapter 不重复手写） |
| `AppDescriptor` | 同上 | AppId + HostKind + AdapterId + 自由属性 |
| `AppHostRegistry` | `Core/Hosting/AppHostRegistry.cs` | 单引擎进程注册表，双注册拒绝 |
| `RaylibAppHost` | `Adapters/Raylib/RaylibAppHost.cs` | Raylib 实现（包装 HostComposer + HostLoop） |

## Adapter 分工

| 组件 | 职责 |
|---|---|
| Adapter（如 `Ludots.Adapter.Raylib`） | 进程内宿主能力库：渲染、输入、音频 |
| HostLoop（如 `RaylibHostLoop`） | App 的帧循环 |
| AppHost | 生命周期合同的外壳——包装 HostLoop，不替代它 |

## 与其他层的关系

- 一个 Machine 上可跑多个 App
- 一个 App 内可有多个 Seat（当前生产路径 sole-seat，多座是 P3）
- App 由 launcher（app/preset）启动和编排
