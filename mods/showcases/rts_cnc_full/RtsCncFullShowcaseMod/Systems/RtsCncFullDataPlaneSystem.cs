using Arch.System;
using Ludots.WebUI.DataPlane;

namespace RtsCncFullShowcaseMod.Systems;

internal sealed class RtsCncFullDataPlaneSystem : ISystem<float>
{
    private const float PublishIntervalSeconds = 0.1f;

    private readonly WebUiDataPlaneTickPump _pump;
    private float _elapsedSeconds;
    private bool _disposed;

    public RtsCncFullDataPlaneSystem(WebUiDataPlaneTickPump pump)
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
        _elapsedSeconds += MathF.Max(0f, dt);
        if (_elapsedSeconds < PublishIntervalSeconds)
        {
            return;
        }

        _elapsedSeconds = 0f;
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
