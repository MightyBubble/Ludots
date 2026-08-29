## 来源

PR #1303（禁则④存档守卫）落地过程中核实 main 既有失败清单时发现：**禁则②守卫（`TerminologyGovernanceTests`）当前在 main 上是红的**——`RaylibHostLoop.cs` 注册 `CoreServiceKeys.InputDeviceWatcher`，命中守卫正则（Adapter 层 `SetService(CoreServiceKeys.<*Device*|SyntheticInput>)`），未在豁免白名单内。系 #1132（Device 一等抽象）引入注册点时未同步豁免/收敛所致。

同时核实既有失败清单已漂移：ConfigPipeline 容量项现已不红；另有 BehaviorKind 白名单一项红。本单只认领禁则②项，其余照旧记录在案。

## 待裁决的张力

禁则②（#902 §3.5）：设备实例只能由 Seat 持有，Adapter 不得把设备句柄放进 App 级服务容器。但 Device→Seat 三层分工同时写明：**设备枚举/热插拔归 Adapter，往上只暴露出现/消失 + 稳定标识**。`IInputDeviceWatcher` 恰好就是这个枚举/热插拔能力——两种读法：

1. **watcher 属 Adapter 合法职责**：枚举/热插拔本来就归 Adapter，守卫正则把「设备枚举服务」和「设备实例句柄」混在一起误伤了。修法：守卫语义收窄（豁免/放行 watcher 类注册，禁则②继续拦截设备**实例**句柄进 App 容器），豁免仍只减不增原则不破。
2. **watcher 也应收敛**：按 #1058 P3 收敛项方向（`CoreServiceKeys.SyntheticInput` 同类违反点），watcher 的事件消费应挂 `ClientLocalSeatDeviceBinding`，注册点移出 App 级容器。

两个方向都能让守卫回绿，但语义边界不同（「Adapter 可以暴露枚举服务」vs「Adapter 连枚举服务也不进容器」），需要 owner 拍板后回填本单。

## Acceptance criteria

- [ ] Owner 拍板方向（1 或 2）并回填
- [ ] main 上禁则②守卫回绿（含探针/白名单纪律：豁免名单只减不增）
- [ ] 术语文档 `gitbook/architecture/terminology.md` 禁则②条目按裁决补一句枚举服务的边界说明

## 边界

- 不动 #1118 裁决与禁则④豁免
- BehaviorKind 白名单红不在本单（如需治理另开）
