# Mod Extensible Runtime Showcases

## 概述

本目录是 Mod 扩展运行时的标准案例入口。它面向第一次写 Ludots Mod 的作者，也面向验收这些能力的测试者：先看玩家在屏幕上能看到什么，再看作者应该放哪些文件，最后看启动时如何证明它没有绕过正式管线。

这组 showcase 拆成五个 root mod，每个 root mod 只展示一种能力：

| 能力 | Root Mod | 玩家可见结果 |
|------|----------|--------------|
| 配置拆分 | `CapabilityStandardConfigShardsShowcaseMod` | 点击按钮后，面板显示来自独立 ability/effect shard 的技能被加载并触发 |
| Effect preset type 代码扩展 | `CapabilityStandardEffectPresetTypeCodeShowcaseMod` | 点击按钮后，Heat Mark 被正式执行，面板显示调用次数 |
| Presenter behavior 扩展 | `CapabilityStandardPerformerBehaviorExtensionShowcaseMod` | 进入地图后 CloudDrift 持续 tick，点击按钮时面板显示行为仍在运行 |
| Presenter command 扩展 | `CapabilityStandardPerformerCommandExtensionShowcaseMod` | 点击按钮发送信号，面板显示信号被处理的次数 |

> Graph op 扩展（`RegisterGraphOp` + JSON 图引用 mod 算子）尚未迁移到 L1 control-flow 编译 SSOT（issue #861 之后的作者形态），
> 对应 showcase 暂缓合入；hub 级注册 API 与执行侧 handler table 支持已在 Core 中保留。

## 结构

```text
mods/showcases/capability_standard/
  CapabilityStandardModExtensibleRuntimeShowcaseShared/
  CapabilityStandardConfigShardsShowcaseMod/
  CapabilityStandardEffectPresetTypeCodeShowcaseMod/
  CapabilityStandardGraphOpProviderMod/
  CapabilityStandardPerformerBehaviorExtensionShowcaseMod/
  CapabilityStandardPerformerCommandExtensionShowcaseMod/
```

每个 root mod 都有自己的 `mod.json`、game.json、Maps 目录和独立配置 shard。共享项目只承载可视化面板、debug draw、按钮流程，不拥有任何业务扩展 key，也不替 root mod 注册能力。

## 详情

五个案例共用同一条运行线：

1. Mod 在 `IMod.OnLoad` 注册自己拥有的扩展 key。
2. 配置文件通过 `config_catalog.json` 和 `ConfigPipeline` 加载。
3. GAS 和 Presenter 编译阶段消费已经冻结的注册表。
4. 玩家进入地图后，UI 面板和按钮只调用正式 runtime 服务。
5. 缺 key、缺 shard、命名空间不归属、route 或 lane 不匹配时，启动直接失败。

标准启动命令：

```powershell
.\scripts\run-mod-launcher.cmd cli launch 'preset:capability_standard_config_shards_showcase_raylib'
.\scripts\run-mod-launcher.cmd cli launch 'preset:capability_standard_effect_preset_type_code_showcase_raylib'
.\scripts\run-mod-launcher.cmd cli launch 'preset:capability_standard_performer_behavior_extension_showcase_raylib'
.\scripts\run-mod-launcher.cmd cli launch 'preset:capability_standard_performer_command_extension_showcase_raylib'
```

## 场景

一个作者想做火法 Mod，可以按五个案例拼出完整链路：

1. 用配置拆分把 `Ember Bolt` 的 ability 和 effect 放进自己的 shard。
2. 用 effect preset type 代码扩展定义 `Heat Mark` 的 C# phase handler。
3. 用 graph op provider 给其他 Mod 暴露威胁评分。
4. 用 presenter behavior 让火焰标记持续动起来。
5. 用 presenter command 在命中事件后生成一次性提示。

玩家看到的是可操作的技能、评分变化、持续表现和事件反馈；作者看到的是五条可以单独复用的扩展链路。

## 边界

- 不新增 Core enum 来承载用户玩法变体。
- 不绕过 `config_catalog.json` 私自扫目录。
- 不在运行中注册扩展 key。
- 不把 provider Mod 的注册命名空间交给 consumer Mod。
- 不用 fallback 让缺失配置静默通过。
- 不在 showcase root mod 里塞入多个无关能力。一个 root mod 只负责一个验收点。

## UAT

```gherkin
Feature: 玩家通过五个独立入口看见扩展运行时能力

  Scenario: 玩家能从入口找到每个能力的独立案例
    Given 我打开 Mod Extensible Runtime Showcases
    When 我查看案例清单
    Then 我能分别看到配置拆分、Heat Mark、威胁评分、CloudDrift、Signal Ping 五个案例
    And 每个案例都说明进入地图后能看到的按钮或数字变化

  Scenario: 玩家逐个启动五个案例
    Given 我从启动器选择 capability standard showcase 案例
    When 我分别进入四张 showcase 地图
    Then 每个地图都会显示自己的面板和主按钮
    And 每个面板都只展示当前案例的一种玩法结果