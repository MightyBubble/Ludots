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

- **小队**：2 队 × 48（`assets/MassNavigationConfig.json` 的 `scenario` 全配置化），单位模板组合
  `MovementParticipation`（kinematic + displacement.allowed）+ kinematic 刚体（半径与 agent profile
  `bodyRadiusCm` 同源，桥启动时校验）+ massnav agent。
- **木箱堆**：通路上的 dynamic 箱体，行军穿越时被推开。
- **压力板 → 门**：压力板是薄 dynamic 体（卡在四面 static 插槽墙内），携带 `ContactEventEmitter2D`，
  EntityLayer `arena.plate` 在 `assets/Configs/Physics2D/kinematic.json` 的 `contactEventEmitterLayers`
  允许清单里。桥的 `ContactEventRouter2D` 把 Begin/End 事件路由给
  `CrowdPhysicsArenaPressurePlateDoorSystem`，agent ContactBegin 达到门模板的
  `CrowdPhysicsArena.Door.OpenThresholdContacts`（地图 override，默认 20）后开门——门体的
  `ManifestationObstacleIntent2D` sink 位清零并标脏，物理碰撞体与导航障碍同时移除。
- **Q 冲击波**：纯 effect 步骤组合（无新 BuiltinHandler/enum）：`CreateUnit` 在目标点生成震中
  （`linkSourceAsParent` + `DestroyWhenParentExecutionEnds`，随能力执行结束销毁）→ 震中
  `onSpawnEffect` 触发 `Search`（Circle，origin=Source）→ 对命中单位派发 `Displacement`
  （`AwayFromSource`）。位移期间单位仍被桥喂进 kinematic 物理体（物理视角单位永远在场）。
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
