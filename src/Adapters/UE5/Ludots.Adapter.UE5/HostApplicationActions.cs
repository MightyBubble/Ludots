namespace Ludots.Adapter.UE5
{
    /// <summary>
    /// 宿主应用级操作结果。
    /// </summary>
    public readonly record struct HostApplicationActionResult(
        bool Success,
        string ErrorMessage)
    {
        public static HostApplicationActionResult Ok()
            => new(true, string.Empty);

        public static HostApplicationActionResult Fail(string errorMessage)
            => new(false, errorMessage ?? string.Empty);
    }

    /// <summary>
    /// 宿主应用级操作抽象，封装 Mod 需要触发但由宿主实现的通用交互。
    /// <para>
    /// 与 <see cref="IHostLevelNavigator"/> 同级别的宿主合约。
    /// 宿主平台在启动时通过 <c>engine.SetService(UE5AdapterServiceKeys.HostApplicationActions, impl)</c> 注入实现。
    /// </para>
    /// </summary>
    public interface IHostApplicationActions
    {
        /// <summary>打开宿主原生设置面板（ZOrder 高于 Web 面板，自然覆盖）。</summary>
        HostApplicationActionResult OpenSettingsPanel();

        /// <summary>请求宿主退出游戏进程。</summary>
        HostApplicationActionResult RequestQuitGame();
    }
}