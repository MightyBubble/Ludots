# Showcase: Config Shards

## 概述

这个 showcase 展示 Mod 作者如何把大配置拆成小文件。玩家进入地图后会看到一个配置拆分面板，面板先显示 ability shard 和 effect shard 都已加载；点击 `Cast Ember Bolt` 后，目标生命值从 `100` 降到 `90`。

它证明 `GAS/abilities.json`、`GAS/effects.json` 这类正式配置可以由多个 shard 贡献，而不是让所有 Mod 都改同一个大 JSON。

## 结构

Root mod:

```text
CapabilityStandardConfigShardsShowcaseMod/
  assets/
    game.json
    Maps/
      capability_standard_config_shards_showcase.json
    Configs/
      GAS/
        abilities/
          capability_standard.config_shards.ember_bolt.json
        effects/
          capability_standard.config_shards.ember_bolt_damage.json
```

核心 catalog 声明 shard 入口：

```json
{
  "Path": "GAS/abilities.json",
  "Policy": "ArrayById",
  "IdField": "id",
  "ShardDirectories": [ "GAS/abilities" ],
  "AllowEmpty": true
}
```

## 详情

`GAS/abilities.json` 是逻辑入口，`GAS/abilities/*.json` 是贡献目录。启动时 `ConfigPipeline` 会先读主文件，再按稳定 VFS 顺序读取 shard。每个 shard 仍然使用正式 schema，只是文件变小。

验收会检查：

- launcher binding 和 preset 能找到 root mod。
- 地图能加载并挂载 UI 面板。
- `Ability.CapabilityStandard.ConfigShards.EmberBolt` 进入 `AbilityDefinitionRegistry`。
- `Effect.CapabilityStandard.ConfigShards.EmberBoltDamage` 进入 `EffectTemplateRegistry`。
- 点击按钮后，按钮激活正式 `castAbility` order，`AbilityExecSystem` 执行 ability shard 里的 `EffectSignal`，GAS 消费 `Effect.CapabilityStandard.ConfigShards.EmberBoltDamage`，面板显示目标生命值从 `100` 变为 `90`。

## 场景

作者做一个火球技能时，只需要在自己的 Mod 下增加 ability shard 和 effect shard。玩家不关心这些文件来自哪里，只关心技能能出现在游戏里并打到目标。测试者通过注册表确认它确实走了 `ConfigPipeline` 合并后的正式配置，再通过目标生命值变化确认按钮没有绕过 GAS。

## 边界

- shard 目录必须来自 `config_catalog.json` 的 `ShardDirectories`。
- shard 文件必须使用正式 schema，不能定义 showcase 私有字段。
- 找不到非空正式配置时必须启动失败，除非 catalog 明确 `AllowEmpty: true`。
- 同一个 `id` 的覆盖语义由 catalog policy 决定，loader 不能私自解释。
- root mod 不负责展示 effect preset type、graph op 或 performer 扩展。

## UAT

```gherkin
Feature: 玩家启动配置拆分 showcase 后能使用 shard 提供的技能

  Scenario: 玩家看到 shard 技能已加载并能施放
    Given 我启动 `capability_standard_config_shards_showcase_raylib`
    When 地图加载完成
    Then 我能看到配置拆分面板
    And 面板显示能力文件和效果文件都已加载
    And 面板显示目标生命值为 `100`
    When 我点击 `Cast Ember Bolt`
    Then 面板提示 Ember Bolt 已正式施放
    And 目标生命值变为 `90`

  Scenario: 玩家再次施放时看到同一技能继续生效
    Given 我已经看到目标生命值变为 `90`
    When 我再次点击 `Cast Ember Bolt`
    Then 面板的 Actions 计数增加
    And 目标生命值继续下降
```
