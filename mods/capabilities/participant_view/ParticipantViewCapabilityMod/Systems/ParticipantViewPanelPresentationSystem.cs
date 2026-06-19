using Arch.System;
using Ludots.Core.Engine;
using ParticipantViewCapabilityMod.Runtime;

namespace ParticipantViewCapabilityMod.Systems;

internal sealed class ParticipantViewPanelPresentationSystem : ISystem<float>
{
    private readonly GameEngine _engine;
    private readonly ParticipantViewCapabilityRuntime _runtime;

    public ParticipantViewPanelPresentationSystem(GameEngine engine, ParticipantViewCapabilityRuntime runtime)
    {
        _engine = engine;
        _runtime = runtime;
    }

    public void Initialize()
    {
    }

    public void BeforeUpdate(in float t)
    {
    }

    public void Update(in float t)
    {
        _runtime.RefreshPanel(_engine);
    }

    public void AfterUpdate(in float t)
    {
    }

    public void Dispose()
    {
    }
}
