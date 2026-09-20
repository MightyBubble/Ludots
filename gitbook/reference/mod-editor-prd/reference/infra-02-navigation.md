# infra-02 reference · 导航配置

> 现状参考。第一性需求见 [infra-02 PRD](../prd/infra-02-navigation.md)；配置说明见 [infra-02 配置说明](../config/infra-02-navigation.md)。

## 1. 现状快照

- agent_profiles：ArrayById；字段 id、radiusCm、heightCm、clearanceCm、draftCm、beamCm、mass、layer；至少一个档案否则抛错；引擎默认六档案（Small/Medium/Large/light/heavy/formation）。
- pathing：DeepObject，根键封闭为 agentTypes（非空）；条目 id、profileId、selection（mode/权重）、navMesh.areaCosts、nodeGraph（projectionMaxRadiusCm/useDynamicOverlay/tag 规则）。
- navmesh：DeepObject；mode=offline、algorithm=recast、profiles（maxClimbCm/maxSlopeDeg）、layers、areas、runtimeIncremental（tileBudgetPerFixedTick 等）；挂接 GameEngine 初始化。

## 2. 代码锚点

| 机制 | 位置 |
|---|---|
| 体型档案加载（至少一条） | src/Core/Navigation/AgentProfiles/AgentProfileConfigLoader.cs:11,30 |
| 寻路配置加载（agentTypes 根键） | src/Core/Navigation/Pathing/Config/PathingConfigLoader.cs:22 |
| 烘焙表路径 | src/Core/Navigation/NavMesh/Config/NavMeshConfigPaths.cs:5 |
| 烘焙挂接 | src/Core/Engine/GameEngine.cs:3204 |
| 实配资产 | assets/Navigation/agent_profiles.json、pathing.json、navmesh.json |

**相关文档**：[infra-02 PRD](../prd/infra-02-navigation.md) · [infra-01 reference](infra-01-engine-physics.md)
