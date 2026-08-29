# Epic: 本机座位 · 逻辑视觉 · 呈现绑定（Viewport 建模收口）

> 状态：总项目看板。抓大放小，小点不漏。  
> 关联已开子单：#896（原四分模型 Epic，可并入本单或降为子里程碑）· #897 P0 · #898 P1 · #899 P2 · PR #900  
> 正式合同：`gitbook/architecture/client-local-seat-and-logic-view.md` · `map-owned-participant-contract.md`  
> 修订说明：呈现剔除（culling）跟 **PresentBinding**，不跟裸 LogicView。

## 1. 概述

把「全局唯一本地玩家 + 唯一会话相机 = 唯一视觉」拆成稳定的本机 / 模拟 / 呈现分层，使：

- **单机单人**生产路径干净（当前主交付）
- **同屏多座位 / 旁观 / 换机占有**不靠旁路硬接（模型预留，不抢当前刀）
- **逻辑镜头**与**画面矩形**解耦：有没有分屏，PresentBinding / LogicView 建模都要站住

不做兼容桥，不镜像旧 `LocalPlayer*` 键。

## 2. 结构（大钉）

```text
地图身份          MapConfig.Entities → Players/Teams     Participant（先实体，再绑定）
冷启动默认        GameConfig.startupLocalSeats[]         默认怎么坐
本次进图 SSOT     MapLaunchContext.LocalSeats[]          进图座位表
运行时            ClientLocalSeatRegistry                占有 / PresentBinding
模拟视觉          LogicViewRegistry                      可选逻辑镜头（非人手一份）
呈现              PresentBinding → Presenter / 拾取 / 剔除   画谁、画哪、用何呈现度量
```

四分职责（禁止混写）：

| 概念 | 管什么 | 有无画面 |
|---|---|---|
| Participant | 世界身份 | 无关 |
| Possession | 谁在驾驶该身份 | 无关 |
| LogicView | 逻辑镜头权威（姿态 / Follow / VCam / 逻辑投影参数） | **无关** |
| ClientLocalSeat | 本机 I/O（设备、ControlScheme、交互栈） | 有设备才有 |
| PresentBinding | Seat → 某个 LogicView 的呈现面（rect / 呈现分辨率） | **仅要画时** |

### 2.1 镜头权威 vs 呈现剔除（必须分清）

| 能力 | 驱动锚 | 说明 |
|---|---|---|
| Follow / VCam / 镜头权威态 | **LogicView** | 逻辑眼睛在哪、怎么动；无显示器也可存在 |
| 逻辑域 Cast / 逻辑视锥查询 | **LogicView**（逻辑度量） | 不要求 PresentBinding |
| 画面剔除 / 视觉 LOD / 跳过绘制（`CullState` 等） | **PresentBinding** | **启用呈现面才算**；姿态取自绑定的 LogicView，矩形与分辨率取自 PresentBinding |
| 屏幕拾取 / Presenter 插值 | **PresentBinding** | 呈现度量 + 绑定 LogicView |

禁止：

- 仅因存在 LogicView 就跑呈现剔除
- 把 `GameSession.Camera` 当成 seat0 / 唯一视觉真相
- 把 Knowledge / Fog 观察者混进 LogicView 或 PresentBinding

## 3. 详情

### 3.1 已完成（对照 PR #900 · #1025 · #1059）

- [x] P0 合同文档 + RFC-0065「同进程多 viewport」非目标修订
- [x] P1 删除全局 `LocalPlayerId` / `LocalPlayerEntity`；消费面改读座位占有
- [x] 座位配置四层 SSOT（`startupLocalSeats` → launch → registry）
- [x] P2 单座 PresentBinding → Presenter / ScreenProjector / ScreenRayProvider
- [x] 启动链路：实体先刷 → Participant 绑定 → 仅对有占有的 seat 建 LogicView
- [x] fallback 治理（#1024 / PR #1025）：`PlayerId=0` 只做无占有哨兵；删 `EnsureDefaultSoleSeat` 自愈；seat/control/viewer/order 缺失明确失败；守卫测试防回流
- [x] P2.5 收口（PR #1059）：sole seat `controlSchemeId` 激活链（进图真相优先于偏好存储；未安装/被拒 fail-fast）+ 存档 `launchContext.localSeats[]` round-trip 直接测试

### 3.2 单座生产路径收口（2026-08 复核：全部落地）

- [x] **LogicView 为镜头权威地址**：`GameSession.Camera` 已删除；Follow / VCam / 权威姿态按 LogicView
- [x] **呈现剔除挂 PresentBinding**：仅对有 PresentBinding 的呈现面计算 culling / 视觉 LOD；镜头姿态读绑定 LogicView，度量读 PresentBinding
- [x] **PresentBinding 单座也完整**：`presentResolutionPx` / 全屏 rect 来自宿主真实呈现面
- [x] **Sole 路径生产验收**：主 showcase / 启动进图全程只走座位 + PresentBinding，无旧键残留
- [x] **文档与示例对齐**：game.json `startupLocalSeats`、进图命令 `LocalSeats`、存档 `localSeats[]` 示例齐全；合同写明「剔除 ≠ 跟裸 LogicView」

§3.3 模型预留项已开 P3 子单 **#1058**（多 PresentBinding 呈现管线 / per-seat 输入路由与 scheme 激活 / UI per-seat owner / 分屏布局产品化）。

### 3.3 模型预留（抓大：要稳；放小：不抢刀）

- [ ] 多座同时占有 + 同时输入（设备 / ControlScheme 挂 seat）→ P3 #1058
- [ ] PresentBinding.rect 分屏（布局配置，不另起视觉子系统；每 binding 各自剔除）→ P3 #1058
- [ ] UI per-seat owner（面板归属座位，不抢同一套 HUD）→ P3 #1058
- [ ] 旁观：PresentBinding 盯乙的 LogicView，而不 possess 乙 → 远端时代，认领 issue 待开
- [ ] 换 client：只改 Possession / PresentBinding，集合与 LogicView 不搬家 → 远端时代，认领 issue 待开

### 3.4 小点清单（不能漏）

**配置 / 生命周期**

- [x] 禁止 `startupLocalPlayerId` / 存档 `localPlayerId` 回流
- [x] `LocalSeats[].playerId` 必须已在 map Players 绑定，否则 fail-fast
- [x] seatId 非空、同次进图唯一
- [x] 空座位表合法（纯旁观 / 无本机驾驶）
- [x] map unload / unfocus 清理座位占有与 LogicView，禁止串图
- [ ] 无 PresentBinding 时不写呈现 `CullState` 路径（或 不启用呈现剔除系统）→ 随 P3 #1117 术语治理落地

**Participant / LogicView**

- [x] **不是每个 Participant 都有 LogicView**（启动不对全图玩家人手一份）
- [x] AI / 其它玩家代表：可只有身份 + 集合；LogicView 需显式创建时再开子单
- [ ] 有 LogicView、无 PresentBinding：可做逻辑域查询，**不算**呈现剔除
- [ ] EntityCollection 地址仍 `(participantRep, key)`，不跟 seat 走

**输入 / 控制 / Cast（本 Epic 换锚，不重做分层）**

- [x] 输入 / 下令 / Follow 锚 = seat 或 possessed rep，禁止猜本地
- [x] Control Plane = 可控拓扑现算，不是 selection 槽
- [x] Cast 是几何原语（ScreenBoxCast 等），不叫 `Selection.*`；写集合是业务另线
- [x] Cast 候选集是输入，不是默认全图实体

**呈现**

- [x] Adapter 不拥有镜头权威（只吃 Presenter 插值结果）
- [x] 有 PresentBinding：拾取与剔除用呈现度量 + 该 LogicView
- [x] 无 PresentBinding：逻辑 Cast 只用 LogicView 逻辑度量；不跑画面剔除
- [ ] 多 PresentBinding 时：按 binding 各自剔除 / 拾取，禁止误合成「唯一全局可见集」当真相 → P3 #1058
- [ ] 多 LogicView 时全局 CameraPose / VCam 请求的作用域要明确（禁止误广播成唯一真相）→ P3 #1058

**明确不做 / 不混写**

- [x] 不把 Knowledge / Fog 并进 LogicView（观察者实体另一条线，本 Epic 不展开）
- [x] 不把 InteractionContextStack.ownerToken 当成座位表
- [x] 不在 `MapConfig.Players` 声明静态 local 标记
- [x] Active 冒充多座位 — 禁止（#1025 已删 Active/兜底路径）
- [x] 「有 LogicView 就必须 culling」— 禁止（剔除挂 PresentBinding）

## 3.5 术语治理：机器 / App / Seat / Device 四层阶梯（2026-08 定案，经 Pi opus 评审）

| 层 | 正式术语 | SSOT 归属 | 代码建类型？ |
|---|---|---|---|
| 机器 | **Machine / 机器**（原设计稿称 Client，已更名） | 唯一表达形式 = 一个 AgentBridge discovery 目录（环回 + pid/port 集合点）；launcher/部署层概念 | 不建（跨机需求出现前） |
| 进程 | **App**（进程宿主） | launcher 启动构型 + Adapter（进程内宿主能力库）+ HostLoop（App 帧循环）；多进程编排归 #711 联机线（待合入） | 已有 |
| 席位 | **Seat** | `ClientLocalSeatRegistry`（本 Epic 落地） | 已是 |
| 设备 | **Device（Seat 域内）** | ControlScheme=设备布局档案；Input Action Mapping 解释设备参数为语义；Mock=SyntheticInputDevice/AgentBridge | 暂不建设备注册表；P3 预留唯一钩子=ClientLocalSeat 设备句柄集合 |

**"client" 一词仓库内只保留两个精确义**：①client-local 前缀=本机；②ReplicatedClient=网络角色（待 #711 合入 main，合入前不得在合同中写作现状）。**禁止第四义（client≠机器）**。

**Device→Seat 归属（P3 落地形态）**：启动配置（StartupLocalSeatConfig）只管开几个 Seat + controlSchemeId，不放物理设备 id；设备→Seat 绑定归 Seat 运行时；设备枚举/热插拔归 Adapter，往上只暴露出现/消失 + 稳定标识。

**四条禁则**（前三条经 Pi 评审修订，第四条为 Pi 补充）：
1. client 不指机器——文档与代码标识符一律（机器=Machine，禁 MachineClient 类命名钻空）
2. 设备实例只能由 Seat 持有；Adapter 不得把**可写设备实例句柄或设备→Seat 绑定状态**放进 App 级服务容器。**无状态设备观察端口**（枚举/热插拔，仅稳定标识 + 设备类别，如 `IInputDeviceWatcher`）属 Adapter 本职，经 App 容器发布合法（#1305 裁决精确化）。已知违反点：`CoreServiceKeys.SyntheticInput` 单例——收敛归宿为 per-seat mock 设备由 Seat 持有（与 #1315 D 项同刀），豁免名单只减不增
3. 没有跨宿主真实需求前，不新增 Machine/Client/Device/UISurface 空壳类型
4. **本机 I/O 层概念（seatId、controlSchemeId、设备标识）不得进入存档与网络载荷**——存档/网络只传 participant/player 与语义 order。已知现存冲突：存档 `launchContext.localSeats[].controlSchemeId`（#1059 round-trip 测试固化了该行为），裁决见 #1118

**已知缺口（记录在案不修）**：AgentBridge 单 discovery 目录 + 单端口段（47921 起 16 个），并行跑两组三进程验收会抢端口——"Mock 两台机器"目前不可达，属机器维度的寻址缺口，远端/CI 并行化时处理。

## 4. 场景

- 单机单人：`startupLocalSeats: [seat.0 → player 1]` → 全屏 PresentBinding → 对该 binding 做呈现剔除  
- 读档 / 命令进图：只认 `MapLaunchContext.LocalSeats`  
- AI bot：地图有 Participant，无 seat → 无自动 LogicView；即便显式建了 LogicView，无 PresentBinding 也不做画面剔除  
- （预留）同屏双人 / 旁观 / 换机 — 模型能接，单独立项交付  

## 5. 边界

- 本总单管：**本机锚 + 逻辑视觉 + 呈现绑定** 的建模与生产收口  
- 本总单不管：Cast→Query→WriteCollection 业务图重做、Knowledge/Fog 重做、完整分屏产品化（子单）  
- 分屏是 PresentBinding.rect，不另起 viewport 子系统  
- **LogicView ≠ viewport**：viewport / 呈现面 = PresentBinding；LogicView 只提供镜头权威  

## 6. UAT

```gherkin
Feature: 本机座位与逻辑视觉总项目

  Scenario: 冷启动座位来自游戏全局配置
    Given game.json 声明 startupLocalSeats 含 seat.0 → playerId 1
    When 引擎 LoadStartupMap
    Then MapLaunchContext.LocalSeats 与该配方一致
    And 运行时座位表发布对应占有

  Scenario: 单座呈现不依赖全局 LocalPlayer 槽
    Given 本机仅 seat.0 占有甲且 PresentBinding 全屏
    When 玩家移动镜头并做屏幕拾取
    Then 呈现与拾取均走该 PresentBinding 的 LogicView
    And 不存在 LocalPlayerEntity 服务槽

  Scenario: 无座位的参与者不必有逻辑镜头
    Given 地图上存在未被任何 seat 占有的玩家代表
    When 地图加载完成
    Then 该代表是合法 Participant
    And 系统不自动为其创建 LogicView

  Scenario: 仅有逻辑镜头时不算画面剔除
    Given 某 Participant 持有 LogicView
    And 本机没有任何 PresentBinding 指向它
    When 模拟推进一帧
    Then 可用该 LogicView 做逻辑域查询
    And 不因其存在而计算呈现剔除

  Scenario: 有呈现绑定才做画面剔除
    Given seat.0 的 PresentBinding 绑定甲的 LogicView 且全屏
    When 呈现帧更新
    Then 画面剔除使用该 PresentBinding 的矩形与呈现分辨率
    And 镜头姿态取自甲的 LogicView

  Scenario: 换机不搬家（预留合同）
    Given 甲的 LogicView 与指挥集已存在
    When Possession 转到另一 client
    Then 甲的 LogicView 与 collection 仍挂在甲上
```

## 7. 工作切分建议

| 优先级 | 主题 | 说明 |
|---|---|---|
| P0–P1 | 合同 + 删旧键 + 座位 SSOT | 已完成（#897/#898 / PR #900） |
| 单座生产路径收口 | LogicView 权威 + PresentBinding 完整（含剔除挂接）+ fallback 治理 + P2.5 | 已完成（PR #900 / #1025 / #1059） |
| 后 | 多座输入 / rect / UI per-seat / 每 binding 剔除 | 模型已预留，产品单独立项 |
| 并行小点 | 上表 §3.4 清单逐项勾掉 | 抓大放小，但不许漏 |

## 8. 子单索引

- #896 原 Epic（建议：关闭或改为指向本总单）  
- #897 P0 合同  
- #898 P1 SeatRegistry  
- #899 P2 LogicView + PresentBinding（续：单座权威收口 + 剔除挂 PresentBinding）  
- #1024 / PR #1025 fallback 治理  
- #1058 P3 多座位生产化（分屏管线 / per-seat 路由 / UI per-seat owner）  
- PR https://github.com/MightyBubble/Ludots/pull/900  
- PR https://github.com/MightyBubble/Ludots/pull/1059（P2.5 收口）  




