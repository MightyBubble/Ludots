# Showcase: Effect Preset Type Code

## 概述

这个 showcase 展示作者如何直接编码一个 effect preset type 背后的 C# handler，再用数据把它变成可复用的效果类型。玩家进入地图后能看到 `Effect Preset Type Code` 面板；点击 `Apply Heat Mark` 后，面板显示 Heat Mark 已被正式执行，并且调用次数增加。

关键点是：作者不新增 `EffectPresetType` Core enum。preset type 仍然是数据，C# 只注册 phase handler。

## 结构

```text
CapabilityStandardEffectPresetTypeCodeShowcaseMod/
  CapabilityStandardEffectPresetTypeCodeShowcaseModEntry.cs
  assets/
    game.json
    Maps/
      capability_standard_effect_preset_type_code_showcase.json
    Configs/
      GAS/
        preset_types/
          capability_standard.effect_preset_type_code.heat_mark.json
        effects/
          capability_standard.effect_preset_type_code.heat_mark.json
```

## 详情

Root mod 在 `IMod.OnLoad` 注册 handler：

```csharp
context.Extensions.Gas.RegisterBuiltinHandler(
    "CapabilityStandardEffectPresetTypeCodeShowcaseMod.ApplyHeatMark",
    ApplyHeatMark);
```

preset type shard 声明这个 handler 参与 `OnApply`：

```json
{
  "id": "CapabilityStandard.EffectPresetTypeCode.HeatMark",
  "defaultPhaseHandlers": {
    "OnApply": {
      "type": "builtin",
      "id": "CapabilityStandardEffectPresetTypeCodeShowcaseMod.ApplyHeatMark"
    }
  }
}
```

`type: "builtin"` 使用 C# handler registry。`type: "graph"` 也是合法选择，适合完全能用 GAS graph 表达的组合逻辑。需要访问正式运行时服务或执行小段专用代码时用 builtin；只是串联现有 op 时用 graph。

## 场景

玩家点击按钮后，showcase 发布一个 `EffectRequest`。正式 GAS effect pipeline 处理请求，effect phase executor 调用 Mod 注册的 `ApplyHeatMark`。面板上的 `calls` 增加，表示效果不是 UI 假装触发，而是经过了 GAS phase。

## 边界

- 用户玩法变体不进入 Core enum。
- handler key 必须属于当前加载的 Mod 命名空间。
- `preset_types.json` 和 `effects.json` 必须通过 catalog shard 进入 `ConfigPipeline`。
- 引用不存在的 handler 必须在加载或编译期失败。
- showcase 不直接修改目标属性来假装 GAS 生效。

## UAT

```gherkin
Feature: 玩家触发 Heat Mark 并看到执行反馈

  Scenario: Heat Mark 点击后显示已执行
    Given 我启动 `capability_standard_effect_preset_type_code_showcase_raylib`
    And 地图显示 `Effect Preset Type Code` 面板
    When 我点击 `Apply Heat Mark`
    Then 面板显示 Heat Mark 已执行
    And 面板的 Calls 数量大于 0

  Scenario: 玩家连续触发 Heat Mark 时看到调用次数继续增长
    Given 我已经点击过一次 `Apply Heat Mark`
    When 我再次点击 `Apply Heat Mark`
    Then 面板的 Actions 计数增加
    And 面板的 Calls 数量继续增长
```
