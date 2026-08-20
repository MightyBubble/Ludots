using System;
using Arch.System;
using Ludots.UI.Surface;
using Ludots.WebUI.DataPlane;

namespace PanelSkinWebMod;

internal sealed class FireballWebSkinDataPlaneSystem : ISystem<float>
{
    private const float TopicPublishIntervalSeconds = 0.25f;

    private readonly WebUiDataPlaneTickPump _pump;
    private readonly UiSurfaceLeaseHandle _lease;
    private readonly IUiSurfaceHost _surfaceHost;
    private float _secondsSincePublish;
    private bool _disposed;

    public FireballWebSkinDataPlaneSystem(
        WebUiDataPlaneTickPump pump,
        IUiSurfaceHost surfaceHost,
        UiSurfaceLeaseHandle lease)
    {
        _pump = pump ?? throw new ArgumentNullException(nameof(pump));
        _surfaceHost = surfaceHost ?? throw new ArgumentNullException(nameof(surfaceHost));
        if (lease == default)
        {
            throw new ArgumentException("A valid surface lease handle is required.", nameof(lease));
        }

        _lease = lease;
    }

    public void Initialize() { }
    public void BeforeUpdate(in float dt) { }
    public void AfterUpdate(in float dt) { }

    public void Update(in float dt)
    {
        if (_disposed)
        {
            return;
        }

        // Browser frames advance on their own cadence; the composited canvas node only
        // re-rasterizes when its surface lease is invalidated, mirroring the native skins.
        _surfaceHost.Invalidate(_lease);

        _pump.FlushCommandsAsync().AsTask().GetAwaiter().GetResult();
        _secondsSincePublish += MathF.Max(0f, dt);
        if (_secondsSincePublish < TopicPublishIntervalSeconds)
        {
            return;
        }

        _secondsSincePublish = 0f;
        _pump.PublishTopicsAsync().AsTask().GetAwaiter().GetResult();
    }

    public void Dispose()
    {
        _disposed = true;
    }
}
