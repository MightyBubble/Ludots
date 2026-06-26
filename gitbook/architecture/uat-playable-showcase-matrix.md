# UAT 可玩 Showcase 矩阵

本文把当前仓库内已经具备正式入口、可玩运行态、可复用验收链路的 showcase / playground 收敛成一套 UAT 矩阵。

目标不是再造一套新 demo 体系，而是复用已有 mod、地图、runtime、panel 与 acceptance 路径，给每个 UAT 阶段一个明确的主入口。

## 1 选型原则

- 必须是正式 `mod.json + assets/game.json + startupMapId` 入口
- 必须已有可观察玩家操作，不是纯 headless fixture
- 必须能复用现有 acceptance / screenshot / trace 工件链路
- 必须服从单写真相，不新增 showcase 私有 contract
- 必须能说明自己对应哪个 UAT 阶段，而不是“什么都能演示一点”

## 2 推荐矩阵

| UAT | 主 showcase | 入口 map | 目标 | 复用重点 |
|------|-------------|----------|------|----------|
| `UAT-1` | `FormationPhysicsPlaygroundMod` | `formation_physics_playground` | 少量高价值方阵本体、碰撞、推挤、狭窄通过 | 复用 scenario config、panel 与 scenario selection tests |
| `UAT-2` | `CapabilityStandardMassNavigationLargeWorld10kMod` | `mass_navigation` | 大规模 crowd、MassFlow 执行、drag-select、move command | 复用 `MassNavigationMod` runtime、panel、contract tests 与 performer tests |
| `UAT-3` | `RelationshipShowcaseMod` | `relationship_showcase` | 预算、状态、前端场景卡、artifact 产出链路 | 复用 production battle-report / trace / path artifact 输出模式 |
| `UAT-4` | `InteractionShowcaseMod` | `interaction_showcase_hub` | 统一入口、控制组、formation 视图、entity info、HUD 面板、跨系统联动 | 复用 hub/stress 双地图、selection dock、entity collection inspector、playable acceptance |

## 3 UAT-1：FormationPhysicsPlaygroundMod

### 3.1 为什么是它

- 它已经是独立 mod，不依赖临时 fixture
- `<Mod>/assets/game.json` 已提供正式 `startupMapId`
- scenario config 已覆盖 `PassThrough`、`OrthogonalCross`、`Bottleneck`、`LaneMerge`、`CircleSwap`、`GoalQueue`
- 已有 scenario selection 和 runtime load tests，说明入口和 scenario catalog 都是正式 contract

### 3.2 建议操作脚本

1. 进入 `formation_physics_playground`
2. 从 `PassThrough / Bottleneck / GoalQueue` 三个 scenario 开始
3. 分别验证 100、300、1000 量级阵列穿越、窄道、排队
4. 记录碰撞生效、停下与恢复、推挤与回正

### 3.3 通过标准

- 方阵本体不穿透
- 狭窄路径下能形成可感知排队
- 玩家可以通过 playground 面板切换 scenario，而不是改代码
- 运行结果可被 scenario selection tests 和 runtime load tests 覆盖

## 4 UAT-2：CapabilityStandardMassNavigationLargeWorld10kMod

### 4.1 为什么是它

- 它是当前 MassFlow 大规模执行基线
- 它通过地图 `mass_navigation` 与 `MassNavigationMod` 使用正式执行引擎
- 它覆盖 team-slot、direct target、邻居分离、硬解析、performer 展示与配置面板
- NAV-6 到 NAV-9 的路由、per-agent target、move-plan 与 road 迁移都以 MassFlow 为执行 sink

### 4.2 建议操作脚本

1. 进入 `mass_navigation`
2. 拖框选中一片 crowd，右键下达 move goal
3. 切换执行/避障相关配置 preset
4. 观察 HUD、单位移动、局部避让、arrival 计数与 performer 展示
5. 与 `RoadNetworkShowcaseMod` 的 road move-plan UAT 一起跑，确认路由与 MassFlow sink 共存

### 4.3 通过标准

- crowd 在大规模下仍可选、可命令、可重现
- MassFlow panel / runtime 不因规模增加而丢失交互
- route-to-execution 和 move-plan sink 都使用正式 runtime，不使用脚本桩
- contract tests 能覆盖配置、执行、arrival、performer 与 road move-plan 回归

## 5 UAT-3：RelationshipShowcaseMod

### 5.1 为什么是它

- 它现成具备前端驱动、场景卡、artifact 输出链路
- 非常适合作为“预算 / 语义 / 展示证据”的标准模板
- 可用于把 AOI / LOD / 预算状态展示为用户可观察的结论，而不是只看控制台

### 5.2 建议操作脚本

1. 进入 `relationship_showcase`
2. 运行 scripted scenario
3. 记录 state change、front-end view、battle report、trace、path
4. 用同一 artifact 模式承载未来的 AOI / LOD / budget 观测结果

### 5.3 通过标准

- scenario card、battle report、trace、path artifact 全部产出
- 状态变化能由用户侧证据回放，而不是只依赖日志
- 该链路可被后续 AOI / LOD 阶段复用

## 6 UAT-4：InteractionShowcaseMod

### 6.1 为什么是它

- 它已经是统一入口风格的 showcase
- 有 `hub` 与 `stress` 双地图
- 有 selection dock、control group、formation view、entity collection inspector、ability HUD
- 是目前最接近“统一 showcase”定义的现货

### 6.2 建议操作脚本

1. 进入 `interaction_showcase_hub`
2. 多选编队并保存到 control group
3. 切换 formation view 与 live view
4. 打开 entity info / collection inspector
5. 验证语义属性预览、selection virtualization、control group recall、HUD 联动
6. 再进入 `stress` 路线观察高压输入和技能连段

### 6.3 通过标准

- 一个入口里同时看到 selection、orders、HUD、entity info
- entity collection panel 只消费正式 semantic preview contract
- formation / live selection 视图切换不产生第二套 truth
- 统一入口能继续挂接后续 budget / crowd / authority 观测面板

## 7 本轮推荐落地顺序

1. `FormationPhysicsPlaygroundMod`
2. `CapabilityStandardMassNavigationLargeWorld10kMod`
3. `RoadNetworkShowcaseMod`
4. `InteractionShowcaseMod`
5. `RelationshipShowcaseMod`

这个顺序的目的：

- 先把 `UAT-1` 和 `UAT-2` 的实体仿真主线入口定下来
- 再用 road showcase 回归 route / move-plan / MassFlow sink
- 再用 `InteractionShowcaseMod` 做 `UAT-4` 统一入口
- 最后用 `RelationshipShowcaseMod` 补强 artifact-first 的证据产出模板

## 8 禁止事项

- 不新增一个“UAT 专用 launcher”绕开现有 mod 入口
- 不在 showcase 里复制 Core / capability contract
- 不靠硬编码 English copy 让面板看起来“能用”
- 不把 acceptance 变成依赖未注册 attribute 名称的隐式行为测试

## 9 后续实现建议

- `FormationPhysicsPlaygroundMod` 继续补 UAT-1 操作说明与性能记录模板
- `CapabilityStandardMassNavigationLargeWorld10kMod` 继续补 MassFlow 档位记录与统一性能字段
- `RoadNetworkShowcaseMod` 继续承接 route / move-plan / MassFlow sink UAT
- `InteractionShowcaseMod` 作为 UAT-4 主入口，继续收敛 selection / entity-info / HUD 的正式 contract
- `RelationshipShowcaseMod` 作为 artifact-first 模板，承接 AOI / LOD / budget 证据产出
