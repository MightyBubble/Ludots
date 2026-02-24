---
文档类型: 架构设计
创建日期: 2026-02-09
最后更新: 2026-02-09
维护人: X28技术团队
文档版本: v0.1
适用范围: Toggle 型技能
状态: 设计中
---
# Toggle Ability — 架构设计

## 1 动机

部分技能需要开关状态（如 DotA 辉耀燃烧、LoL 安妮被动切换）。当前 Timeline 是线性执行模型，不足以描述"开启→持续→关闭"的生命周期。

## 2 核心思路

in/out 是 Timeline，中间持续状态不用 Timeline，而是持续性 Effect + Tag。

## 3 数据模型

```csharp
public struct AbilityToggleSpec
{
    /// <summary>开启时执行的 Timeline（播放开启动画、施加初始效果等）。</summary>
    public AbilityExecSpec ActivateExec;
    
    /// <summary>关闭时执行的 Timeline（播放关闭动画、清理效果等）。</summary>
    public AbilityExecSpec DeactivateExec;
    
    /// <summary>开启期间持续存在的 Infinite Effect ID 列表。</summary>
    public int ActiveEffectId0;
    public int ActiveEffectId1;
    public int ActiveEffectId2;
    public int ActiveEffectCount;
    
    /// <summary>开启状态标识 Tag ID。</summary>
    public int ToggleTagId;
}
```

## 4 生命周期

```
首次按键（无 Toggle Tag）:
  → 检查 BlockTags
  → 执行 ActivateExec Timeline
  → 添加 ToggleTag
  → 添加 Infinite Effect(s)
  → 技能进入 "Toggled On" 状态

再次按键（有 Toggle Tag）:
  → 移除 Infinite Effect(s)
  → 移除 ToggleTag
  → 执行 DeactivateExec Timeline
  → 技能进入 "Toggled Off" 状态
```

## 5 AbilityExecSystem 修改

在 `AbilityExecSystem` 中增加 Toggle 分支：

1. 收到 castAbility Order 时，检查 ability definition 是否有 `AbilityToggleSpec`
2. 若有，检查当前 entity 是否拥有 `ToggleTagId`
3. 若无 tag → 激活流程（执行 ActivateExec + 添加 Effect/Tag）
4. 若有 tag → 关闭流程（移除 Effect/Tag + 执行 DeactivateExec）

## 6 配置示例

```json
{
  "id": "Ability.BurnAura",
  "toggle": {
    "activateExec": {
      "clockId": "FixedFrame",
      "items": [
        { "tick": 0, "kind": "EventSignal", "tag": "Event.BurnAura.Activate" },
        { "tick": 10, "kind": "End" }
      ]
    },
    "deactivateExec": {
      "clockId": "FixedFrame",
      "items": [
        { "tick": 0, "kind": "EventSignal", "tag": "Event.BurnAura.Deactivate" },
        { "tick": 10, "kind": "End" }
      ]
    },
    "activeEffects": ["Effect.BurnAura.Periodic"],
    "toggleTagId": 150
  }
}
```

## 7 与 Tag 系统的交互

- 硬直/沉默等 CC 效果通过 `BlockedTags` 阻止 Toggle 切换
- Toggle 状态本身可作为其他技能/效果的条件（通过 Tag 查询）
- 被打断时的行为由 TagRuleSet 的 `RemovedTags` 控制（如移除 ToggleTag 自动触发关闭流程）
