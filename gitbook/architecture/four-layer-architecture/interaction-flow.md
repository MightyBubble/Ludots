# 一次输入的完整旅程

从手柄 B 键按下到画面反馈，经过全部四层。

## 全链路图

```text
[Machine]  一台机器（AgentBridge 可 Mock 整层）
    │
[App]      进程宿主（Raylib / Web / DedicatedServer）
    │       AppHostLifecycle: Created→…→Running
    │
[Seat]     玩家席位
    │       ControlScheme 定位当前设备布局
    │       InteractionContextStack 管理输入上下文
    │
[Device]   手柄 B 键
    │       Input Action Mapping: 设备参数→语义 Action:Jump
    │       （灵敏度/按压时长在 Device→Action 层消亡）
    │
    ▼
CommandIntentProfile + Arbiter + CommandPref
    │       依当前状态选择意图
    │       （蜘蛛侠同键不同技；按/抬/蓄/超时 = 不同意图）
    │       玩家下单偏好挂 representative 的 CommandPref（换 scheme 不动偏好）
    ▼
ControlPlaneView
    │       从唯一 Representative 出发
    │       经查询图得到可控实体集合
    ▼
OrderFanout
    │       一次意图 → N 个实体的正式 order
    │       （框选群攻 / 战神父命令子）
    ▼
LogicView → CameraPresenter → PresentBinding
    │       纯逻辑视角（确定性回放友好）
    │       → 相机解释器 → 座位画面绑定
    ▼
AggregationPanel
            面板反馈命令状态
            （潘森长矛：1 键 · 4 意图 · 1 面板）
```

## 各层职责一句话

| 层 | 管什么 | 不管什么 |
|---|---|---|
| Machine | 进程发现、Mock 隔离 | 不知道游戏是什么 |
| App | 进程生命周期、宿主服务 | 不知道玩家是谁 |
| Seat | 玩家 I/O、占有、画面绑定 | 不知道世界内容 |
| Device | 设备枚举、热插拔、参数→语义 | 不知道座位里有谁 |
