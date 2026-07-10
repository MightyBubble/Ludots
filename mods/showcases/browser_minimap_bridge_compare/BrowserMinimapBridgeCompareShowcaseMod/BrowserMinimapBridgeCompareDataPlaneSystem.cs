using Arch.System;
using Ludots.WebUI.DataPlane;

namespace BrowserMinimapBridgeCompareShowcaseMod;

internal sealed class BrowserMinimapBridgeCompareDataPlaneSystem : ISystem<float>
{
	private readonly BrowserMinimapBridgeCompareMarkerWorld _world;
	private readonly WebUiDataPlaneTickPump _pump;
	private readonly float _publishIntervalSeconds;
	private float _secondsSincePublish;
	private bool _disposed;

	public BrowserMinimapBridgeCompareDataPlaneSystem(
		BrowserMinimapBridgeCompareMarkerWorld world,
		WebUiDataPlaneTickPump pump,
		float publishHz)
	{
		_world = world ?? throw new ArgumentNullException(nameof(world));
		_pump = pump ?? throw new ArgumentNullException(nameof(pump));
		_publishIntervalSeconds = 1f / Math.Clamp(publishHz, 1f, 60f);
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

		_world.Advance(MathF.Max(0f, dt));
		_secondsSincePublish += MathF.Max(0f, dt);
		if (_secondsSincePublish < _publishIntervalSeconds)
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
