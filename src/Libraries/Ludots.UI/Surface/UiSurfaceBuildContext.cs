using System;
using Ludots.UI.Runtime;

namespace Ludots.UI.Surface;

public sealed class UiSurfaceBuildContext
{
	public UiScene Scene { get; }

	internal UiSurfaceBuildContext(UiScene scene)
	{
		Scene = scene ?? throw new ArgumentNullException(nameof(scene));
	}
}
