---
文档类型: RFC 提案
创建日期: 2026-03-12
维护人: X28技术团队
RFC编号: RFC-0001
状态: Draft
---

# RFC-0001 统一 Launcher CLI 与 Workspace 方案

本提案定义 Ludots 下一代产品级启动体验，目标是让 CLI、Web Launcher 与启动 backend 共用一套真实规则，消除当前多入口、多配置、多扫描语义带来的错误选择、路径歧义和体验分裂。

本提案同时明确三件事：

1. `preferences`、`preset`、`config` 必须分层，不允许混写在同一个文件里。
2. `game.json` 不是 launcher 的产品级配置入口，只是某些适配层可选的运行时 bootstrap artifact。
3. 启动体验的用户心智模型应围绕 selector、preset、adapter 和 launch plan，而不是围绕“改哪个 json 文件”。

## 1 问题与现状

当前启动链路已经有可复用基础，但产品体验仍然分裂。

已存在的正面基础：

* `scripts/run-mod-launcher.ps1` 在 `cli` 分支下已经转发到 `src/Tools/Ludots.Launcher.Cli/Program.cs`
* `src/Tools/Ludots.Editor.Bridge/Program.cs` 与 `src/Tools/Ludots.Launcher.Cli/Program.cs` 已经共用 `src/Tools/Ludots.Launcher.Backend/LauncherService.cs`
* `src/Tools/Ludots.Launcher.Backend/LauncherService.cs` 已经具备平台选择、依赖闭包、`game.json` 写入和启动能力
* `src/Core/Modding/ModDiscovery.cs` 已经有可复用的递归扫描实现

当前的关键缺口：

1. `src/Tools/Ludots.Launcher.Backend/LauncherService.cs` 只扫描 workspace source 的一级子目录，不会递归发现 `mods/fixtures/camera/CameraAcceptanceMod` 这类嵌套 Mod。
2. `src/Tools/Ludots.Launcher.Backend/LauncherConfigService.cs` 默认读取用户 roaming 配置，当前 repo 没有显式的 workspace SSOT，导致 CLI 与 Web 容易被外部历史工作区污染。
3. 当前 backend 只有“扫描目录”语义，没有“显式别名到路径”的绑定语义，无法稳定支持“全局变量名启动”。
4. `src/Tools/Ludots.Launcher.Backend/LauncherService.cs` 中的构建工程解析与 graph compile 逻辑默认把 Mod 视为 `mods/<ModId>`，与任意路径和嵌套路径需求不匹配。
5. 当前配置模型把 workspace、preset、最后选择状态和运行时 bootstrap 语义混在 launcher 配置里，无法稳定区分团队共享规则与用户偏好。
6. `docs/reference/cli_runbook.md` 仍然把已准备废弃的 WPF `ModLauncher` 体验写成 CLI 主线，和当前 `run-mod-launcher` 实际转发到的新 CLI 不一致。
7. 玩家与开发者路径仍不统一：源码 Mod、预编译 Mod、资源 Mod 没有被收束为同一套“准备并启动”的产品语义。

## 2 目标

本 RFC 的目标是：

1. Web Launcher 与 CLI 共用同一个 launcher backend library，不再维护两套启动语义。
2. Mod 可以位于任意路径，不要求位于 `mods/` 根目录下。
3. Mod 可以通过显式配置的全局变量名启动，变量名与路径映射关系必须可审计、可导出、可共享。
4. Mod 依赖闭包自动解析，`mod.json.main`、构建产物和 ref DLL 导出自动处理。
5. 玩家与开发者共用同一套启动命令；差异只体现在是否存在可构建源码，而不是体现在不同工具链。
6. Web Launcher 与 CLI 都可以显式指定使用哪个适配层启动，当前适配层至少包括 `raylib` 与 `web`。
7. `preferences`、`preset`、`config` 三类数据严格分层，分别承担不同职责。
8. `game.json` 降为适配层内部可选产物，而不是用户必须关心的主配置入口。
9. WPF Launcher 不再作为产品主入口，但递归扫描、依赖闭包和可执行启动能力必须在新 backend 中完整继承。

## 3 非目标

本 RFC 不做以下事情：

1. 不改变运行时 ModLoader 的 `mod.json` 合同；若某适配层继续使用 `game.json`，它仍是该适配层与运行时之间的 bootstrap 输入。
2. 不把“外部目录扫描”做成隐式兼容回退；所有额外路径都必须来自显式 workspace 配置。
3. 不保留 WPF Launcher 作为长期兼容主线；它只在迁移期间作为对照实现存在。
4. 不引入新的 Mod 依赖协议；依赖仍以 `mod.json.dependencies` 为唯一来源。

## 4 统一产品体验

### 4.1 统一概念模型

无论是 Web Launcher 还是 CLI，都围绕以下对象工作：

* `Workspace`
  * 一组显式配置的扫描根、别名绑定和适配层配置
* `Binding`
  * 一个用户可见名字到一个真实 Mod 入口的显式映射
* `Preset`
  * 一个命名的启动意图模板，组合 selector、adapter、build mode 和可共享的启动参数
* `Preferences`
  * 一个用户本地状态对象，只保存最近选择、UI 偏好和非共享默认值
* `Selector`
  * 一种启动输入表达式，可来自别名、ModId、路径或预设
* `Adapter`
  * 一个可显式选择的启动目标，例如 `raylib` 或 `web`
* `Launch Plan`
  * 一次启动前经过解析、校验、构建与运行时 bootstrap 准备后得到的确定计划；只有在适配层仍依赖文件输入时，才物化 `game.json` 等 artifact

### 4.2 CLI 体验

CLI 不再要求用户记忆“构建 Mod -> 构建 App -> 写 `game.json` -> 运行”这四段式流水线。产品级体验的主命令应为：

```bash
ludots launch $camera_acceptance --adapter raylib
ludots launch $camera_acceptance --adapter web
```

主线命令建议为：

```bash
ludots launch <selector...> [--adapter <id>] [--build auto|always|never]
ludots build <selector...> [--adapter <id>]
ludots resolve <selector...> [--json]
ludots workspace add-root <path> [--scan recursive]
ludots workspace bind <alias> <path>
ludots workspace list
ludots preset save <name> <selector...> [--adapter <id>]
ludots adapter list
```

其中：

* `$camera_acceptance` 表示一个显式绑定名
* `mod:CameraAcceptanceMod` 表示按 `mod.json.name` 选择
* `path:<external-camera-mod-root>` 表示直接按路径选择
* `preset:camera_acceptance_raylib` 表示按预设选择

默认行为：

* `launch` 自动完成解析、依赖闭包、源码构建、ref 导出、运行时 bootstrap 准备和适配层启动
* `build` 只做到“准备可启动产物”，不启动进程
* `resolve` 输出最终 Mod 闭包、路径、构建状态和冲突信息，供 CI、调试和 Web UI 共用

对用户来说，`launch` 的输出应该是“我将以哪个 adapter 启动哪些 Mod”，而不是“我刚写了哪个 `game.json`”。如果某个适配层需要 `game.json`，那是 backend 内部实现细节。

### 4.3 Web Launcher 体验

Web Launcher 继续通过 `src/Tools/Ludots.Editor.Bridge/Program.cs` 暴露 API，但界面模型需要对齐 CLI：

1. 顶栏显示当前 `Adapter`，与 CLI 的 `--adapter` 完全等价。
2. Workspace 面板拆成两个区块：
   * `Scan Roots`
   * `Bindings`
3. Preset 与 Preferences 拆成不同 UI 区块，不再复用同一份数据结构：
   * `Preset` 是可保存、可共享、可导出的启动模板
   * `Preferences` 是用户本地最近选择与界面偏好
4. Mod 详情区展示：
   * `ModId`
   * 绑定名列表
   * 根路径
   * 依赖闭包
   * 构建模式
   * `main` DLL 与 `ref` DLL 状态
5. Launch 按钮底层调用与 CLI 完全相同的 launch pipeline，而不是前端自己拼命令。
6. 界面应能直接生成当前选择对应的 CLI 命令，确保 Web 与 CLI 的认知模型一致。

### 4.4 玩家与开发者统一体验

同一条 `launch` 命令需要同时适配三类 Mod：

1. 资源型 Mod
   * 没有 `main`
   * 只参与路径解析和依赖闭包
2. 预编译 Mod
   * 有 `main`
   * 无源码工程或无需构建
   * 只验证 DLL 是否存在
3. 源码 Mod
   * 有 `main`
   * 能定位到 `.csproj`
   * 启动前自动构建，并自动导出 `ref/<ModId>.dll`

用户不需要在“玩家模式”和“开发模式”之间切换工具。是否执行构建，只取决于 launcher backend 对当前 Mod 类型与 build mode 的判断。

## 5 统一架构设计

### 5.1 Backend 仍然是唯一业务真相

保留 `src/Tools/Ludots.Launcher.Backend/` 作为唯一 launcher backend，实现继续由 Bridge 与 CLI 共同调用。

建议把当前 `LauncherService` 拆成以下协作组件：

* `LauncherWorkspaceService`
  * 负责加载、合并、保存 workspace 配置
* `LauncherCatalogService`
  * 负责递归扫描、显式绑定注册、冲突检测与 Mod 清单生成
* `LauncherSelectorService`
  * 负责解析 `$alias`、`mod:`、`path:`、`preset:`
* `LauncherResolutionService`
  * 负责依赖闭包、拓扑排序、歧义检测
* `LauncherBuildService`
  * 负责源码工程定位、构建、ref 导出、产物校验
* `LauncherAdapterRegistry`
  * 负责注册 `raylib`、`web` 等适配层
* `LauncherLaunchService`
  * 负责生成 launch plan、按适配层策略准备运行时 bootstrap，并启动适配层

`LauncherService` 本身可以保留，但应收束为 façade，而不是同时承担配置、扫描、构建和启动决策。

### 5.2 配置分层

建议把配置拆成四类文件，而不是继续混在一个 launcher 配置里：

1. repo-local config
   * `launcher.config.json`
   * 跟随仓库提交
   * 保存共享扫描根、共享 bindings、适配层配置、project hint 与共享解析规则
2. repo-local presets
   * `launcher.presets.json`
   * 跟随仓库提交
   * 保存团队共享的命名启动模板
3. user-local config overlay
   * 用户 profile 下的 launcher config overlay
   * 保存本机额外扫描根、私有 bindings 或本机 project hint
4. user-local preferences
   * 用户 profile 下的 preferences
   * 只保存最近 adapter、最近 preset、UI 布局、最近打开工作区等本地偏好

配置解析优先级：

1. CLI 显式 `--config` / `--presets`
2. repo-local `launcher.config.json`
3. repo-local `launcher.presets.json`
4. user-local config overlay

preferences 加载规则：

1. CLI 显式 `--preferences`
2. user-local `preferences.json`

`preferences` 不参与 workspace config、preset 合并，也不参与 Mod 解析、依赖闭包和冲突裁决；它只影响默认选择、最近状态与界面展示。

禁止行为：

* 只有 user-local 配置、没有 repo config SSOT
* 把最近选择状态写回团队共享 preset 文件
* 在没有冲突报告的情况下静默使用第一个扫描到的同名 Mod

### 5.3 Config / Preset / Preferences 模型

建议的 `launcher.config.json` 骨架：

```json
{
  "schemaVersion": 1,
  "scanRoots": [
    {
      "id": "repo_mods",
      "path": "mods",
      "scanMode": "recursive",
      "enabled": true
    },
    {
      "id": "shared_camera",
      "path": "<shared-camera-root>",
      "scanMode": "recursive",
      "enabled": true
    }
  ],
  "bindings": [
    {
      "name": "camera_acceptance",
      "target": {
        "type": "path",
        "value": "mods/fixtures/camera/CameraAcceptanceMod"
      }
    },
    {
      "name": "camera_showcase",
      "target": {
        "type": "modId",
        "value": "CameraShowcaseMod"
      }
    }
  ],
  "adapters": {
    "default": "raylib"
  }
}
```

建议的 `launcher.presets.json` 骨架：

```json
{
  "schemaVersion": 1,
  "presets": [
    {
      "id": "camera_acceptance_raylib",
      "selectors": ["$camera_acceptance"],
      "adapterId": "raylib",
      "buildMode": "auto"
    }
  ]
}
```

建议的 `preferences.json` 骨架：

```json
{
  "schemaVersion": 1,
  "lastPresetId": "camera_acceptance_raylib",
  "lastAdapterId": "raylib",
  "viewMode": "card"
}
```

关键规则：

1. `scanRoots` 负责发现候选 Mod。
2. `bindings` 负责给用户稳定名字，不再依赖扫描顺序。
3. `bindings.name` 是产品级“全局变量名”，在 CLI 中通过 `$name` 使用。
4. `target.type=path` 允许显式绑定任意目录，不要求位于 `mods/` 下。
5. `target.type=modId` 只在 catalog 中唯一时允许解析；多候选必须报冲突。
6. `preset` 只表达启动意图，不保存最近选择和 UI 状态。
7. `preferences` 绝不参与 Mod 解析真相，只影响默认值与展示行为。

### 5.4 递归扫描与冲突策略

递归扫描应直接复用 `src/Core/Modding/ModDiscovery.cs` 的遍历语义，收束到新 backend，而不是继续维护一级目录扫描逻辑。

冲突规则：

1. 同一 `bindings.name` 不可重复。
2. 同一 `path` 可以有多个绑定名，但必须指向同一个实际 Mod 根。
3. 同一 `ModId` 如果来自多个根目录：
   * 若请求来自 `$alias` 或 `path:`，按显式选择处理
   * 若请求来自 `mod:<ModId>`，且 workspace 中没有唯一缺省绑定，则返回歧义错误
4. backend 禁止“扫描顺序优先”的静默选中。

这条规则是为了彻底修复当前“历史 workspace source 污染当前仓库”的问题。

### 5.5 构建、DLL 与 ref 导出

launcher backend 必须把“构建可启动 Mod”定义成稳定流水线，而不是若干散落命令：

1. 解析 `mod.json.main`
2. 决定 Mod 类型：
   * `ResourceOnly`
   * `BinaryOnly`
   * `BuildableSource`
3. 对 `BuildableSource`：
   * 找到真实 `.csproj`
   * 执行 Release build
   * 自动导出 `ref/<ModId>.dll`
   * 校验 `mod.json.main` 指向的 DLL 存在
4. 对 `BinaryOnly`：
   * 不构建
   * 只验证 `main` DLL 存在
5. 对 `ResourceOnly`：
   * 直接通过

工程定位规则：

1. 优先使用显式配置的 `projectPath`
2. 其次使用 Mod 根目录下的 `.csproj`
3. 再其次使用绑定项或 catalog 中声明的 repo-relative project hint
4. 禁止硬编码回退到 `mods/<ModId>/<ModId>.csproj`

### 5.6 依赖闭包

依赖闭包仍然只读 `mod.json.dependencies`，不引入第二套依赖模型。

launcher backend 负责：

1. 从 selector 集合解析出根 Mod
2. 按 `dependencies` 递归收集 closure
3. 生成稳定拓扑序
4. 为 runtime bootstrap 输出稳定的 `ModPaths`

运行时不再推测用户意图；用户意图应完全体现在 launch plan 中。

### 5.7 适配层注册与启动

当前 `LauncherPlatformProfile` 已经具备适配层雏形，但产品语义应改名为 `AdapterProfile`，并通过注册表驱动。

建议接口：

```csharp
public interface ILauncherAdapter
{
    string Id { get; }
    Task<AdapterBuildResult> BuildAsync(LaunchContext context, CancellationToken ct);
    Task<RuntimeBootstrapResult> PrepareRuntimeAsync(LaunchContext context, CancellationToken ct);
    Task<LaunchResult> LaunchAsync(LaunchContext context, CancellationToken ct);
}
```

当前至少保留两个适配层：

* `raylib`
  * 构建 `src/Apps/Raylib/Ludots.App.Raylib/Ludots.App.Raylib.csproj`
  * 若运行时仍要求 bootstrap 文件，则由 adapter 写入可选的 `game.json`
  * 启动 `Ludots.App.Raylib`
* `web`
  * 构建 `src/Apps/Web/Ludots.App.Web/Ludots.App.Web.csproj`
  * 准备前端产物
  * 若 Web runtime 需要 bootstrap 文件，则由 adapter 自行决定是否物化 `game.json`
  * 启动 `Ludots.App.Web` 或返回已运行实例 URL

CLI 与 Web Launcher 都必须显式传入 `adapterId`，不再把平台选择埋在不同命令里。

### 5.8 Runtime Bootstrap Artifact

`game.json` 应从“用户直接编辑的启动配置”降级为“adapter 可选生成的 bootstrap artifact”。

规则：

1. launch plan 才是 launcher 的产品级真相。
2. adapter 若能直接把 launch plan 传给 app host，则可以完全不生成 `game.json`。
3. adapter 若暂时受限于现有 app host 合同，可以在启动前临时物化 `game.json`。
4. 这个 artifact 只包含运行时 bootstrap 必需信息，例如 `ModPaths`，不承载以下内容：
   * 用户 preferences
   * launcher presets
   * workspace bindings
   * 最近选择状态
5. `game.json` 的存在与否，不应影响 CLI 与 Web Launcher 的主体验，也不应成为用户理解启动链路的前提。

### 5.9 Launch Plan 是最终审计对象

每次启动前 backend 都应该生成一个可审计的 launch plan，至少包含：

* adapter
* selectors
* resolved root mods
* dependency closure
* final ordered mod list
* per-mod root path
* per-mod build mode
* per-mod project path
* per-mod main DLL path
* target bootstrap artifact strategy
* target bootstrap artifact path（如果需要物化）
* target app output directory

CLI 的 `resolve --json` 与 Web Launcher 的“启动前预览”都直接消费同一个 launch plan。

## 6 建议命令与 API

### 6.1 CLI 命令面

建议 CLI 主命令收束为：

```bash
ludots launch <selector...> --adapter raylib
ludots launch preset:camera_acceptance_raylib
ludots resolve $camera_acceptance --json
ludots build $camera_acceptance --build always
ludots workspace bind camera_acceptance mods/fixtures/camera/CameraAcceptanceMod
```

建议保留但降级为调试命令：

```bash
ludots internal write-gamejson <selector...> --adapter raylib
ludots internal export-sdk
```

这样用户只记住 `launch`、`build`、`resolve`、`workspace` 四类主命令。

### 6.2 Bridge API

建议保留 `/api/launcher/state`，并新增或收束为：

* `POST /api/launcher/resolve`
* `POST /api/launcher/build`
* `POST /api/launcher/launch`
* `GET /api/launcher/workspace`
* `GET /api/launcher/config`
* `GET /api/launcher/presets`
* `GET /api/launcher/preferences`
* `POST /api/launcher/config/roots`
* `POST /api/launcher/config/bindings`
* `DELETE /api/launcher/config/bindings/{name}`
* `POST /api/launcher/preferences`
* `GET /api/launcher/adapters`

React 前端不直接拼构建细节，所有业务动作都经由这些 backend API。

## 7 迁移计划

### 7.1 Phase 1: 收束 backend

1. 在 `src/Tools/Ludots.Launcher.Backend/` 内引入递归 catalog 服务。
2. 引入 repo-local `launcher.config.json` 与 `launcher.presets.json`。
3. 引入 user-local config overlay 与 `preferences.json`。
4. 把 `$alias` 与 `path:` selector 解析收口到 backend。
5. 修复 project path 与 graph compile 的硬编码假设。

### 7.2 Phase 2: 收束 CLI

1. 让 `src/Tools/Ludots.Launcher.Cli/Program.cs` 变成新的产品 CLI 主入口。
2. 引入 `launch/build/resolve/workspace` 命令面。
3. 让 CLI 直接输出 launch plan、build summary 与错误诊断。

### 7.3 Phase 3: 收束 Web Launcher

1. Bridge API 切到新的 config / presets / preferences 分层模型。
2. React UI 增加 bindings 管理、launch preview 和 adapter 明示选择。
3. 所有启动动作与 CLI 共享 launch plan。

### 7.4 Phase 4: 废弃 WPF Launcher

1. `src/Tools/ModLauncher/` 标记为 deprecated。
2. `scripts/run-mod-launcher.*` 切到新 CLI 命令面，或由更通用的 `scripts/launcher.*` 包装。
3. 当新 CLI 对递归扫描、别名绑定、依赖闭包和 ref 导出全部覆盖后，移除 WPF 主路径文档。

## 8 验收标准

方案落地后，至少需要满足以下验收：

1. `CameraAcceptanceMod` 位于 `mods/fixtures/camera/CameraAcceptanceMod` 时，`ludots launch $camera_acceptance --adapter raylib` 可以直接启动。
2. 同一台机器存在多个同名 `CameraAcceptanceMod` 时，`mod:CameraAcceptanceMod` 会返回歧义，而 `$camera_acceptance` 会稳定解析到显式绑定路径。
3. 对有源码的 Mod，`launch --build auto` 会自动构建并导出 `ref/<ModId>.dll`。
4. 对只有 DLL 的 Mod，`launch --build auto` 不会要求用户补 `.csproj`。
5. Web Launcher 与 CLI 对同一 selector 集合生成的 launch plan 完全一致。
6. `raylib` 与 `web` 都可以通过相同的 selector 集和相同的 backend pipeline 启动。
7. 删除 `preferences.json` 不会改变同一 preset 的解析结果，只会影响默认选择与 UI 状态。
8. adapter 在不需要 bootstrap 文件时可以直接启动，不强制生成 `game.json`。

## 9 风险与取舍

### 9.1 风险

* 当前 Bridge 内还保留了一些自己的 Mod 发现与编辑上下文逻辑，迁移时需要避免出现“展示用 catalog”和“启动用 catalog”双真相。
* 旧 CLI 与新 CLI 同时存在一段时间，文档与脚本入口必须明确谁是 canonical。
* `path:` 选择会把外部目录带入 workspace，必须做好冲突报告与路径存在性验证。

### 9.2 取舍

* 选择“显式绑定优先”而不是“扫描到谁用谁”，因为产品稳定性比零配置更重要。
* 选择 repo-local workspace SSOT + user overlay，而不是只靠用户 roaming 配置，因为团队协作需要可提交、可审计的共享配置。
* 选择统一 `launch` 主命令，而不是继续暴露 `gamejson write` 给普通用户，因为产品主线应该表达用户意图，而不是实现步骤。

## 10 相关文档

* 启动顺序与入口：见 [../architecture/startup_entrypoints.md](../architecture/startup_entrypoints.md)
* Mod 架构与配置系统：见 [../architecture/mod_architecture.md](../architecture/mod_architecture.md)
* Mod 运行时唯一真相：见 [../architecture/mod_runtime_single_source_of_truth.md](../architecture/mod_runtime_single_source_of_truth.md)
* CLI 运行与 Launcher 手册：见 [../reference/cli_runbook.md](../reference/cli_runbook.md)
* `run-mod-launcher` 当前入口：见 `scripts/run-mod-launcher.ps1`
* 当前 launcher backend：见 `src/Tools/Ludots.Launcher.Backend/LauncherService.cs`
* 当前 Bridge 入口：见 `src/Tools/Ludots.Editor.Bridge/Program.cs`
* 当前递归 Mod 发现实现：见 `src/Core/Modding/ModDiscovery.cs`
