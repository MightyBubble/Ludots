# 10K MassNavigation showcase 数据化治理 TODO

**状态**：待评审。本文只登记"要做什么、怎么验"，不含结论性断言。
**背景**：`CapabilityStandardMassNavigationLargeWorld10kMod` 用 396 行 C# 承担本应是数据的职责，
并与作者侧权威模型（`ludotsJS` 的 map instance / team / player / representative entity）重复。
详见 `docs/analysis/massnav10k-mod-production-readiness.md`。

---

## 0. 权威源声明（不可协商）

| 概念 | 唯一真源 | 现状问题 |
|---|---|---|
| 地图实例 | `MapConfig`（`entities[].instance_id / template / position`） | 10K 地图是 10 行空壳，零实体 |
| 队伍 | `MapConfig.teams[].team_id + representative_instance_id` | showcase 另用 `Scenario.Teams` |
| 玩家 | `MapConfig.players[].player_id + team_id + representative_instance_id` | showcase 另用 `Scenario.AgentsPerTeam` |
| 单位归属 | `PlayerOwner` 组件（`ParticipantBindingResolver` 据此建 `Owns(rep→unit)` 边） | 程序化 bootstrap 生成 |
| 可见性 | 核心 `VisionSystem` + `Vision/fog_layers.json` + `VisionEmitterCm` 组件 | showcase 手搓 + `PresentationAudienceRevealHidden` 后门 |

`ludotsJS/src/components/map/mapConfigFieldGuide.js` 与 `participant/participantModel.js`
是 authoring 权威描述，C# `MapConfig` 字段与其逐一对齐（已核对）。

---

## 1. 已确证的事实（实测过，可直接引用）

- [x] **那 209 行不是可有可无的**。屏蔽 `MassNavigationObserverVisibilityBindingSystem`
      + `PresentationAudienceRevealHidden` 后：`worldHud` 20000 → **0**，HUD 亮像素 75502 → **239**。
      HUD 走 `WorldHudPresentBehavior` → `KnowledgeProjectionConsumer.TryResolveForViewer`，
      查的就是这 209 行写的 knowledge。
- [x] **核心 Vision 引擎存在且是生产级**：`src/Core/Vision/` 2645 行，
      11 个 showcase（fog_of_war / fog_vision_decay / multi_layer / vision_cone / line_of_sight /
      explored_memory / gap_generator / stealth_detection / shared_vision_snapshot），
      2 个 CI gate 验收测试。架构守卫测试强制 `GameEngine` 必须驱动 `VisionSystem`。
- [x] **核心 Vision 装配是数据驱动的**：`GameEngine:946` 走
      `VisionFogLayerConfigLoader.Load(ConfigCatalog, ...)`，`VisionSystem` 无条件注册在 `PostMovement`。
- [x] **视野发射器可在实体配置里写**：`ComponentRegistry:101` 注册 `VisionEmitterCm`，
      含 `scope` 等属性，无需 C#。
- [x] **`presentation.teams` 死结**：`MassNavigationConfig.cs:695` 校验器禁止外部 authoring 时非空，
      但运行时 `GetTeam()` / `ResolveAgentPresenterId` 需要它 —— 而这两个 API **全仓只被
      两个重复的 C# 可见性系统调用**，authored 路径根本不需要。

---

## 2. 待验证假设（**未实测，不要当结论用**）

- [ ] **A1**：给 10K 场景加 `Vision/fog_layers.json` + 单位模板挂 `VisionEmitterCm` 后，
      核心 `VisionSystem` 能自动产出 knowledge，使 HUD/minimap 正常点亮，**无需那 209 行**。
      *风险*：`VisionSystem` 的 viewer 解析走 `PlayerOwnedEntityObserverResolver`（按 `PlayerOwner` 找），
      authored 路径能否满足其前置条件未验证。若需要玩家代表实体持有额外组件，需一并数据化。
- [ ] **A2**：`PresentationAudienceRevealHidden` 后门删除后，
      fog 家族 showcase 的既有验收是否仍通过（即该后门是否被别处依赖）。
- [ ] **A3**：11 个 fog showcase 是否真有人肉眼验收过
      （registry 中 `artifactDir: null` / `screenshot: null`，notes 写"截图证据待 CI 验收产物补录"）。

---

## 3. 治理 TODO（按依赖顺序）

### T1 — 打通 authoring 死结（前置，阻塞 T2/T3）
- [ ] 移除 `GetTeam()` / `ResolveAgentPresenterId` 对 `presentation.teams` 的运行时依赖，
      或改由 map 拓扑提供；使 `MassNavigationConfig.cs:695` 的校验器不再与运行时冲突。
- [ ] 验收：authored 地图（`autoSpawnConfiguredScenario: false` + `presentation.teams` 为空）能启动，
      10K 单位满员，无异常。

### T2 — 10K 场景转为 map instance authoring
- [ ] 由 map instance 承载 10006 实体（4 队 × 2500 + 4 blocker + 4 代表实体），
      位置写死在文件里（离线生成，`WorldPositionCm` 必须 Int32）。
- [ ] 单位模板挂 `PlayerOwner`（`PlayerId`）+ `Team` + `VisionEmitterCm`；
      `ParticipantBindingResolver` 自动建 `Owns(rep→unit)` 边。
- [ ] 障碍用 `mass_navigation_blocker` 模板 + `ManifestationObstacleIntent2D`
      （`sinkNavigationObstacle: true`, `navRadiusCm`）。
- [ ] presenter 靠 `presenters.json` 的 `EntitySpawned` + `key = <template id>` 规则自动挂，禁止 C#。
- [ ] 验收：实机 `visibleEntities=10006`、`worldHud=20000`，四队颜色可量测。

### T3 — 删掉野鸡代码，改用核心 Vision
- [ ] 删除 `Systems/MassNavigationObserverVisibilityBindingSystem.cs`（209 行）。
- [ ] 删除 `Systems/MassNavigationLargeWorldLocalOrderSourceSystem.cs`（80 行，先确认输入映射能否数据化）。
- [ ] 删除 `PresentationAudienceRevealHidden` 后门用法。
- [ ] 删除 `MapFocus.cs`（19 行，"是不是启动图"判断应数据化）。
- [ ] `ModEntry.cs` 只保留 mod 生命周期，不注册任何 system。
- [ ] 验收：T2 的指标在零 C# 可见性代码下仍成立（= A1 被证实），
      且迷雾行为与 `fog_of_war` 家族一致（视线遮挡、分层、记忆 TTL）。

### T4 — 回填缺失的验收证据
- [ ] 为 fog 家族 11 个 showcase 补截图/artifact 证据（对应 A3）。
- [ ] 10K 场景补"带迷雾"的视觉验收：可见区/记忆区/未知区三态可辨。

---

## 4. 明确不做

- 不为 10K 场景新增任何并行拓扑字段（队伍/玩家/归属一律读 `MapConfig`）。
- 不用 `PresentationAudienceRevealHidden` 之类的"全显示后门"绕过视角系统。
- 不在 showcase 里重复实现核心已有能力（`AGENTS.md` §3 防重复造轮子）。
- 不为"让旧写法继续跑"保留兼容层。

---

## 5. 验收口径（每项治理条目的通用门槛）

1. **零 C# 判据**：相关能力在 showcase 里无对应 `.cs` 实现。
2. **运行实证**：实机跑出的计数器/截图，不靠推理。
3. **A/B 干净基线**：与改动前同一 launcher、同一窗口尺寸对比。
4. **测试基线不降**：PresentationTests / ThreeCTests 的既有失败集合不扩大。
