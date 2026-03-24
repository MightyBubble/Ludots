# Tech Debt Report: TD-2026-03-23-raylib-relationship-showcase-launch

Date: 2026-03-23
Reporter: Codex
Owner: Ludots App / Adapter / ModLoader maintainers
Severity: P2
Scope: Cross-layer

## Trigger
- Scenario: relationship showcase 需要 live raylib 启动做桌面截图与可视化验收。
- Entry point: `scripts/acceptance/run-relationship-showcase-raylib.ps1` -> `scripts/run-mod-launcher.cmd cli launch mod:RelationshipShowcaseMod --adapter raylib --build never`
- Repro steps:
  1. 在仓根执行 `scripts/acceptance/run-relationship-showcase-raylib.ps1 -ScreenshotPath artifacts/acceptance/relationship-showcase/screens/relationship-showcase-raylib.png`
  2. 或直接执行 `src/Tools/Ludots.Launcher.Cli/bin/Release/net8.0/Ludots.Launcher.Cli.exe launch mod:RelationshipShowcaseMod --adapter raylib --build never`
  3. 桌面宿主在启动阶段失败，报 `Arch, Version=2.1.0.0` 程序集装载错误

## Evidence
- `scripts/acceptance/run-relationship-showcase-raylib.ps1`
- `src/Apps/Raylib/Ludots.App.Raylib/Program.cs`
- `src/Adapters/Raylib/Ludots.Adapter.Raylib/RaylibHostComposer.cs`
- `src/Core/Hosting/GameBootstrapper.cs`
- `src/Core/Modding/ModLoadContext.cs`
- `src/Tests/GasTests/Production/RelationshipShowcasePlayableAcceptanceTests.cs`
- `artifacts/acceptance/relationship-showcase/battle-report.md`

## Impact
- User-visible impact: 关系 showcase 当前无法通过 raylib live launch 直接出桌面窗口截图。
- Correctness/stability risk: 关系系统逻辑本身未失真，headless acceptance 可稳定通过；风险集中在桌面宿主启动链。
- Blast radius: 该问题位于 App / Adapter / ModLoader 交界，后续其他需要 live raylib 验收的 mod 也可能命中。

## Fuse Decision
- Mode: explicit-degrade
- Reason: 本次 feature 不允许静默 fallback 到另一套运行时，因此保留统一关系运行时与可玩 acceptance，只把 live raylib 截图能力显式降级为 headless PNG evidence。
- Observability fields:
  - debt_id=`TD-2026-03-23-raylib-relationship-showcase-launch`
  - scenario_id=`relationship-showcase`
  - fuse_mode=`explicit-degrade`
  - reason_code=`raylib_host_arch_assembly_load_failure`

## Containment and Follow-up
- Immediate containment: 以 `RelationshipShowcasePlayableAcceptanceTests` 生成 `battle-report.md`、`trace.jsonl`、`path.mmd`、PNG screenshots，保证 feature 验收闭环。
- Permanent fix direction: 排查 raylib app bootstrap 与 mod assembly load context 之间的共享程序集解析顺序，补充可回归的桌面启动测试。
- Target milestone: 下一轮桌面启动链治理 / launcher 稳定性修复。
