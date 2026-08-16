# fx-20 配置说明 · 进度完成

> 配置写法与行为。第一性需求见 [fx-20 PRD](../prd/fx-21-progression.md)；编辑器需求见 [UXD](../uxd/fx-21-progression.md)；现状见 [reference](../reference/fx-21-progression.md)。

## 1. 示例配置

真实条目（`mods/showcases/progression_scope/ProgressionScopeShowcaseMod/assets/GAS/effects.json`，显式作用域 + 设级）：

```json
{
  "id": "Effect.Showcase.CompleteCityDrill",
  "presetType": "CompleteProgression",
  "lifetime": "Instant",
  "progression": {
    "id": "Progression.Showcase.CityDrill",
    "scope": "explicit",
    "level": 1
  }
}
```

同族还有 `scope: "self"`、命名作用域与 `delta` 推进写法（CompleteFactionMandate 等）。

## 2. 字段与行为

| 字段 | 这样配会产生什么效果 |
|---|---|
| `id` | 进度注册表中的进度名，须已注册 |
| `scope` | `self`：施法者宿主；`explicit`：显式宿主（效果上下文的 TargetContext）；命名：进度作用域表声明的名字 |
| `level` | 直接设到该等级（正数）；与 `delta` 互斥 |
| `delta` | 推进增量（正数）；与 `level` 互斥 |
| 都不写 | 即"直接完成"该进度 |

块只允许挂在 `presetType: CompleteProgression` + Instant。进度本体（等级阶梯、需求、奖励）属于进度域三张表，见 misc-01；本篇只管"效果如何完成进度"。

## 3. 文件结构

`assets/GAS/effects.json` 效果条目的 `progression` 块；id 引用 `Progression/progressions.json`，命名 scope 引用 `Progression/scopes.json`（见 misc-01）。

## 4. 运行时加载效果

loader 把进度名解析为注册 id、把 scope 解析为作用域键、把 level/delta 编译为等级变更；运行期解析作用域宿主后由进度求值器应用变更。

## 5. 异常处理

| 异常情形 | 系统响应 |
|---|---|
| 非 CompleteProgression 带块 / 缺块 / 非 Instant | 启动失败，指明效果 |
| id 未注册 | 启动失败，指明名字 |
| scope 缺省或未注册 | 启动失败，提示 self/explicit/命名三选 |
| level 与 delta 同写、或任一 <=0 | 启动失败，指明互斥与正数要求 |
| 运行期作用域宿主不可解析 / 状态缓冲缺失 | 抛错并说明前置条件 |

## 6. 实例

- 三作用域示例族：`mods/showcases/progression_scope/ProgressionScopeShowcaseMod/assets/GAS/effects.json`（CityDrill/FactionMandate/ProvinceLogistics）
- 团队科研完成：`mods/showcases/team_research/TeamResearchShowcaseMod/assets/GAS/effects.json`（SignalRelayComplete）

**相关文档**：[fx-20 PRD](../prd/fx-21-progression.md) · 见 misc-01（进度域，第三期）
