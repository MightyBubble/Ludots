# Showcase: Effect Preset Type Code

## 概述

这个案例展示用户如何直接编码一个 effect preset type 需要的 C# 行为, 再用配置把它变成可复用效果类型。玩家视角是: 火法技能命中后目标获得 `Heat Mark`, 后续燃烧、爆裂或 UI 提示都从这个效果类型出发。

这里的关键点是: `preset_types.json` 仍然是数据真相, C# 只注册 phase handler。作者不是给 Core 加一个新 `EffectPresetType` 枚举值。

## 结构

```text
ArcMageMod/
  ArcMageModEntry.cs
  assets/
    Configs/
      GAS/
        preset_types/
          arc_mage.heat_mark.json
        effects/
          arc_mage.heat_mark.json
```

## 详情

Mod 启动时注册自己拥有的 handler key:

```csharp
public void OnLoad(IModContext context)
{
    context.Extensions.Gas.RegisterBuiltinHandler(
        "ArcMageMod.ApplyHeatMark",
        ApplyHeatMark);
}
```

handler 使用现有 GAS phase handler 签名:

```csharp
private static void ApplyHeatMark(
    World world,
    Entity effectEntity,
    ref EffectContext context,
    in EffectConfigParams mergedParams,
    in EffectTemplateData templateData)
{
    // 读取 mergedParams, 修改目标实体的正式属性或 tag。
}
```

然后用 `GAS/preset_types` shard 声明 preset type:

```json
[
  {
    "id": "ArcMage.HeatMark",
    "components": [ "ModifierParams", "DurationParams" ],
    "activePhases": [ "OnApply", "OnPeriod" ],
    "allowedLifetimes": [ "After" ],
    "defaultPhaseHandlers": {
      "OnApply": { "type": "builtin", "id": "ArcMageMod.ApplyHeatMark" },
      "OnPeriod": { "type": "builtin", "id": "ArcMageMod.ApplyHeatMark" }
    }
  }
]
```

`type: "builtin"` 表示走 C# handler registry。另一个合法选择是 `type: "graph"`, 适合完全能用 GAS graph 表达的组合逻辑。选择标准很简单: 需要新代码访问正式运行时服务时用 C# handler; 只是串联已有 op 时用 graph。

## 场景

1. 玩家释放 `Ember Bolt`。
2. 效果模板引用 `presetType: "ArcMage.HeatMark"`。
3. `OnApply` 让目标获得热量标记。
4. `OnPeriod` 按时间继续推进燃烧反馈。
5. 其他 Mod 可以引用 `ArcMage.HeatMark`, 但不能注册 `ArcMageMod.*` handler。

## 边界

- 用户变体不进 Core enum。
- handler key 必须以当前加载 Mod id 加点号开头。
- `preset_types.json` 必须通过 catalog 和 shard 进入管线。
- 引用不存在的 handler 必须启动失败。
- 不用 legacy enum parser 去猜用户 handler。

## UAT

```gherkin
Feature: Mod 作者用 C# handler 定义新的 effect preset type

  Scenario: 火法 Mod 注册热量标记效果
    Given `ArcMageMod` 在启动时注册 `ArcMageMod.ApplyHeatMark`
    And `ArcMageMod` 的 preset type shard 声明 `ArcMage.HeatMark`
    When 玩家释放会施加 `ArcMage.HeatMark` 的技能
    Then 目标获得热量标记
    And 后续周期效果继续通过同一个 preset type 执行

  Scenario: preset type 引用了不存在的 handler
    Given `GAS/preset_types/arc_mage.heat_mark.json` 引用 `ArcMageMod.MissingHandler`
    When 游戏加载 preset type
    Then 启动失败并指出 handler 未注册
```
