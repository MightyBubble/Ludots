namespace Ludots.WebUI;

/// <summary>
/// GlobalContext 中 WebUI 相关的标准键名常量。
/// <para>
/// 宿主在注入实现时使用这些键，Mod 在消费时也使用同一组键，避免魔法字符串。
/// </para>
/// </summary>
public static class WebUIContextKeys
{
    /// <summary>
    /// GlobalContext 中存放 <see cref="IWebUIBridgeFactory"/> 实现的键（推荐使用）。
/// <para>
/// 工厂支持多面板多实例，Mod 通过 <c>Create(panelId)</c> 按需获得独立的 Bridge。
/// </para>
/// <example>
/// <code>
/// // 宿主注入（UE5 LudotsHostSystem）:
/// engine.GlobalContext[WebUIContextKeys.BridgeFactory] = new UE5WebUIBridgeFactory(scriptWorld);
///
/// // Mod 消费:
/// var factory = engine.GlobalContext[WebUIContextKeys.BridgeFactory] as IWebUIBridgeFactory;
/// var mainHud = factory.Create("MainHud");
/// </code>
/// </example>
/// </summary>
public const string BridgeFactory = "WebUI.BridgeFactory";

/// <summary>
/// GlobalContext 中存放单例 <see cref="IWebUIBridge"/> 实现的键（兼容旧版，单面板场景可用）。
/// <para>推荐使用 <see cref="BridgeFactory"/> 替代。</para>
/// </summary>
[Obsolete("请使用 WebUIContextKeys.BridgeFactory 获取工厂，支持多面板多实例")]
public const string Bridge = "WebUI.Bridge";

/// <summary>
/// GlobalContext 中标记 WebUI 是否已初始化的布尔标志键。
/// </summary>
public const string IsInitialized = "WebUI.IsInitialized";

/// <summary>
/// GlobalContext 中存放 WebUI 根 URL 的键（可选）。
/// </summary>
public const string RootUrl = "WebUI.RootUrl";
}
