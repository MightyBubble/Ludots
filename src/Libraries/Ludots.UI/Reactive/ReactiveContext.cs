using System;

namespace Ludots.UI.Reactive;

public sealed class ReactiveContext<TState>
{
	private readonly ReactivePage<TState> _page;

	public TState State => _page.State;

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
}
