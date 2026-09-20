# 双 Seat 面板 Showcase 设计（dual_seat_panels）

## 一句话与目标用户

同一块屏幕、两位玩家：每人半屏一块自己的面板，另有一块共享面板两边都能操作——面板归属三轴（owner / audience / surface）的第一块可玩演示。写给没读过面板代码的引擎用户：想给自己的本地分屏游戏配 per-seat HUD 时，进这个场景 60 秒看清「谁看见哪块面板、谁能操作哪块面板」。

## 为什么做这块

#1314 把三轴（ownerKind / audienceSeats / PanelSeatSurfacePlacement）做进了引擎，但此前只有合同测试：`PanelTriaxisOwnershipTests` 在无头环境里断言实例数与受众拒绝，没有一场能启动、能按键、能看见拒绝理由的演示。#1058 整单验收同时发现旧 InteractionShowcaseMod 的面板走的是 PublishReactivePage 全窗 overlay，压根不是模板面板。本 showcase 补上这块欠账：不写一行引擎代码，只用正式消费面（模板声明 + 图取数 + 事件准入 + seat 输入通道）把三轴演全。

## 主循环

- **谁改变世界**：两位玩家各自按键（seat.0 左手键位 Q/W/E/R/T，seat.1 右手键位 U/I/O/P/Y；Agent Bridge 用 `input.inject` 带 seatId 注入同一批 action）。
- **用户看到什么**：按键 → 事件按该 seat 的输入通道归因 → 受众 admission → 准入的沿自定义事件出扇（事件总线）→ 血量结算 + 计数进图 → 自己面板的血条/数值当帧变化（延迟 < 1s，realtime pin 每帧刷新）。
- **惊喜时刻**：seat.0 按 E「戳对面面板」——对面 seat.1 的面板受众不含 seat.0，操作当场被拒，理由（点名面板、seat、受众）显示在底部反馈条上，对面血量纹丝不动。被拒的操作一个字节都不进游戏状态。

## 消融对照

场景本身把三轴的两个形态并排摆着，无需开关：

- **per-seat 面板**（`panel.dsp.seat0` / `panel.dsp.seat1`）：ownerKind=seat，audienceSeats 各自一席——左半屏只见左边的，右半屏只见右边的；对面来操作就被拒。
- **共享面板**（`panel.dsp.shared`）：audienceSeats=[seat.0, seat.1]，一份实例两席挂载两份呈现拷贝；两边按各自蓄能键，改的是同一份计数。

热座轮换（R/T/Y 里的轮换键）再加一层动态消融：把共享面板受众临时收窄到单席，另一席的操作立即从「准入」变「拒绝」，再按一次恢复——同一场景内看到受众声明如何改变操作权。

## 解释层

- 每块面板的数值全部来自图求值（`LoadSelfAttribute` 读各自 possessed rep；共享面板 `AggSumAttribute` 聚合战场属性、`ReadMapVarInt` 读共享计数），HUD 无第二份数据。
- 底部全窗反馈条（PublishReactivePage overlay，跨席位信息，属 #1058 已声明划界的全窗 overlay 取舍）显示：首屏按键引导、共享面板当前受众、每席最近一次操作是准入还是拒绝——拒绝时原文展示引擎回流的 reason。
- 图例：面板标题末段即身份（Seat0 / Seat1 / Shared）；反馈条上准入绿、拒绝红。

## 旋钮清单

| 旋钮 | 输入 | 演示什么（回答用户什么问题） |
|------|------|------------------------------|
| 自己面板 ±血量 | seat.0 Q/W；seat.1 U/I | 「我的操作只动我自己的面板吗」——事件按 seat 归因，属性只落在自己的 rep 上 |
| 戳对面面板 | seat.0 E；seat.1 O | 「对面能替我操作吗」——受众外 seat 被拒，reason 回流，游戏状态零变化 |
| 共享面板蓄能 | seat.0 R；seat.1 P | 「一份实例两席可操作是什么样」——两边操作累进同一份共享计数 |
| 共享面板受众轮换 | 任一 seat T（seat.1 为 Y） | 「受众是声明还是运行时态」——SetPanelAudience 同一写入口把受众收窄/恢复，被收窄席的挂载与操作权同步消失/恢复 |

## 场景结构

单一主演示场景 `dual_seat_panels_arena`（`startupPresentLayout: horizontal-equal-split`，两席各持一位勇者 rep，两位勇者初始血量/补给刻意不同——左 120/30、右 90/50，串台一眼可辨）。三块面板由 MapLoaded 触发图创建（CreatePanel + ShowPanel，scope 挂各自勇者），零 mod 代码。首屏引导在底部反馈条上。

## 门户资产

- 启动入口：`.\scripts\run-mod-launcher.cmd cli launch '$dual_seat_panels_showcase' --adapter raylib`
- 注册表：`showcase.registry.json` 条目 `dual_seat_panels`（含验收测试名与产物目录）
- 截图/证据：`artifacts/acceptance/dual-seat-panels/`（两半屏各见各面板、拒绝 reason、受众轮换前后）
- 本页即设计说明；引擎合同见 [ClientLocalSeat · Possession · LogicView · PresentBinding](client-local-seat-and-logic-view.md) §UI 面板归属三轴。

## 反向 API 审计

showcase 作为 API 完整性的验收器，实现中确认的接口现状：

- **面板事件 sink 的生产布线未落**：`PanelEventDispatcher` 构造于消费方，引擎还没有「UI 输入 → FireFromSeat」的通用路由。本 showcase 自带 dispatcher 与 sink（准入事件转自定义事件进事件总线），与 #1013 面板事件线衔接，属后续归属。
- **TriggerGraph 没有属性写算子**：`ModifyAttributeAdd`/`WriteSelfAttribute` 只在 Effect/Derived 图可写，而 `InvokeGraph` 只能调 TriggerGraph——「面板事件 → 图结算属性」这条路今天走不通。本 showcase 的血量结算落在 mod 的自定义事件处理器里走 `AttributeMutationOps`（属性写入权威合同，夜袭 kill tool 同型），计数类结算（蓄能、强化审计）留在 TriggerGraph 写地图变量。缺口归属：面板事件效果链（#1013 线）。
- **默认皮不渲染面板按钮**：模板 events 是数据合同，默认皮没有可点控件；本 showcase 用 per-seat 热键承载同一份合同（payload 校验照走）。
- **audience 是模板级声明**：per-seat 面板今天一席一模板（`panel.dsp.seat0` / `panel.dsp.seat1`），实例级受众不存在；热座轮换用 `SetPanelAudience` 覆盖。是否引入实例级受众由面板线决定，showcase 不绕。
- **触发图不知道 seat**：TriggerGraph 的 scope 来自地图挂载实例，因此 per-seat 面板的 scope 挂载（一图一勇者）是数据声明能表达的极限，无需新 op。
- 已具备而本 showcase 直接复用：`PanelSeatSurfacePlacement` 逐 seat 挂载、`FireFromSeat` 受众准入回流、`AggSumAttribute`/`ReadMapVar*` 图取数、`input.inject` per-seat 注入、`session.info` 座位表。

## 交付边界与完成判据

- 本次实现：showcase mod（`mods/showcases/dual_seat_panels/DualSeatPanelsShowcaseMod`）、注册表/launcher/gitbook 三处登记、headless 验收测试（双 seat 进图 → 3 实例 → per-seat 取数隔离 → 受众准入/拒绝 → 受众轮换）、真机 Agent Bridge 取证。
- 不动引擎面板/座位生产代码；发现引擎缺口只记录（见反向 API 审计），不在 showcase 里绕。
- 已知取舍：底部反馈条为全窗 overlay（#1058 划界）；Web 皮 per-seat 路由不在本 showcase 范围。
