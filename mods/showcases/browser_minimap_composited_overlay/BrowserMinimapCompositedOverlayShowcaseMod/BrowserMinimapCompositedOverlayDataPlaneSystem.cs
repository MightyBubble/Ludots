using Arch.System;
using Ludots.WebUI.DataPlane;

namespace BrowserMinimapCompositedOverlayShowcaseMod;

internal sealed class BrowserMinimapCompositedOverlayDataPlaneSystem : ISystem<float>
{
	private const float TopicPublishIntervalSeconds = 0.25f;

	private readonly WebUiDataPlaneTickPump _pump;
	private float _secondsSincePublish;
	private bool _disposed;

	public BrowserMinimapCompositedOverlayDataPlaneSystem(WebUiDataPlaneTickPump pump)
	{
		_pump = pump ?? throw new ArgumentNullException(nameof(pump));
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

		_pump.FlushCommandsAsync().AsTask().GetAwaiter().GetResult();
		_secondsSincePublish += MathF.Max(0f, dt);
		if (_secondsSincePublish < TopicPublishIntervalSeconds)
		{
			return;
		}

		_secondsSincePublish = 0f;
		_pump.PublishTopicsAsync().AsTask().GetAwaiter().GetResult();
	}

	public void AfterUpdate(in float dt)
	{
	}

	public void Dispose()
	{
		_disposed = true;
	}
}
