# Navigation2D Group Commands

## 正式语义

`Selection`、`Formation`、`ControlGroup` 现在都只是 producer。
真正的导航协同行为单元是 `NavGroupRuntimeService` 维护的 `NavGroup` 运行态。

正式入口固定为：

- `selection -> group command`
- `AI / order system -> group command`

group 自己持有：

- `GroupId`
- `TeamId`
- `group target`
- `formation spacing / rotation`
- `solver mode`
- `arrival / retry / timeout / abandon`

agent 自己只持有：

- `NavGroupMember`

## 最小 Working Example

```csharp
groups.IssueMoveCommand(
    owner: localPlayer,
    members: selectedAgents,
    teamId: 3,
    targetCm: Fix64Vec2.FromInt(9000, 1200),
    radiusCm: Fix64.FromInt(120),
    formationSpacingCm: 140,
    rotationRad: Fix64.Zero);
```

## Solver 切换

solver 切换粒度固定为 `NavGroup`，不是单 agent。

切换规则来自：

- `Navigation2D.Contracts.GroupSolver.Rules`

运行时结果会写入：

- `NavGroupRuntimeState.ActiveRuleId`
- `NavSolverModeComponent.RuleId`
- `NavDiagnosticsSnapshot.ActiveRuleSummary`

## 禁止项

- 不让 selection runtime 直接写 steering 输出
- 不把当前框选结果当 `NavGroup` SSOT
- 不跳过 `NavGroupRuntimeService` 手写 group slot 分配
- 不按单 agent 高频切 solver
