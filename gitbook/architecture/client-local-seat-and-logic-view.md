# ClientLocalSeat · Possession · LogicView · PresentBinding

Epic: [#896](https://github.com/MightyBubble/Ludots/issues/896)

本页是本机座位与逻辑视觉的正式合同。取代全局 `LocalPlayerId` / `LocalPlayerEntity` 与「唯一 `GameSession.Camera` = 唯一本地视觉」的旧模型。禁止兼容桥、禁止镜像旧键。

## 1. 概述

四分职责，禁止混写：

| 概念 | 含义 | 有无画面 |
|---|---|---|
| **Participant** | 世界身份（化身 entity）；AI bot 也是 | 无关 |
| **Possession** | 谁在驾驶该身份（Seat / AI Brain / 空）；可转移 | 无关 |
| **LogicView** | Participant 持有的纯逻辑视觉（逻辑相机 + 逻辑投影参数） | **无关** |
| **ClientLocalSeat** | 本机 I/O：设备、ControlScheme、InteractionContextStack | 有设备才有 |
| **PresentBinding** | Seat → 某个 LogicView 的呈现绑定（rect / 呈现分辨率） | 仅要画时 |

`EntityCollectionStore` 地址仍是 `(participantRep, collectionKey)`。  
Control Plane = 化身当前可控对象（拓扑现算）。  
Cast 是几何原语；写集合是业务/蓝图另线。

## 2. 结构

```text
ClientLocalSeatRegistry（本机）
  seats[]
    seatId
    devices / controlSchemeId
    interactionContextStack
    possession → playerId + repEntity | none
    presentBinding? → logicViewId + screenRect + presentMetrics

LogicViewRegistry（模拟）
  views[]
    logicViewId
    ownerParticipantRep
    CameraManager / VirtualCameraBrain
    logicalProjectionMetrics（数据声明，不读窗口）

Possession 转移只改箭头；Participant、LogicView、collection 不搬家。
```

## 3. 详情

### 3.1 启动

`MapLaunchContext.LocalSeats`：

- `seatId`（非空、同次启动唯一）
- `playerId`（必须已在 map Players 绑定）
- 可选 `controlSchemeId`

单人 = 一个 seat。禁止再提供全局 `LocalPlayerId` 槽。  
`GameConfig.StartupLocalSeats`（或等价）注入启动 context；非法 seat / 未绑定 player → map load fail-fast。

### 3.2 消费规则

- 输入 / Cast owner / 下令 / Follow 锚：**显式 seat 或 seat 的 possessed rep**
- 禁止隐式「本机唯一玩家」全局服务键
- 恰有一个 seat 时，可用 `RequireSolePossessedRep()`（座位数量 ≠ 1 则失败）——这是基数断言，不是 Active 槽

### 3.3 LogicView（纯逻辑视觉）

- 挂在 Participant（或其显式 view 资产）上，**不**挂在 Seat
- AI 无 Seat 也可有 LogicView
- 逻辑宽高比 / 投影参数由数据声明，不从窗口反推为真相
- 与 Knowledge/Fog viewer **分离**：雾仍按 viewer 实体

### 3.4 PresentBinding（呈现）

- 仅 ClientLocalSeat 可选字段
- Presentation：对每个 binding，读 LogicView 权威态 → 插值 → adapter 画到 rect
- 拾取：有 binding 时用呈现度量 + 该 LogicView；无 binding 的逻辑 Cast 只用 LogicView 逻辑度量
- Adapter 仍不拥有镜头权威（沿用 camera/presentation 纪律）

### 3.5 删除清单

- `CoreServiceKeys.LocalPlayerId` / `LocalPlayerEntity`
- `TryKeepExplicitLocalPlayerBinding` 及一切手写旁路
- 扫描 `PlayerOwner` 猜本地
- `Player.Camera` 与 session 单例相机双真相（收敛到 LogicView）
- 「有画面才有 viewport」
- RFC-0065「不做同进程多 viewport」非目标（改为本页合同）

## 4. 场景

- 单机单人：1 Seat possess 甲，PresentBinding 全屏 → 甲的 LogicView  
- 同屏双人：2 Seat 同时输入，各 possess、各 PresentBinding  
- AI bot：有 LogicView，无 Seat  
- 换 client：改 Possession / PresentBinding；甲的集合与 LogicView 不动  
- 旁观：Seat 可 PresentBinding 盯乙的 LogicView，而不 possess 乙  

## 5. 边界

- Seat ≠ 「玩家属于 client」  
- 不把 InteractionContextStack 的 ownerToken 当成座位表  
- 本页不定义 Cast→Query→WriteCollection 业务图（见交互/蓝图合同）  
- 分屏布局是 PresentBinding.rect 配置，不另起视觉子系统  

## 6. UAT

```gherkin
Feature: 本机座位与逻辑视觉
  Scenario: AI 无画面也有逻辑视觉
    Given bot participant 拥有 LogicView
    When 脚本推动其逻辑相机
    Then 可用该 LogicView 做逻辑域查询
    And 无需 ClientLocalSeat

  Scenario: 换机不搬家
    Given 甲的 LogicView 与指挥集已存在
    When Possession 从 ClientA 转到 ClientB
    Then 甲的 LogicView 与 collection 仍挂在甲上

  Scenario: 双 Seat 同时操作
    Given 两 Seat 各绑一个 participant 与设备
    When 两路同时下令
    Then 各自写到自己化身的指挥集且互不覆盖

  Scenario: 无全局 LocalPlayer 槽
    Given 本机已按 LocalSeats 启动
    Then 不存在 LocalPlayerEntity 服务槽
    And 输入锚点来自 Seat 的 Possession
```

## 7. 分期

- P0 合同（本页 + participant 合同修订 + RFC 修订）— #897  
- P1 SeatRegistry + Possession + 删除全局 LocalPlayer* — #898  
- P2 LogicView 多实例 + PresentBinding 呈现/拾取 — #899（Sole PresentBinding → Presenter / ScreenRay / ScreenProjector 已接线；多座分屏 rect / UI per-seat 仍属 P3）  

- P3 分屏布局与 UI per-seat owner（同模型，另开子单）
