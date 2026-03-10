using System;
using System.Collections.Generic;
using System.Linq;

namespace Ludots.UI.Runtime;

public sealed record UiTransitionSpec(IReadOnlyList<UiTransitionEntry> Entries)
{
	public bool TryGet(string propertyName, out UiTransitionEntry? entry)
	{
		entry = Entries.FirstOrDefault((UiTransitionEntry item) => string.Equals(item.PropertyName, propertyName, StringComparison.OrdinalIgnoreCase) || string.Equals(item.PropertyName, "all", StringComparison.OrdinalIgnoreCase));
		return entry != null;
	}
}
