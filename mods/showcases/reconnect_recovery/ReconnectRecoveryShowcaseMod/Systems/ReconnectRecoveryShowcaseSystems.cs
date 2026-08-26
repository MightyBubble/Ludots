using Ludots.Core.Engine;
using Ludots.Core.Input.Runtime;
using Ludots.Core.Scripting;
using ReconnectRecoveryShowcaseMod.Runtime;

namespace ReconnectRecoveryShowcaseMod.Systems;

internal sealed class ReconnectRecoveryShowcaseInputSystem : Arch.System.ISystem<float>
{
    private readonly GameEngine _engine;
    private readonly ReconnectRecoveryShowcaseRuntime _runtime;

    public ReconnectRecoveryShowcaseInputSystem(GameEngine engine, ReconnectRecoveryShowcaseRuntime runtime)
    {
        _engine = engine;
        _runtime = runtime;
    }

    public void Initialize() { }
    public void BeforeUpdate(in float t) { }
    public void AfterUpdate(in float t) { }
    public void Dispose() { }

    public void Update(in float t)
    {
        if (_engine.GetService(CoreServiceKeys.InputHandler) is not PlayerInputHandler input) return;
        if (input.PressedThisFrame(ReconnectRecoveryShowcaseIds.Checkpoint)) _runtime.RequestCheckpoint();
        if (input.PressedThisFrame(ReconnectRecoveryShowcaseIds.Disconnect)) _runtime.Disconnect();
        if (input.PressedThisFrame(ReconnectRecoveryShowcaseIds.ReconnectAuthority)) _runtime.ReconnectAuthority();
        if (input.PressedThisFrame(ReconnectRecoveryShowcaseIds.ReconnectReset)) _runtime.ReconnectLocalReset();
        if (input.PressedThisFrame(ReconnectRecoveryShowcaseIds.InjectMissing)) _runtime.InjectMissing();
        if (input.PressedThisFrame(ReconnectRecoveryShowcaseIds.InjectDuplicate)) _runtime.InjectDuplicate();
        if (input.PressedThisFrame(ReconnectRecoveryShowcaseIds.InjectStale)) _runtime.InjectStale();
        if (input.PressedThisFrame(ReconnectRecoveryShowcaseIds.InjectOutOfOrder)) _runtime.InjectOutOfOrder();
        if (input.PressedThisFrame(ReconnectRecoveryShowcaseIds.AdvanceAuthority)) _runtime.AdvanceAuthority();
    }
}

internal sealed class ReconnectRecoveryShowcasePresentationSystem : Arch.System.ISystem<float>
{
    private readonly GameEngine _engine;
    private readonly ReconnectRecoveryShowcaseRuntime _runtime;

    public ReconnectRecoveryShowcasePresentationSystem(GameEngine engine, ReconnectRecoveryShowcaseRuntime runtime)
    {
        _engine = engine;
        _runtime = runtime;
    }

    public void Initialize() { }
    public void BeforeUpdate(in float t) { }
    public void AfterUpdate(in float t) { }
    public void Dispose() { }
    public void Update(in float t) => _runtime.AdvanceFixedStep(_engine);
}
