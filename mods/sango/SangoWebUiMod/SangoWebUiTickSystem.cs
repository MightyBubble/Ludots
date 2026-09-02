using Arch.System;
using Ludots.WebUI.DataPlane;

namespace Sango.WebUi;

// 照 SangoWebUiSpikeMod 范式:命令在每引擎 tick 冲刷,话题按固定间隔节流推送。
internal sealed class SangoWebUiTickSystem : ISystem<float>
{
    private const float TopicPublishIntervalSeconds = 0.1f;

    private readonly WebUiDataPlaneTickPump _pump;
    private float _secondsSincePublish;
    private bool _disposed;

    public SangoWebUiTickSystem(WebUiDataPlaneTickPump pump)
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
