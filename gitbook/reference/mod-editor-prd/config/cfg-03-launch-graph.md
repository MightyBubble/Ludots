# cfg-03 配置说明 · 启动计划

> 配置写法与行为。第一性需求见 [cfg-03 PRD](../prd/cfg-03-launch-graph.md)；编辑器需求见 [UXD](../uxd/cfg-03-launch-graph.md)；现状见 [reference](../reference/cfg-03-launch-graph.md)。

## 1. 示例配置

四份配置各一段真实示例，从输入到产物：

**绑定表**（仓库根 `launcher.config.json`）——"哪些目录有 mod、别名指向谁"：

```json
{
  "scanRoots": [ { "id": "repo_mods", "path": "mods", "scanMode": "recursive", "enabled": true } ],
  "bindings": [ { "name": "camera_acceptance",
    "target": { "type": "path", "value": "mods/fixtures/camera/CameraAcceptanceMod",
                 "projectPath": "CameraAcceptanceMod.csproj" } } ]
}
```

**预设表**（仓库根 `launcher.presets.json`）：

```json
{ "schemaVersion": 1, "presets": [
  { "id": "camera_acceptance_raylib", "name": "Camera Acceptance Raylib",
    "selectors": ["$camera_acceptance"], "adapterId": "raylib", "buildMode": "auto" } ] }
```

**启动计划**（启动器生成）与**锚文件**（可执行文件旁）见 UXD 预览与合并链路；字段效果如下。

## 2. 字段与行为

**绑定表**：

| 字段 | 这样配会产生什么效果 |
|---|---|
| `scanRoots[]` | 启动器发现 mod 的扫描根；不在其中且无绑定的 mod 进不了计划 |
| `bindings[].name / target` | 绑定别名（`$名字` 选择器的展开目标）与指向 |

**预设表**：

| 字段 | 这样配会产生什么效果 |
|---|---|
| `presets[].selectors` | 一键展开的选择器组（可含 `$绑定` 与 `preset:其他预设` 递归） |
| `presets[].adapterId / buildMode` | 平台壳（桌面/浏览器）与构建档 |

**计划**（生成物，只读）：`orderedModIds`/`plannedMods` 为顺序唯一事实来源；`plannedMods[].rootPath`/`mainAssemblyPath` 逐 mod 定位；`planFingerprint` 为三重一致性凭据。

## 3. 文件结构

| 配置 | 位置 | 谁维护 |
|---|---|---|
| 绑定表 / 预设表 | 仓库根 | 作者 |
| 完整计划 | `artifacts/launcher/` 与应用构建输出 | 启动器生成 |
| 锚文件 | 可执行文件旁 | 启动器生成 |
| 手写最小变体 | 壳工程源码目录 | 开发者，仅供直启调试 |

## 4. 启动入口

| 入口 | 用法 |
|---|---|
| 启动器 CLI（推荐） | `scripts/run-mod-launcher.cmd` 加 `cli launch --preset <预设id>`（或 `preset:<id>`） |
| 启动器 GUI | `scripts/run-launcher.ps1` |
| 应用直接启动 | 运行壳可执行文件（读旁锚） |
| 调试直启 | 宿主显式传 mod 路径，无计划——非产品语义 |

## 5. 异常处理

| 异常情形 | 系统响应 |
|---|---|
| 找不到锚文件 | 启动失败 |
| 计划与锚不一致 | 拒绝启动（陈旧计划保护） |
| 计划内目录不存在 | 启动失败 |
| 选择器指向不存在的绑定/根 | 生成期报错，指明选择器 |

## 6. 实例

- 绑定与预设正本：`launcher.config.json`、`launcher.presets.json`
- 生成计划（构建产物，不入版本控制）：`artifacts/launcher` 目录下的 `raylib.launch.graph.json`；锚：Ludots.App.Raylib 构建输出目录（bin 下的 Release/net9.0）中的 `launcher.runtime.json`
- CLI 全集：`gitbook/reference/cli-runbook.md`

**相关文档**：[cfg-03 PRD](../prd/cfg-03-launch-graph.md) · [cfg-01 配置说明](cfg-01-mod-manifest.md) · [cfg-05 配置说明](cfg-05-config-pipeline.md)
