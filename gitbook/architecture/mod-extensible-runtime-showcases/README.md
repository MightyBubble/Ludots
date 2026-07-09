# Mod Extensible Runtime Showcases

本目录是 Mod 扩展运行时的标准案例入口。它面向第一次写 Ludots Mod 的作者：先看玩家会看到什么, 再看 Mod 作者要放哪些文件, 最后看启动时如何验收。

## 概述

扩展运行时不是让 Mod 改 Core, 也不是让每个新玩法都新增枚举。标准路径是:

1. Mod 在启动时注册自己拥有的语义 key。
2. 配置文件用这些 key 组合效果、图、表现行为或表现命令。
3. 引擎冻结扩展注册表后再加载配置和编译图。
4. 缺 key、抢别人命名空间、漏 route、漏 lane、漏 shard 都直接启动失败。

## 结构

| Feature | Showcase |
|---------|----------|
| 配置文件拆分 | [Config Shards](config-shards.md) |
| Effect preset type 代码扩展 | [Effect Preset Type Code](effect-preset-type-code.md) |
| GAS Graph op 扩展 | [Graph Op Extension](graph-op-extension.md) |
| Performer behavior 扩展 | [Performer Behavior Extension](performer-behavior-extension.md) |
| Performer command 扩展 | [Performer Command Extension](performer-command-extension.md) |

## 详情

这些 showcase 共同遵守同一条运行线:

- 入口: `IMod.OnLoad` 注册扩展 key。
- 数据: `assets/Configs/**` 通过 `config_catalog.json` 进入 `ConfigPipeline`。
- 组合: GAS 和 Performer 只消费注册后的 key, 不直接拿 Mod 实例。
- 冻结: Mod 加载结束后扩展注册表冻结, 之后任何注册都是错误。
- 复用: 消费方 Mod 可以引用提供方 Mod 的 key, 但不能注册到提供方命名空间。

## 场景

一个新作者要做火法 Mod:

1. 用配置拆分把 `Ability.ArcMage.EmberBolt` 能力放进自己的 shard。
2. 用 effect preset type 代码扩展做 `ArcMage.HeatMark`。
3. 用 graph op 扩展把威胁值查询开放给其他 Mod。
4. 用 performer behavior 扩展让火焰标记持续抖动。
5. 用 performer command 扩展在施法时生成一次性提示环。

玩家看到的是一个完整能力: 点击施法, 目标被点燃, 地面出现提示, 持续燃烧时表现会变化。作者看到的是五条可独立复用的扩展链路。

## 边界

- 不新增 Core enum 来承载用户玩法变体。
- 不绕过 `config_catalog.json` 自己读文件。
- 不在运行中注册扩展 key。
- 不把 provider Mod 的注册权交给 consumer Mod。
- 不用私有 fallback 让缺失配置静默跳过。

## UAT

```gherkin
Feature: Mod 作者可以按标准案例扩展运行时

  Scenario: 新作者按目录入口找到每个 feature 的独立案例
    Given 我正在阅读 Mod 扩展运行时文档
    When 我打开 showcase 目录
    Then 我能分别看到配置拆分、Effect preset type、Graph op、Performer behavior、Performer command 的案例

  Scenario: 标准案例不要求修改 Core 枚举
    Given 我照着任意一个 showcase 写新玩法
    When 我需要新增一个用户玩法变体
    Then 案例引导我注册 Mod key 并写配置组合
    And 案例不会要求我新增 Core enum
```
