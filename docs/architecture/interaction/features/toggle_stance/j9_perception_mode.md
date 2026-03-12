# J9: 感知模式切换

> 清单编号: J9 | 游戏示例: Arkham侦探视觉, Sekiro义眼道具

## 交互层

- **InputConfig**: ReactsTo = **Down**
- **TargetMode**: **None**
- **Acquisition**: **Explicit**

玩家按键切换感知模式（如侦探视觉、X光视野），视觉效果由 Performer 层响应 tag 变化；交互层仅切换 tag。

## Ludots实现

```
Down → Toggle tag "detective_vision"
  Performer rules:
    if HasTag("detective_vision"):
      enable highlighting performers on all entities with specific tags
      enable X-ray overlay performer
```

感知模式切换是纯视觉效果，交互层仅添加/移除 `detective_vision` tag。Performer 系统监听 tag 变化，启用/禁用对应的高亮、X光、轮廓等视觉效果。

## 依赖组件

| 组件 | 状态 | 说明 |
|------|------|------|
| InputOrderMapping (None) | ✅ 已有 | selectionType: None |
| GameplayTagContainer | ✅ 已有 | detective_vision tag 切换 |
| Performer system | ✅ 已有 | 响应 tag 变化的视觉效果系统 |
| AbilityToggleSpec | ✅ 已有 | toggle 机制，与 J1 共用 |

## 新增需求

无。所有依赖组件已实现。

## 相关文档

- `docs/developer-guide/13_gas_combat_infrastructure.md` — GAS 战斗基础设施
- `docs/architecture/interaction/features/11_toggle_stance_transform.md` — Toggle / Stance / Transform 总览
- `docs/architecture/interaction/user_experience_checklist.md` — 用户体验清单
