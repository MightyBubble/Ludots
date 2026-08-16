# cfg-03 · 启动计划

> 产品承诺 · 已冻结。理想实现见 [cfg-03 spec](../spec-runtime/cfg-03-launch-graph.md)；现状见 [cfg-03 reference](../reference/cfg-03-launch-graph.md)。

## 1. 定位

启动一个游戏实例 = 选一组 mod + 定一个顺序 + 选一个平台适配器。围绕这件事有四份配置：**绑定表、预设表、启动计划、锚文件**，由启动器工具链生成和维护，作者只动前两份。理解这条链，就理解"我的 mod 最终怎么被组装进游戏"。

## 2. 示例配置

四份配置各一段真实示例，从输入到产物：

**绑定表**（`launcher.config.json`，仓库根）——声明"哪些目录里有 mod"和"绑定别名指向哪个 mod"：

```json
{
  "scanRoots": [ { "id": "repo_mods", "path": "mods", "scanMode": "recursive", "enabled": true } ],
  "bindings": [
    { "name": "boom_beach_like",
      "target": { "type": "path", "value": "mods/showcases/boom_beach_like/BoomBeachLikeShowcaseMod" } }
  ]
}
```

**预设表**（`launcher.presets.json`，仓库根）——把选择器组合成一键启动项：

```json
{ "schemaVersion": 1, "presets": [
  { "id": "camera_acceptance_raylib", "name": "Camera Acceptance Raylib",
    "selectors": ["$camera_acceptance"], "adapterId": "raylib", "buildMode": "auto" } ] }
```

**启动计划**（启动器生成，如 `artifacts/launcher/raylib.launch.graph.json`）——闭包解析与指纹的结果：

```json
{
  "schemaVersion": 1,
  "planFingerprint": "1fa0f1b3…4f64c46",
  "adapter": { "id": "raylib", "hostKind": "desktop", "buildPipeline": "dotnet",
               "runtimeBootstrapFileName": "launcher.runtime.json" },
  "selectors": [ "$boom_beach_like" ],
  "rootModIds": [ "BoomBeachLikeShowcaseMod" ],
  "orderedModIds": [ "LudotsCoreMod", "CoreInputMod", "EntityCommandPanelMod", "BoomBeachLikeShowcaseMod" ],
  "plannedMods": [ { "id": "LudotsCoreMod", "rootPath": "C:/…/mods/LudotsCoreMod",
                     "mainAssemblyPath": "C:/…/bin/net8.0/LudotsCoreMod.dll", "kind": 2, "buildState": 4 } ]
}
```

**锚文件**（可执行文件旁的 `launcher.runtime.json`，启动器生成）——计划的定位与校验凭据：

```json
{
  "LaunchGraphPath": "../../../../../../../artifacts/launcher/raylib.launch.graph.json",
  "PlanSelectors": [ "$boom_beach_like" ],
  "PlanRootModIds": [ "BoomBeachLikeShowcaseMod" ],
  "PlanOrderedModIds": [ "LudotsCoreMod", "CoreInputMod", "EntityCommandPanelMod", "BoomBeachLikeShowcaseMod" ],
  "PlanFingerprint": "1fa0f1b3…4f64c46"
}
```

读法：绑定表说"有什么、叫什么名"，预设说"一键启动选哪些"，计划说"这次到底装哪些 mod 什么顺序"，锚说"本次进程该用哪份计划"。

## 3. 字段与效果

**绑定表**：

| 字段 | 这样配会产生什么效果 |
|---|---|
| `scanRoots[]` | 启动器发现 mod 的扫描根（可多根、递归）；mod 不在其中且无绑定则进不了计划 |
| `bindings[].name` | 绑定别名——`$名字` 形式选择器的展开目标 |
| `bindings[].target` | 指向 mod 目录（或工程），预设与计划经它定位 |

**预设表**：

| 字段 | 这样配会产生什么效果 |
|---|---|
| `presets[].selectors` | 一键展开的选择器组（可含 `$绑定` 与 `preset:其他预设` 递归） |
| `presets[].adapterId` | 平台壳：桌面（raylib）或浏览器（web） |
| `presets[].buildMode` | 构建档位（auto 等） |

**启动计划**（生成物，作者只读）：

| 字段 | 效果 |
|---|---|
| `orderedModIds` / `plannedMods` | 加载顺序的唯一事实来源：依赖闭包烘焙（依赖按键名字母序，同组依赖永远同顺序）；无运行期平局决胜 |
| `plannedMods[].rootPath` / `mainAssemblyPath` | 每个 mod 的目录与代码入口，运行期按此加载、不再扫描 |
| `planFingerprint` | 整份计划摘要；计划、锚、实际加载三者必须一致，旧计划拒绝启动 |
| `adapter` / `buildMode` | 平台壳与构建信息 |

## 4. 文件结构

| 配置 | 位置 | 谁维护 |
|---|---|---|
| 绑定表 `launcher.config.json` | 仓库根 | 作者/项目 |
| 预设表 `launcher.presets.json` | 仓库根 | 作者/项目 |
| 完整启动计划 | `artifacts/launcher/<适配器>.launch.graph.json`，并复制到应用构建输出 | 启动器生成 |
| 锚文件 `launcher.runtime.json` | 可执行文件旁（应用构建输出目录） | 启动器生成 |
| 手写最小计划变体 | 各平台壳工程源码目录（如 `src/Apps/Raylib/Ludots.App.Raylib/raylib.launch.graph.json`） | 开发者，仅供直启调试 |

计划不进 mod 目录——mod 里没有参与计划的字段，计划是生成期从各 mod 清单推导的视图。

## 5. 启动入口与工具链

| 入口 | 用法示例 | 说明 |
|---|---|---|
| 启动器 CLI（推荐入口） | `scripts/run-mod-launcher.cmd` 加参数 `cli launch --preset entity_command_panel_raylib` | 等价 selector 形式 `cli launch 'preset:xxx'`；解析预设 → 构建依赖 → 生成计划与锚 → 拉起应用 |
| 启动器 GUI | `scripts/run-launcher.ps1` | 起本地服务与启动器界面（浏览器访问），在界面上选预设/绑定启动 |
| 应用直接启动 | 运行桌面壳可执行文件 | 读 exe 旁锚 → 定位计划 → 校验后按计划装配 |
| 调试直启（无计划） | 宿主显式传入 mod 路径列表 | 跳过计划，引擎就地扫描解析依赖——调试/无头专用，非产品语义 |

各 showcase 仓库里还有历史遗留的手动脚本（绕过启动器逐个构建再启动），常规验收一律走 preset 入口。

## 6. 运行时加载效果

**生成侧**（构建/启动时）：扫描根发现 mod → 展开选择器（绑定别名、预设递归）→ 从根集合闭包解析（确定性顺序）→ 计算指纹 → 写出计划与锚。

**启动侧**（进程起来后）：读 exe 旁锚 → 定位计划 → 新鲜度三重校验（产物自指、锚与计划逐项一致、适配器一致）→ 通过后按计划顺序逐 mod 装配：目录挂载进文件系统（cfg-02）、`main` 的 DLL 加载并调用入口（cfg-01）→ 全部配置按此顺序合并（cfg-05）。顺序在这一刻消费完毕，运行期不再有顺序概念。

## 7. 预期反馈

- **启动期**：装配日志按计划顺序逐 mod 输出；任何不一致在装配前被拦下。
- **运行期**：覆盖胜负可从计划直接读出——两个无依赖关系的 mod，计划中靠后者赢。
- **编辑器内**：mod 组合预览即计划依赖图的可视化，勾选根 mod 即见闭包与顺序。

## 8. 异常处理

| 异常情形 | 系统响应 |
|---|---|
| 可执行文件旁找不到锚文件 | 启动失败 |
| 计划与锚的指纹、顺序、适配器不一致 | 拒绝启动（陈旧计划保护） |
| 计划中某 mod 的目录已不存在 | 启动失败 |
| 选择器指向不存在的绑定或根 mod | 生成期报错，指明选择器 |
| 预设引用的绑定缺失 | 启动器报错，指明预设与绑定名 |

## 9. 编辑器要点

- **组合预览**：勾选根 mod 即见依赖闭包与最终顺序，以 DAG 呈现；环与缺失在图上直接可见。
- **启动入口**：编辑器的运行按钮走同一条计划生成链路，保证编辑器里跑的就是计划里写的。
- **预设管理**：预设与绑定在编辑器中一等可编辑（对应绑定表与预设表两份文件），保存规范化。
- 热应用级别：不适用——计划在进程外生成、进程启动时消费。

## 10. 实例

- 绑定与预设正本：`launcher.config.json`、`launcher.presets.json`（仓库根）
- 生成的完整计划：`artifacts/launcher/raylib.launch.graph.json`
- 真实锚文件：`src/Apps/Raylib/Ludots.App.Raylib/bin/Release/net8.0/launcher.runtime.json`
- CLI 用法全集：`gitbook/reference/cli-runbook.md`
- 手写最小计划变体：`src/Apps/Raylib/Ludots.App.Raylib/raylib.launch.graph.json`（浏览器壳在 `src/Apps/Web/Ludots.App.Web/web.launch.graph.json`）

**相关文档**：[cfg-03 spec](../spec-runtime/cfg-03-launch-graph.md) · [cfg-03 reference](../reference/cfg-03-launch-graph.md) · [cfg-01](cfg-01-mod-manifest.md)（依赖声明如何决定闭包）· [cfg-05](cfg-05-config-pipeline.md)（计划顺序如何决定合并胜负）
