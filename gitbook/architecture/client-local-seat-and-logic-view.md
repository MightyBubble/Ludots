# ClientLocalSeat · Possession · LogicView · PresentBinding

总看板：[#902](https://github.com/MightyBubble/Ludots/issues/902) · 原里程碑：[#896](https://github.com/MightyBubble/Ludots/issues/896)

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

### 3.1 配置分层（SSOT）

| 层 | 字段 | 角色 |
|---|---|---|
| 地图身份 | `MapConfig.Entities` + `Players` / `Teams` | 世界上有哪些 Participant（先实体，再绑定身份） |
| 冷启动默认 | `GameConfig.startupLocalSeats[]` | 默认怎么坐；只注入 launch，不是运行时真相 |
| 本次进图 | `MapLaunchContext.LocalSeats[]` | **进图座位表 SSOT**（来自 game 默认 / 命令 / 大厅 / 存档） |
| 运行时 | `ClientLocalSeatRegistry` | 开局后可变的占有 / PresentBinding |

禁止：

- `GameConfig.startupLocalPlayerId`（已删除；用 `startupLocalSeats`）
- 把座位表写进 `MapConfig`（地图身份）
- 全局 `LocalPlayerId` / `LocalPlayerEntity` 服务槽

`startupLocalSeats` / `LocalSeats` 每项：

- `seatId`（非空、同次唯一）
- `playerId`（必须已在 map Players 绑定）
- 可选 `controlSchemeId`：进图激活真相（见 3.3 控制方案激活链）；存档 `launchContext.localSeats[]` 原样 round-trip，未声明时省略字段

### 3.2 启动与进图链路

```text
1. 引擎注册空 ClientLocalSeatRegistry / LogicViewRegistry
2. Host Start → GameStart
3. 组装 MapLaunchContext.LocalSeats
   - LoadStartupMap：GameConfig.CreateStartupLaunchContext()
   - LoadMapCommand：命令携带 LocalSeats
   - 读档：launchContext.localSeats[]
   - 大厅/其它：显式 MapLoadRequest.LaunchContext
4. 加载地图内容（板面等）
5. MapConfig.Entities → 刷实体（先）
6. MapConfig.Players/Teams → 绑定 Participant（后）
7. PublishLocalSeats：座位占有 →（仅对有占有的 seat）EnsureDefaultView → 可选 PresentBinding → sole seat 声明的 controlSchemeId 激活（见 3.3）
8. MapLoaded；快照 session.LocalSeats
```

非法 seat / 未绑定 player → map load fail-fast。  
**不是每个 Participant 都有 LogicView**：今天只对「本机座位占有」自动建；AI/其它玩家代表可只有身份。

### 3.3 消费规则

- 输入 / Cast owner / 下令 / Follow 锚：**显式 seat 或 seat 的 possessed rep**
- 禁止隐式「本机唯一玩家」全局服务键
- 恰有一个 seat 时，可用 `RequireSolePossessedRep()`（座位数量 ≠ 1 则失败）——这是基数断言，不是 Active 槽

**控制方案激活链（P2.5 收口）**：

- 唯一 seat 声明的 `controlSchemeId` 是本次进图的激活真相：`PublishLocalSeats` 末尾通过全局 `ControlSchemeRuntime.TrySwitch` 激活，优先于偏好存储的旧选择
- seat 未声明 `controlSchemeId`：维持既有激活链（偏好存储 → 首个 allowed），`TrySwitch` 热切换照常写偏好存储
- 声明了但 scheme 未安装、或被 mod allowed-set 拒绝：map load **fail-fast**，不静默回退到初始 scheme
- 多 seat 的 per-seat scheme 路由是 P3：多座位发布时不激活任何声明（今日多座位本就止步于 present 管线之前）

### 3.4 LogicView（纯逻辑视觉）

- 挂在 Participant（或其显式 view 资产）上，**不**挂在 Seat
- 可选能力：无 Seat 的 Participant 也可以持有 LogicView（需显式创建；非启动默认）
- 管镜头权威：姿态 / Follow / VCam / 逻辑投影参数
- 逻辑宽高比 / 投影参数由数据声明，不从窗口反推为真相
- **LogicView ≠ viewport**：有 LogicView 不等于启用呈现面，也不自动触发画面剔除

### 3.5 PresentBinding（呈现面 / viewport）

- 仅 ClientLocalSeat 可选字段；**启用呈现面才有**
- Presentation：对每个 binding，读 LogicView 权威态 → 插值 → adapter 画到 rect
- 拾取：有 binding 时用呈现度量 + 该 LogicView；无 binding 的逻辑 Cast 只用 LogicView 逻辑度量
- 画面剔除 / 视觉 LOD / 跳过绘制：仅对 PresentBinding 计算；姿态取自绑定 LogicView，矩形与分辨率取自 PresentBinding
- 禁止：仅因存在 LogicView 就跑呈现剔除
- Adapter 仍不拥有镜头权威（沿用 camera/presentation 纪律）

### 3.6 删除清单

- `CoreServiceKeys.LocalPlayerId` / `LocalPlayerEntity`
- `GameSession.LocalPlayerId` / `SelectLocalPlayer` / 存档 GameSession 域 `localPlayerId`
- `GameSession.Camera` 会话单例与存档 GameSession 域根 `camera`（权威改挂 LogicView；无座引导用 `logicview.client.present`）
- `GameConfig.StartupLocalPlayerId` / `startupLocalPlayerId`
- `LoadMapCommand.LocalPlayerId`（改为 `LocalSeats`）
- 存档 `launchContext.localPlayerId`（改为 `launchContext.localSeats[]`）
- 命令 contextKey `"LocalPlayerEntity"` 兼容别名（仅 `solePossessedRep` / 座位表键）
- `InputActionAttributeTargetKind.LocalPlayerEntity`（改为 `SolePossessedRep`）
- `TryKeepExplicitLocalPlayerBinding` 及一切手写旁路
- 扫描 `PlayerOwner` 猜本地
- `Player.Camera` 与 LogicView 相机双真相（后续收敛；今日权威已是 LogicView）
- 「有画面才有 viewport」
- RFC-0065「不做同进程多 viewport」非目标（改为本页合同）

## 4. 场景

- 单机单人：`startupLocalSeats: [{ seat.0, playerId: 1 }]` → 占有甲 → PresentBinding 全屏  
- 同屏双人：两条 seat 写入 launch / game 默认  
- AI bot：地图 Players 有代表，无 seat → 无自动 LogicView  
- 换 client：改 Possession / PresentBinding；甲的集合与 LogicView 不动  
- 旁观：Seat 可 PresentBinding 盯乙的 LogicView，而不 possess 乙  

## 5. 边界

- Seat ≠ 「玩家属于 client」  
- `GameConfig.startupLocalSeats` 不是运行时座位真相；进图后以 `ClientLocalSeatRegistry` 为准  
- 不把 InteractionContextStack 的 ownerToken 当成座位表  
- 本页不定义 Cast→Query→WriteCollection 业务图（见交互/蓝图合同）  
- 分屏布局是 PresentBinding.rect 配置，不另起视觉子系统  
- 画面剔除挂 PresentBinding，不挂裸 LogicView；LogicView 只提供镜头权威  

## 6. UAT

```gherkin
Feature: 本机座位与逻辑视觉
  Scenario: AI 无画面也有逻辑视觉
    Given bot participant 拥有 LogicView
    When 脚本推动其逻辑相机
    Then 可用该 LogicView 做逻辑域查询
    And 无需 ClientLocalSeat
    And 不因其存在而计算呈现剔除

  Scenario: 有呈现绑定才做画面剔除
    Given seat.0 的 PresentBinding 绑定甲的 LogicView 且全屏
    When 呈现帧更新
    Then 画面剔除使用该 PresentBinding 的矩形与呈现分辨率
    And 镜头姿态取自甲的 LogicView

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

  Scenario: 冷启动座位来自游戏全局配置
    Given game.json 声明 startupLocalSeats 含 seat.0 → playerId 1
    When 引擎 LoadStartupMap
    Then MapLaunchContext.LocalSeats 与该配方一致
    And 运行时 ClientLocalSeatRegistry 发布对应占有

  Scenario: 会话不再持有全局相机
    Given 引擎已启动
    When 任何系统需要镜头权威
    Then 只从 LogicView（或 seat 的 PresentBinding 指向的 LogicView）读取
    And 不存在 GameSession.Camera

  Scenario: 双人分屏矩形可声明
    Given 两 Seat 各有 PresentBinding
    When 用水平对半分屏布局写入各自 rect
    Then 两个 PresentBinding 的矩形不重叠且并集覆盖全屏
    And 各自镜头权威仍来自绑定的 LogicView

  Scenario: 座位声明的控制方案在发布时激活
    Given 唯一 seat 的 LocalSeats 项声明 controlSchemeId 且该 scheme 已安装并被 allowed-set 允许
    When PublishLocalSeats 发布座位
    Then ControlSchemeRuntime 激活该 scheme 并覆盖偏好存储的旧选择
    And 未声明 controlSchemeId 的 seat 维持偏好存储 → 首个 allowed 的既有激活链

  Scenario: 声明非法控制方案时进图失败
    Given 唯一 seat 声明的 controlSchemeId 未安装或被 allowed-set 拒绝
    When PublishLocalSeats 发布座位
    Then map load fail-fast 并指明该 scheme
    And 不静默回退到初始 scheme

  Scenario: 座位表存档 round-trip 不丢失
    Given MapSession.LaunchContext 的 localSeats 含 controlSchemeId 声明与 metadata
    When mapSessions 存档域 CaptureState 后 RestoreState
    Then 每项 seatId / playerId / controlSchemeId 与 metadata 原样恢复
    And 未声明的 controlSchemeId 不序列化为空默认值
```


## 7. 分期

- P0 合同（本页 + participant 合同修订 + RFC 修订）— #897  
- P1 SeatRegistry + Possession + 删除全局 LocalPlayer* — #898  
- P2 LogicView 多实例 + PresentBinding 呈现/拾取 — #899（Sole PresentBinding → Presenter / ScreenRay / ScreenProjector / 呈现剔除；LogicView 自有相机权威；已删除 `GameSession.Camera` 会话单例；无座图可用 `logicview.client.present`）  
- P2.5 多分屏基建底座 — PresentBinding.rect 布局工厂 + `CopyPresentBindings` / per-seat 解析；Sole 消费路径仍是今日默认。已收口：存档 `launchContext.localSeats[]` round-trip 直接测试（mapSessions 存档域）+ sole seat `controlSchemeId` 激活链（3.3）  
- P3 分屏布局产品化、per-seat scheme 路由与 UI per-seat owner（同模型，另开子单）
