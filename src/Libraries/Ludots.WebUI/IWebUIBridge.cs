using System.Threading.Tasks;

namespace Ludots.WebUI;

/// <summary>
/// WebUI 桥接接口，解耦 Ludots Mod 与宿主平台（如 UE5 CEF/BluBrowser）之间的通信。
/// <para>
/// Mod 通过 GlobalContext 获取该接口的实现：
/// <code>
/// var bridge = context.GetEngine()?.GlobalContext[WebUIContextKeys.Bridge] as IWebUIBridge;
/// </code>
/// 宿主平台（UE5 Host System）在引擎启动时注入具体实现。
/// </para>
/// <para>
/// 典型使用流程：<see cref="Init"/> → <see cref="Open"/> → 事件通信 → <see cref="Close"/>。
/// </para>
/// </summary>
public interface IWebUIBridge
{
    // ==================== 生命周期 ====================

    /// <summary>
    /// 初始化 WebUI 面板（加载浏览器实例并注册基础事件）。
    /// <para>必须在 <see cref="Open"/> 之前调用，且只能调用一次。</para>
    /// </summary>
    /// <param name="settings">面板初始化参数（URL、大小、层级等）。</param>
    void Init(WebUISettings settings);

    /// <summary>
    /// 打开并显示 WebUI 面板。
    /// <para>对应 UE5 侧 <c>Web.Open()</c>，同时将 <see cref="IsOpen"/> 和 <see cref="IsShow"/> 置为 true。</para>
    /// </summary>
    void Open();

    /// <summary>
    /// 关闭 WebUI 面板（销毁浏览器实例）。
    /// <para>对应 UE5 侧 <c>Web.Close()</c>，同时将 <see cref="IsOpen"/> 和 <see cref="IsShow"/> 置为 false。</para>
    /// </summary>
    void Close();

    /// <summary>
    /// 显示已打开的 WebUI 面板（不重建浏览器实例）。
    /// <para>对应 UE5 侧 <c>Web.Show()</c>，将 <see cref="IsShow"/> 置为 true。</para>
    /// </summary>
    void Show();

    /// <summary>
    /// 隐藏 WebUI 面板（不销毁浏览器实例）。
    /// <para>对应 UE5 侧 <c>Web.Hide()</c>，将 <see cref="IsShow"/> 置为 false。</para>
    /// </summary>
    void Hide();

    /// <summary>
    /// 刷新 WebUI 面板（等效于先 Close 再 Open）。
    /// <para>对应 UE5 侧 <c>Web.Refresh()</c>。</para>
    /// </summary>
    void Refresh();

    /// <summary>
    /// 获取当前 WebUI 面板是否已打开（浏览器实例存在）。
    /// <para>对应 UE5 侧 <c>Web.IsOpen</c>。</para>
    /// </summary>
    bool IsOpen { get; }

    /// <summary>
    /// 获取当前 WebUI 面板是否可见（已打开且未隐藏）。
    /// <para>对应 UE5 侧 <c>Web.IsShow</c>。</para>
    /// </summary>
    bool IsShow { get; }

    // ==================== 事件通信 ====================

    /// <summary>
    /// 向 Web 层发送事件（JS 可监听该事件）。
    /// <para>该方法为 fire-and-forget，不等待 JS 回调。</para>
    /// </summary>
    /// <param name="eventName">事件名称，建议使用 <c>domain.action</c> 格式，如 <c>"game.playerDied"</c>。</param>
    /// <param name="payload">事件附带数据（任意可 JSON 序列化的对象）。可为 null。</param>
    void SendToWeb(string eventName, object? payload = null);

    /// <summary>
    /// 注册一个来自 Web 层的事件处理器（JS 触发，C# 响应）。
    /// </summary>
    /// <param name="eventName">Web 侧触发的事件名称。</param>
    /// <param name="handler">
    /// 异步处理器，接收事件负载字符串，返回可选的响应数据字符串。
    /// 若返回值非 null，将作为该次调用的响应回传给 JS。
    /// </param>
    /// <param name="owner">
    /// 事件所有者（弱引用追踪）。当 <paramref name="owner"/> 被 GC 回收后，
    /// 该处理器将自动注销，防止悬挂引用。可传 null 表示全局生命周期。
    /// </param>
    void RegisterWebEvent(string eventName, Func<string?, Task<string?>> handler, object? owner = null);

    /// <summary>
    /// 注销指定事件名的所有处理器。
    /// </summary>
    /// <param name="eventName">要注销的事件名称。</param>
    void UnregisterWebEvent(string eventName);
}
