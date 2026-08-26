using DeterministicReplayShowcaseMod.Runtime;
using Ludots.Core.Engine;
using Ludots.Core.Input.Runtime;
using Ludots.Core.Scripting;

namespace DeterministicReplayShowcaseMod.Systems;

internal sealed class DeterministicReplayShowcaseInputSystem : Arch.System.ISystem<float>
{
    private readonly GameEngine _engine;
    private readonly DeterministicReplayShowcaseRuntime _runtime;

    public DeterministicReplayShowcaseInputSystem(GameEngine engine, DeterministicReplayShowcaseRuntime runtime)
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
        if (input.PressedThisFrame(DeterministicReplayShowcaseIds.RequestCheckpoint)) _runtime.RequestCheckpoint();
        if (input.PressedThisFrame(DeterministicReplayShowcaseIds.StartRecording)) _runtime.StartRecording();
        if (input.PressedThisFrame(DeterministicReplayShowcaseIds.StopRecording)) _runtime.StopRecording();
        if (input.PressedThisFrame(DeterministicReplayShowcaseIds.Play)) _runtime.Play();
        if (input.PressedThisFrame(DeterministicReplayShowcaseIds.Pause)) _runtime.TogglePause();
        if (input.PressedThisFrame(DeterministicReplayShowcaseIds.Step)) _runtime.Step();
        if (input.PressedThisFrame(DeterministicReplayShowcaseIds.Reset)) _runtime.Reset();
        if (input.PressedThisFrame(DeterministicReplayShowcaseIds.Speed)) _runtime.CycleSpeed();
        if (input.PressedThisFrame(DeterministicReplayShowcaseIds.JumpMid)) _runtime.JumpMid();
        if (input.PressedThisFrame(DeterministicReplayShowcaseIds.InjectDuringPlay)) _runtime.InjectDuringPlay();
        if (input.PressedThisFrame(DeterministicReplayShowcaseIds.SnapshotAblation)) _runtime.ToggleSnapshotAblation();
    }
}

internal sealed class DeterministicReplayShowcasePresentationSystem : Arch.System.ISystem<float>
{
    private readonly GameEngine _engine;
    private readonly DeterministicReplayShowcaseRuntime _runtime;

    public DeterministicReplayShowcasePresentationSystem(GameEngine engine, DeterministicReplayShowcaseRuntime runtime)
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
