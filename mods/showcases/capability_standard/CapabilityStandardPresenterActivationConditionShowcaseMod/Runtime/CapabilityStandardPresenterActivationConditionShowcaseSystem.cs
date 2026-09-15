using System;
using System.Numerics;
using Arch.Core;
using Arch.System;
using Ludots.Core.Components;
using Ludots.Core.Engine;
using Ludots.Core.Input.Runtime;
using Ludots.Core.Presentation.Commands;
using Ludots.Core.Presentation.Components;
using Ludots.Core.Presentation.Presenters;
using Ludots.Core.Scripting;
using Ludots.Platform.Abstractions;

namespace CapabilityStandardPresenterActivationConditionShowcaseMod.Runtime;

public sealed class CapabilityStandardPresenterActivationConditionShowcaseSystem : ISystem<float>
{
    public const string MapId = "capability_standard_presenter_activation_condition_showcase";
    public const string BeaconDefinitionKey = "activation_condition_showcase.beacon";
    public const string MarkerDefinitionKey = "activation_condition_showcase.station_marker";
    public const string RespawnWithTransformActionId = "ActivationConditionShowcase.RespawnFalseWithTransform";
    public const string RespawnWithoutTransformActionId = "ActivationConditionShowcase.RespawnFalseWithoutTransform";

    private const int TrueOwnerStableId = 62001;
    private const int FalseOwnerStableId = 62002;
    private const int TrueMarkerStableId = 62011;
    private const int FalseMarkerStableId = 62012;
    private const string FalseBeaconScopeTag = "activation_condition_showcase.beacon.false";

    private static readonly Vector3 TrueStationPosition = new(-4f, 0.6f, 0f);
    private static readonly Vector3 FalseStationPosition = new(4f, 0.6f, 0f);

    private readonly GameEngine _engine;
    private readonly World _world;
    private readonly PlayerInputHandler _input;
    private readonly PresenterCommandBuffer _presenterCommands;
    private readonly int _beaconDefinitionId;
    private readonly int _markerDefinitionId;

    private Entity _trueOwner = Entity.Null;
    private Entity _falseOwner = Entity.Null;
    private bool _spawned;

    public CapabilityStandardPresenterActivationConditionShowcaseSystem(GameEngine engine)
    {
        _engine = engine ?? throw new ArgumentNullException(nameof(engine));
        _world = engine.World;
        _input = engine.GetService(CoreServiceKeys.InputHandler)
            ?? throw new InvalidOperationException("ActivationCondition showcase requires InputHandler.");
        _presenterCommands = engine.GetService(CoreServiceKeys.PresenterCommandBuffer)
            ?? throw new InvalidOperationException("ActivationCondition showcase requires PresenterCommandBuffer.");
        var definitions = engine.GetService(CoreServiceKeys.PresenterDefinitionRegistry)
            ?? throw new InvalidOperationException("ActivationCondition showcase requires PresenterDefinitionRegistry.");
        _beaconDefinitionId = definitions.GetId(BeaconDefinitionKey);
        _markerDefinitionId = definitions.GetId(MarkerDefinitionKey);
        if (_beaconDefinitionId <= 0 || _markerDefinitionId <= 0)
        {
            throw new InvalidOperationException(
                $"ActivationCondition showcase presenter definitions '{BeaconDefinitionKey}'/'{MarkerDefinitionKey}' are not registered.");
        }
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
        PollActions();
    }

    private void EnsureShowcaseSpawned()
    {
        if (_spawned)
        {
            return;
        }

        Entity trueMarker = CreateOwner(TrueMarkerStableId, TrueStationPosition + new Vector3(0f, -0.55f, 0f));
        Entity falseMarker = CreateOwner(FalseMarkerStableId, FalseStationPosition + new Vector3(0f, -0.55f, 0f));
        EnqueuePresenterCreate(_markerDefinitionId, "activation_condition_showcase.marker.true", trueMarker);
        EnqueuePresenterCreate(_markerDefinitionId, "activation_condition_showcase.marker.false", falseMarker);

        _trueOwner = CreateOwner(TrueOwnerStableId, TrueStationPosition);
        EnqueuePresenterCreate(_beaconDefinitionId, "activation_condition_showcase.beacon.true", _trueOwner);

        SpawnFalseStation(withTransform: false);
        _spawned = true;
    }

    private void PollActions()
    {
        if (_input.PressedThisFrame(RespawnWithTransformActionId))
        {
            SpawnFalseStation(withTransform: true);
            return;
        }

        if (_input.PressedThisFrame(RespawnWithoutTransformActionId))
        {
            SpawnFalseStation(withTransform: false);
        }
    }

    private void SpawnFalseStation(bool withTransform)
    {
        DestroyPresenterForOwner(_falseOwner, _beaconDefinitionId);
        if (_falseOwner != Entity.Null && _world.IsAlive(_falseOwner))
        {
            _world.Destroy(_falseOwner);
        }

        _falseOwner = withTransform
            ? CreateOwner(FalseOwnerStableId, FalseStationPosition)
            : _world.Create(new PresentationStableId { Value = FalseOwnerStableId });

        EnqueuePresenterCreate(
            _beaconDefinitionId,
            FalseBeaconScopeTag,
            _falseOwner,
            withTransform ? PresentationAnchorKind.Entity : PresentationAnchorKind.WorldPosition,
            FalseStationPosition);
    }

    private void DestroyPresenterForOwner(Entity owner, int definitionId)
    {
        if (owner == Entity.Null || !_world.IsAlive(owner))
        {
            return;
        }

        Entity presenter = Entity.Null;
        var query = new QueryDescription().WithAll<PresenterState>();
        _world.Query(in query, (Entity entity, ref PresenterState state) =>
        {
            if (state.DefId == definitionId && state.OwnerEntity == owner)
            {
                presenter = entity;
            }
        });

        if (presenter == Entity.Null)
        {
            return;
        }

        if (!_presenterCommands.TryAdd(new PresenterCommand
            {
                CommandKind = PresenterCommandKind.DestroyPresenter,
                CommandKindId = (byte)PresenterCommandKind.DestroyPresenter,
                RouteStrategy = PresenterCommandRouteStrategy.ExistingInstances,
                PresenterEntity = presenter,
            }))
        {
            throw new InvalidOperationException("PresenterCommandBuffer overflowed while destroying an activation showcase presenter.");
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

    private void EnqueuePresenterCreate(
        int definitionId,
        string scopeTagName,
        Entity source,
        PresentationAnchorKind anchorKind = PresentationAnchorKind.Entity,
        Vector3 position = default)
    {
        if (!_presenterCommands.TryAdd(new PresenterCommand
            {
                CommandKind = PresenterCommandKind.CreatePresenter,
                CommandKindId = (byte)PresenterCommandKind.CreatePresenter,
                RouteStrategy = PresenterCommandRouteStrategy.CreatePresenter,
                PresenterDefinitionId = definitionId,
                ScopeTag = PresenterScopeTagRegistry.Register(scopeTagName),
                ScopeSource = PresenterCommandScopeSource.Fixed,
                AnchorKind = anchorKind,
                Source = source,
                Position = position,
            }))
        {
            throw new InvalidOperationException("PresenterCommandBuffer overflowed while creating the ActivationCondition showcase hierarchy.");
        }
    }

    public void Dispose()
    {
    }
}
