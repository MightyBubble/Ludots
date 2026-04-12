# Navigation2D Authoring

## 正式接入面

生产环境里，可导航单位只 author 这些正式 contract：

- `NavActor`
- `NavProfileRef`
- `Team` 或 `TeamIdentity`
- `WorldPositionCm`
- 可选 `NavCrowdProfileRef`
- 可选 `NavKnockbackPolicyRef`

业务层不直接 author 这些底层组件：

- `NavGoal2D`
- `NavDesiredVelocity2D`
- `ForceInput2D`
- `Position2D`
- `Mass2D`（除非单位长期语义就是 `FullPhysics2D`）

运行时由：

- `NavContractValidationSystem`
- `NavActorMaterializationSystem`

统一校验并展开到底层运行时组件。

## 最小 Working Example

```csharp
int navProfileId = catalog.RequireNavProfileId("rts_default");
int crowdProfileId = catalog.RequireCrowdProfileId("crowd_default");
int knockbackPolicyId = catalog.RequireKnockbackPolicyId("knockback_default");

world.Create(
    new NavActor
    {
        IsEnabled = 1,
        PhysicsMode = NavPhysicsMode.NavCrowdResolve,
        DefaultSolverMode = NavSolverMode.Hybrid,
    },
    new NavProfileRef { ProfileId = navProfileId },
    new NavCrowdProfileRef { ProfileId = crowdProfileId },
    new NavKnockbackPolicyRef { PolicyId = knockbackPolicyId },
    new Team { Id = 3 },
    new WorldPositionCm { Value = Fix64Vec2.FromInt(1200, 800) });
```

这里没有任何 `Default*ProfileId` fallback。所有 profile id 都必须来自显式配置。

## Fail-Fast 规则

以下情况会直接阻止进入可玩态：

- 缺 `NavProfileRef`
- 缺 `Team` / `TeamIdentity`
- 缺 `WorldPositionCm`
- `NavCrowdResolve` 缺 `NavCrowdProfileRef`
- profile id / policy id 在 `Navigation2D.Contracts` 里不存在
- map 没有 board
- pathing 环境未接通
- `NavOnly` / `NavCrowdResolve` 单位手填了 `Mass2D`

## 禁止项

- 不依赖 `OrderBuffer` 隐式补齐导航组件
- 不直接写 `NavDesiredVelocity2D`
- 不直接写 `ForceInput2D`
- 不直接积分 `Position2D` 或 `WorldPositionCm`
- 不把 `Mass2D` 当 crowd 质量
- 不使用隐式默认 contract id
