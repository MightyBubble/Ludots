# In-App Launcher Shell v2（launcher-as-mod）：raylib + web 双端落地验收

- 分支：`codex/in-app-launcher-shell`；日期：2026-08-23；对应 epic #1055 IALS-4/5/6 首刀
- 形态：Shell = 普通游戏会话（raylib：LudotsCoreMod + LauncherShellUxMod + CEF；web：纯 Kestrel 伺服），
  皮肤 = `Ludots.Launcher.React` 应用本体（双宿主单一实现），跨会话 = 会话中继

## 变更清单

| 件 | 内容 |
|---|---|
| `Core.Hosting/LauncherShellSite` | Shell 站点地址合同（宿主 Compose 后、Loop 前注入引擎服务） |
| `Launcher.Backend/PrepareLaunchAsync(buildApp)` | 新参数：运行中进程跳过自建（见"发现"节） |
| `Launcher.Backend/LauncherShellApiMapper` | 19 条 launcher API（Bridge 同形薄封装）+ shell 语义 `/api/launch`（prepare→响应→中继）+ React dist 静态伺服 |
| `Launcher.Backend/LauncherShellWebApp` | 环回 Kestrel 工厂（47951 起 16 端口探测） |
| `LauncherShellLifecycle.BuildSessionStartInfo/RelayTo` | 会话中继原语：spawn 自身 + bootstrap 路径 + 本进程 exit 0 |
| `mods/launcher_shell/LauncherShellUxMod` | CEF 表面宿主（独占 Main lease、全屏 Canvas、导航环回站点；克隆 browser_react_flow 模式） |
| `App.Raylib/Program` | 无参 = Shell 会话（prepare shell plan[buildApp:false] → 起环回 → onComposed 注入 Site → Run）；v1 手绘前厅已删除 |
| `App.Web/Program` | 无参 = Shell 模式（Kestrel 5200 直供 launcher + `/` 重定向；launch → 中继）；有参 = 原游戏路径不变 |
| React `launchGame` | `shell:true` 时轮询 /health 至游戏会话接管后 `location = url`（Bridge 场景行为不变） |
| 注册 | binding `launcher_shell` + preset `launcher_shell_raylib`（browserRuntime cef）+ registry 豁免（校验 0 错误） |

## 验证证据（双端真实运行）

**web 端中继闭环**：
1. 无参启动 `Ludots.App.Web` → `/health` = `{"ok":true,"mode":"shell"}`，`/launcher/index.html` 200，`/` 302。
2. `POST /api/launch {"selectors":["$camera_acceptance"],"platformId":"web"}` → 响应 `{ok:true, shell:true, url:"/", plan:{adapterId:"web",...}}`（注：curl 中 `$camera` 被 bash 展开为空，按合同回退到已选 preset——本身验证了 allowDefaultPreset 语义）。
3. shell 进程 exit 0（后台任务干净完成），继任游戏会话接管 5200：`/health` 返回游戏负载 `status:ok, loop.running:true, tick:405`。**shell→游戏单命令换进程实证。**

**raylib 端 CEF 闭环**：
1. 无参启动 `Ludots.App.Raylib` → shell plan prepare（buildApp:false）→ 引擎会话装载 `LauncherShellUxMod`（ModLoader 日志：load→OnLoad→completed）。
2. 环回 47951 LISTENING；Kestrel 向 CEF 发送 React bundle（`Sending file: index.html / index-*.js / index-*.css`）。
3. React 应用活跃：CEF 内发起点查 `/api/mods/{id}/readme` 200（UI 已渲染并在拉数据）。窗口 = 原 P 社暗色主题完整启动器。

**合同测试**（ArchitectureTests）：`ShellPreset_ResolvesToLauncherShellUxModWithCefRuntime`、`LauncherShellLifecycle_SessionStartInfo_CarriesBootstrapPath` + 既有 launcher 套件全绿；`validate-registry.py` 0 错误。

## 发现并已修的架构缺陷：进程内自建锁

运行中的 app 实例锁着自己的 bin，`BuildAppAsync` 向同目录拷贝新程序集必然 MSB3027 死锁（raylib 首次冒烟即触发；web 端此前侥幸因增量无拷贝）。修复：`PrepareLaunchAsync(buildApp:false)` —— shell 会话对自己的 adapter 跳过 app 构建（新程序集本就要经中继才生效，运行中自建无意义）；异构 adapter（raylib shell 建 web app）不锁、照常构建。该语义已入 mapper（`currentAdapterId` 判定）与 raylib shell 宿主。

## 已知边界

1. raylib shell 的 `/api/launch` 中继后 CEF 随旧进程消亡（ successor 窗口接管），浏览器会话不迁移——符合不变式 2（每进程一次浏览器运行时）。
2. web 中继的端口接管存在亚秒级空窗（旧进程退出→新进程 bind 5200），React 侧轮询容忍。
3. dist 未提交仓库（与 Bridge 约定一致），运行前需 `npm ci --include=dev && npm run build`（用户级 `~/.npmrc omit=dev` 需 `--include=dev`）。
4. Bridge 尚未改用 `MapLauncherShellApi`（下一步去重，两处 19 路由暂时并存——均薄封装同一 LauncherService，无平行真相）。
