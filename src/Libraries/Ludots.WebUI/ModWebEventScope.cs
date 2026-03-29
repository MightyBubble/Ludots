using System.Threading.Tasks;

namespace Ludots.WebUI;

/// <summary>
/// 为 Ludots Mod 提供 WebUI 事件的便捷作用域封装。
/// <para>
/// 通过 <c>using</c> 语句创建作用域，在作用域销毁时自动注销所有已注册的 Web 事件，
/// 防止 Mod 卸载后出现悬挂的事件处理器。
/// </para>
/// </summary>
/// <example>
/// <code>
/// // 在 Trigger 或 System 中：
/// using var scope = new ModWebEventScope(bridge, this);
/// scope.Register("ui.buyItem", async payload =>
/// {
///     // 处理购买逻辑...
///     return """{"success": true}""";
/// });
/// // scope Dispose 时自动调用 bridge.UnregisterWebEvent 清理
/// </code>
/// </example>
public sealed class ModWebEventScope : IDisposable
{
    private readonly IWebUIBridge _bridge;
    private readonly object? _owner;
    private readonly List<string> _registeredEvents = [];
    private bool _disposed;

    /// <summary>
    /// 初始化 WebUI 事件作用域。
    /// </summary>
    /// <param name="bridge">WebUI 桥接实现（从 GlobalContext 获取）。</param>
    /// <param name="owner">所有者对象（弱引用），用于 GC 安全清理。通常传入持有该 Scope 的 Mod 对象。</param>
    public ModWebEventScope(IWebUIBridge bridge, object? owner = null)
    {
        _bridge = bridge ?? throw new ArgumentNullException(nameof(bridge));
        _owner = owner;
    }

    /// <summary>
    /// 在此作用域内注册一个 Web 事件处理器。
    /// 作用域 Dispose 时将自动注销该事件。
    /// </summary>
    /// <param name="eventName">Web 侧触发的事件名称。</param>
    /// <param name="handler">异步处理器，返回值将作为响应回传给 JS。</param>
    public void Register(string eventName, Func<string?, Task<string?>> handler)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _bridge.RegisterWebEvent(eventName, handler, _owner);
        _registeredEvents.Add(eventName);
    }

    /// <summary>
    /// 向 Web 层发送事件（透传到 <see cref="IWebUIBridge.SendToWeb"/>）。
    /// </summary>
    public void Send(string eventName, object? payload = null)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _bridge.SendToWeb(eventName, payload);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        foreach (var eventName in _registeredEvents)
        {
            _bridge.UnregisterWebEvent(eventName);
        }
        _registeredEvents.Clear();
    }
}
