# In-App Launcher Shell 首切片验收报告（epic #1055 / IALS-2·3·4·6 首刀）

- 分支：`codex/in-app-launcher-shell`（基线 origin/main @ b5ff09490d）
- 日期：2026-08-23
- 范围：Backend 迁 Libraries（IALS-3）、PrepareLaunchAsync 转正（IALS-2）、
  模式分发 + LauncherShellHost + 中继重启（IALS-4/6 首刀：共享生命周期合同 + Raylib 文本 UI 前厅）

## 场景卡

- 玩家意图：双击 `Ludots.App.Raylib`（无参）进入启动器前厅 → 选 preset → 构建预检 → 同进程引导引擎 → 游戏结束中继重启回前厅。
- 确定性输入：preset = `mod:LudotsCoreMod`（ResourceOnly，免构建），adapter = raylib，buildMode = Never。
- 关键动作：无参启动进 ShellMode；`args[0]` 传 bootstrap 进 GameMode；PrepareLaunch 产出 bootstrap+graph；GameBootstrapper 严格校验后装载引擎。

## 验证结果（全绿）

| 验证 | 证据 |
|---|---|
| 模式分发合同 | `LauncherShellLifecycle_DispatchesMode_ByFirstArgument`（ArchitectureTests）通过：args[0] 非空=GameMode，空/空白/无参=ShellMode |
| 中继重启合同 | `LauncherShellLifecycle_RelayRestart_SpawnsCurrentProcessWithoutLauncherArguments` 通过：dotnet 宿主带 dll 参数、apphost 直启、UseShellExecute=false、工作目录=BaseDirectory |
| Prepare → 引擎装载 E2E | `PrepareLaunchAsync_WritesBootstrapAndGraph_ConsumableByGameBootstrapper` 通过：PrepareLaunchAsync 产出 bootstrap+graph，GameBootstrapper 校验链全过，`LoadedModIds == OrderedModIds` |
| 工件同构 | `LaunchAsync` 重构为调用 `PrepareLaunchAsync` 后再 spawn——同构由构造保证；既有 23 项 LauncherBootstrapContractTests 全部通过（零回归） |
| 外部 spawn 链冒烟 | `cli launch mod:LudotsCoreMod --adapter raylib`：pid=50272、bootstrap 写入 app 输出目录、游戏进程带 `LUDOTS_AUTO_EXIT_FRAME=10` 运行后自退、CLI exit=0 |
| 迁移零破坏 | `Ludots.Launcher.Backend` 迁 `src/Libraries/` 后 Cli/Bridge/Evidence/ArchitectureTests/GasTests/ThreeCTests 六处引用全部更新，构建零错误 |

## 已知边界（明示，不隐藏）

1. Shell UI 首版是 Raylib 文本列表（IALS-5 的 Markup/Skia 正式 UI 未在本切片）；CEF/浏览器会话不进入 Shell 阶段，满足不变式 2。
2. 窗口采用 CloseWindow→InitWindow 串行（shell→game），未做窗口交接；epic ADR 决策点 2 仍开放。
3. checked-in dev fallback（bin 目录 `launcher.runtime.json` 相对路径）在 bin 直跑下本来就失败（main 既有行为），本切片未修复也未依赖；无参语义已按 epic 改为 ShellMode。
4. CLI `--record` 路径尚未改走 PrepareLaunchAsync（语义略异：BuildAsync 会写 graph 文档），留下一刀。

## 复用清单（防重复造轮子声明）

- `LauncherService.Resolve/ResolvePlan/WriteBootstrap/BuildAppAsync/BuildPlanRuntimeAsync`（全复用，PrepareLaunchAsync 只是转正既有非正式序列）
- `GameBootstrapper.InitializeFromBaseDirectory` 校验链（原样生效，零改动）
- `RaylibGameHost/RaylibHostComposer/RaylibHostLoop`（GameMode 完全未动）
- Raylib-cs shim（App 已有链路引入）

## 测试路径（path.mmd）

见同目录 `path.mmd`；事件流见 `trace.jsonl`。
