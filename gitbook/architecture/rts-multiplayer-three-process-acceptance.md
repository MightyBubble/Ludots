# 《双人前线》三进程会话验收

> 上游 Epic：[平台无关服务器权威联机核心：RTS 最小闭环与双人 Showcase #709](https://github.com/MightyBubble/Ludots/issues/709)。完整玩家体验以[《双人前线》RTS 联机 Showcase 设计与验收](rts-multiplayer-showcase.md)为准；本页只定义当前三进程启动与会话建立证据。

## 1. 概述

这项验收同时启动一个独立权威对局和两个独立 Raylib 客户端。它回答三个问题：第二个客户端启动时会不会挤掉第一个客户端、两个客户端是否真的连接同一权威进程、两个玩家是否得到彼此独立的重连身份。

验收通过只表示三进程会话已经建立，不表示玩家已经完成就绪、采集、生产、战斗、结算、断线重连或迷雾信息隔离。完整玩法证据缺失时，Showcase 必须保持 `experimental`，不得登记截图或标记为 `active`。

## 2. 结构

```text
同一次 Launcher 解析结果
  -> 同一玩法计划与 PlanFingerprint
  -> 权威进程 bootstrap + 对应图副本
  -> 客户端甲 bootstrap + 对应图副本 + 凭据甲
  -> 客户端乙 bootstrap + 对应图副本 + 凭据乙
```

Launcher 的图文件会校验它对应的 bootstrap 路径，因此三个角色各持有一份图副本。三份图的玩法计划、Mod 顺序和 `PlanFingerprint` 必须相同，只有证据文件路径可以不同。验收脚本直接启动三个进程，不调用会替换同适配器旧进程的 Launcher 启动命令。

## 3. 详情

运行配置的单一来源是 `scripts/acceptance/rts-multiplayer-frontline-three-process.profile.json`。端口、连接码、等待时间、存活观察时间、产物根目录和项目路径均来自该文件；命令行参数只用于本次覆盖。

脚本 `scripts/acceptance/run-rts-multiplayer-frontline-three-process.ps1` 按以下顺序工作：

1. 检查 UDP 端口可用，并构建 Launcher、网络版 Mod 计划、Raylib 应用和 DedicatedServer。
2. 只解析一次网络版 preset，保留 Launcher 原始解析结果和图文件。
3. 为权威进程、客户端甲和客户端乙写入互相隔离的 bootstrap、图副本、标准输出和错误输出路径。
4. 顺序启动权威进程、客户端甲和客户端乙；客户端乙启动后再次确认客户端甲仍存活。
5. 等待两个不同的凭据文件出现。进程仅仅活着、窗口仅仅打开，均不算会话建立。
6. 在配置的观察时间内监控三个进程；任一进程提前退出即失败。
7. 结束时只清理本次记录且 PID 与启动时间均匹配的三个进程。

每次运行在 `artifacts/acceptance/rts-multiplayer-frontline-three-process/<UTC 时间>/` 留下解析结果、三组 bootstrap/图、两份客户端凭据、三个进程的输出和 `run-manifest.json`。任一构建失败、启动失败、提前退出、凭据缺失或证据缺失都返回非零退出码，不产生通过结论。

## 4. 场景

在仓库根目录运行：

```powershell
./scripts/acceptance/run-rts-multiplayer-frontline-three-process.ps1
```

端口被占用时，可以为本次运行显式覆盖：

```powershell
./scripts/acceptance/run-rts-multiplayer-frontline-three-process.ps1 `
  -Port 28777 `
  -ConnectionKey "frontline-local-review"
```

验收人员先查看脚本退出码，再打开最新的 `run-manifest.json`。只有 `status` 为 `passed`、两份凭据路径不同、三个进程均有独立 PID，且失败字段为空时，才能确认“三进程会话建立”这一小步完成。

## 5. 边界

- 不使用浏览器、网页接口或浏览器专属联机语义；Raylib 只是本次被认证的客户端宿主。
- 不用单进程切换玩家身份、进程内 loopback 或两个本地完整世界冒充联机。
- 不把凭据文件存在解释为房间就绪、命令闭环、状态一致、断线恢复或完整对局通过。
- 不自动点击就绪、采集、生产、移动或攻击；这些仍需独立的玩家流程自动化与人工验收。
- 不生成或复用截图；没有真实画面证据时注册表中的 `screenshot` 必须为 `null`。
- 不按进程名、窗口标题或启动时间范围清理其他进程；只处理本次持有的 PID，并再次核对启动时间。
- 不在失败时切换本地模式、忽略缺失证据或写入伪通过清单。

## 6. UAT

```gherkin
# language: zh-CN
功能: 两名玩家从独立客户端进入同一权威会话

  场景: 第二名玩家加入时第一名玩家仍在会话中
    假如 《双人前线》的独立权威对局已经启动
    并且 玩家甲已经从独立客户端甲连接该对局
    当 玩家乙从独立客户端乙连接同一对局
    那么 玩家甲的客户端不会被关闭或替换
    并且 玩家甲与玩家乙的客户端都保持连接
    并且 两名玩家获得彼此不同的重连身份

  场景: 任一角色没有真正进入会话时验收失败
    假如 权威对局、客户端甲或客户端乙中任一进程提前退出
    或者 任一玩家没有获得自己的重连身份
    当 验收等待时间结束
    那么 本次验收明确显示失败
    并且 不会把窗口曾经打开解释为联机成功
    并且 不会把 Showcase 标记为可正式游玩
```

上述场景只覆盖会话建立。完整 Showcase 的玩家 UAT 仍必须按照主设计文档完成两人就绪、经济循环、命令反馈、战斗、胜负、重连和信息隔离，且提供真实截图与跨进程证据。
