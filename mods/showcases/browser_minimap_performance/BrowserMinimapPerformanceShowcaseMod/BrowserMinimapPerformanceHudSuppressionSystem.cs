using Arch.System;
using Ludots.Core.Engine;
using Ludots.Core.Presentation.Hud;
using Ludots.Core.Scripting;

namespace BrowserMinimapPerformanceShowcaseMod;

internal sealed class BrowserMinimapPerformanceHudSuppressionSystem : ISystem<float>
{
	private readonly GameEngine _engine;
	private bool _disposed;

	public BrowserMinimapPerformanceHudSuppressionSystem(GameEngine engine)
	{
		_engine = engine ?? throw new ArgumentNullException(nameof(engine));
	}

	public void Initialize()
	{
	}

	public void BeforeUpdate(in float dt)
	{
	}

	public void Update(in float dt)
	{
		if (_disposed)
		{
			return;
		}

		_engine.GetService(CoreServiceKeys.PresentationWorldHudBuffer)?.Clear();
		_engine.GetService(CoreServiceKeys.PresentationScreenHudBuffer)?.Clear();
		_engine.GetService(CoreServiceKeys.ScreenOverlayBuffer)?.Clear();
		if (_engine.GetService(CoreServiceKeys.RenderDebugState) is RenderDebugState renderDebug)
		{
			renderDebug.DrawWorldHudBars = false;
			renderDebug.DrawWorldHudText = false;
			renderDebug.DrawCombatText = false;
			renderDebug.DrawDebugDraw = false;
		}
	}

	public void AfterUpdate(in float dt)
	{
	}

	public void Dispose()
	{
		_disposed = true;
	}
}
