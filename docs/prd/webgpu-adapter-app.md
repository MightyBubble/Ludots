# WebGPU 适配 App PRD

## 1. 概述

Ludots 需要一条由 C# 编写的 WebGPU 原生适配器路径，让同一套 Core gameplay、ECS、Presentation、UI Surface 与输入管线，可以在不依赖 Raylib 渲染后端、不走浏览器 WebSocket 串流的情况下，通过 WebGPU 直接出画面。

这不是新玩法 Mod，也不是给现有 Web 客户端加一个视觉开关。它是一个新的平台适配器 App：

- Core 仍然只拥有游戏真相、配置真相、表现缓冲和 UI 真相。
- WebGPU adapter 只负责窗口、输入、设备初始化、相机应用、渲染资源、帧提交与宿主生命周期。
- 如果 WebGPU 运行时、设备、surface、shader、必需资源不可用，必须显式失败并输出可诊断错误；禁止退回 Raylib、WebGL、Canvas 或空白窗口。

第一阶段目标是交付一个可编译、可由 launcher 选择、可运行到首帧的最小竖切。它要证明：

1. `--adapter webgpu` 能进入正式 launcher/runtime 链路。
2. `Ludots.App.WebGpu` 能像 Raylib/Web 一样通过 `GameBootstrapper` 加载 `launcher.runtime.json`。
3. WebGPU host 能建立窗口和 WebGPU 设备，能消费 Core 的 camera、primitive、HUD/UI 相关服务。
4. 首帧至少显示可读背景、相机参考网格、基础 primitive 或明确的无内容诊断。
5. 失败时 fail-fast，不隐藏问题。

## 2. 结构

### 2.1 新增模块

建议新增四个独立模块，保持现有六边形架构边界：

| 模块 | 职责 |
|------|------|
| `Ludots.App.WebGpu` | 可执行入口，读取 launcher runtime config，创建 `WebGpuGameHost` 并运行。 |
| `Ludots.Adapter.WebGpu` | 宿主组合根与主循环，注册 `UIRoot`、`UiSurfaceHost`、输入、相机、view controller、culling、HUD projection 等服务。 |
| `Ludots.Client.WebGpu` | WebGPU 设备、swapchain/surface、shader、pipeline、instance buffer、draw submission、窗口输入后端。 |
| `WebGpuAdapterTests` | 验证 adapter 边界、launcher 配置、fail-fast、输入捕获和基础同步计划。 |

### 2.2 复用清单

必须复用现有正式基建：

- Launcher Runtime Pipeline：selector -> launcher graph artifact -> `launcher.runtime.json` -> adapter app。
- `GameBootstrapper.InitializeFromBaseDirectory`：统一加载配置、Mod、VFS、系统与服务。
- `IGameHost`：新 App 与现有 Raylib/Web App 保持同一宿主入口。
- `IInputBackend`、`PlayerInputHandler`、`InputConfigPipelineLoader`：不新增输入系统。
- `IViewController`、`ICameraAdapter`、`CoreScreenProjector`、`CoreScreenRayProvider`、`CameraPresenter`：不在 adapter 私自实现投影数学。
- `UiSurfaceHost`、`UIRoot`、`MarkupUiSystem`、`SkiaTextMeasurer`、`SkiaImageSizeProvider`：UI 所有权仍由唯一 Host 管理。
- `PresentationPrimitiveDrawBuffer`、`SkinnedVisualBatchBuffer`、`GroundOverlayBuffer`、`WorldHudBatchBuffer`、`ScreenHudBatchBuffer`、`ScreenOverlayBuffer`、`DebugDrawCommandBuffer`、`GlobalFieldVisualBuffer`：WebGPU 只消费 Core 已投影好的表现数据。
- `CameraCullingSystem`、`WorldHudToScreenSystem`、`PresentationOverlaySceneBuilder`：保持 Raylib/Web 一致的可见性和 HUD 投影口径。
- `StaticMeshAdapterSyncPlanner` 与 Raylib ISM bucket 思路：用于 WebGPU 静态/实例批同步，不另建语义真相。

### 2.3 新增清单

允许新增的能力仅限 adapter 侧：

- WebGPU 运行时初始化器：创建 instance、adapter、device、queue、surface/swapchain。
- WebGPU 窗口与输入后端：把键鼠、窗口尺寸、焦点状态翻译成 Core 已有输入语义。
- WebGPU 资源缓存：mesh/material/shader/pipeline/bind group/instance buffer 的平台缓存。
- WebGPU primitive renderer：消费 Core `PrimitiveDrawBuffer` 或静态 mesh 同步结果，按 mesh/material/lane 批量提交。
- WebGPU HUD/UI 合成策略：第一阶段可用明确的占位实现或 Skia raster 到 texture，但必须声明能力缺口；不能假装完整支持。
- WebGPU adapter launcher 配置：新增 `webgpu` adapter id、app project path、output directory 与 CLI 帮助文本。

## 3. 详情

### 3.1 用户价值

玩家和开发者希望看到的是“同一个 Ludots 游戏，在 WebGPU 路径下也能打开、能交互、能看到正确的世界和 UI”，而不是一份渲染库实验报告。

因此 MVP 的玩家可见效果必须聚焦：

- 打开指定 showcase 后，窗口标题、分辨率和画面状态明确。
- 画面不空白；如果没有可画物，显示可读诊断。
- 已有地图、相机和输入仍然能工作。
- 操作失败时给出清楚原因，而不是悄悄换后端或黑屏。

### 3.2 启动与配置

`webgpu` 必须成为正式 adapter id：

- `scripts/run-mod-launcher.cmd cli resolve <binding> --adapter webgpu` 能生成 runtime plan。
- `scripts/run-mod-launcher.cmd cli build app --adapter webgpu` 能构建 WebGPU App。
- `scripts/run-mod-launcher.cmd cli launch <binding> --adapter webgpu --build auto` 能启动 WebGPU App。
- `launcher.runtime.json` 仍由 launcher 写入，WebGPU App 只读取，不自己推导 Mod 列表。

若 launcher 尚未支持动态 adapter 配置，本阶段可以沿现有 Raylib/Web 的代码形态增量扩展；但不能临时写一条独立启动脚本绕过 launcher。

### 3.3 Host 组合根

`WebGpuHostComposer` 应与 Raylib/Web 保持同构：

- 初始化日志。
- 调用 `GameBootstrapper`。
- 解析并应用 host asset config，adapter id 使用 `webgpu`。
- 注册 UI runtime 与输入 runtime。
- 注册 view controller、screen projector、screen ray provider。
- 验证必需服务缺失时立即抛错。

禁止从 Mod 加载链路解析 WebGPU native runtime、shader 或窗口库依赖；这些是 host adapter 依赖。

### 3.4 主循环

`WebGpuHostLoop` 每帧职责：

1. 轮询窗口和输入。
2. 把 UI 输入送入 `UIRoot.HandleInput`，并写回 `CoreServiceKeys.UiCaptured`。
3. `engine.Tick(dt)`。
4. 使用 `CameraPresenter` 应用 Core camera。
5. 运行 HUD 投影。
6. 从 Core Presentation buffers 读取一帧可见数据。
7. 录入/更新 WebGPU instance buffers。
8. 提交 world pass、overlay pass、UI pass。
9. 窗口关闭时停止 engine 并释放 WebGPU 资源。

热路径不得每帧创建新的大数组、字符串或对象池外临时集合。批渲染更新应使用 span、固定容量缓冲、bucket 或可复用 staging buffer。

### 3.5 渲染 MVP

第一阶段的渲染范围：

- 必须：清屏、参考网格或背景、camera 跟随、基础 cube/sphere primitive、可读诊断 overlay。
- 应该：按 `meshAssetId` 分组实例化绘制，支持颜色、位置、缩放。
- 应该：基础 debug line/circle/box。
- 可选：GroundOverlay 简化绘制。
- 可选：Skia UI raster 上传为 texture。
- 暂不要求：完整材质系统、skinned animation、terrain、global field、浏览器 surface、复杂文字排版。

即使 UI pass 暂不完整，也必须暴露清楚的能力诊断，让用户知道当前 WebGPU adapter 到了哪一层。

### 3.6 包与 API 选择

候选技术路线：

- WebGPU binding：优先评估 `Silk.NET.WebGPU` 及其 WGPU/Dawn 相关扩展。
- Window/input：优先评估 `Silk.NET.Windowing` 或同一生态中能直接创建 WebGPU surface 的窗口库。

Cursor 在写任何引用前必须验证 NuGet 包、类型、方法和 native runtime 交付方式确实存在。若候选包不能满足要求，必须停下报告缺口，不得改用 Raylib/OpenGL/Vulkan/Direct3D 冒充 WebGPU。

### 3.7 测试策略

优先测试最高层可观察行为：

- Launcher 能识别并解析 `webgpu`。
- WebGPU App project 能构建。
- Host composer 能注册必需服务并在缺失配置时 fail-fast。
- WebGPU 初始化缺少 runtime/device 时有明确错误。
- UI 捕获逻辑不吞掉 world input。
- Primitive 同步计划不新建 Core 真相。

真实 GPU 出画面属于 smoke/UAT，不能只靠单元测试声称完成。

## 4. 场景

### 4.1 开发者跑一个已有 showcase

开发者执行：

```powershell
.\scripts\run-mod-launcher.cmd cli launch camera_acceptance --adapter webgpu --build auto
```

期望结果：

- launcher 生成 WebGPU runtime plan。
- WebGPU App 启动。
- 打开窗口。
- 显示非空画面和相机参考。
- 可通过鼠标键盘操作相机或已有输入。

### 4.2 玩家打开能力标准场景

玩家打开一个已有 capability showcase，期望看到基础单位或占位体随相机移动显示。即使 WebGPU 还不支持完整材质，也不能黑屏，也不能让玩家误以为玩法没加载。

### 4.3 机器不支持 WebGPU

用户机器缺少 WebGPU backend、native runtime 或兼容 GPU 时：

- App 直接启动失败。
- 错误信息包含缺少的能力或文件。
- 不自动改用 Raylib/WebGL。
- launcher/CI 能捕获失败原因。

### 4.4 UI 覆盖输入

当 UI 面板位于屏幕上时：

- 鼠标点在 UI 上由 `UIRoot` 捕获。
- 鼠标点在世界上继续走世界输入。
- WebGPU adapter 不自建第二套 UI 命中规则。

### 4.5 大量静态实例

开发者启动静态 performer 压测场景时：

- Core 仍然产出同一份 presentation buffer。
- WebGPU adapter 按 mesh/material/lane 建立实例批。
- 稳态帧不重复重建所有静态实例。
- 若容量不足，明确记录 drop/limit 诊断。

## 5. 边界

### 5.1 范围内

- 新增 WebGPU 原生桌面 adapter app。
- 新增 adapter id 与 launcher 接入。
- 新增 WebGPU host、window、input、view controller、camera adapter。
- 基础 primitive/instance 渲染。
- 基础 fail-fast 诊断和构建测试。
- 保持 Core、Mod、Presentation、UI Surface 单一真相。

### 5.2 范围外

- 不重写 Core Presentation Pipeline。
- 不新增 Mod 私有渲染链路。
- 不把 WebGPU 能力写进 gameplay 组件。
- 不做 WebGL、Raylib、OpenGL、Vulkan 或 Direct3D fallback。
- 不实现完整 PBR、terrain、skinned animation、browser surface、IME、复杂富文本。
- 不修复现有 Web 客户端死文件、WorldHud 跳过、WebSocket 拷贝等旁路债务；发现后只报告。
- 不调整 UE5、Unity、Godot 等商业引擎 adapter。

### 5.3 架构红线

- Core 不引用 WebGPU、Silk.NET、Dawn、wgpu-native 或任何平台渲染类型。
- Adapter 不直接读领域 store；只能消费 Core 投影到 Presentation buffer 的 adapter-facing 数据。
- UI 仍通过 `UiSurfaceHost` 统一拥有；adapter 不叠第二棵场景。
- Grounding、instance count、mesh/material 语义仍由 Core/配置真相决定；adapter cache 只是执行细节。
- 热路径禁止结构性 ECS 变更，禁止 per-entity 分配，禁止内存飞线。

## 6. UAT

```gherkin
Feature: WebGPU 适配 App 启动
  作为想试玩 Ludots 的玩家
  我希望能选择 WebGPU 运行方式打开已有场景
  以便确认同一个游戏世界可以在新的原生渲染路径下运行

  Scenario: 用 WebGPU 打开相机验收场景
    Given 我的机器支持 WebGPU 运行时
    And 我已经选择 camera_acceptance 场景
    When 我用 webgpu 适配器启动游戏
    Then 我看到一个非空的游戏窗口
    And 我能看到相机参考或场景物体
    And 画面上没有把 WebGPU 伪装成其他适配器的提示
```

```gherkin
Feature: WebGPU 适配 App 的失败反馈
  作为开发者
  我希望不支持 WebGPU 的机器能直接告诉我缺少什么
  以便我修环境，而不是误以为游戏逻辑坏了

  Scenario: 机器缺少 WebGPU 必需能力
    Given 当前机器没有可用的 WebGPU device 或 native runtime
    When 我用 webgpu 适配器启动游戏
    Then 启动失败
    And 错误信息说明 WebGPU 初始化失败的具体原因
    And 程序没有自动切换到 Raylib、WebGL 或空白渲染
```

```gherkin
Feature: WebGPU 适配 App 的玩家输入
  作为玩家
  我希望鼠标键盘在 WebGPU 窗口中和现有桌面版本一样工作
  以便我能移动相机、点击世界和操作 UI

  Scenario: UI 捕获点击而世界输入不被误触发
    Given WebGPU 窗口中显示了一个可交互 UI 面板
    When 我点击面板上的按钮
    Then 面板响应我的点击
    And 世界选择或移动命令不会同时触发

  Scenario: 点击 UI 外的世界区域
    Given WebGPU 窗口中显示了游戏世界
    And 鼠标位置不在 UI 面板上
    When 我点击一个可选择的实体
    Then 实体被选中
    And UI 没有错误吞掉这次点击
```

```gherkin
Feature: WebGPU 适配 App 的基础表现
  作为玩法开发者
  我希望已有 Core 表现缓冲能在 WebGPU 中变成可见画面
  以便不用为 WebGPU 复制一套玩法或表现真相

  Scenario: 基础 primitive 被绘制
    Given 场景中存在由 Core 输出的基础 primitive
    When WebGPU adapter 渲染下一帧
    Then 我能在窗口中看到对应的物体
    And 物体的位置、缩放和颜色来自 Core 表现数据
    And adapter 没有创建自己的实体真相
```

```gherkin
Feature: WebGPU 适配 App 的批量实例
  作为性能验证者
  我希望 WebGPU 能按 Core 的实例批数据绘制大量静态物体
  以便确认新适配器不是只适合小 demo

  Scenario: 静态 performer 压测场景稳态显示
    Given 我启动 capability_standard_static_performer_30k 场景
    When WebGPU adapter 完成首帧和后续稳态帧
    Then 我能看到大量静态实例
    And 稳态帧不会每帧重建全部静态实例
    And 如果超过当前实现容量，屏幕或日志给出清楚的容量诊断
```

```gherkin
Feature: WebGPU 适配 App 与 Launcher 集成
  作为使用命令行启动 Ludots 的开发者
  我希望 webgpu 是正式 adapter 选项
  以便所有 showcase 都能复用同一套启动方式

  Scenario: 解析 WebGPU runtime plan
    Given 我在仓库根目录
    When 我执行 launcher resolve 并指定 webgpu adapter
    Then launcher 输出的 runtime plan 指向 WebGPU App
    And plan 中的 Mod 加载列表仍由 launcher graph 决定
    And 没有出现独立脚本绕过 launcher 的结果
```
