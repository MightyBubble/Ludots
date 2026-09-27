# EQS + Influence Map：配置契约与可视化 Showcase

## 1. 概述

给 AI「找落点」和「看威胁场」两套能力提供**数据驱动配置**，并用正式 Presentation 管线做可读演示：

- 威胁/机会场画成全图热力（`GlobalFieldVisualKind.Influence`）
- EQS 候选点与最优落点画成地面环/圆（`GroundOverlayBuffer`）
- GitHub Pages 提供同场景交互预览页，配置结构一目了然

**不做**：平行渲染器、配置静默缺省、在热路径堆分配。

## 2. 结构

```
Configs/Spatial/
  influence_fields.json   # 命名影响力场 + 投影源
  eqs_queries.json        # 命名 EQS 查询（生成→打分→挑选）
  eqs_scenarios.json      # 演示/验收场景（绑查询 + 呈现）

Presentation
  InfluenceFieldRegistry ──▶ InfluenceGlobalFieldVisualProjector ──▶ GlobalFieldVisualBuffer
  EqsQuery 结果 ──────────▶ GroundOverlayBuffer（候选 / 最优）

Pages
  docs/eqs-influence.html  # 交互预览（读取同一份 demo JSON）
```

复用：`ChunkedField2D`、`IEqsGenerator`/`IEqsTest`、`GlobalFieldVisualBuffer`、`GroundOverlayBuffer`、`ConfigCatalog`/`ConfigPipeline`。

## 3. 详情

### 3.1 `influence_fields.json`

| 字段 | 含义 |
|------|------|
| `id` | 场名（threat / opportunity） |
| `cellSizeCm` | 密度 |
| `chunkSizeCells` | 分块 |
| `sources[]` | 投影源：`xCm,yCm,radiusCm,peak,falloff` |

`falloff` ∈ `Constant|Linear|Quadratic`。未知枚举硬失败。

### 3.2 `eqs_queries.json`

| 字段 | 含义 |
|------|------|
| `id` | 查询名 |
| `generator` | `{ kind, ... }`：`Grid|Ring|Donut|Circle` |
| `tests[]` | `Distance|Influence|Overlap` |
| `selection` | `{ kind: Best|TopN|AboveThreshold, ... }` |

缺依赖（Influence 场未注册 / Overlap 无空间服务）硬失败。

### 3.3 `eqs_scenarios.json`

| 字段 | 含义 |
|------|------|
| `id` | 场景名 |
| `origin` | EQS 原点 |
| `queryId` | 引用 `eqs_queries.json` |
| `influenceFieldIds` | 本场景启用的场 |
| `presentation` | `influenceFieldId`、是否画候选/最优、`normalizePeak` |

### 3.4 Presentation 合同

- Influence：量化到 byte（相对 `normalizePeak`）写入 `GlobalFieldVisualKind.Influence`，Raylib 用威胁热力调色；禁止 per-cell GroundOverlay 当生产热力。
- EQS：候选 → 半透明 Circle（分越高越亮）；最优 → Ring；威胁半径可画辅助 Ring。
- Decay / Scale：SoA 就地乘，0-alloc。

## 4. 场景

**避威胁接近目标（默认 demo）**

1. 演员在原点，目标在前方，威胁挡在直线上。
2. 威胁场投影后，直线路径发红。
3. EQS 在环形候选上打分：靠近目标 + 远离威胁。
4. 最优落点偏出威胁线，玩家能「一眼看懂为什么绕开」。

## 5. 边界

- Core `Fields.Influence` / `Spatial.Eqs` **不引用** Presentation / Raylib。
- Projector 放在 `Core.Presentation.Fields`。
- `AI/inputs.json` 的 `InfluenceSample01` 仍须 registry 注入后才可作者；本契约先服务 Spatial 配置与 showcase。
- Web adapter 暂无 GlobalField 段：Pages 用 Canvas 预览；可玩验收以 Raylib preset 为准。
- Stamp 径向仍含 float/`Sqrt`（定点近似 Future）。

## 6. UAT（Cucumber）

```gherkin
Feature: 避威胁落点可视化
  作为一名刚上手的关卡/AI 作者
  我想用配置描述威胁场和选点规则，并在画面上看到热力与候选
  以便确认 AI 会绕开危险而不是直冲目标

  Scenario: 加载配置后威胁场可投影
    Given 场景配置声明威胁场 peak 为 10、半径 200cm
    When 系统按 sources 投影影响力场
    Then 威胁中心附近采样值大于 8
    And Presentation 缓冲区出现 Influence 种类的场记录

  Scenario: EQS 选出偏出威胁线的落点
    Given 环形候选围绕演员、目标在威胁另一侧
    When 运行查询 avoid_threat_near_goal
    Then 最优候选的威胁采样小于 3
    And 最优候选的横向偏移绝对值大于 50cm
    And 地面叠加绘制了候选圆与最优环

  Scenario: 缺场配置硬失败
    Given 某 Influence 测试引用未注册场 "missing"
    When 加载或运行该查询
    Then 系统抛出明确错误且不产生静默零分

  Scenario: Pages 预览与配置同源
    Given 打开 eqs-influence 门户页
    When 页面载入 demo JSON
    Then 画布显示威胁热力与环形候选
    And 高亮最优落点不在威胁直线上
```
