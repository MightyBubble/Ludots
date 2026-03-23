using System;
using Ludots.Core.Config;
using Ludots.Core.Diagnostics;
using Ludots.Core.Engine;
using Ludots.Core.Hosting;
using Ludots.Core.Input.Config;
using Ludots.Core.Input.Runtime;
using Ludots.Core.Presentation.Camera;
using Ludots.Core.Presentation.Hud;
using Ludots.Core.Presentation.Systems;
using Ludots.Core.Scripting;
using Ludots.Core.Systems;
using Ludots.Platform.Abstractions;
using Ludots.UI;
using Ludots.UI.HtmlEngine.Markup;
using Ludots.UI.Runtime;
using Ludots.UI.Skia;

namespace Ludots.Adapter.UE5
{
    /// <summary>
    /// UE5 适配器宿主的组合结果。
    /// UE5 C# 脚本持有此对象，并通过 <see cref="SharedState"/> 每帧推送平台数据。
    /// </summary>
    public sealed class UE5HostSetup
    {
        /// <summary>已初始化并注入 UE5 适配器的 Ludots 引擎实例。</summary>
        public GameEngine Engine { get; }

        /// <summary>合并后的游戏配置（含 FixedHz、StartupMapId 等）。</summary>
        public GameConfig Config { get; }

        /// <summary>
        /// UE5 ↔ Ludots 共享状态容器。
        /// UE5 C# 脚本每帧调用其 Push/Set 方法推入相机、视口、输入数据。
        /// </summary>
        public UE5SharedCameraState SharedState { get; }

        /// <summary>
        /// 相机适配器实例（实现 <see cref="ICameraAdapter"/>）。
        /// UE5 C# 脚本在 <c>engine.Start()</c> 完成后，用此对象驱动 Ludots 相机系统。
        /// </summary>
        public UE5CameraAdapter CameraAdapter { get; }

        /// <summary>
        /// 相机表现者（Ludots 平滑相机状态计算器）。
        /// UE5 C# 脚本每帧在调用 <c>engine.Tick(dt)</c> 之后调用：
        /// <code>
        /// float alpha = setup.Engine.GetService(CoreServiceKeys.PresentationFrameSetup)?.GetInterpolationAlpha() ?? 1f;
        /// setup.CameraPresenter.Update(setup.Engine.GameSession.Camera, alpha, RenderCameraDebug);
        /// </code>
        /// 更新结果会自动通过 <see cref="UE5CameraAdapter"/> 写入 <see cref="SharedState"/>。
        /// </summary>
        public CameraPresenter CameraPresenter { get; }

        /// <summary>
        /// WorldHud → 屏幕坐标投影系统（可为 <c>null</c>，当 WorldHud 缓冲区未就绪时）。
        /// UE5 C# 脚本每帧在 <c>engine.Tick(dt)</c> 之后调用：
        /// <code>setup.HudProjection?.Update(dt);</code>
        /// </summary>
        public WorldHudToScreenSystem? HudProjection { get; }

        /// <summary>
        /// 相机调试渲染状态（供 <see cref="CameraPresenter.Update"/> 第三个参数使用）。
        /// </summary>
        public RenderCameraDebugState RenderCameraDebug { get; }

        /// <summary>Ludots UI 根节点（管理面板 Scene Tree，供每帧 Render 调用）。</summary>
        public UIRoot? UiRoot { get; }

        /// <summary>Skia UI 渲染器（通过 SetCanvas 绑定目标画布后由 UIRoot.Render 使用）。</summary>
        public SkiaUiRenderer? UiRenderer { get; }

        internal UE5HostSetup(
            GameEngine               engine,
            GameConfig               config,
            UE5SharedCameraState     sharedState,
            UE5CameraAdapter         cameraAdapter,
            CameraPresenter          cameraPresenter,
            WorldHudToScreenSystem?  hudProjection,
            RenderCameraDebugState   renderCameraDebug,
            UIRoot?                  uiRoot     = null,
            SkiaUiRenderer?          uiRenderer = null)
        {
            Engine            = engine;
            Config            = config;
            SharedState       = sharedState;
            CameraAdapter     = cameraAdapter;
            CameraPresenter   = cameraPresenter;
            HudProjection     = hudProjection;
            RenderCameraDebug = renderCameraDebug;
            UiRoot            = uiRoot;
            UiRenderer        = uiRenderer;
        }
    }

    /// <summary>
    /// UE5 平台宿主组合器（对标 <c>RaylibHostComposer</c>）。
    ///
    /// 负责：
    /// <list type="number">
    ///   <item>初始化日志后端（控制台 + 可选文件）。</item>
    ///   <item>通过 <see cref="GameBootstrapper"/> 从磁盘读取 game.json / Mod 并初始化引擎。</item>
    ///   <item>构造 <see cref="UE5SharedCameraState"/> 共享状态容器。</item>
    ///   <item>将全套 UE5 适配器（输入、UI、相机、视口、射线、屏幕投影）注入 GlobalContext。</item>
    /// </list>
    ///
    /// UE5 C# 脚本示例用法：
    /// <code>
    /// // 在 BeginPlay 中：
    /// var setup = UE5HostComposer.Compose(gameJsonAbsPath);
    /// _engine      = setup.Engine;
    /// _sharedState = setup.SharedState;
    /// _engine.Start();
    /// _engine.LoadMap(setup.Config.StartupMapId);
    ///
    /// // 在 Tick 中：
    /// _sharedState.PushCameraState(cameraState);
    /// _sharedState.MouseX = mouseX;
    /// _engine.Tick(deltaTime);
    /// float alpha = setup.Engine.GetService(CoreServiceKeys.PresentationFrameSetup)?.GetInterpolationAlpha() ?? 1f;
    /// setup.CameraPresenter.Update(setup.Engine.GameSession.Camera, alpha, null);
    /// _sharedState.FlushTransient();   // 帧末重置瞬态输入
    /// </code>
    /// </summary>
    public static class UE5HostComposer
    {
        /// <summary>
        /// 从指定 game.json 初始化引擎并注入全套 UE5 适配器。
        /// </summary>
        /// <param name="gameJsonAbsPath">
        ///   指向 UE5 项目内或其可访问路径上的 game.json 绝对路径，
        ///   文件中需包含 <c>ModPaths</c> 数组（相对于文件所在目录，或绝对路径）。
        /// </param>
        /// <param name="initialViewportWidth">初始视口宽度（像素），UE5 侧应在 Tick 中持续更新。</param>
        /// <param name="initialViewportHeight">初始视口高度（像素）。</param>
        /// <param name="externalBackend">
        ///   可选的外部日志后端。如果调用方已经创建并通过 <see cref="Log.Initialize"/> 设好了后端
        ///   （例如 <c>MultiLogBackend(ConsoleLogBackend, FileLogBackend)</c>），
        ///   传入该实例即可跳过 Compose 内部的日志初始化，避免覆盖调用方的设置。
        ///   传 <c>null</c> 则保持原有行为（内部创建 <see cref="ConsoleLogBackend"/>）。
        /// </param>
        /// <returns><see cref="UE5HostSetup"/> 宿主结果，UE5 脚本持有并每帧与之交互。</returns>
        public static UE5HostSetup Compose(
            string gameJsonAbsPath,
            float  initialViewportWidth  = 1920f,
            float  initialViewportHeight = 1080f,
            ILogBackend? externalBackend = null)
        {
            if (string.IsNullOrWhiteSpace(gameJsonAbsPath))
                throw new ArgumentException("gameJsonAbsPath must not be null or empty.", nameof(gameJsonAbsPath));

            // ── 1. 日志初始化（对标 RaylibHostComposer）───────────────────
            // 如果调用方已提供外部 backend（并已调用 Log.Initialize），直接复用，
            // 避免 Compose 内部覆盖调用方的日志配置。
            ILogBackend effectiveBackend;
            if (externalBackend != null)
            {
                effectiveBackend = externalBackend;
                // 调用方负责 Log.Initialize，这里不再重复调用
            }
            else
            {
                var consoleBackend = new ConsoleLogBackend();
                effectiveBackend = consoleBackend;
                Log.Initialize(effectiveBackend);
            }

            // ── 2. 引擎初始化 ────────────────────────────────────────────
            var baseDir = System.IO.Path.GetDirectoryName(gameJsonAbsPath)
                          ?? throw new ArgumentException("Cannot resolve directory from gameJsonAbsPath.");
            var gameConfigFile = System.IO.Path.GetFileName(gameJsonAbsPath);

            var result = GameBootstrapper.InitializeFromBaseDirectory(baseDir, gameConfigFile);
            var engine = result.Engine;
            var config = result.Config;

            // 升级为文件日志（如果 config 中已启用且调用方未提供外部 backend）
            if (config.Logging.FileLogging && externalBackend == null)
            {
                var consoleForMulti = effectiveBackend; // 此时是内部创建的 ConsoleLogBackend
                var fileBackend = new FileLogBackend(config.Logging.LogFilePath);
                var multiBackend = new MultiLogBackend(consoleForMulti, fileBackend);
                effectiveBackend = multiBackend;
                Log.Initialize(
                    multiBackend,
                    Enum.TryParse<LogLevel>(config.Logging.GlobalLevel, true, out var lvl) ? lvl : LogLevel.Info);
                LogConfigApplier.Apply(config.Logging);
            }
            else if (externalBackend != null)
            {
                // 调用方已负责 backend，仅应用 config 中的 channel level 覆盖
                LogConfigApplier.Apply(config.Logging);
            }

            engine.SetService(CoreServiceKeys.LogBackend, effectiveBackend);

            // ── 3. 共享状态容器 ──────────────────────────────────────────
            var sharedState = new UE5SharedCameraState
            {
                ViewportWidth  = initialViewportWidth,
                ViewportHeight = initialViewportHeight,
            };

            // ── 4. 注入 UE5 适配器 ───────────────────────────────────────

            // UI 系统（对标 RaylibHostComposer：SkiaUiRenderer + UIRoot + MarkupUiSystem）
            var uiRenderer = new SkiaUiRenderer();
            IUiTextMeasurer textMeasurer = new SkiaTextMeasurer();
            IUiImageSizeProvider imageSizeProvider = new SkiaImageSizeProvider();
            var uiRoot = new UIRoot(uiRenderer);
            engine.SetService(CoreServiceKeys.UIRoot, (object)uiRoot);
            engine.SetService(CoreServiceKeys.UiTextMeasurer, (object)textMeasurer);
            engine.SetService(CoreServiceKeys.UiImageSizeProvider, (object)imageSizeProvider);
            engine.SetService(CoreServiceKeys.UISystem, (Ludots.Core.UI.IUiSystem)new MarkupUiSystem(uiRoot, textMeasurer, imageSizeProvider));

            // 输入后端（UE5 PlayerController → Ludots InputHandler）
            IInputBackend inputBackend = new UE5InputBackend(sharedState);
            engine.SetService(CoreServiceKeys.InputBackend, inputBackend);

            // 输入配置 & 处理器
            var inputConfig = new InputConfigPipelineLoader(engine.ConfigPipeline).Load();
            var inputHandler = new PlayerInputHandler(inputBackend, inputConfig);
            if (config.StartupInputContexts != null)
            {
                foreach (var contextId in config.StartupInputContexts)
                {
                    if (!string.IsNullOrWhiteSpace(contextId))
                        inputHandler.PushContext(contextId);
                }
            }
            engine.SetService(CoreServiceKeys.InputHandler, inputHandler);

            // 视口控制器（提供宽高，用于 UI 投影 / 裁剪）
            var viewController = new UE5ViewController(sharedState);
            engine.SetService(CoreServiceKeys.ViewController, (Ludots.Core.Presentation.Camera.IViewController)viewController);

            // 屏幕射线提供者（鼠标拾取）
            var screenRayProvider = new UE5ScreenRayProvider(sharedState);
            engine.SetService(CoreServiceKeys.ScreenRayProvider, (IScreenRayProvider)screenRayProvider);

            // 相机适配器 + 相机表现者
            var cameraAdapter   = new UE5CameraAdapter(sharedState);
            var cameraPresenter = new CameraPresenter(engine.SpatialCoords, cameraAdapter);

            // 屏幕投影器（WorldHud 世界坐标 → 屏幕坐标）
            var screenProjector = new CoreScreenProjector(engine.GameSession.Camera, viewController);
            screenProjector.BindPresenter(cameraPresenter);
            engine.SetService(CoreServiceKeys.ScreenProjector, (IScreenProjector)screenProjector);

            // ── 5. Presentation 系统（对标 RaylibHostLoop）───────────────

            // 5a. 相机调试渲染状态
            var renderCameraDebug = new RenderCameraDebugState();
            engine.SetService(CoreServiceKeys.RenderCameraDebugState, renderCameraDebug);

            // 5b. 视锥裁剪系统（注册为 Presentation System，引擎每 Tick 自动驱动）
            var cullingSystem = new CameraCullingSystem(
                engine.World,
                engine.GameSession.Camera,
                engine.SpatialQueries,
                viewController);
            engine.RegisterPresentationSystem(cullingSystem);
            engine.SetService(CoreServiceKeys.CameraCullingDebugState, cullingSystem.DebugState);

            // 5c. 可视化调试系统（可选，UE5 侧通常不使用，但保持与 Raylib 对称）
            engine.RegisterPresentationSystem(
                new CullingVisualizationPresentationSystem(engine.GlobalContext));

            // 5d. WorldHud → 屏幕坐标投影系统（有条件：需要 WorldHud / ScreenHud buffer 均已注册）
            WorldHudToScreenSystem? hudProjection = null;
            var worldHud  = engine.GetService(CoreServiceKeys.PresentationWorldHudBuffer);
            var screenHud = engine.GetService(CoreServiceKeys.PresentationScreenHudBuffer);
            if (worldHud != null && screenHud != null)
            {
                var hudStrings = engine.GetService(CoreServiceKeys.PresentationWorldHudStrings);
                hudProjection = new WorldHudToScreenSystem(
                    engine.World,
                    worldHud,
                    hudStrings,
                    screenProjector,
                    viewController,
                    screenHud);
            }

            ValidateRequiredContext(engine);

            return new UE5HostSetup(
                engine,
                config,
                sharedState,
                cameraAdapter,
                cameraPresenter,
                hudProjection,
                renderCameraDebug,
                uiRoot,
                uiRenderer);
        }

        // ── 启动前校验 ────────────────────────────────────────────────────

        private static void ValidateRequiredContext(GameEngine engine)
        {
            ValidateKey<Ludots.Core.UI.IUiSystem>(engine, CoreServiceKeys.UISystem.Name);
            ValidateKey<IInputBackend>(engine, CoreServiceKeys.InputBackend.Name);
            ValidateKey<PlayerInputHandler>(engine, CoreServiceKeys.InputHandler.Name);
            ValidateKey<Ludots.Core.Presentation.Camera.IViewController>(engine, CoreServiceKeys.ViewController.Name);
            ValidateKey<IScreenRayProvider>(engine, CoreServiceKeys.ScreenRayProvider.Name);
            ValidateKey<IScreenProjector>(engine, CoreServiceKeys.ScreenProjector.Name);
        }

        private static void ValidateKey<T>(GameEngine engine, string key)
        {
            if (!engine.GlobalContext.TryGetValue(key, out var obj) || obj is not T)
                throw new InvalidOperationException(
                    $"GlobalContext 缺少或类型不匹配：{key}，期望 {typeof(T).FullName}");
        }
    }
}