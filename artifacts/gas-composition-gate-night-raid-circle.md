## GAS Composition Gate — Self Review

- **Task**: Make the `raid_circle` region visible in the night raid showcase.
- **Date**: 2026-08-24
- **Agent / Author**: Codex

### 1. Core judgment

新变体主要交付物是（A/B/C/D）: **A（现有表现能力组合）**

结论: **PASS**

理由: 复用现有 `GroundOverlay/Ring` presenter 资产，为地图区域增加一个静态数据实体；不新增 GAS op、spawn handler、profile enum、preset 开关或平行物化管线。

### 2. Layer assignment

| 步骤/能力 | Layer | 实现载体 |
|---|---:|---|
| 区域视觉锚点实体 | 2 | `assets/Entities/templates.json` + `assets/Maps/night_raid.json` |
| 金色环形表现 | 2 | 现有 `GroundOverlay` / `Ring` presenter 组合 |

### 3. Reuse list

- Handlers / lifecycle: 无新增；地图静态实体仍由现有 MapConfig 物化链创建。
- Presentation: 复用 `GroundOverlay` 的 `Ring` 资产、Grounding 行为和 presenter 事件规则。
- Gameplay graph: `Graph.NightRaid.Flow` 与 `raid_circle` 触发合同保持不变。

### 4. New Layer 0 ops

N/A。

### 5. Transaction boundary

无新的 gameplay 事务；地图加载失败时沿用现有 fail-closed 配置加载行为。

### 6. Config SSOT

行为仍在现有地图/实体/presenter JSON；没有新增 JSON schema。

### 7. Red flag scan

- [x] 未新增 profile inherit/placement enum
- [x] 未新建与 spawn 平行的物化管线
- [x] 未把 placement 校验塞进 lifecycle op
- [x] 未添加 fallback

### 8. Next variant test

下一个区域视觉变体只修改实体模板、地图实例或 presenter 参数，不增加 Core enum。
