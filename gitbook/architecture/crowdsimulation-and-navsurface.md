# CrowdSimulation 与 NavSurface：导航体系重构

> 状态：S0（配置门禁）与 S1（地形与障碍物，含逐格对拍）已交付。后续阶段见文末路线。

## 这是什么

CrowdSimulation 是新的确定性群体导航内核：分层地表（NavSurface）、逐（移动类型 × 净空）的导航上下文、HPA* 走廊 + 流场、虚拟领队与阵型槽位、局部避让与推挤、脏 tile 增量重烘焙、战争迷雾与认知导航。行为参照是 CrowdSimulation Web 沙盒（Base44 参考实现，16 km 地图、5 万单位上限），交付节奏按该项目的 S0–S8 阶段与 UAT（F-01–F-13）执行。

NavSurface（`.navsurface`）是新增的地表资产：逐导航格的地形类型编号 + 跳跃候选。它是导航的唯一地表输入；高度真相仍在 `.height`，桥面不写进地表——桥实体在运行时生成独立的桥面层，拆桥即恢复。

## 为什么不用 MassNavigation 求解器

四个结构性原因（证据见 Web 规格 LU-12 ~ LU-15）：每个成员同步寻路且每次调用都重建网格；地图变化不会让路线失效；方向场是启发式而非沿路网积分；避让与到达规则参数无法一一对应。调参数对齐不了行为，所以内核按 CrowdSimulation 重写。

并存期的纪律：**一张地图只能启用一种求解器**（加载时按 mapId 互斥检查），同一单位模板不能同时带 `CrowdSimulationAgent` 与 `MassNavigationAgent`。MassNavigation 的删除不做在对拍通过之前——它仍被 GAS 下令链、Physics2D 桥、10k 性能门禁与 showcase 引用，对拍基线建立后按 S8 之后的决议退役。

## 数学一律走 Ludots 定点体系

不引入第二套数学：内核全部使用 `src/Core/Math/FixedPoint` 的 Fix64 / Fix64Math（Q31.32 与其确定性超越函数），长度单位为厘米，与 `WorldPositionCm` 同单位制。配置文件维持 Ludots 约定（厘米、`Cm` 后缀），文件值在组装层一次性转 Fix64，内核不再做单位换算。Web 侧的 `DetMath` 计划由此废止——对拍时以 Ludots 定点语义为准生成基线，Web 参考实现如需对拍要适配同一套定点算法。

## 目录落点

```
src/Core/CrowdSimulation/            内核（命名空间 Ludots.Core.CrowdSimulation.*）
  Config/                            CrowdSimulationConfig（DTO，严格 camelCase 全必填）
                                     CrowdSimulationConfigValidator（数值 + 结构规则）
                                     CrowdSimulationConfigLoader（LU-20 组装入口）
                                     CrowdSimulationAuthoringContract（模板级契约）
  World/                             NavSurfaceAsset（.navsurface v1 读写与格式门禁）
                                     NavSurfaceContract（资产 ↔ 配置一致性）
                                     CrowdSimulationSpace（格几何原语）
  CrowdSimulationAgent.cs            单位组件 { profileId }
mods/capabilities/navigation/CrowdSimulationMod/
  assets/config_catalog.json         登记 CrowdSimulationConfig.json = DeepObject
  assets/CrowdSimulationConfig.json  全量默认值（地图 Mod 只写要改的字段）
src/Tests/CrowdSimulationTests/      S0 验收测试
```

目录与 Web 参考实现 `src/engine/` 的模块对照按 TR-04 执行；后续阶段（Nav / Planning / Movement / Crowd / Sim / Fog / Threads）陆续补齐。

## 复用的既有设施

- ConfigPipeline + `config_catalog.json`（DeepObject 合并；缺键 / 未知键拒绝）
- `AgentProfileRegistry`（`Navigation/agent_profiles.json`，ArrayById）
- `MapConfig`（Boards / Teams / Players / ParticipantRelationships / Entities）
- `Engine/clock.json` 的 `FixedHz`（仿真频率的唯一来源，CrowdSimulationConfig 不再有自己的 tickRate）
- 组件注册走 `ComponentRegistry`（`CrowdSimulationAgent` 已登记）
- 测试沿用 NUnit + `src/Tests/*` 项目格局

## 配置门禁（S0 的核心交付）

加载在进入地图时进行，错误消息写明文件、字段路径与规则，例如：

```
CrowdSimulationConfig.json: hpa.clusterSize = 2，需为整数、≥ 4、≤ 128
```

覆盖的规则：声明式数值范围（与参考实现 schema.js 的 RULES 同源，路径已按 Ludots 文件归属换算）、id 唯一性与容量上限（区域 ≤ 256、移动类型 / 单位模板 ≤ 255、组合 ≤ 255）、引用闭包（areaCost / profile / agentType / deploy.bases.playerId / CrowdSimulationAgent.profileId）、地图正方形与格数整除、1–16 个玩家、双求解器互斥、阻挡物只能是正方形 Box（不禁止圆形非阻挡区域实体）。版本不等于当前 v7 一律拒绝，无迁移。

## .navsurface v1 格式要点

小端二进制：`magic "LNSF"` + version + 尺寸 / 格边长 + 地形类型表（id 字符串，格值 = 表下标）+ 逐格 u8 地形编号 + 跳跃候选（端点格、落差厘米、格距）。读取侧做精确长度校验（多一字节少一字节都拒绝）、编号越界拒绝、端点越界拒绝。资产必须与配置的 `world.terrainTypes` 逐字同序——编号语义是“表中下标”，顺序错一位全图错位。

## 阶段路线（来自 Web 规格第三部分）

S0 配置门禁 → S1 地形与障碍物（读 .navsurface、阻挡实体生成障碍）→ S2 逐移动类型可走区域与烘焙 → S3 后台路线与流场 → S4 部署与命令回放 → S5 移动与阵型 → S6 避让推挤 → S7 动态地图与迷雾认知 → S8 正式下令链与 5 万单位。上一阶段对拍通过后才进入下一阶段。

## S1 交付：地形与障碍物（本次）

- **导出工具**：`tools/crowdsimulation-export/exportLudots.mjs`（正本在本仓库，运行于 Web 沙盒 `scripts/` 下），把作者态测试场景（AU-07 / AU-08）按种子写成 Ludots 资产——`.height`（CHTM v2，uint16 缩放）、`.navsurface`（v1）、`Maps/<id>.json`（队伍 / 敌对关系 / 300 阻挡物 / 桥实体）、配置覆盖片段与 S1 对拍摘要。坐标约定：Web 的米 × 100 = Ludots 地图局部厘米，轴向沿用同一平面朝向。
- **演示 Mod**：`mods/showcases/crowd_simulation/CrowdSimulationS1Terrain{1337,2024,7}Mod`，三个种子各一个 Mod，launcher 选择即切换（config 按 mapId 绑定地图，多地图共用一份配置的问题见缺口）。
- **验收（自动化，`src/Tests/CrowdSimulationTests/Parity/`）**：三种子的地形类型栅格、区域栅格、阻挡栅格、阻挡物 / 桥 / 跳跃候选清单与 Web 导出做 FNV-1a 逐格对拍，全相等；删一个阻挡物栅格变、改模板尺寸全体变、覆盖率阈值（< 0.5 不阻挡）生效；`.height` 经 Ludots 正式读取器加载。
- **验收（目视）**：`artifacts/acceptance/crowdsimulation-s1/index.html` 查看器，`CrowdSimulationMapProbe` 工具从真实资产渲染地形类型着色 / 阻挡格 / 桥与跳跃候选三层视图。
- **验收（真机）**：Raylib 宿主经 launcher 预设启动，地图、阻挡物实体与高度图地形（含高程着色）真实渲染，见 `artifacts/acceptance/crowdsimulation-s1/raylib-s1337-live.png`。地形类型着色叠加层在宿主内尚无 presenter，由探针视图承担。
- **顺手的基建修复**：`ContinuousHeightSampleScale.Decode` 的 int32 溢出（大缩放比例资产会触发，10k 资产因恒等比例从未踩到）改为先 widening 再乘。

## 当前缺口（诚实清单）

- S0 演示 Mod 的屏幕化验收（F1–F8 错误展示、配置清单屏）需要宿主系统接线。
- 多地图共用一份 CrowdSimulationConfig 的寻址方式未定——DeepObject 合并只产出一份配置，按 mapId 绑定意味着一张地图一个地图 Mod；14.1 协议冻结时需定案。
- 桥实体的桥面层导航语义（RT-16 分层）在 S2 接入，S1 只有数据与呈现。
- Raylib 宿主内的地形类型着色 / 阻挡格叠加层还没有 presenter（当前宿主画面是高程着色的高度图地形）；阻挡物还没有呈现网格。
- Web 侧对拍基线的定点数学适配未开工（Fix64 语义在 Ludots 侧，Web 需跟进同一套）。
