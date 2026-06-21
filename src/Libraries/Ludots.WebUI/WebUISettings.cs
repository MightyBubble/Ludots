namespace Ludots.WebUI;

/// <summary>
/// WebUI 面板的渲染类型。
/// </summary>
public enum WebUIType
{
    /// <summary>
    /// HUD 模式：支持基于 Alpha 的 HitTest 穿透，适合叠加在 3D 场景之上的 UI。
    /// </summary>
    Hud = 0,

    /// <summary>
    /// 全屏面板模式：不启用 Alpha HitTest，适合全屏遮罩类 UI（加载屏、菜单等）。
    /// </summary>
    FullScreenPanel = 1,

    /// <summary>
    /// 自定义模式：由宿主平台自行决定渲染行为。
    /// </summary>
    Custom = 2,
}

/// <summary>
/// WebUI 面板初始化参数，平台无关的纯 C# 版本。
/// </summary>
public sealed class WebUISettings
{
    /// <summary>
    /// 界面渲染类型（Hud / FullScreenPanel / Custom）。
    /// </summary>
    public WebUIType WebType { get; init; } = WebUIType.Hud;

    /// <summary>
    /// 要加载的网页 URL（支持 http/https 和本地协议）。
    /// </summary>
    public string Url { get; init; } = string.Empty;

    /// <summary>
    /// 浏览器渲染帧率（每秒帧数）。
    /// </summary>
    public int FrameRate { get; init; } = 60;

    /// <summary>
    /// 面板中心点在屏幕上的归一化位置（[0,1] 范围，原点在左上角，X 向右，Y 向下）。
    /// </summary>
    public (float X, float Y) Position { get; init; } = (0.5f, 0.5f);

    /// <summary>
    /// 面板相对于屏幕的归一化大小（[0,1] 范围）。
    /// </summary>
    public (float X, float Y) Size { get; init; } = (1f, 1f);

    /// <summary>
    /// 浏览器实际分辨率（像素）。
    /// <c>(0, 0)</c> 表示自动匹配 <see cref="Size"/>（推荐）。
    /// </summary>
    public (float X, float Y) Resolution { get; init; } = (0f, 0f);

    /// <summary>
    /// 在 Viewport 中的层级（Z-Order），数值越大越靠前。
    /// </summary>
    public int Layer { get; init; } = 0;

    /// <summary>
    /// 网页内容变化时持续缓存 Alpha 帧数，用于 HitTest 穿透功能。
    /// <c>0</c> 表示关闭该功能。Hud 类型默认等于 <see cref="FrameRate"/>（即持续缓存 1 秒）。
    /// </summary>
    public int CacheAlphaFrames { get; init; } = 0;
}
