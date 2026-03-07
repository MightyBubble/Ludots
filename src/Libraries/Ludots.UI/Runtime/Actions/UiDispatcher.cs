namespace Ludots.UI.Runtime.Actions;

public sealed class UiDispatcher
{
    private readonly Dictionary<int, Action<UiActionContext>> _handlers = new();
    private int _nextHandleValue = 1;

    public UiActionHandle Register(Action<UiActionContext> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);

        int handleValue = _nextHandleValue++;
        _handlers[handleValue] = handler;
        return new UiActionHandle(handleValue);
    }

    public bool Unregister(UiActionHandle handle)
    {
        return handle.IsValid && _handlers.Remove(handle.Value);
    }

    public bool Dispatch(UiActionHandle handle, UiActionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (!handle.IsValid || !_handlers.TryGetValue(handle.Value, out Action<UiActionContext>? handler))
        {
            return false;
        }

        handler(context);
        return true;
    }

    public void Reset()
    {
        _handlers.Clear();
        _nextHandleValue = 1;
    }
}
