namespace Ludots.WebUI;

/// <summary>
/// GlobalContext 中 WebUI 相关的标准键名常量。
/// <para>
/// 宿主和 Mod 都使用这组键，避免魔法字符串。
/// </para>
/// </summary>
public static class WebUIContextKeys
{
    /// <summary>
    /// GlobalContext 中存放 <see cref="IWebUIBridgeFactory"/> 实现的键（推荐使用）。
    /// <para>工厂支持多面板多实例，Mod 通过 <c>Create(panelId)</c> 按需获得独立的 Bridge。</para>
    /// </summary>
    public const string BridgeFactory = "WebUI.BridgeFactory";

    /// <summary>
    /// GlobalContext 中标记 WebUI 是否已初始化的布尔键。
    /// </summary>
    public const string IsInitialized = "WebUI.IsInitialized";

    /// <summary>
    /// GlobalContext 中存放 WebUI 根 URL 的键（可选）。
    /// </summary>
    public const string RootUrl = "WebUI.RootUrl";
}
