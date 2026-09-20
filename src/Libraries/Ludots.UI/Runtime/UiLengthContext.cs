using System;

namespace Ludots.UI.Runtime;

/// <summary>
/// Explicit resolution context for <see cref="UiLength"/>. Carries the containing-block
/// size (for %) and the scene viewport size (for vw/vh/vmin/vmax). Passed, never stored
/// statically, so resolution stays deterministic per layout pass.
/// </summary>
public readonly struct UiLengthContext
{
	/// <summary>Containing block size along the axis being resolved (percent reference).</summary>
	public float Available { get; }

	/// <summary>Scene viewport width (100vw reference).</summary>
	public float ViewportWidth { get; }

	/// <summary>Scene viewport height (100vh reference).</summary>
	public float ViewportHeight { get; }

	public UiLengthContext(float available, float viewportWidth = 0f, float viewportHeight = 0f)
	{
		Available = available;
		ViewportWidth = viewportWidth;
		ViewportHeight = viewportHeight;
	}

	public bool HasViewport => ViewportWidth > 0f && ViewportHeight > 0f;

	public float ViewportMin => MathF.Min(ViewportWidth, ViewportHeight);

	public float ViewportMax => MathF.Max(ViewportWidth, ViewportHeight);
}
