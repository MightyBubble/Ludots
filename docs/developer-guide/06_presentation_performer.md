# 表现管线与 Performer 体系

本篇介绍 Ludots 的表现侧数据流：从逻辑系统产出“事件/命令/缓冲”，到表现系统消费并输出渲染所需的数据，再到平台适配层把这些输出映射到具体图形 API。

## 1 分层与数据流

表现侧的关键目标是：逻辑层保持确定性与纯数据，表现层可以插值、特效、UI、调试绘制，但不反向污染逻辑状态。

常见数据流：

1.  **逻辑层输出**：逻辑系统在 FixedStep 中更新世界状态、生成事件、推入命令缓冲。
2.  **表现层准备**：每帧计算插值参数，并把“可插值状态”同步到表现组件。
3.  **Performer 执行**：Performer 系统消费事件/命令，根据配置与规则生成实例、驱动特效/指示器/图元输出。
4.  **平台渲染**：适配器/客户端渲染器读取 draw buffers 与可视组件，完成真正的绘制调用。

## 2 表现系统组装与顺序

引擎在 `GameEngine.InitializeCoreSystems` 中注册表现系统，关键顺序要点：

1.  `PresentationFrameSetupSystem` 必须最先执行，用于计算插值参数并提供给后续同步系统。
2.  `WorldToVisualSyncSystem` 负责把 `WorldPositionCm` 插值为 `VisualTransform`，用于渲染平滑。
3.  `ResponseChain*` 系列系统负责响应链（玩家/AI 输入源、UI 同步、导演系统）。
4.  `PerformerRuleSystem` 读取事件流并生成表现命令。
5.  `PerformerRuntimeSystem` 消费命令并管理 performer 实例生命周期。
6.  `PerformerEmitSystem` tick 实例与绑定，并把可渲染输出写入 draw buffers。

相关代码入口：

*   系统注册：`src/Core/Engine/GameEngine.cs`
*   表现系统列表：`RegisterPresentationSystem(...)`

## 3 核心缓冲与概念

表现侧主要通过以下缓冲与注册表解耦“逻辑”与“渲染/平台”：

*   **PresentationEventStream**：事件流，连接逻辑事件与表现规则系统。
*   **PresentationCommandBuffer**：命令缓冲，表现规则系统写入命令，runtime 系统消费执行。
*   **PrimitiveDrawBuffer**：调试/表现图元输出（线、圆、框等）。
*   **GroundOverlayBuffer**：地面叠加绘制输出。
*   **WorldHudBatchBuffer**：世界空间 HUD 文本/标记的批量输出。
*   **PerformerDefinitionRegistry**：Performer 定义注册表（含内建定义与配置加载）。
*   **PerformerInstanceBuffer**：Performer 实例缓冲（活跃实例的运行态）。

## 4 ResponseChain 与表现同步

响应链（ResponseChain）用于处理“连锁/响应”类的指令管线，并同步给 UI 与表现层：

*   导演系统负责把响应链状态转为表现命令；
*   人类/AI OrderSource 负责向链路注入决策；
*   UI Sync 系统负责把链路状态写入 UI 可用的状态结构。

相关实现位于：

*   `src/Core/Presentation/Systems/ResponseChain*`

## 5 平台适配与渲染器

平台侧的实现通常在 `src/Adapters/<Platform>` 与 `src/Client/<PlatformClient>`：

*   适配器层负责窗口/生命周期/平台服务（相机、屏幕投影、输入桥接等）。
*   客户端层提供渲染器实现，读取 Core 的输出缓冲与可视组件，调用具体图形 API。

以 Raylib 为例：

*   Host 与平台服务：`src/Adapters/Raylib/Ludots.Adapter.Raylib`
*   输入与渲染器：`src/Client/Ludots.Client.Raylib`

