using System;
using System.Collections.Generic;

namespace Ludots.UI.Runtime;

public sealed class UiThemePack
{
	public string Key { get; }

	public IReadOnlyList<UiStyleSheet> StyleSheets { get; }

	public UiThemePack(string key, params UiStyleSheet[] styleSheets)
	{
		if (string.IsNullOrWhiteSpace(key))
		{
			throw new ArgumentException("Theme key is required.", "key");
		}
		Key = key;
		StyleSheets = ((styleSheets != null && styleSheets.Length != 0) ? ((IReadOnlyList<UiStyleSheet>)styleSheets) : ((IReadOnlyList<UiStyleSheet>)Array.Empty<UiStyleSheet>()));
	}
}
