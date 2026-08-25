using Arch.Core;
using Arch.System;
using Ludots.Core.Engine;
using Ludots.Core.Input.Runtime;
using PersistenceOnlineReplayShowcaseMod.Runtime;

namespace PersistenceOnlineReplayShowcaseMod.Systems;

internal sealed class PersistenceOnlineReplayInputSystem : BaseSystem<World, float>
{
    private readonly GameEngine _engine;
    private readonly PersistenceOnlineReplayRuntime _runtime;
    public PersistenceOnlineReplayInputSystem(GameEngine engine, PersistenceOnlineReplayRuntime runtime) : base(engine.World) { _engine = engine; _runtime = runtime; }
    public override void Update(in float dt)
    {
        if (!PersistenceOnlineReplayShowcaseIds.IsShowcaseMap(_engine.CurrentMapSession?.MapId.Value)) return;
        if (_runtime.IsReplayPlaying)
        {
            _runtime.AdvanceReplayFixedStep(_engine);
            return;
        }
        if (_engine.GetService(Ludots.Core.Scripting.CoreServiceKeys.AuthoritativeInput) is IInputActionReader input)
        {
            if (input.PressedThisFrame(PersistenceOnlineReplayShowcaseIds.RequestCheckpoint)) _runtime.RequestCheckpoint();
            if (input.PressedThisFrame(PersistenceOnlineReplayShowcaseIds.SaveSlot)) _runtime.SaveSlot();
            if (input.PressedThisFrame(PersistenceOnlineReplayShowcaseIds.RestoreSlot)) _runtime.RestoreSlot();
            if (input.PressedThisFrame(PersistenceOnlineReplayShowcaseIds.StartRecording)) _runtime.StartRecording();
            if (input.PressedThisFrame(PersistenceOnlineReplayShowcaseIds.StopRecording)) _runtime.StopRecording();
            if (input.PressedThisFrame(PersistenceOnlineReplayShowcaseIds.PlayReplay)) _runtime.PlayReplay();
            if (input.PressedThisFrame(PersistenceOnlineReplayShowcaseIds.SimulateDisconnect)) _runtime.SimulateDisconnect();
            if (input.PressedThisFrame(PersistenceOnlineReplayShowcaseIds.Reconnect)) _runtime.Reconnect();
            if (input.PressedThisFrame(PersistenceOnlineReplayShowcaseIds.AblateFrame)) _runtime.AblateFrame();
            if (input.PressedThisFrame(PersistenceOnlineReplayShowcaseIds.SwapFrames)) _runtime.SwapFrames();
            if (input.PressedThisFrame(PersistenceOnlineReplayShowcaseIds.ToggleReplayPause)) _runtime.ToggleReplayPause();
            if (input.PressedThisFrame(PersistenceOnlineReplayShowcaseIds.StepReplay)) _runtime.StepReplay();
            if (input.PressedThisFrame(PersistenceOnlineReplayShowcaseIds.ResetReplay)) _runtime.ResetReplay();
        }
        _runtime.AdvanceFixedStep(_engine);
    }
}
