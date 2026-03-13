using System;
using Ludots.UI.Runtime;

namespace Ludots.UI.Reactive;

public sealed class ReactiveContext<TState>
{
	private readonly ReactivePage<TState> _page;

	public TState State => _page.State;

	public UiScene Scene => _page.Scene;

	internal ReactiveContext(ReactivePage<TState> page)
	{
		_page = page;
	}

	public void SetState(Func<TState, TState> updater)
	{
		_page.SetState(updater);
	}

	public void Mutate(Action<TState> update)
	{
		_page.Mutate(update);
	}

	public UiVirtualWindow GetVerticalVirtualWindow(string hostElementId, int totalCount, float itemExtent, float viewportExtent, int overscan = 2)
	{
		return _page.GetVerticalVirtualWindow(hostElementId, totalCount, itemExtent, viewportExtent, overscan);
	}
}
