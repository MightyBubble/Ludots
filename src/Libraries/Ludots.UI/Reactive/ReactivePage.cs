using System;
using Ludots.UI.Compose;
using Ludots.UI.Runtime;

namespace Ludots.UI.Reactive;

public sealed class ReactivePage<TState>
{
	private readonly Func<ReactiveContext<TState>, UiElementBuilder> _render;

	private readonly UiStyleSheet[] _styleSheets;

	private readonly ReactiveContext<TState> _context;

	public TState State { get; private set; }

	public UiScene Scene { get; }

	public UiThemePack? Theme { get; private set; }

	public ReactivePage(TState initialState, Func<ReactiveContext<TState>, UiElementBuilder> render, UiThemePack? theme = null, params UiStyleSheet[] styleSheets)
	{
		State = initialState;
		_render = render ?? throw new ArgumentNullException("render");
		Theme = theme;
		_styleSheets = styleSheets ?? Array.Empty<UiStyleSheet>();
		Scene = new UiScene();
		_context = new ReactiveContext<TState>(this);
		Recompose();
	}

	public void SetTheme(UiThemePack? theme)
	{
		Theme = theme;
		Recompose();
	}

	public void SetState(Func<TState, TState> updater)
	{
		ArgumentNullException.ThrowIfNull(updater, "updater");
		State = updater(State);
		Recompose();
	}

	public void Mutate(Action<TState> update)
	{
		ArgumentNullException.ThrowIfNull(update, "update");
		update(State);
		Recompose();
	}

	private void Recompose()
	{
		Scene.Dispatcher.Reset();
		int nextId = 1;
		UiNode root = _render(_context).Build(Scene.Dispatcher, ref nextId);
		Scene.Mount(root);
		if (_styleSheets.Length != 0)
		{
			Scene.SetStyleSheets(_styleSheets);
		}
		if (Theme != null)
		{
			Scene.SetTheme(Theme);
		}
	}
}
