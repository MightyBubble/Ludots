using System.Collections.Generic;
using System.Numerics;
using Arch.Core;
using Arch.System;
using CoreInputMod.Systems;
using FormationCapabilityShowcaseMod.Runtime;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.Components;
using Ludots.Core.Gameplay.GAS.Orders;
using Ludots.Core.Input.Orders;
using Ludots.Core.MassNavigation;
using Ludots.Core.MassNavigation.Runtime;
using Ludots.Core.Modding;
using Ludots.Core.Scripting;

namespace FormationCapabilityShowcaseMod.Systems;

internal sealed class FormationCapabilityLocalOrderSourceSystem : ISystem<float>
{
    private readonly GameEngine _engine;
    private readonly FormationCapabilityShowcaseRuntime _showcaseRuntime;
    private readonly World _world;
    private readonly Dictionary<string, object> _globals;
    private readonly IModContext _context;
    private readonly LocalOrderSourceHelper _helper;
    private readonly Entity[] _acceptedActors = new Entity[1];
    private InputOrderMappingSystem? _mapping;
    private readonly MassNavigationRuntimeBinding _runtimeBinding;
    private int _massNavigationMoveOrderTypeId;
    private bool _initialized;

    public FormationCapabilityLocalOrderSourceSystem(
        GameEngine engine,
        FormationCapabilityShowcaseRuntime showcaseRuntime,
        World world,
        Dictionary<string, object> globals,
        OrderQueue orders,
        IModContext context,
        MassNavigationRuntimeBinding runtimeBinding)
    {
        _engine = engine ?? throw new System.ArgumentNullException(nameof(engine));
        _showcaseRuntime = showcaseRuntime ?? throw new System.ArgumentNullException(nameof(showcaseRuntime));
        _world = world;
        _globals = globals;
        _context = context;
        _runtimeBinding = runtimeBinding ?? throw new System.ArgumentNullException(nameof(runtimeBinding));
        _helper = new LocalOrderSourceHelper(world, globals, orders);
    }

    public void Initialize()
    {
    }

    public void BeforeUpdate(in float dt)
    {
    }

    public void Update(in float dt)
    {
        if (!_showcaseRuntime.IsCurrentShowcaseMap(_engine))
        {
            return;
        }

        EnsureInitialized();
        if (_mapping == null)
        {
            return;
        }

        Entity actor = _helper.GetControlledActor();
        if (_helper.TrySetLocalPlayer(_mapping, actor))
        {
            _mapping.Update(dt);
        }
    }

    public void AfterUpdate(in float dt)
    {
    }

    public void Dispose()
    {
    }

    private void EnsureInitialized()
    {
        if (_initialized)
        {
            return;
        }

        _initialized = true;
        _mapping = _helper.TryCreateMapping(_context);
        if (_mapping != null)
        {
            _helper.BeforeOrderSubmit = CanLocalPlayerSubmitOrder;
            _helper.AfterOrderAccepted = ObserveAcceptedOrder;
            _globals[SkillBarOverlaySystem.SkillBarKeyLabelsKey] = new[] { "Q", "W", "E", "R" };
        }
    }

    private bool CanLocalPlayerSubmitOrder(in Order order)
    {
        if (!IsMassNavigationMoveOrder(in order))
        {
            return true;
        }

        if (!TryResolveLocalPlayerOwnerId(out int localPlayerId))
        {
            RejectOrder(in order);
            return false;
        }

        if (!_world.IsAlive(order.Actor) ||
            !_world.TryGet(order.Actor, out PlayerOwner owner) ||
            owner.PlayerId != localPlayerId)
        {
            RejectOrder(in order);
            return false;
        }

        return true;
    }

    private void ObserveAcceptedOrder(in Order order)
    {
        if (!IsMassNavigationMoveOrder(in order) ||
            order.Args.Spatial.Kind != OrderSpatialKind.WorldCm ||
            order.Args.Spatial.Mode != OrderCollectionMode.Single ||
            !TryResolveSimulation(out MassNavigationSimulationRuntime simulation))
        {
            return;
        }

        _acceptedActors[0] = order.Actor;
        simulation.FocusCommandTarget(
            new Vector2(order.Args.Spatial.WorldCm.X, order.Args.Spatial.WorldCm.Z),
            _acceptedActors.AsSpan(0, 1));
        simulation.MarkCommandApply();
    }

    private void RejectOrder(in Order order)
    {
        if (!TryResolveSimulation(out MassNavigationSimulationRuntime simulation) ||
            order.Args.Spatial.Kind != OrderSpatialKind.WorldCm ||
            order.Args.Spatial.Mode != OrderCollectionMode.Single)
        {
            return;
        }

        simulation.RejectCommandUnauthorizedCommandActors(
            order.Args.Spatial.WorldCm.X,
            order.Args.Spatial.WorldCm.Z);
    }

    private bool IsMassNavigationMoveOrder(in Order order)
    {
        if (_massNavigationMoveOrderTypeId == 0)
        {
            if (!_globals.TryGetValue(CoreServiceKeys.OrderTypeRegistry.Name, out object? orderTypesObj) ||
                orderTypesObj is not OrderTypeRegistry orderTypes ||
                !orderTypes.TryGetId(MassNavigationOrderKeys.Move, out _massNavigationMoveOrderTypeId))
            {
                return false;
            }
        }

        return order.OrderTypeId == _massNavigationMoveOrderTypeId;
    }

    private bool TryResolveSimulation(out MassNavigationSimulationRuntime simulation)
    {
        if (_runtimeBinding.Current is not MassNavigationSimulationRuntime resolved ||
            !resolved.IsReadyForWorldOperations)
        {
            simulation = null!;
            return false;
        }

        simulation = resolved;
        return true;
    }

    private bool TryResolveLocalPlayerOwnerId(out int playerId)
    {
        playerId = 0;
        return _globals.TryGetValue(CoreServiceKeys.LocalPlayerEntity.Name, out object? localObj) &&
               localObj is Entity local &&
               _world.IsAlive(local) &&
               _world.TryGet(local, out PlayerOwner owner) &&
               owner.PlayerId > 0 &&
               (playerId = owner.PlayerId) > 0;
    }
}
