using System;
using System.Collections.Generic;

namespace Ludots.UI.Runtime.Actions;

public sealed class UiDispatcher
{
	private readonly Dictionary<int, Action<UiActionContext>> _handlers = new Dictionary<int, Action<UiActionContext>>();

	private int _nextHandleValue = 1;

	public UiActionHandle Register(Action<UiActionContext> handler)
	{
		ArgumentNullException.ThrowIfNull(handler, "handler");
		int num = _nextHandleValue++;
		_handlers[num] = handler;
		return new UiActionHandle(num);
	}

	public bool Unregister(UiActionHandle handle)
	{
		return handle.IsValid && _handlers.Remove(handle.Value);
	}

	public bool Dispatch(UiActionHandle handle, UiActionContext context)
	{
		ArgumentNullException.ThrowIfNull(context, "context");
		if (!handle.IsValid || !_handlers.TryGetValue(handle.Value, out Action<UiActionContext> value))
		{
			return false;
		}
		value(context);
		return true;
	}

	public void Reset()
	{
		_handlers.Clear();
		_nextHandleValue = 1;
	}
}
