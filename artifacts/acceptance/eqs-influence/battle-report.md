# EQS + Influence Map 验收报告（MUD 风格）

## 场景：AI 避威胁保持接近目标

```
━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
  最小场景：Utility AI 空间落点决策
━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
  地图：确定性栅格（密度 = 50cm/cell，chunk = 8 cells）
  演员：AI actor @ (0, 0)
  目标：goal @ (500cm, 0)
  威胁：threat source @ (300cm, 0) — 正好挡在直线路径上
━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

[T0] 威胁投影
  > threat source 在 (300,0) 释放影响力：peak=10.0, radius=200cm, 线性衰减
  > 采样威胁中心：influence = 8.23（cell 量化到 50cm 密度，中心偏移导致 <peak）
  > 威胁场覆盖 actor→goal 直线路径

[T1] EQS 候选生成
  > 生成器：Ring（半径 400cm，16 个候选点）
  > 候选点环绕 actor，跨越 goal 距离

[T2] EQS 多维打分
  > 测试 1（Distance，权重 1.0）：偏好接近 goal
  > 测试 2（Influence，权重 2.0）：偏好低威胁（安全 > 距离）
  > 每个候选点 = 距离分 + 2×安全分

[T3] EQS 选择
  > 策略：Best（最高分非过滤候选）
  > 胜出候选：偏离威胁直线（Y ≠ 0），威胁 influence < 3.0
  > actor 选择绕过威胁的落点，而非直冲 goal 撞进威胁场

━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
  结果：PASS
━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
  ✓ 胜出点在 ~400cm 环上（生成器正确）
  ✓ 胜出点威胁 influence < 3.0（避险生效）
  ✓ 胜出点 |Y| > 50cm（成功偏离威胁直线）

完成度备注（合入口径）：
  - 本验收覆盖 EQS + Influence 基础设施与 headless 场景
  - Influence 主循环投影 / EQS config authoring / EqsBestScore01 尚未接线
  - Decay 走 SoA 就地乘（0-alloc）；缺 registry/空间查询硬失败，禁止静默当 0
```

## 覆盖的测试路径

| 测试 | 覆盖 | 结果 |
|------|------|------|
| Scenario_AvoidThreatWhileNearGoal | happy path: 生成→打分→选择 | ✓ |
| InfluenceFieldTests (6) | Stamp/Decay/Registry/衰减 | ✓ |
| EqsGeneratorTests (5) | Grid/Ring/Donut/Circle/容量 | ✓ |
| EqsSelectionTests (5) | Best/TopN/Threshold/过滤 | ✓ |
| EqsInfluenceBoundaryTests (2) | 架构边界：不引用 Presentation | ✓ |

总计 **19/19 通过**。

## 复用的既有基建（无重复造轮子）

- `ChunkedField2D<float>` ← Influence Map 底层存储
- `ISpatialQueryService`（Radius/Cone/OBB/Line cast）← OverlapTest 复用
- `INodeGraphSpatialIndex` + `NodeGraph` ← NodeGenerator 复用（transport/nav 节点）
- `WorldCmInt2` + `FieldGridSpec2D` ← 确定性定点坐标 + 密度/范围尺度

## 密度参数与范围尺度（验证）

- **密度**：`cellSizeCm=50` → 采样量化到 50cm 网格（威胁中心采样 8.23 而非 10.0 即为量化证据）
- **范围尺度**：Influence `radiusCm=200` + Ring `radiusCm=400` 独立可调
- 两套体系共享同一尺度语义，无歧义。
