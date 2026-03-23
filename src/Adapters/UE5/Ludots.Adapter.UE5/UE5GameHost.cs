using System;
using Ludots.Platform.Abstractions;

namespace Ludots.Adapter.UE5
{
    /// <summary>
    /// UE5 平台宿主入口（对标 <c>RaylibGameHost</c>）。
    ///
    /// 实现 <see cref="IGameHost"/>，封装完整的 Ludots 引擎启动序列：
    /// <c>Compose → engine.Start() → engine.LoadMap(StartupMapId)</c>。
    ///
    /// <b>注意：</b> 与 Raylib 宿主不同，<see cref="Run"/> 不含阻塞帧循环。
    /// UE5 的帧驱动由引擎侧 Tick 回调负责；
    /// UE5 C# 脚本每帧需手动调用：
    /// <code>
    /// // 在 UE5 Tick 中：
    /// _sharedState.SetAxis(...)；
    /// _sharedState.MouseX = ...；
    /// _engine.Tick(deltaTime);
    /// float alpha = _setup.Engine
    ///                      .GetService(CoreServiceKeys.PresentationFrameSetup)
    ///                      ?.GetInterpolationAlpha() ?? 1f;
    /// _setup.CameraPresenter.Update(_setup.Engine.GameSession.Camera, alpha, null);
    /// _sharedState.FlushTransient();
    /// </code>
    ///
    /// <b>生命周期：</b>
    /// <list type="number">
    ///   <item>在 <c>BeginPlay</c> 中构造并调用 <see cref="Run"/>。</item>
    ///   <item>在 <c>EndPlay</c> 中调用 <see cref="Dispose"/>（停止引擎）。</item>
    /// </list>
    /// </summary>
    public sealed class UE5GameHost : IGameHost
    {
        private readonly string _gameJsonAbsPath;
        private readonly float  _initialViewportWidth;
        private readonly float  _initialViewportHeight;

        private UE5HostSetup? _setup;

        /// <summary>
        /// 启动后可访问的宿主结果（含 Engine、SharedState、CameraPresenter 等）。
        /// 在 <see cref="Run"/> 返回后才有效；未调用 Run 前为 <c>null</c>。
        /// </summary>
        public UE5HostSetup? Setup => _setup;

        /// <param name="gameJsonAbsPath">game.json 的绝对路径，用于定位 Mod 和基础配置。</param>
        /// <param name="initialViewportWidth">初始视口宽度（像素），默认 1920。</param>
        /// <param name="initialViewportHeight">初始视口高度（像素），默认 1080。</param>
        public UE5GameHost(
            string gameJsonAbsPath,
            float  initialViewportWidth  = 1920f,
            float  initialViewportHeight = 1080f)
        {
            if (string.IsNullOrWhiteSpace(gameJsonAbsPath))
                throw new ArgumentException("gameJsonAbsPath must not be null or empty.", nameof(gameJsonAbsPath));

            _gameJsonAbsPath      = gameJsonAbsPath;
            _initialViewportWidth  = initialViewportWidth;
            _initialViewportHeight = initialViewportHeight;
        }

        /// <summary>
        /// 执行完整启动序列：Compose → <c>engine.Start()</c> → <c>engine.LoadMap(StartupMapId)</c>。
        ///
        /// 此方法在 UE5 的 <c>BeginPlay</c> 阶段调用，<b>不阻塞</b>，调用后立即返回。
        /// 帧驱动由 UE5 侧 Tick 回调负责。
        /// </summary>
        /// <exception cref="InvalidOperationException">
        ///   若 <c>StartupMapId</c> 为空，或引擎上下文验证失败，则抛出。
        /// </exception>
        public void Run()
        {
            var setup = UE5HostComposer.Compose(
                _gameJsonAbsPath,
                _initialViewportWidth,
                _initialViewportHeight);

            setup.Engine.Start();

            if (string.IsNullOrWhiteSpace(setup.Config.StartupMapId))
                throw new InvalidOperationException(
                    "Invalid launcher bootstrap: 'StartupMapId' cannot be empty.");

            setup.Engine.LoadMap(setup.Config.StartupMapId);

            _setup = setup;
        }

        /// <summary>
        /// 停止引擎，释放全部 ECS World 和 Mod 持有的资源。
        /// 应在 UE5 的 <c>EndPlay</c> 阶段调用。
        /// </summary>
        public void Dispose()
        {
            _setup?.Engine.Stop();
            _setup = null;
        }
    }
}
