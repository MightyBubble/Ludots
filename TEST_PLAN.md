# 脚本与 Mod 架构测试计划 (Test Plan)

本计划旨在验证新架构的功能正确性、性能与稳定性。

## 1. 验证场景 (Verification Scenarios)

### 1.1 异步执行与时间控制
- **目标**: 验证 `GameTask` 是否正确响应时间缩放与暂停。
- **测试 Mod**: `PerformanceMod` (`BenchmarkTrigger`)
- **操作**:
  1. 运行游戏并加载 `benchmark` 地图。
  2. 观察控制台输出，确认 Entity 生成过程没有阻塞主线程（UI 应保持响应，如果有的话）。
  3. *高级验证*: 修改 Trigger 增加 `await GameTask.Delay(5.0f)`，调整 `Time.TimeScale = 2.0f`，验证实际等待时间是否减半。

### 1.2 强类型地图加载
- **目标**: 验证 `MapDefinition` 类能否正确被扫描并加载，Tags 是否生效。
- **测试 Mod**: `UiTestMod` (`UiTestMap`)
- **操作**:
  1. 运行游戏并加载 `ui_test` 地图。
  2. 确认 `UiStartTrigger` 被触发（它依赖 `ctx.IsMap<UiTestMap>()`）。
  3. 如果 UI 正确显示，说明地图 ID 匹配成功，且 Trigger 条件判断通过。

### 1.3 Trigger Hook 与 Mod 互操作
- **目标**: 验证 Mod 之间通过 Anchor 进行逻辑注入的能力。
- **测试 Mod**: `ExampleMod` (需要修改代码以进行测试)
- **操作**:
  1. 在 `ExampleMod` 中获取 `UiStartTrigger`。
  2. 使用 `OnAnchor` 注入一个 `LogCommand`。
  3. 运行游戏，观察控制台是否在 UI 显示前后输出了日志。

### 1.4 响应式 UI 与上下文
- **目标**: 验证 `ScriptContextExtensions` 在 UI Mod 中的兼容性。
- **测试 Mod**: `ReactiveTestMod`
- **操作**:
  1. 加载 `ReactiveTestMod`。
  2. 确认计数器 UI 显示且点击 "Increment" 按钮能正常工作。
  3. 这验证了 `context.Get<UIRoot>` 和异步 Trigger 的集成。

## 2. 手动测试步骤 (Manual Test Steps)

### 步骤 1: 运行 Desktop Debug 版本
```powershell
cd src/Platforms/Desktop
dotnet run
```

### 步骤 2: 加载 UiTestMod
在游戏启动后（或通过代码配置），加载 `ui_test` 地图。
预期结果：
- 控制台输出 `[UiStartTrigger] Executing...`
- 屏幕上显示 "LUDOTS UI TEST" 界面。

### 步骤 3: 运行 GasBenchmark
如果已加载 `GasBenchmarkMod`：
- 在控制台或通过 Trigger 触发 `RunGasBenchmark` 事件。
- 预期结果：控制台输出大量 GAS 系统日志，最后显示 FPS 和 GC 数据。

## 3. 自动化测试 (如有)
运行 `ModdingTest` 项目以验证依赖解析和加载逻辑。
```powershell
dotnet test src/Tests/ModdingTest/ModdingTest.csproj
```
