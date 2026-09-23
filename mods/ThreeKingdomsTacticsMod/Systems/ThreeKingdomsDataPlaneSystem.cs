using Arch.System;
using Ludots.WebUI.DataPlane;

namespace ThreeKingdomsTacticsMod.Systems;

internal sealed class ThreeKingdomsDataPlaneSystem : ISystem<float>
{
    private const float PublishIntervalSeconds = 0.1f;
    private readonly WebUiDataPlaneTickPump _pump;
    private float _elapsed;
    private bool _disposed;

    public ThreeKingdomsDataPlaneSystem(WebUiDataPlaneTickPump pump)
    {
        _pump = pump ?? throw new ArgumentNullException(nameof(pump));
    }

    public void Initialize() { }
    public void BeforeUpdate(in float t) { }
    public void AfterUpdate(in float t) { }

    public void Update(in float t)
    {
        if (_disposed)
        {
            return;
        }

        _pump.FlushCommandsAsync().AsTask().GetAwaiter().GetResult();
        _elapsed += MathF.Max(0f, t);
        if (_elapsed < PublishIntervalSeconds)
        {
            return;
        }

        _elapsed = 0f;
        _pump.PublishTopicsAsync().AsTask().GetAwaiter().GetResult();
    }

    public void Dispose()
    {
        _disposed = true;
    }
}
