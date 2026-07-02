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
```

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

## 5 深度材料

- 仓库深度版：`docs/reference/cli_runbook.md`
