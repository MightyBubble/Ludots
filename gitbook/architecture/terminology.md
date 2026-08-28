# 术语阶梯：Machine / App / Seat / Device

定案：[#902 §3.5](https://github.com/MightyBubble/Ludots/issues/902)（2026-08，经 Pi opus 评审） · 执行项：[#1117](https://github.com/MightyBubble/Ludots/issues/1117)

本页是 Ludots 分层术语的唯一权威定义，取代一切把「client」当机器讲的旧用法。术语只在本页定义一次；各层的行为合同在各自正式页（座位与呈现见 [client-local-seat-and-logic-view.md](client-local-seat-and-logic-view.md)）。

## 1. 概述：四层阶梯

| 层 | 正式术语 | SSOT 归属 | 代码建类型？ |
|---|---|---|---|
| 机器 | **Machine / 机器**（原设计稿称 Client，已更名） | 唯一表达形式 = 一个 AgentBridge discovery 目录（环回 + pid/port 集合点）；launcher / 部署层概念 | 不建（跨机需求出现前） |
| 进程 | **App**（进程宿主） | launcher 启动构型 + Adapter（进程内宿主能力库）+ HostLoop（App 帧循环，`src/Adapters/Raylib/Ludots.Adapter.Raylib/RaylibHostLoop.cs`）；多进程编排归 [#711](https://github.com/MightyBubble/Ludots/issues/711) 联机线（待合入，main 上暂无） | 已有 |
| 席位 | **Seat** | `ClientLocalSeatRegistry`（已落地，`src/Core/Client/ClientLocalSeatRegistry.cs`） | 已是 |
| 设备 | **Device（Seat 域内）** | ControlScheme = 设备布局档案；Input Action Mapping 解释设备参数为语义；Mock = SyntheticInputDevice（`src/Core/Input/Runtime/SyntheticInputDevice.cs`）/ AgentBridge | 暂不建设备注册表；P3 预留唯一钩子 = ClientLocalSeat 设备句柄集合（见 #1058） |

Machine 不建 C# 类型：跨宿主真实需求出现前，「一台机器」就是它在 AgentBridge discovery 目录里的那个条目，不是代码对象。

## 2. 「client」一词的两个合法义

仓库内「client」只保留两个精确义：

1. **client-local 前缀 = 本机**（如 `ClientLocalSeatRegistry` = 本机座位表）
2. **ReplicatedClient = 网络角色**（待 [#711](https://github.com/MightyBubble/Ludots/issues/711) 合入 main；main 上暂无，合入前不得在任何合同中写作现状）

禁止第四义：**client ≠ 机器**。机器一律称 Machine。

## 3. 四条禁则

1. **client 不指机器**——文档与代码标识符一律（机器 = Machine，禁 `MachineClient*` 类命名钻空）。
2. **可写设备实例与设备→Seat 绑定状态只能由 Seat 持有；Adapter 经 App 容器只允许暴露无状态设备观察端口（枚举 / 热插拔，仅稳定标识 + 设备类别）。** 判据：服务携带的是「观察」还是「可交互状态」。观察端口按端口类型显式归类放行（现例：`CoreServiceKeys.InputDeviceWatcher`，注册面 `IInputDeviceWatcher`，键定义在 `src/Core/Scripting/CoreServiceKeys.cs`）；含 Device 字样的新服务键默认拒，需显式归类后才可注册。已知违反点：`CoreServiceKeys.SyntheticInput` 单例（`src/Adapters/Raylib/Ludots.Adapter.Raylib/RaylibHostComposer.cs`）——收敛归宿 = per-seat mock 设备由 Seat 持有（#1058，与 #1315 D 项 input.inject per-seat 路由同刀），收敛前由架构守卫豁免名单看管，只减不增。
3. **没有跨宿主真实需求前，不新增 Machine / Client / Device / UISurface 空壳类型。**
4. **本机 I/O 层概念（seatId、controlSchemeId、设备标识）不得进入存档与网络载荷**——存档 / 网络只传 participant / player 与语义 order。已知现存冲突：存档 `launchContext.localSeats[].controlSchemeId`（#1059 round-trip 测试固化了该行为），已裁决保留、生产化前复审（[#1118](https://github.com/MightyBubble/Ludots/issues/1118)）。

禁则 ①② 由架构守卫 `src/Tests/ArchitectureTests/Governance/TerminologyGovernanceTests.cs` 强制：① 扫描 src/Core + mods 拒绝 Client/Machine 并置标识符；② 扫描 src/Adapters——观察端口按注册面类型显式归类放行，含 Device 字样的新服务键默认拒，注册可写设备实例（`SyntheticInputDevice` 或实现其设备写入面的类型）无论键名一律拒（SyntheticInput 豁免名单只减不增）。

## 4. Device→Seat 归属（P3 落地形态）

- 启动配置（StartupLocalSeatConfig）只管开几个 Seat + controlSchemeId，不放物理设备 id
- 设备→Seat 绑定归 Seat 运行时（绑定寿命 = seat 寿命）
- 设备枚举 / 热插拔归 Adapter，往上只暴露「出现 / 消失 + 稳定标识」

## 5. 已知缺口（记录在案不修）

AgentBridge 单 discovery 目录 + 单端口段（47921 起 16 个，`src/Libraries/Ludots.AgentBridge/AgentBridgeConfig.cs`）：并行跑两组三进程验收会抢端口——「Mock 两台机器」目前不可达，属机器维度的寻址缺口，远端 / CI 并行化时处理。

## 6. 沿革

用户原始设计稿（`commandSystem.md`）曾以 Client 称机器、以「客户端接入的控制器设备」描述设备归属；#902 §3.5 定案后，顶层术语已按本页更名（Client → Machine）并把 Device 归属改挂 Seat。
