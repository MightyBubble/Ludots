namespace Ludots.WebUI;

/// <summary>
/// WebUI 面板工厂接口。
/// <para>
/// 宿主平台（如 UE5）在引擎启动时将实现注入到 GlobalContext，
/// Mod 通过工厂按需创建独立的 <see cref="IWebUIBridge"/> 实例（每个实例对应一个 Web 面板/页面）。
/// </para>
/// <example>
/// <code>
/// // Mod 中使用：
/// var factory = engine.GlobalContext[WebUIContextKeys.BridgeFactory] as IWebUIBridgeFactory;
///
/// var mainHud = factory.Create("MainHud");
/// mainHud.Init(new WebUISettings { Url = "file:///ui/main_hud.html", WebType = WebUIType.Hud });
/// mainHud.Open();
///
/// var shop = factory.Create("Shop");
/// shop.Init(new WebUISettings { Url = "file:///ui/shop.html", WebType = WebUIType.FullScreenPanel });
/// shop.Open();
///
/// // 不再需要时：
/// factory.Destroy("Shop");
/// </code>
/// </example>
/// </summary>
public interface IWebUIBridgeFactory
{
    /// <summary>
    /// 创建一个新的 WebUI 面板，并返回其操作接口。
    /// <para>
    /// 若 <paramref name="panelId"/> 已存在，将返回已有实例（幂等）。
    /// </para>
    /// </summary>
    /// <param name="panelId">面板唯一标识符，建议使用 <c>PascalCase</c> 名称，如 <c>"MainHud"</c>、<c>"ShopPanel"</c>。</param>
    /// <returns>与该面板绑定的 <see cref="IWebUIBridge"/> 实例。</returns>
    IWebUIBridge Create(string panelId);

    /// <summary>
    /// 销毁指定面板，释放底层 Web 浏览器资源，并移除内部引用。
    /// <para>
    /// 销毁后对该 <see cref="IWebUIBridge"/> 实例的调用行为未定义。
    /// </para>
    /// </summary>
    /// <param name="panelId">要销毁的面板标识符。</param>
    void Destroy(string panelId);

    /// <summary>
    /// 获取已创建的面板 Bridge，若面板不存在则返回 <c>null</c>。
    /// </summary>
    /// <param name="panelId">面板标识符。</param>
    IWebUIBridge? Get(string panelId);

    /// <summary>
    /// 销毁所有已创建的面板，释放全部资源。通常在世界卸载时由宿主调用。
    /// </summary>
    void DestroyAll();
}
