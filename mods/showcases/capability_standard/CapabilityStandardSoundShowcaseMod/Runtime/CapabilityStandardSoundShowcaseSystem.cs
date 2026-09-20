using System;
using System.Numerics;
using Arch.Core;
using Arch.System;
using Ludots.Core.Components;
using Ludots.Core.Engine;
using Ludots.Core.Input.Runtime;
using Ludots.Core.Presentation.Commands;
using Ludots.Core.Presentation.Components;
using Ludots.Core.Presentation.Events;
using Ludots.Core.Presentation.Presenters;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.Scripting;
using Ludots.Platform.Abstractions;

namespace CapabilityStandardSoundShowcaseMod.Runtime;

public sealed class CapabilityStandardSoundShowcaseSystem : ISystem<float>
{
    public const string MapId = "capability_standard_sound_showcase";
    public const string EmitterDefinitionKey = "sound_showcase.emitter";
    public const string BeaconDefinitionKey = "sound_showcase.beacon";
    public const string ToggleEmitterActionId = "SoundShowcase.ToggleEmitter";
    public const string FireBeaconActionId = "SoundShowcase.FireBeacon";
    public const string StopAllActionId = "SoundShowcase.StopAll";

    private const int EmitterOwnerStableId = 61701;
    private const int BeaconOwnerStableId = 61702;
    private const float OrbitRadiusMeters = 8f;
    private const float OrbitRadiansPerSecond = 0.8f;
    private static readonly Vector3 BeaconPosition = new(-10f, 0.5f, 6f);

    private readonly GameEngine _engine;
    private readonly World _world;
    private readonly IInputActionReader _input;
    private readonly PresenterCommandBuffer _presenterCommands;
    private readonly int _emitterDefinitionId;
    private readonly int _beaconDefinitionId;
    private readonly int _emitterOnKeyId;
    private readonly int _emitterOffKeyId;
    private readonly int _beaconOnKeyId;
    private readonly int _beaconOffKeyId;

    private Entity _emitterOwner = Entity.Null;
    private Entity _beaconOwner = Entity.Null;
    private float _orbitPhase;
    private bool _emitterActive;
    private bool _beaconActive;

    public CapabilityStandardSoundShowcaseSystem(GameEngine engine)
    {
        _engine = engine ?? throw new ArgumentNullException(nameof(engine));
        _world = engine.World;
        // This system runs in the fixed-step InputCollection group, so it reads the
        // per-tick frozen snapshot: the live handler's PressedThisFrame edge only spans one
        // visual frame and is lost whenever the pacemaker skips the logic tick.
        _input = engine.GetService(CoreServiceKeys.AuthoritativeInput)
            ?? throw new InvalidOperationException("Sound showcase requires the authoritative input snapshot.");
        _presenterCommands = engine.GetService(CoreServiceKeys.PresenterCommandBuffer)
            ?? throw new InvalidOperationException("Sound showcase requires PresenterCommandBuffer.");
        var definitions = engine.GetService(CoreServiceKeys.PresenterDefinitionRegistry)
            ?? throw new InvalidOperationException("Sound showcase requires PresenterDefinitionRegistry.");
        _emitterDefinitionId = definitions.GetId(EmitterDefinitionKey);
        _beaconDefinitionId = definitions.GetId(BeaconDefinitionKey);
        if (_emitterDefinitionId <= 0 || _beaconDefinitionId <= 0)
        {
            throw new InvalidOperationException(
                $"Sound showcase presenter definitions '{EmitterDefinitionKey}'/'{BeaconDefinitionKey}' are not registered.");
        }

        _emitterOnKeyId = TagRegistry.Register("sound_showcase.emitter.on");
        _emitterOffKeyId = TagRegistry.Register("sound_showcase.emitter.off");
        _beaconOnKeyId = TagRegistry.Register("sound_showcase.beacon.on");
        _beaconOffKeyId = TagRegistry.Register("sound_showcase.beacon.off");
    }

    public void Initialize()
    {
    }

    public void BeforeUpdate(in float dt)
    {
    }

    public void AfterUpdate(in float dt)
    {
    }

    public void Update(in float dt)
    {
        if (!string.Equals(_engine.CurrentMapSession?.MapId.Value, MapId, StringComparison.Ordinal))
        {
            return;
        }

        EnsureShowcaseSpawned();
        AdvanceEmitterOrbit(dt);
        PollActions();
    }

    private void EnsureShowcaseSpawned()
    {
        if (_emitterOwner != Entity.Null && _beaconOwner != Entity.Null)
        {
            return;
        }

        _emitterOwner = CreateOwner(EmitterOwnerStableId, new Vector3(OrbitRadiusMeters, 0.5f, 0f));
        _beaconOwner = CreateOwner(BeaconOwnerStableId, BeaconPosition);
        EnqueuePresenterCreate(_emitterDefinitionId, "sound_showcase.emitter", _emitterOwner);
        EnqueuePresenterCreate(_beaconDefinitionId, "sound_showcase.beacon", _beaconOwner);
    }

    private void AdvanceEmitterOrbit(float dt)
    {
        if (_emitterOwner == Entity.Null || !_world.IsAlive(_emitterOwner) || !_world.Has<VisualTransform>(_emitterOwner))
        {
            return;
        }

        _orbitPhase += OrbitRadiansPerSecond * dt;
        _world.Get<VisualTransform>(_emitterOwner).Position = new Vector3(
            MathF.Cos(_orbitPhase) * OrbitRadiusMeters,
            0.5f,
            MathF.Sin(_orbitPhase) * OrbitRadiusMeters);
    }

    private void PollActions()
    {
        if (_input.PressedThisFrame(ToggleEmitterActionId))
        {
            _emitterActive = !_emitterActive;
            PublishGameplayEvent(
                _emitterActive ? _emitterOnKeyId : _emitterOffKeyId,
                _emitterOwner);
        }

        if (_input.PressedThisFrame(FireBeaconActionId))
        {
            _beaconActive = !_beaconActive;
            PublishGameplayEvent(
                _beaconActive ? _beaconOnKeyId : _beaconOffKeyId,
                _beaconOwner);
        }

        if (_input.PressedThisFrame(StopAllActionId))
        {
            _emitterActive = false;
            _beaconActive = false;
            PublishGameplayEvent(_emitterOffKeyId, _emitterOwner);
            PublishGameplayEvent(_beaconOffKeyId, _beaconOwner);
        }
    }

    private Entity CreateOwner(int stableId, Vector3 position)
    {
        return _world.Create(
            new VisualTransform
            {
                Position = position,
                Rotation = Quaternion.Identity,
                Scale = Vector3.One,
            },
            new PresentationStableId { Value = stableId });
    }

    private void EnqueuePresenterCreate(int definitionId, string scopeTagName, Entity source)
    {
        if (!_presenterCommands.TryAdd(new PresenterCommand
            {
                CommandKind = PresenterCommandKind.CreatePresenter,
                CommandKindId = (byte)PresenterCommandKind.CreatePresenter,
                RouteStrategy = PresenterCommandRouteStrategy.CreatePresenter,
                PresenterDefinitionId = definitionId,
                ScopeTag = PresenterScopeTagRegistry.Register(scopeTagName),
                ScopeSource = PresenterCommandScopeSource.Fixed,
                AnchorKind = PresentationAnchorKind.Entity,
                Source = source,
            }))
        {
            throw new InvalidOperationException("PresenterCommandBuffer overflowed while creating the sound showcase hierarchy.");
        }
    }

    private void PublishGameplayEvent(int keyId, Entity source)
    {
        if (_engine.GetService(CoreServiceKeys.PresentationEventStream) is not PresentationEventStream events)
        {
            throw new InvalidOperationException("Sound showcase requires PresentationEventStream.");
        }

        if (!events.TryAdd(new PresentationEvent
            {
                Kind = PresentationEventKind.GameplayEvent,
                KeyId = keyId,
                Source = source,
                Target = source,
                Position = Vector3.Zero,
            }))
        {
            throw new InvalidOperationException("PresentationEventStream overflowed while publishing the sound showcase event.");
        }
    }

    public void Dispose()
    {
    }
}
