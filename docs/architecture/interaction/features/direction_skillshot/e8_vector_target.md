# E8: Vector Target Skillshot

> 清单覆盖: E8 矢量/拖拽(两点确定路径)

## 交互层

- **InputConfig**: ReactsTo = **Down**
- **TargetMode**: **Vector** (两点拖拽)
- **Acquisition**: **Explicit**

## Ludots 映射

```
InputOrderMapping:
  actionId: "SkillE"
  trigger: PressedThisFrame
  orderTypeKey: "castAbility"
  selectionType: Vector  // 起点+终点
  isSkillMapping: true
```

## 实现方案

### E8: 矢量/拖拽

**输入流程**:
```
Phase 1: 按下技能键 → 确定起点 (caster 位置 或 当前光标位置)
Phase 2: 拖拽 → 确定终点
Phase 3: 松开 → 施放, 参数 = (start, end)
```

```
AbilityExecSpec:
  Item[0]: EffectSignal @ tick 0 → spawn_vector_effect
    → EffectPresetType: CreateUnit or Search
    → Phase Graph:
      1. ReadBlackboard(vector_start)
      2. ReadBlackboard(vector_end)
      3. 计算路径上的碰撞点
      4. FanOutApplyEffect(hit_targets)
```

**矩形扫描区域**:
- 以 vector_start → vector_end 为中轴
- Width = 200 cm (配置)
- SpatialQuery: Rectangle 沿矢量方向

**示例**: LoL Viktor E (激光扫射), Dota Pangolier Swashbuckle

## 依赖组件

| 组件 | 状态 | 说明 |
|------|------|------|
| Vector input | ❌ 需新增 | 两点输入模式 |
| Rectangle SpatialQuery | ❌ 需新增 | 矩形沿任意方向 |
| Blackboard vector params | ⚠️ 需扩展 | 传递起终点到 Phase Graph |

## 新增需求

| 需求 | 优先级 | 说明 |
|------|--------|------|
| Vector input mode | P2 | selectionType: Vector |
| Two-point drag UI | P2 | 起点+拖拽终点交互 |
| Rectangle sweep | P2 | 沿矢量路径的矩形搜索 |
