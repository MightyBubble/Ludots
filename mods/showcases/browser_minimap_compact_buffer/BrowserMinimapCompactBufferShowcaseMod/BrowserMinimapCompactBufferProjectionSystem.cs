using Arch.System;
using Ludots.WebUI.DataPlane;

namespace BrowserMinimapCompactBufferShowcaseMod;

internal sealed class BrowserMinimapCompactBufferProjectionSystem : ISystem<float>
{
	private readonly WebUiDataPlaneTickPump _pump;
	private readonly float _publishIntervalSeconds;
	private float _secondsSincePublish;
	private bool _disposed;

	public BrowserMinimapCompactBufferProjectionSystem(WebUiDataPlaneTickPump pump, float publishHz)
	{
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
