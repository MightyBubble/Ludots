# Navigation2D Knockback Override

## 正式语义

RTS 单位的长期配置默认是：

- `NavPhysicsMode.NavCrowdResolve`

击飞、强制位移、真实冲量窗口期才临时切到：

- `NavPhysicalOverride`
- `NavPhysicsMode.FullPhysics2D`

当前链路是：

- `DisplacementRuntimeSystem` 负责捕获导航状态
- override 激活时暂停当前导航目标输出
- override 结束后自动回落到原始 `NavPhysicsMode`

## 最小 Working Example

```csharp
world.Create(new DisplacementState
{
    TargetEntity = target,
    OverrideNavigation = true,
    RemainingTicks = 12,
    TotalDurationTicks = 12,
    TotalDistanceCm = 600
});
```

如果目标身上有 `NavKnockbackPolicyRef`，override tick 会按 policy 扩展。

## 禁止项

- 不把击飞做成常驻 `FullPhysics2D`
- 不手动恢复 `ForceInput2D` 或 `NavDesiredVelocity2D`
- 不直接删 `NavActor` 规避 override
- 不让 override 结束后残留 runtime 状态
