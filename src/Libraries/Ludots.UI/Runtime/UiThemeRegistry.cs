using System;
using System.Collections.Generic;

namespace Ludots.UI.Runtime;

public sealed class UiThemeRegistry
{
	private readonly Dictionary<string, UiThemePack> _themes = new Dictionary<string, UiThemePack>(StringComparer.OrdinalIgnoreCase);

	public void Register(UiThemePack theme)
	{
		ArgumentNullException.ThrowIfNull(theme, "theme");
		_themes[theme.Key] = theme;
	}

	public bool TryGet(string key, out UiThemePack theme)
	{
		return _themes.TryGetValue(key, out theme);
	}

	public UiThemePack GetRequired(string key)
	{
		if (!_themes.TryGetValue(key, out UiThemePack value))
		{
			throw new InvalidOperationException("Theme '" + key + "' is not registered.");
		}
		return value;
	}
}
