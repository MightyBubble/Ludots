using System;
using System.Numerics;
using Arch.Core;
using Arch.System;
using CapabilityStandardVirtualCameraShowcaseMod.Runtime;
using Ludots.Core.Components;
using Ludots.Core.Engine;
using Ludots.Core.EntityCollections;
using Ludots.Core.Gameplay.Camera;
using Ludots.Core.Input.CommandSources;
using Ludots.Core.Input.Runtime;
using Ludots.Core.Scripting;

namespace CapabilityStandardVirtualCameraShowcaseMod.Systems;

internal sealed class CapabilityStandardVirtualCameraImpulseSystem : ISystem<float>
{
    private readonly GameEngine _engine;
    private readonly CameraImpulseDemoConfig _config;
    private IInputActionReader? _input;
    private bool _inputValidated;

    public CapabilityStandardVirtualCameraImpulseSystem(
        GameEngine engine,
        CapabilityStandardVirtualCameraShowcaseConfig config)
    {
        _engine = engine ?? throw new ArgumentNullException(nameof(engine));
        _config = (config ?? throw new ArgumentNullException(nameof(config))).ImpulseDemo;
    }

    public void Initialize() { }
    public void BeforeUpdate(in float dt) { }
    public void AfterUpdate(in float dt) { }
    public void Dispose() { }

    public void Update(in float dt)
    {
        if (!_config.Enabled ||
            !CapabilityStandardVirtualCameraShowcaseIds.IsShowcaseMap(_engine.CurrentMapSession?.MapId.Value))
        {
            return;
        }

        EnsureInput();
        if (!_input!.PressedThisFrame(_config.TriggerActionId))
        {
            return;
        }

        CameraImpulseRuntime runtime = _engine.GetService(CoreServiceKeys.CameraImpulseRuntime)
            ?? throw new InvalidOperationException("Capability standard virtual camera impulse demo requires CameraImpulseRuntime.");
        Entity sourceEntity = ResolveCommandSourcePrimary();
        Vector2 positionCm = _engine.World.Get<WorldPositionCm>(sourceEntity).Value.ToVector2();

        runtime.Emit(new CameraImpulseSource
        {
            PositionCm = positionCm,
            HeightCm = _engine.GameSession.Camera.State.TargetHeightCm,
            InnerRadiusCm = _config.InnerRadiusCm,
            RadiusCm = _config.RadiusCm,
            DurationSeconds = _config.DurationSeconds,
            FrequencyHz = _config.FrequencyHz,
            PhaseRadians = _config.PhaseRadians,
            PositionAmplitudeCm = _config.PositionAmplitudeCm,
            YawAmplitudeDeg = _config.YawAmplitudeDeg,
            PitchAmplitudeDeg = _config.PitchAmplitudeDeg,
            Falloff = _config.Falloff
        });
    }

    private void EnsureInput()
    {
        if (_input == null)
        {
            _input = _engine.GetService(CoreServiceKeys.AuthoritativeInput)
                ?? throw new InvalidOperationException("Capability standard virtual camera impulse demo requires AuthoritativeInput.");
        }

        if (_inputValidated)
        {
            return;
        }

        PlayerInputHandler inputHandler = _engine.GetService(CoreServiceKeys.InputHandler)
            ?? throw new InvalidOperationException("Capability standard virtual camera impulse demo requires InputHandler.");
        if (!inputHandler.HasAction(_config.TriggerActionId))
        {
            throw new InvalidOperationException(
                $"Capability standard virtual camera impulse demo requires input action '{_config.TriggerActionId}'.");
        }

        _inputValidated = true;
    }

    private Entity ResolveCommandSourcePrimary()
    {
        if (!_engine.GlobalContext.TryGetValue(CoreServiceKeys.LocalPlayerEntity.Name, out object? localObj) ||
            localObj is not Entity localPlayer ||
            localPlayer == Entity.Null ||
            !_engine.World.IsAlive(localPlayer))
        {
            throw new InvalidOperationException(
                "Capability standard virtual camera impulse demo requires a live LocalPlayerEntity avatar.");
        }

        if (!_engine.GlobalContext.TryGetValue(CoreServiceKeys.EntityCollectionStore.Name, out object? collectionsObj) ||
            collectionsObj is not EntityCollectionStore collections)
        {
            throw new InvalidOperationException(
                "Capability standard virtual camera impulse demo requires EntityCollectionStore.");
        }

        if (!EntityCollectionContextRuntime.TryGetPrimary(
                _engine.World,
                collections,
                localPlayer,
                EntityCollectionKeys.CommandSource,
                out Entity controlledEntity))
        {
            throw new InvalidOperationException(
                "Capability standard virtual camera impulse demo requires a command source primary entity.");
        }

        if (!_engine.World.Has<WorldPositionCm>(controlledEntity))
        {
            throw new InvalidOperationException(
                "Capability standard virtual camera impulse demo command source primary requires WorldPositionCm.");
        }

        return controlledEntity;
    }
}
