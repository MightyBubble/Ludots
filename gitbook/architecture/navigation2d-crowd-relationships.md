# Navigation2D Crowd Relationships

## 正式语义

crowd 语义和 `Physics2D` 语义已经拆开：

- `GeometryRadiusCm`：几何占地和避障外扩
- `Mass2D`：真实物理刚体质量
- `NavMass / YieldWeight / PushClass`：crowd resolve 语义

crowd profile 来自显式配置：

- `Navigation2D.Contracts.CrowdProfiles`

team 间让路策略来自：

- `Navigation2D.Contracts.CrowdRelationship`
- relationship runtime

## 当前策略

- `Friendly`：cooperative yield，双方按 `NavMass` / `YieldWeight` 分摊位移
- `Neutral / Hostile blocker`：非合作，低质量一侧主要让路
- `Dominant push`：高 `NavMass` 一侧可明显推开低质量一侧

## 生产约束

- 不再靠直接穿模伪装通过
- 不再把 team0 / team1 写死进 solver
- 不再把 `Mass2D` 当 RTS crowd 质量

## 常见错误

- 把半径当成 crowd 质量
- 把 `Mass2D` 当让路权重
- 在玩法层手写敌我分支，绕过 relationship / contract catalog
