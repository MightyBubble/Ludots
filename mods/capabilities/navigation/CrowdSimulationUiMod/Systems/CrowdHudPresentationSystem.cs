using Arch.System;
using CrowdSimulationUiMod.UI;
using Ludots.Core.Engine;

namespace CrowdSimulationUiMod.Systems;

/// <summary>呈现帧驱动 HUD 控制器。不在固定步里，仿真 tick 不经过这里。</summary>
internal sealed class CrowdHudPresentationSystem : ISystem<float>
{
    private readonly GameEngine _engine;
    private readonly CrowdHudController _controller;

    public CrowdHudPresentationSystem(GameEngine engine, CrowdHudController controller)
    {
        _engine = engine ?? throw new ArgumentNullException(nameof(engine));
        _controller = controller ?? throw new ArgumentNullException(nameof(controller));
    }

    public void Initialize() { }
    public void BeforeUpdate(in float t) { }
    public void Update(in float t) => _controller.Tick(_engine);
    public void AfterUpdate(in float t) { }
    public void Dispose() => _controller.Unmount();
}
