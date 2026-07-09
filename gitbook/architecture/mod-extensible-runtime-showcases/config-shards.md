# Showcase: Config Shards

## 概述

这个案例展示 Mod 作者如何把大配置拆成小文件。玩家视角是: 安装 `ArcMageMod` 后, 技能列表里多出 `Ember Bolt`, 但 Core 的 `GAS/abilities.json` 没被 Mod 改写。

适合用在能力、效果、图、performer 定义这类会不断增长的配置。目标是让每个 Mod 只提交自己的 shard, 启动时由 `ConfigPipeline` 合并成同一个逻辑配置。

## 结构

```text
ArcMageMod/
  mod.json
  assets/
    Configs/
      GAS/
        abilities/
          arc_mage.ember_bolt.json
        effects/
          arc_mage.ember_bolt_damage.json
```

Core 或加载中的 Mod 必须提供 catalog entry:

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

`GAS/abilities.json` 是逻辑入口, `GAS/abilities/*.json` 是贡献目录。启动时管线先读主文件, 再按稳定 VFS 顺序读取 shard。每个 shard 仍然使用该配置的正式 schema, 只是文件变小了。

示例 shard 只展示关键字段:

```json
[
  {
    "id": "Ability.ArcMage.EmberBolt",
    "exec": {
      "clockId": "FixedFrame",
      "items": [
        {
          "kind": "EffectSignal",
          "tick": 0,
          "template": "Effect.ArcMage.EmberBoltDamage"
        },
        {
          "kind": "End",
          "tick": 0
        }
      ]
    }
  }
]
```

如果正式配置入口完全没有文件或 shard, 且 catalog entry 没有 `AllowEmpty: true`, 启动必须失败。作者把文件放到未声明目录时, 管线不会把那个目录当作正式来源; showcase 验收必须检查目标能力真的进入注册表。

## 场景

1. 玩家安装 `ArcMageMod`。
2. 作者只新增 `assets/Configs/GAS/abilities/arc_mage.ember_bolt.json`。
3. 启动后, 能力注册表里出现 `Ability.ArcMage.EmberBolt`。
4. 删除正式来源时启动报错; 放错目录时, showcase 验收发现能力没有进入注册表。

## 边界

- shard 目录必须来自 `config_catalog.json`, 不能由 feature loader 私自扫描。
- shard 文件必须使用正式 schema, 不能定义 showcase 私有字段。
- 同一个 `id` 的覆盖语义由 catalog policy 决定, 不允许 loader 自己解释。
- 不允许因为找不到 shard 就静默降级到空配置, 除非 catalog 明确 `AllowEmpty: true`。

## UAT

```gherkin
Feature: Mod 作者用小文件贡献能力配置

  Scenario: 安装 Mod 后出现新技能
    Given `GAS/abilities.json` 的 catalog entry 声明了 `GAS/abilities` shard 目录
    And `ArcMageMod` 包含 `assets/Configs/GAS/abilities/arc_mage.ember_bolt.json`
    When 玩家启动游戏并加载该 Mod
    Then 技能列表里能看到 `Ability.ArcMage.EmberBolt`
    And Core 的 `GAS/abilities.json` 不需要为这个技能新增条目

  Scenario: 非空正式配置没有任何来源
    Given 某个正式配置 catalog entry 没有设置 `AllowEmpty: true`
    And 主文件和所有声明的 shard 目录都没有提供片段
    When 游戏启动并加载该配置
    Then 启动失败并提示该配置没有解析到任何来源

  Scenario: 作者把能力 shard 放错目录
    Given catalog entry 只声明了 `GAS/abilities`
    And `ArcMageMod` 把能力文件放在 `assets/Configs/GAS/custom_abilities/arc_mage.ember_bolt.json`
    When showcase 验收检查能力注册表
    Then `Ability.ArcMage.EmberBolt` 不存在
    And 该 showcase 验收失败
```
