# 三层抽象 PR 审计报告

审计时间：2026-08-24  
审计者：Pi Agent  
基准提交：922737ab55 (PR #1059 merge)

## 执行摘要

- **PR #1130 Machine**：✅ **合并安全**（2 个轻微建议）
- **PR #1131 App**：❌ **有硬伤不合入**（3 个阻断问题 + 2 个隐患）
- **PR #1132 Device**：⚠️ **有隐患需修正后合**（1 个阻断 + 1 个性能问题）

**总裁决**：PR #1131 必须修正后重审；PR #1132 需解决命名空间一致性后可合；PR #1130 可先合入。

---

## PR #1130 Machine (✅ 合并安全)

**提交**: codex/abstraction-mach c49c933f6c  
**文件**: 4 个 (+229 行)

### 审计清单验证

#### 1. AgentBridge discovery 格式解析 ✅

**对照项**：`AgentBridgeHttpServer.WriteDiscoveryFile` 格式
```json
{
  "pid": 12345,
  "port": 47921,
  "version": 1,
  "startedAtUtc": "2026-08-24T...",
  "processPath": "...",
  "tools": {...}
}
```

**MachineContext 解析逻辑**：
- ✅ 只检查 `pid` 和 `port` 为 Number（JsonValueKind.Number）
- ✅ 其他字段（version/startedAtUtc/tools）作为可选扩展不检查
- ✅ 解析失败跳过文件而非抛异常（容错设计）

**结论**：正确匹配，且解析策略鲁棒。

#### 2. machineId 消毒（路径注入风险）✅

**代码**：`ToDirectorySegment` 方法
```csharp
char[] invalidChars = Path.GetInvalidFileNameChars();
string segment = new string(machineId.Select(c => invalidChars.Contains(c) ? '_' : c).ToArray());
segment = segment.TrimEnd(' ', '.');
return segment.Length == 0 || segment == "." || segment == ".." ? "machine" : segment;
```

**安全性检查**：
- ✅ 过滤 `Path.GetInvalidFileNameChars()` 所有非法字符
- ✅ 处理尾随空格/点（Windows 路径陷阱）
- ✅ 拒绝 `.` 和 `..` 穿越攻击
- ✅ 空输入降级为 "machine" fallback

**结论**：消毒充分，无路径注入风险。

#### 3. IO 竞态处理 ✅

**代码**：`GetDiscoveredProcesses` 捕获
```csharp
catch (IOException)
{
    // Discovery 文件可能正被写入或在枚举后被删除；跳过本次快照。
    continue;
}
catch (JsonException)
{
    continue;
}
```

**场景覆盖**：
- ✅ `Directory.EnumerateFiles` 期间文件被删除（EnumerateFiles 本身不抛异常）
- ✅ `File.ReadAllText` 读取一半被删除/锁定（IOException）
- ✅ 畸形 JSON（JsonException）
- ✅ 注释明确说明竞态意图

**结论**：合理处理高频写场景的竞态。

#### 4. 测试覆盖 ✅

**GasTests/Hosting/MachineContextTests.cs**：
- ✅ 隔离目录独立性（两机器不见对方）
- ✅ machineId 消毒（`ci/shard:1` → 合法目录名）
- ✅ 解析 discovery 文件字段与时间戳
- ✅ 跳过畸形 JSON 和缺字段
- ✅ 目录不存在返回空列表
- ✅ 多进程并发（测试名：`IsolatedMachines_DoNotSeeEachOthersProcesses`）

**边界覆盖率**：✅ 空目录/畸形 JSON/多文件并发全覆盖。

### 轻微建议（非阻断）

1. **命名空间不一致**：
   - `MachineContext` 使用 `namespace Ludots.Platform.Abstractions.Hosting`
   - 而 App 层的 `IAppHost` 等使用 `namespace Ludots.Platform.Abstractions`（无 `.Hosting` 后缀）
   - **建议**：统一为 `Ludots.Platform.Abstractions.Hosting`（因为文件都在 `Hosting/` 目录下）
   - **影响**：不影响合并，但后续需统一

2. **AgentBridgeHttpServer 依赖**：
   - `Ludots.AgentBridge.csproj` 新增对 `Ludots.Platform.Abstractions` 的依赖
   - 创建了反向依赖：AgentBridge（库）→ Platform.Abstractions（平台）
   - **建议**：确认依赖方向符合六边形架构（Platform.Abstractions 应该是最底层合约）
   - **现状**：`GetMachineContext()` 是便利方法，不是核心职责，可接受

### 裁决

✅ **合并安全**。代码质量高，测试充分，安全检查到位。命名空间问题建议后续统一修正。

---

## PR #1131 App (❌ 有硬伤不合入)

**提交**: codex/abstraction-app d01664ec86  
**文件**: 13 个 (+456 行)

### 阻断问题

#### 🚨 阻断 #1: RequestShutdown 不生效（合约违反）

**问题**：`RaylibAppHost.RequestShutdown` 设置 `_shutdownRequested = true` 但该标志从未被读取。

**代码**：
```csharp
// RaylibAppHost.cs:95
public void RequestShutdown(string reason)
{
    _shutdownRequested = true;  // ← 设置
    _lifecycle.TransitionTo(AppLifecyclePhase.ShuttingDown);
}

// RaylibAppHost.cs:79
public void Run()
{
    _lifecycle.TransitionTo(AppLifecyclePhase.Running);
    try
    {
        RaylibHostLoop.Run(setup);  // ← 阻塞在这里，无法观测 _shutdownRequested
    }
    // ...
}
```

**根因**：`RaylibHostLoop.Run` 是阻塞调用，内部循环条件是 `while (!Rl.WindowShouldClose())`，不检查 `IAppHost.RequestShutdown` 的状态。

**影响**：
- `IAppHost.RequestShutdown(reason)` 合约失效
- AgentBridge 工具调用 `app.shutdown(reason)` 时无法退出进程
- 测试中的 `host.RequestShutdown("test")` 永远阻塞

**修复要求**：
1. `RaylibHostLoop.Run` 需接受外部终止信号（如 `CancellationToken` 或共享布尔标志）
2. 或 `RaylibAppHost` 改为异步模式，`Run()` 中轮询 `_shutdownRequested` 并中断循环

#### 🚨 阻断 #2: RaylibGameHost 成为死代码但未删除

**问题**：
- `Program.cs` 已切换到 `RaylibAppHost`，不再使用 `RaylibGameHost`
- `RaylibGameHost.cs` 仍保留，只有测试扫描源码时引用（`BrowserUiRuntimePrReviewTests.cs:90`）
- 仓库禁则：禁止重复造轮子/空壳类型

**代码证据**：
```csharp
// Program.cs (base 922737ab55)
using var host = new RaylibGameHost(baseDir, configFile);
host.Run();

// Program.cs (PR #1131)
var appHost = new RaylibAppHost(configFile);
appHost.Initialize(new AppInitContext(baseDir, Array.Empty<string>(), AssetsRoot: null));
appHost.Run();
```

**影响**：
- `RaylibGameHost` 实现了 `IGameHost`，但 `RaylibAppHost` 实现 `IAppHost`，两者并存造成 "Host" 概念分裂
- 违反 AGENTS.md：禁止向后兼容/重复造轮子

**修复要求**：
1. 删除 `RaylibGameHost.cs` 和 `IGameHost.cs`
2. 或保留 `RaylibGameHost` 但标记为 `[Obsolete]` 并在下一个 PR 中删除

#### 🚨 阻断 #3: Dispose 清理逻辑丢失

**问题**：`RaylibGameHost` 实现了 `IDisposable`，`Program.cs` 使用 `using var host` 确保异常时清理。切换到 `RaylibAppHost` 后：
- `RaylibAppHost` 不实现 `IDisposable`
- `Program.cs` 不再有 `using` 或 `try-finally`
- 浏览器运行时清理只在 `Run()` 的 finally 块中，意味着如果 `Initialize()` 抛异常，清理不会执行

**代码**：
```csharp
// RaylibGameHost (旧)
public void Run()
{
    RaylibHostSetup? setup = null;
    try
    {
        setup = RaylibHostComposer.Compose(_baseDir, _gameConfigFile);
        RaylibHostLoop.Run(setup);
    }
    finally
    {
        ShutdownBrowserRuntimeForHostExit(setup, setup?.BrowserRuntime);
    }
}

// RaylibAppHost (新)
public void Run()
{
    // ... setup 从 _setup 字段读取（在 Initialize 中设置）
    try
    {
        RaylibHostLoop.Run(setup);
    }
    finally
    {
        // 清理逻辑
    }
}
```

**场景**：
```csharp
var appHost = new RaylibAppHost(configFile);
appHost.Initialize(context);  // ← 如果这里抛异常
appHost.Run();                 // ← 永远不会执行，清理代码在 Run() 的 finally 里
```

**修复要求**：
1. `RaylibAppHost` 实现 `IDisposable`，将清理逻辑移到 `Dispose()`
2. `Program.cs` 使用 `using var appHost = ...`

### 隐患

#### ⚠️ 隐患 #1: 状态机转换守卫不足

**问题**：`AppHostLifecycle.TransitionTo` 允许跳跃转换。

**代码**：
```csharp
bool forward = newPhase > Phase;
bool resumeFromSuspend = Phase == AppLifecyclePhase.Suspending && newPhase == AppLifecyclePhase.Running;
if (!forward && !resumeFromSuspend)
{
    throw new InvalidOperationException(...);
}
```

**允许的非法转换**：
- `Created → Terminated`（跳过 Configuring/Initialized/Running/ShuttingDown）
- `Configuring → Running`（跳过 Initialized）
- `Initialized → Terminated`（跳过 Running/ShuttingDown）

**测试未覆盖**：
- `AppHostLifecycleTests` 只测试正常路径和 Resume
- 没有测试 `Created → Terminated` 是否应该被拒绝

**建议**：
1. 增加显式状态转换表（如 `Dictionary<(Phase, Phase), bool>`）
2. 或在 TransitionTo 中显式枚举合法转换

#### ⚠️ 隐患 #2: AppInitContext 的死字段

**问题**：
- `AppInitContext` 有三个字段：`BaseDirectory`, `ModPaths`, `AssetsRoot`
- `RaylibAppHost.Initialize` 只使用 `BaseDirectory`，传给 `RaylibHostComposer.Compose`
- `ModPaths` 和 `AssetsRoot` 从未被读取

**代码**：
```csharp
public void Initialize(AppInitContext context)
{
    _setup = RaylibHostComposer.Compose(context.BaseDirectory, _gameConfigFile);
    // context.ModPaths 未用
    // context.AssetsRoot 未用
}
```

**问题分析**：
- 注释说 "AssetsRoot may be null when the host's bootstrap derives it"
- 但 `RaylibHostComposer` 内部已经处理资产路径，不需要外部传入
- `ModPaths` 也是空列表

**影响**：
- 接口设计膨胀，引入未来可能不会用到的字段
- 违反 YAGNI（You Aren't Gonna Need It）原则

**建议**：
1. 如果确定不会用，删除 `ModPaths` 和 `AssetsRoot`
2. 或在注释中明确说明"保留给未来 WebAppHost/EditorAppHost 使用"

### 非阻断观察

- `AppHostRegistry` 双注册检查：✅ 合理（一个进程一个 App）
- `RaylibGameHost.ShutdownBrowserRuntimeForHostExit` 改为 internal：✅ 正确复用
- 命名空间：所有新类型用 `namespace Ludots.Platform.Abstractions`（无 `.Hosting`），与 PR #1130 不一致（见 Machine 审计）

### 裁决

❌ **有硬伤不合入**。必须修复三个阻断问题：
1. 实现 RequestShutdown 的实际终止机制
2. 删除或标记废弃 RaylibGameHost
3. 实现 IDisposable 或调整清理策略

---

## PR #1132 Device (⚠️ 有隐患需修正后合)

**提交**: codex/abstraction-dev 517a1ac0c2  
**文件**: 11 个 (+457 行)

### 阻断问题

#### 🚨 阻断 #1: 命名空间不一致（跨 PR）

**问题**：
- PR #1130 Machine 的类型使用 `namespace Ludots.Platform.Abstractions.Hosting`
- PR #1131 App 的类型使用 `namespace Ludots.Platform.Abstractions`
- PR #1132 Device 的类型使用 `namespace Ludots.Platform.Abstractions`（`InputDeviceDescriptor`, `IInputDeviceWatcher` 等）

**影响**：
- 所有类型都在 `src/Platform/Ludots.Platform.Abstractions/Hosting/` 或 `Input/` 目录下
- 但命名空间混乱：一半有 `.Hosting` 后缀，一半没有
- 违反 C# 约定（命名空间应该匹配文件夹结构）

**修复要求**：
- 三个 PR 统一使用 `Ludots.Platform.Abstractions.Hosting` 或 `Ludots.Platform.Abstractions.Input`
- 或调整文件夹结构使其与命名空间匹配

### 隐患

#### ⚠️ 隐患 #1: RaylibInputDeviceWatcher 性能

**问题**：`Poll()` 每帧调用 `Rl.IsGamepadAvailable(i)` 4 次（MaxGamepads = 4），即使没有手柄连接。

**代码**：
```csharp
for (int i = 0; i < MaxGamepads; i++)
{
    if (!Rl.IsGamepadAvailable(i))
    {
        continue;
    }
    // ...
}
```

**性能评估**：
- `IsGamepadAvailable` 是 P/Invoke 调用（Raylib C 库）
- 60 FPS 下每秒 240 次 native 调用
- 在空手柄场景下全是 false 返回

**对比方案**：
- SDL2/GLFW 使用事件驱动的热插拔通知
- Raylib 5.0+ 是否有 `GetGamepadAvailabilityChange()` 事件？

**建议**：
1. 查阅 Raylib 5.0 文档，确认是否有事件 API
2. 如果没有，当前实现可接受（Raylib 的限制）
3. 添加注释说明 "Raylib does not provide hotplug events; polling is necessary"

### 审计清单验证

#### 1. ClientLocalSeatDeviceBinding 服务注册 ✅

**GameEngine.cs 修改**：
```csharp
SetService(CoreServiceKeys.ClientLocalSeatDeviceBinding, 
    new Client.ClientLocalSeatDeviceBinding(clientLocalSeatRegistry));
```

**时机**：在 `RegisterClientComponents` 方法中，紧跟 `ClientLocalSeatRegistry` 注册后。

**结论**：✅ 注册时机正确，依赖关系清晰。

#### 2. 热插拔自动归座策略 ✅

**代码**：`ClientLocalSeatDeviceBinding.HandleDeviceChange`
```csharp
if (change.Kind == InputDeviceChangeKind.Disconnected)
{
    RemoveBinding(change.Device.DeviceId);
    return;
}

if (TryGetSeatForDevice(change.Device.DeviceId, out _) ||
    !_seats.TryGetSoleSeat(out ClientLocalSeat soleSeat))
{
    return;
}

_bindings.Add(new Binding(soleSeat.SeatId, change.Device));
```

**策略验证**：
- ✅ 单 seat：自动绑定到唯一 seat（`TryGetSoleSeat` 返回 true）
- ✅ 多 seat：不自动绑定（`TryGetSoleSeat` 返回 false）
- ✅ 已绑定设备：不重复绑定（`TryGetSeatForDevice` 返回 true）

**禁则②验证**：
- ✅ 设备实例存储在 `ClientLocalSeatDeviceBinding._bindings`（Seat 域）
- ✅ 适配器（RaylibInputDeviceWatcher）不持有设备句柄，只推送事件
- ✅ `InputDeviceDescriptor` 是值类型（record struct），不是引用句柄

**结论**：✅ 完全符合禁则②。

#### 3. SyntheticInputDevice.WatchAsDeviceWatcher() ✅

**代码**：`SyntheticInputDevice.cs`
```csharp
public IInputDeviceWatcher WatchAsDeviceWatcher()
{
    return new SyntheticDeviceWatcher(this);
}
```

**测试**：`InputDeviceBindingTests.cs`
```csharp
[Test]
public void SyntheticInputDevice_IsEnumerableAsWatcherAndBindable()
{
    IInputDeviceWatcher watcher = new SyntheticInputDevice().WatchAsDeviceWatcher();
    IReadOnlyList<InputDeviceDescriptor> devices = watcher.GetConnectedDevices();
    // ...
}
```

**结论**：✅ 正确暴露，测试覆盖。

#### 4. Raylib.cs P/Invoke 补丁 ✅

**补丁**：`src/Libraries/Raylib-cs/Raylib.cs`
```csharp
[DllImport(NativeLib, CallingConvention = CallingConvention.Cdecl)]
public static extern IntPtr GetGamepadName(int gamepad);
```

**安全性**：
- ✅ `CallingConvention.Cdecl` 正确（C 库标准调用约定）
- ✅ 返回 `IntPtr`，调用方用 `Marshal.PtrToStringAnsi` 转换（见 `RaylibInputDeviceWatcher.ReadGamepadName`）
- ✅ 空指针检查：`namePtr != IntPtr.Zero`

**结论**：✅ 安全绑定。

#### 5. 测试的禁则②验证 ✅

**测试**：`InputDeviceBindingTests.cs`
- ✅ 设备事件不触碰 Entity（测试中没有 `World.Create` 或实体操作）
- ✅ 设备事件不触碰 Possession/PresentBinding（测试只操作 `ClientLocalSeatDeviceBinding`）
- ✅ 热插拔流程只通过 Seat 域（`binding.HandleDeviceChange` → `ClientLocalSeatDeviceBinding`）

**结论**：✅ 测试正确验证禁则②。

### 非阻断观察

- `RaylibHostLoop.Run` 新增设备 watcher 注册：✅ 时机正确（在 `InitWindow` 后，游戏循环前）
- `deviceWatcher.Poll()` 每帧调用：✅ 位置正确（在 `syntheticInput?.AdvanceFrame()` 后）
- `CoreServiceKeys` 新增两个 key：✅ 命名清晰

### 裁决

⚠️ **有隐患需修正后合**。必须：
1. 统一命名空间（与 PR #1130/#1131 协调）
2. 添加 RaylibInputDeviceWatcher 性能注释

---

## 跨 PR 审计

### 文件冲突检查 ✅

**重叠文件**：
- `src/Core/Engine/GameEngine.cs`：PR #1131 (line 417) vs PR #1132 (line 1694) — 不冲突
- `src/Core/Scripting/CoreServiceKeys.cs`：PR #1131 (line 104) vs PR #1132 (line 158, 415) — 不冲突

**结论**：✅ 无文件级冲突，合并时 git 可自动处理。

### 术语治理四条禁则 ✅

| 禁则 | PR #1130 | PR #1131 | PR #1132 |
|---|---|---|---|
| ① client 不指机器 | ✅ 无违反 | ✅ 无违反 | ✅ 无违反 |
| ② 设备只由 Seat 持有 | N/A | N/A | ✅ 完全遵守 |
| ③ 无真实需求不新增空壳 | ✅ MachineContext 有真实消费者（AgentBridge） | ❌ `RaylibGameHost` 成为空壳 | ✅ 所有类型被测试和集成使用 |
| ④ 本机 I/O 不进存档 | N/A | N/A | ✅ InputDeviceDescriptor 只在运行时 |

### 空壳类型检查

**PR #1130**：
- `MachineContext`：✅ 被 `AgentBridgeHttpServer.GetMachineContext()` 使用
- `DiscoveredProcess`：✅ 被 `MachineContext.GetDiscoveredProcesses()` 返回

**PR #1131**：
- `IAppHost`：✅ 被 `RaylibAppHost` 实现
- `AppHostLifecycle`：✅ 被 `RaylibAppHost` 内部使用
- `AppDescriptor`：✅ 被 `AppHostRegistry` 使用
- ❌ `RaylibGameHost`：成为空壳（Program.cs 不再使用）

**PR #1132**：
- `IInputDeviceWatcher`：✅ 被 `RaylibInputDeviceWatcher` 和 `SyntheticInputDevice` 实现
- `InputDeviceDescriptor`：✅ 被 `ClientLocalSeatDeviceBinding` 使用
- `ClientLocalSeatDeviceBinding`：✅ 被 GameEngine 注册并被 RaylibHostLoop 使用

### 命名空间不一致问题 ❌

**现状**：
- PR #1130：`namespace Ludots.Platform.Abstractions.Hosting`
- PR #1131：`namespace Ludots.Platform.Abstractions`（文件在 `Hosting/` 目录）
- PR #1132：`namespace Ludots.Platform.Abstractions`（文件在 `Input/` 目录）

**建议修正方案**：
```
Hosting/ 目录 → Ludots.Platform.Abstractions.Hosting
Input/ 目录   → Ludots.Platform.Abstractions.Input
```

或全部使用 `Ludots.Platform.Abstractions`（但需要移动文件到父目录）。

---

## 总裁决与合并建议

### 总裁决

1. **PR #1130 Machine**: ✅ **合格，可合入**
2. **PR #1131 App**: ❌ **不合格，需修正三个阻断问题后重审**
3. **PR #1132 Device**: ⚠️ **条件合格，需修正命名空间后可合入**

### 合并顺序建议

#### 场景 A：PR #1131 修正后（推荐）

1. **第一轮**：合并 PR #1130 Machine
2. **修正 PR #1131**：
   - 实现 RequestShutdown 终止机制（可能需要修改 RaylibHostLoop）
   - 删除 RaylibGameHost 或标记废弃
   - 实现 IDisposable
3. **统一命名空间**：三个 PR 协调统一 `Hosting` 命名空间后缀
4. **第二轮**：同时合并 PR #1131 (修正后) + PR #1132 (修正命名空间后)
5. **验证**：合并后运行完整 CI，确认 GameEngine 服务注册顺序正确

#### 场景 B：PR #1131 需要大改（保守）

1. 合并 PR #1130 Machine
2. **暂停 PR #1131**，等待 RaylibHostLoop 重构支持外部终止信号
3. 合并 PR #1132 Device（修正命名空间）
4. 稍后重新设计 App 抽象合约（可能需要异步模式）

### 必须修正的问题清单

#### PR #1131 (阻断)

1. ✋ **RequestShutdown 不生效**：
   - 方案 A：RaylibHostLoop.Run 接受 `CancellationToken`
   - 方案 B：RaylibAppHost.Run 改为异步轮询
   
2. ✋ **RaylibGameHost 死代码**：
   - 方案 A：删除 `RaylibGameHost.cs` 和 `IGameHost.cs`
   - 方案 B：标记 `[Obsolete("Use RaylibAppHost")]`

3. ✋ **清理逻辑丢失**：
   - 方案 A：`RaylibAppHost : IDisposable`
   - 方案 B：`Program.cs` 添加 try-finally

#### PR #1132 (条件阻断)

1. ⚠️ **命名空间不一致**：
   - 统一为 `Ludots.Platform.Abstractions.Input`（因为文件在 `Input/` 目录）

#### 跨 PR (建议)

1. 📋 **命名空间总体对齐**：
   - PR #1130: `Ludots.Platform.Abstractions.Hosting`
   - PR #1131: `Ludots.Platform.Abstractions.Hosting`（修正）
   - PR #1132: `Ludots.Platform.Abstractions.Input`（修正）

---

## 附录：审计方法论

### 审计工具链

```bash
# 1. 对照基准提交
git diff --stat 922737ab55..codex/abstraction-mach

# 2. 提取新增文件内容
git show codex/abstraction-mach:src/Platform/.../MachineContext.cs

# 3. 检查跨 PR 文件冲突
comm -12 <(git diff --name-only 922737ab55..PR1 | sort) \
         <(git diff --name-only 922737ab55..PR2 | sort)

# 4. 验证依赖格式对齐
git show BASE:src/Libraries/Ludots.AgentBridge/AgentBridgeHttpServer.cs | \
  grep -A20 "WriteDiscoveryFile"
```

### 安全审计检查表

- ✅ 路径注入：`Path.GetInvalidFileNameChars()` + `.` / `..` 拒绝
- ✅ IO 竞态：IOException / JsonException 捕获
- ✅ P/Invoke 安全：CallingConvention + 空指针检查
- ✅ 依赖方向：AgentBridge → Platform.Abstractions（可接受）
- ✅ 禁则遵守：四条禁则逐项验证

### 测试覆盖率评估

- ✅ 边界：空目录/畸形 JSON/多进程并发
- ✅ 安全：路径穿越/消毒降级
- ✅ 策略：单 seat 自动绑定/多 seat 不自动
- ❌ 状态机：缺少非法跳跃转换测试

---

## 审计签署

**审计者**：Pi Agent  
**审计日期**：2026-08-24  
**审计范围**：PR #1130 / #1131 / #1132 完整代码与测试  
**审计标准**：Ludots AGENTS.md + 术语治理 #902 + 六边形架构原则  

**最终建议**：PR #1131 修正后，三个 PR 可按建议顺序合并。Device 层禁则遵守优秀，Machine 层设计清晰，App 层需要进一步打磨。
