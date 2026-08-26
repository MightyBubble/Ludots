# Launcher CLI Runbook

本页是当前 Ludots launcher 入口的正式操作摘要。

## 1 产品入口

- 可视化 launcher：`.\scripts\run-mod-launcher.cmd`
- CLI launcher：`.\scripts\run-mod-launcher.cmd cli ...`

两者都复用同一套 backend 规划与启动逻辑。

可视化 launcher 的 canonical URL 是：

- `http://localhost:5299/launcher/index.html`

## 2 最常用命令

```powershell
.\scripts\run-mod-launcher.cmd cli resolve camera_acceptance --adapter raylib
.\scripts\run-mod-launcher.cmd cli launch camera_acceptance --adapter raylib
.\scripts\run-mod-launcher.cmd cli launch camera_acceptance --adapter web
.\scripts\run-mod-launcher.cmd cli preset save --name camera-web camera_acceptance --adapter web
.\scripts\run-mod-launcher.cmd cli workspace add --path ..\ExternalMods
.\scripts\run-mod-launcher.cmd cli launch panel_skin_web --adapter raylib --browser-provider ultralight
.\scripts\run-mod-launcher.cmd cli resolve preset:browser_react_flow_cef_raylib --browser-provider ultralight --adapter raylib
```

`--browser-provider cef|ultralight` 覆盖本次 resolve/build/launch 解析出的 `browserRuntime.provider`（高于 game.json / preset），用于同一条命令在 CEF 与 Ultralight 之间切换。Linux/云环境请使用 `ultralight`。
## 3 规则

- `launch` 是产品命令
- selector 可以是 binding、`mod:<id>`、`path:<mod-root>` 或 `preset:<id>`
- 多 root mod 启动受支持，但启动地图仍只有一个最终胜出结果
- 复现实验时显式传 `--adapter`
- 当前运行时 bootstrap 由 launcher graph artifact 驱动，`launcher.runtime.json` 负责承接 adapter bootstrap 信息
- product launch 不再把手工 `game.json` 当作正式入口

## 4 状态文件

- `launcher.config.json`
- `launcher.presets.json`
- `%AppData%/Ludots/Launcher/preferences.json`
- `%AppData%/Ludots/Launcher/config.overlay.json`
- `launcher.runtime.json`
- `artifacts/launcher/<adapter>.launch.graph.json`

## 5 产物录制与验收联动

### --record 录制

- 命令形式：`.\scripts\run-mod-launcher.cmd cli launch <binding> --adapter raylib --record <目录>`
- 用途：把一次启动的验收证据固化到指定目录，供人工审查、soak 循环与 CI 断言复用
- 实例：`scripts/acceptance/run-mass-navigation-large-world-uat.ps1` 每轮运行落一个 `run-NNNN` 目录并校验六件套齐全，缺任何一件即判该轮失败；`-Iterations 0 -UntilLocalTime <时间>` 即过夜 soak 模式

产物六件套：

| 产物 | 说明 |
| --- | --- |
| `battle-report.md` | 人类可读的战报，汇总本次运行的场景、关键断言与结论 |
| `summary.json` | 机器可读的运行摘要；`success` 字段是总判定，`failed_checks` 列出未过检查，另含归一化签名与各项度量 |
| `trace.jsonl` | 逐行 JSON 事件轨迹，按时间顺序记录运行期关键事件，供回放与排查 |
| `path.mmd` | Mermaid 图源文件，描述本次运行的流程路径，可在支持 Mermaid 的工具中直接渲染 |
| `visible-checklist.md` | 可见性检查清单，逐项核对"玩家应当看到什么" |
| `screens/*.png` | 截图组；含按步骤编号的过程截图与 `screens/timeline.png` 时间线拼图 |

### 无头截图环境变量

照 `scripts/acceptance/run-item-system-showcase-raylib.ps1` 实例：

```powershell
$env:LUDOTS_TAKE_SCREENSHOT_PATH = "artifacts\acceptance\item-system-showcase\item-system-showcase-raylib.png"
$env:LUDOTS_TAKE_SCREENSHOT_FRAME = "120"
.\scripts\run-mod-launcher.cmd cli launch mod:ItemSystemShowcaseMod --adapter raylib
```

- `LUDOTS_TAKE_SCREENSHOT_PATH`：渲染到指定帧后把画面写入该 PNG 路径
- `LUDOTS_TAKE_SCREENSHOT_FRAME`：触发截图的帧号（示例取 120，验收脚本默认 180）
- 配套 `LUDOTS_RAYLIB_DIAGNOSTIC_PATH` 可额外落一份 Raylib 诊断日志
- 脚本在截图文件出现且写入时间戳更新后主动结束 launcher，适合无头与 CI 环境

### 与 dotnet test 的联动断言

照 `scripts/acceptance/run-item-system-showcase-acceptance.ps1` 实例：先跑截图脚本，再设环境变量跑测试：

```powershell
$env:LUDOTS_ACCEPTANCE_REQUIRE_RAYLIB_EVIDENCE = "1"
$env:LUDOTS_ACCEPTANCE_SCREENSHOT_PATH = "<截图路径>"
$env:LUDOTS_ACCEPTANCE_DIAGNOSTIC_PATH = "<诊断日志路径>"
$env:LUDOTS_ACCEPTANCE_SCREENSHOT_NOT_BEFORE_UTC = "<UTC 时间戳>"
dotnet test src\Tests\GasTests\GasTests.csproj -c Release --no-build --filter ItemSystemShowcase
```

- `LUDOTS_ACCEPTANCE_REQUIRE_RAYLIB_EVIDENCE=1` 把 Raylib 证据从可选升级为必须：测试断言截图与诊断文件存在、且写入时间晚于 `LUDOTS_ACCEPTANCE_SCREENSHOT_NOT_BEFORE_UTC`，证据缺失或过期即断言失败
- 不设该变量时同一测试按无证据路径放行，便于本地快速跑
- 验收脚本在 finally 中统一清理这四个环境变量，避免污染后续测试进程

### 进一步阅读

- 门户 tests.html 证据查看器：<https://mightybubble.github.io/Ludots/tests.html>，在线浏览各验收用例的报告与截图
- `scripts/acceptance/` 目录下的 `run-*` 脚本即本节用法的可执行实例

## 6 深度材料

- 仓库深度版：`docs/reference/cli_runbook.md`
