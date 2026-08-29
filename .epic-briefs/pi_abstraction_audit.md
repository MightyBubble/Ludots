# 全面审计邀请：Machine/App/Device 三层抽象 PR（#1130/#1131/#1132）

你是独立审计者。三个 PR 已推送，请对每一个做代码级审计，找出问题。审计通过后将执行合并。

## 审计对象

- **PR #1130**（Machine）：分支 codex/abstraction-mach，commit c49c933f6c
- **PR #1131**（App）：分支 codex/abstraction-app，commit d01664ec86
- **PR #1132**（Device）：分支 codex/abstraction-dev，commit 517a1ac0c2

## 仓库背景

- Ludots：Arch ECS C# 游戏框架，六边形架构，禁止 fallback/向后兼容/重复造轮子/跨越职责
- 术语治理定案（#902 §3.5）：四层阶梯 Machine/App/Seat/Device，四条禁则
- Seat 层已合入 main（PR #1059）
- 三个 PR 从同一 main 基点（922737ab55）切出，互相独立

## 逐 PR 审计清单

### PR #1130 Machine（4 文件 +229 行）

审计要点：
1. `MachineContext.GetDiscoveredProcesses()` 是否正确解析 AgentBridge 的 discovery 文件格式（对照 `src/Libraries/Ludots.AgentBridge/AgentBridgeHttpServer.cs` 的 `WriteDiscoveryFile`）
2. `CreateIsolated` 的 machineId 消毒是否足够（路径注入风险）
3. IO 竞态处理（文件写一半被读）是否合理
4. 测试是否覆盖了边界（空目录/畸形 JSON/多文件并发）

### PR #1131 App（7 文件）

审计要点：
1. `AppLifecyclePhase` 状态机是否有非法转换漏洞（对照 `AppHostLifecycle.cs` 的转换守卫）
2. `RaylibAppHost` 是否真的没改 `RaylibHostLoop` 内部逻辑
3. `Program.cs` 入口切换是否保留旧路径可用性
4. `AppHostRegistry` 双注册拒绝是否会破坏 CI 多引擎场景
5. `AppInitContext.AssetsRoot` 为 nullable 是否合理（agent 说 Raylib bootstrap 自行解析）

### PR #1132 Device（11 文件 +457 行）

审计要点：
1. `ClientLocalSeatDeviceBinding` 是否正确集成到 GameEngine 服务注册（对照 GameEngine.cs 中 ClientLocalSeatRegistry 的注册时机）
2. `RaylibInputDeviceWatcher` 的每帧 diff 逻辑是否有性能隐患（每帧调 IsGamepadAvailable 4 次）
3. 热插拔自动归座策略：单 seat 自动绑定/多 seat 不自动——是否与禁则②（设备只能由 Seat 持有）一致
4. `SyntheticInputDevice.WatchAsDeviceWatcher()` 是否正确暴露 synthetic 设备
5. 测试的禁则②验证是否真实（设备事件不触碰 Possession/PresentBinding）
6. vendored Raylib.cs shim 补丁是否安全（P/Invoke 绑定）

### 跨 PR 审计

1. 三个 PR 是否有文件冲突（合到一起会不会撞）
2. 术语治理四条禁则是否全部满足
3. 是否有空壳类型（没有真实消费者的接口/类）

## 输出要求

对每个 PR 给出独立裁决：✅ 合并安全 / ⚠️ 有隐患需修正后合 / ❌ 有硬伤不合入
最后给出总裁决和合并顺序建议。
