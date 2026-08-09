# Capability Standard: Crowd Physics Arena

massnav→kinematic 桥（issue #643 增量 2）的标准能力验收 showcase（issue #734）。
两支 kinematic 小队在竞技场集结，穿越动态木箱堆、踩压压力板开门，玩家可用
Q 冲击波击退单位、用 E 释放带初速度的巨石。

## 启动

```powershell
.\scripts\run-mod-launcher.cmd cli launch '$capability_standard_crowd_physics_arena' --adapter raylib
# 或
.\scripts\run-mod-launcher.cmd cli launch 'preset:capability_standard_crowd_physics_arena_raylib'
```

## 玩家操作

| 输入 | 行为 |
|------|------|
| 鼠标左键拖框 | 框选己方小队单位 |
| 鼠标右键点地面 | 下达 massnav 移动命令（选中单位行军） |
| `Q` + 光标位置 | 冲击波：以光标点为震中，480cm 半径内所有单位被向外击退 400cm |
| `E` + 光标位置 | 巨石释放：在光标点生成 dynamic 巨石（初速度 -900cm/s X 轴，正常物理路径滚动/碰撞） |

## HUD

屏幕左上角三行计数：

1. `Selected units` — 当前框选单位数（command source 集合）。
2. `Displaced recovering` — 被击飞、位移窗口尚未交还的单位数（PoseAuthorityArbiter 活跃窗口）。
3. `Plate steps` — 压力板累计 agent ContactBegin 人次；括号内为当前在板上的人数（Begin−End 对账）与已开门数。

## 场景机制

- **世界窗口**：`assets/MassNavigationConfig.json` 覆盖 `world.hotZones`，把活跃热区（=仿真窗口
  中心）定在 (5000, 5000)，与地图上箱堆 (4250–4510)、压力板 (5600)、门 (6400) 的世界坐标对齐；
  两队沿 `OrbitOpposedTargets` 轨道（半径 2600cm）在窗口中心两侧对置生成。
- **地形**：竞技场使用自有平地高度图 `assets/terrain/crowd_physics_arena_flat.vhtm`
  （65×65 网格、全域 0cm），不复用 MassNavigationMod 的 10k 大世界 relief（该地形在竞技场
  区域是 287 米高山，会导致单位悬于山顶不可见）。
- **小队**：2 队 × 48（`assets/MassNavigationConfig.json` 的 `scenario` 全配置化），单位模板组合
  `MovementParticipation`（kinematic + displacement.allowed）+ kinematic 刚体（半径与 agent profile
  `bodyRadiusCm` 同源，桥启动时校验）+ massnav agent。
- **木箱堆**：通路上的 dynamic 箱体，行军穿越时被推开。物理材质 `baseDamping` 是每固定步的
  速度保留系数（`IntegrationSystem2D` 中 `velocity *= baseDamping`），必须 < 1.0：
  箱 0.9 / 巨石 0.98。
- **压力板 → 门**：压力板是 static 传感器。broadphase 对 kinematic×static 配对默认跳过
  （无求解意义），但只要任一方声明 `ContactEventEmitter2D`，就会建立 sensor-only 配对：
  窄相照常产出接触供事件边沿检测，求解器、位置修正、冲量与岛屿构建全部跳过。
  所以板永远不动、不吃修正、不需要锚定或插槽墙。板携带 `ContactEventEmitter2D`，
  EntityLayer `arena.plate` 在
  `assets/Configs/Physics2D/kinematic.json` 的 `contactEventEmitterLayers` 允许清单里。
  桥的 `ContactEventRouter2D` 把 Begin/End 事件路由给
  `CrowdPhysicsArenaPressurePlateDoorSystem`（按 agent 去重：N 单位过板恰好 N 次 Begin），
  agent ContactBegin 达到门模板的 `CrowdPhysicsArena.Door.OpenThresholdContacts`
  （地图 override，默认 20）后开门——门体的 `ManifestationObstacleIntent2D` sink 位清零并
  标脏，物理碰撞体与导航障碍同时移除。
- **Q 冲击波**：纯 effect 步骤组合（无新 BuiltinHandler/enum）：`CreateUnit` 在目标点生成震中
  （`linkSourceAsParent` + `DestroyWhenParentExecutionEnds`，随能力执行结束销毁）→ 震中
  `onSpawnEffect` 触发 `Search`（Circle，origin=Source）→ 对命中单位派发 `Displacement`
  （`AwayFromSource`）。位移期间单位仍被桥喂进 kinematic 物理体（物理视角单位永远在场）。
  场景道具（箱/板/墙/门）、本地玩家标记与震中自身模板都带 `SpatialPartitionExcluded`，
  不进空间索引，Search 只会命中 massnav agent。

## Q 冷却硬约束（issue #737）

当前 GAS 位移对同一目标**无去重**：对仍处于位移窗口中的 agent 再次施加位移会触发
`PoseAuthorityArbiter` 双窗口异常。因此本 mod 的授权值必须满足
**Q 冷却时长 严格大于 单位模板 `displacement.maxDurationMs`（留余量）**：

| 配置项 | 位置 | 值 |
|--------|------|----|
| Q 冷却 `TagClip.duration` | `assets/GAS/abilities.json` | 45 tick @20Hz = 2250ms |
| 位移窗口上限 `displacement.maxDurationMs` | `assets/Entities/templates.json`（4 个 agent 模板） | 2000ms |
| 实际位移时长 `totalDurationTicks` | `assets/GAS/effects.json` | 24 tick = 1200ms |

不变量：`冷却 2250ms > maxDurationMs 2000ms > 实际位移 1200ms`。调整任一值时必须保持
该不等式链，否则连续施放 Q 会命中仍在窗口中的单位并按合同 fail-fast 抛异常
（见 <https://github.com/MightyBubble/Ludots/issues/737>）。
- **E 巨石**：`CreateUnit` 生成 `crowd_arena_boulder` 模板（dynamic 刚体 + 模板授权初速度），
  走正常物理积分路径。

## 容量说明

- `scenarioRuntime.runtimeCapacity.displacedAgentCapacity = 128`：冲击波 `Search.maxTargets = 96`
  （全场单位数），容量必须 ≥ 同时处于位移窗口的单位数；命中数超过容量时按合同 fail-fast 抛异常，
  本 showcase 的配置保证容量够用。
- `Physics2D/kinematic.json` 的 `kinematicBodyCapacity`（默认 4096）必须 ≥ 参与 kinematic 物理的
  单位数（96），不足时桥在喂送时 fail-fast。

## 验收测试

```bash
dotnet test src/Tests/PresentationTests/PresentationTests.csproj --filter CapabilityStandardCrowdPhysicsArenaProductionPathTests
```
