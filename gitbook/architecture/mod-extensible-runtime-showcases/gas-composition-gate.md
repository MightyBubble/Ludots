# GAS Composition Gate: Mod Extensible Runtime Showcases

## 概述

本页是五个 Mod Extensible Runtime showcase 的 GAS composition 自审记录。结论是 PASS：本次案例新增的是配置 shard、graph 连接、Mod-owned handler 注册和 performer 数据复用，不新增 Core enum、不新增 profile DSL、不新增平行 GAS 或 Performer 管线。

这份记录用于回答一个问题：新作者照着这些 showcase 扩展玩法时，改的是自己的 Mod 数据和 Mod 代码，还是被引导去改 Core。

## 结构

| 能力 | Root Mod | 组合层 | 复用入口 |
|------|----------|--------|----------|
| Config shards | `CapabilityStandardConfigShardsShowcaseMod` | Layer 2 | `ConfigPipeline` + `config_catalog.json` shard 目录 |
| Effect preset type code | `CapabilityStandardEffectPresetTypeCodeShowcaseMod` | Layer 2 / 3 | Mod builtin handler registry + `GAS/preset_types/*.json` |
| Performer behavior extension | `CapabilityStandardPerformerBehaviorExtensionShowcaseMod` | Layer 2 | performer behavior registry + `Presentation/performers/*.json` |
| Performer command extension | `CapabilityStandardPerformerCommandExtensionShowcaseMod` | Layer 2 | performer command registry + performer rule |

`CapabilityStandardGraphOpProviderMod` 是 provider，不是 root showcase。它只拥有 `CapabilityStandardGraphOpProviderMod.QueryThreat` 和 `CapabilityStandardGraphOpThreatScore` 契约。

## 详情

核心判断：新变体主要交付物是 A，即新的 graph 节点、effect 步骤、已有 op 的连线或参数。

复用清单：

- Handlers: `BuiltinHandlerRegistry`、`GasGraphOpRegistry`、`PerformerCommandKindRegistry`、`PerformerBehaviorKindRegistry`。
- Queues / Systems: GAS config loaders、graph compiler/executor、Performer rule/runtime/behavior systems、UI surface host、debug draw presentation path。
- Resolvers / Registries: `ConfigPipeline`、`ConfigCatalogLoader`、semantic id registries、Mod extension namespace registry。
- Existing presets / graphs: 继续使用正式 GAS preset type loader、graph config loader 和 performer definition loader。

新增 Layer 0 op: N/A。没有新增原子玩法 op。showcase 只注册 Mod-owned handler，用来证明扩展入口可被正式 pipeline 调用。

事务边界: N/A。没有新增 all-or-nothing gameplay transaction。GAS effect 处理、graph 执行、performer command 路由仍由既有系统承担。

配置 SSOT:

- GAS: `assets/GAS/**`
- Performer: `assets/Presentation/performers/**`
- Catalog: 全局 `assets/config_catalog.json`

是否新增 JSON schema: NO。新增文件都落在既有 schema 和既有 catalog entry 下。

## 场景

一个新作者要做火法 Mod，可以按这些案例逐步替换成自己的内容：

1. 把火球的 ability 和 effect 放进自己的 `GAS/abilities`、`GAS/effects` shard。
2. 注册自己的 Heat Mark handler，并用 `GAS/preset_types` shard 把 handler 暴露为数据 preset type。
3. 把威胁评分做成 provider graph op，让另一个 Mod 的 graph 复用。
4. 用 performer behavior 让目标持续表现状态变化。
5. 用 performer command 把一次 gameplay event 路由成一次表现命令。

玩家验收看的是按钮、面板数值、目标生命值、评分变化、持续运行次数和信号处理次数；作者验收看的是这些反馈都来自正式运行链路。

## 边界

- 不新增 `EffectPresetType`、graph op、behavior kind、command kind 的 Core enum。
- 不新增 profile DSL、inherit mode、placement enum 或 preset 开关。
- 不新建与 GAS、graph、Performer 并行的加载器或运行管线。
- 不允许缺失 key、缺失 shard、route 不匹配、handler 未注册时静默通过。
- 不把 provider 命名空间交给 consumer 注册。
- 不在 root showcase 里混入多个无关能力。

## UAT

```gherkin
Feature: 玩家能验证五个扩展 showcase 都是真的可玩能力

  Scenario: 玩家能看到每条 showcase 的结果
    Given 我分别启动五个 showcase 入口
    When 我点击每个 showcase 面板上的主按钮或等待地图运行
    Then 我能看到生命值降低、Heat Mark 次数增加、威胁评分变化、CloudDrift 数字增长、Signal Ping 处理次数增加
    And 每个 showcase 都给出清楚的面板反馈

  Scenario: 玩家能分辨五个案例各自展示的能力
    Given 我正在查看五个 showcase 面板
    When 我比较每个面板标题和按钮反馈
    Then 每个 showcase 只展示一种能力
    And 我不会看到多个无关能力混在同一个入口里
```
