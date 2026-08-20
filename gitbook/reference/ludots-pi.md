# Ludots Pi

## 1. 概述

Ludots Pi 是给作者用的编码助手，不是游戏里的角色，也不是新的启动器。作者在浏览器里打开它，对着 Ludots 仓库说话、改文件、跑官方命令。界面来自 agegr 的 Pi Web，并改了源码；智能体内核仍是官方 Pi。共享技能继续只维护在 `skills/`。

## 2. 结构

```text
作者浏览器
  -> Ludots Pi 网页（src/Tools/Ludots.Pi/web，agegr/pi-web 分叉）
  -> 官方 Pi 会话
  -> Ludots 扩展（src/Tools/Ludots.Pi/package）
  -> 仓库 skills/ 与官方 launcher / 工具命令
```

| 部分 | 位置 | 职责 |
| --- | --- | --- |
| 网页前端 | `src/Tools/Ludots.Pi/web` | 会话、文件、模型登录 |
| 扩展包 | `src/Tools/Ludots.Pi/package` | 开工规矩、官方命令、showcase 列表 |
| 技能 | `skills/` | 唯一技能正文 |
| 启动 | `scripts/run-ludots-pi` | 打开 Ludots 仓库，不另造工作目录 |

## 3. 详情

启动脚本会检查当前目录是不是 Ludots 仓库，并写入 `LUDOTS_PI_WORKSPACE`。网页的默认目录接口只接受这个仓库，不再偷偷新建一个空文件夹。

扩展在项目被信任后加载：

- 开工规矩会写进每一轮对话
- `ludots_workspace` 确认当前是 Ludots 仓库
- `ludots_list_showcases` 只读 `showcase.registry.json`
- `ludots_launch_cli` 只跑官方 `scripts/run-mod-launcher.ps1`

技能同步把 `skills/` 扁平复制到 `~/.pi/agent/skills`。运行时目录不能手改再倒灌回仓库。

## 4. 场景

作者要改一个 showcase，先打开 Ludots Pi，看到的是 Ludots 仓库而不是空白目录。问“现在有哪些可玩条目”时，助手读注册表，不编造名字。要启动某个条目时，助手走官方启动命令。助手准备写代码前，会先按仓库规矩搜现有能力。

## 5. 边界

- 不进 Core，不进游戏帧，不做游戏 Mod。
- 不 fork Pi 内核，不平行再养一套技能正文。
- 未设置仓库路径，或路径不是 Ludots 仓库时，直接失败。
- 启动参数里出现 `;`、`|`、`` ` `` 这类拼接符时，直接拒绝。
- 没有模型登录时，页面可以打开，但不能开始对话；不静默编造回复。

## 6. UAT

```gherkin
Feature: 用 Ludots Pi 当编码助手前端
  作为 Ludots 作者
  我想在浏览器里对着仓库说话并改东西
  以免自己在终端里翻会话、记命令

  Scenario: 打开后就在 Ludots 仓库里
    Given 我在 Ludots 仓库根目录执行正式启动脚本
    When 浏览器打开脚本给出的地址
    Then 我看到标题是 Ludots Pi
    And 当前项目是这个 Ludots 仓库
    And 系统没有另外给我造一个空白工作目录

  Scenario: 问现有可玩条目
    Given Ludots Pi 已经打开 Ludots 仓库
    And 我已经登录可用的模型
    When 我问现在有哪些可玩条目
    Then 助手列出的名字来自仓库注册表
    And 助手不会自己编一个不存在的条目

  Scenario: 仓库路径不对就停
    Given 启动时没有指向 Ludots 仓库
    When 我让界面打开默认项目
    Then 我看到明确失败原因
    And 界面不会假装已经进了一个可用仓库

  Scenario: 技能只有一份
    Given 仓库 skills 目录是技能正文
    When 我执行同步到 Pi 的命令
    Then 本机 Pi 技能目录出现同样的技能
    And 我不会在 Ludots Pi 里看到另一套互相打架的技能说明
```
