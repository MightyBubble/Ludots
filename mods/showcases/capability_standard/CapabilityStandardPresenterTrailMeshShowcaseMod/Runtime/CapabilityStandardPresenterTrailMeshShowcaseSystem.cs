using System;
using System.Numerics;
using Arch.Core;
using Arch.System;
using Ludots.Core.Components;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.Input.Runtime;
using Ludots.Core.Presentation.Commands;
using Ludots.Core.Presentation.Components;
using Ludots.Core.Presentation.Events;
using Ludots.Core.Presentation.Presenters;
using Ludots.Core.Scripting;
using Ludots.Platform.Abstractions;

namespace CapabilityStandardPresenterTrailMeshShowcaseMod.Runtime;

public sealed class CapabilityStandardPresenterTrailMeshShowcaseSystem : ISystem<float>
{
    public const string MapId = "capability_standard_presenter_trailmesh_showcase";
    public const string BladeDefinitionKey = "trailmesh_showcase.blade";
    public const string ToggleTrailActionId = "TrailMeshShowcase.ToggleTrail";

    private const int BladeOwnerStableId = 61801;
    private const float OrbitRadiusMeters = 4f;
    private const float OrbitRadiansPerSecond = 1.6f;
    private const float SpinRadiansPerSecond = 4.5f;

    private readonly GameEngine _engine;
    private readonly World _world;
    private readonly PlayerInputHandler _input;
    private readonly PresenterCommandBuffer _presenterCommands;
    private readonly int _bladeDefinitionId;
    private readonly int _trailOnKeyId;
    private readonly int _trailOffKeyId;

    private Entity _bladeOwner = Entity.Null;
    private float _orbitPhase;
    private float _spinPhase;
    private bool _trailActive = true;

    public CapabilityStandardPresenterTrailMeshShowcaseSystem(GameEngine engine)
    {
        _engine = engine ?? throw new ArgumentNullException(nameof(engine));
        _world = engine.World;
        _input = engine.GetService(CoreServiceKeys.InputHandler)
            ?? throw new InvalidOperationException("TrailMesh showcase requires InputHandler.");
        _presenterCommands = engine.GetService(CoreServiceKeys.PresenterCommandBuffer)
            ?? throw new InvalidOperationException("TrailMesh showcase requires PresenterCommandBuffer.");
        var definitions = engine.GetService(CoreServiceKeys.PresenterDefinitionRegistry)
            ?? throw new InvalidOperationException("TrailMesh showcase requires PresenterDefinitionRegistry.");
        _bladeDefinitionId = definitions.GetId(BladeDefinitionKey);
        if (_bladeDefinitionId <= 0)
        {
            throw new InvalidOperationException(
                $"TrailMesh showcase presenter definition '{BladeDefinitionKey}' is not registered.");
        }

        _trailOnKeyId = TagRegistry.Register("trailmesh_showcase.trail.on");
        _trailOffKeyId = TagRegistry.Register("trailmesh_showcase.trail.off");
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
        AdvanceBladeMotion(dt);
        PollActions();
    }

    private void EnsureShowcaseSpawned()
    {
        if (_bladeOwner != Entity.Null)
        {
            return;
        }

        _bladeOwner = CreateOwner(BladeOwnerStableId, new Vector3(OrbitRadiusMeters, 1.2f, 0f));
        EnqueuePresenterCreate(_bladeDefinitionId, "trailmesh_showcase.blade", _bladeOwner);
    }

    private void AdvanceBladeMotion(float dt)
    {
        if (_bladeOwner == Entity.Null || !_world.IsAlive(_bladeOwner) || !_world.Has<VisualTransform>(_bladeOwner))
        {
            return;
        }

        _orbitPhase += OrbitRadiansPerSecond * dt;
        _spinPhase += SpinRadiansPerSecond * dt;
        ref VisualTransform transform = ref _world.Get<VisualTransform>(_bladeOwner);
        transform.Position = new Vector3(
            MathF.Cos(_orbitPhase) * OrbitRadiusMeters,
            1.2f,
            MathF.Sin(_orbitPhase) * OrbitRadiusMeters);
        transform.Rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitY, _spinPhase);
    }

    private void PollActions()
    {
        if (!_input.PressedThisFrame(ToggleTrailActionId))
        {
            return;
        }

        _trailActive = !_trailActive;
        PublishGameplayEvent(_trailActive ? _trailOnKeyId : _trailOffKeyId, _bladeOwner);
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
            throw new InvalidOperationException("PresenterCommandBuffer overflowed while creating the TrailMesh showcase hierarchy.");
        }
    }

    private void PublishGameplayEvent(int keyId, Entity source)
    {
        if (_engine.GetService(CoreServiceKeys.PresentationEventStream) is not PresentationEventStream events)
        {
            throw new InvalidOperationException("TrailMesh showcase requires PresentationEventStream.");
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
            throw new InvalidOperationException("PresentationEventStream overflowed while publishing the TrailMesh showcase event.");
        }
    }

    public void Dispose()
    {
    }
}
