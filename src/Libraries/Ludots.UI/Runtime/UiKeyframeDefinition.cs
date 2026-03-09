using System;
using System.Collections.Generic;
using System.Linq;

namespace Ludots.UI.Runtime;

public sealed class UiKeyframeDefinition
{
	public string Name { get; }

	public IReadOnlyList<UiKeyframeStop> Stops { get; }

	public UiKeyframeDefinition(string name, IEnumerable<UiKeyframeStop> stops)
	{
		if (string.IsNullOrWhiteSpace(name))
		{
			throw new ArgumentException("Keyframe name is required.", "name");
		}
		ArgumentNullException.ThrowIfNull(stops, "stops");
		Name = name.Trim();
		Stops = stops.OrderBy((UiKeyframeStop stop) => stop.Offset).ToArray();
	}
}
