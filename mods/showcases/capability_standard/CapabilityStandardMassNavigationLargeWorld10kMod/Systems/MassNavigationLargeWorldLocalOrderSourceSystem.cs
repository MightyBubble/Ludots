using System.Collections.Generic;
using System.Numerics;
using Arch.Core;
using Arch.System;
using Ludots.Core.EntityCollections;
using CoreInputMod.Systems;
using Ludots.Core.Gameplay.GAS.Orders;
using Ludots.Core.Input.CommandSources;
using Ludots.Core.Input.Orders;
using Ludots.Core.MassNavigation;
using Ludots.Core.MassNavigation.Runtime;
using Ludots.Core.Modding;
using Ludots.Core.Scripting;

namespace CapabilityStandardMassNavigationLargeWorld10kMod.Systems;

internal sealed class MassNavigationLargeWorldLocalOrderSourceSystem : ISystem<float>
{
    private readonly World _world;
    private readonly Dictionary<string, object> _globals;
    private readonly IModContext _context;
    private readonly LocalOrderSourceHelper _helper;
    private InputOrderMappingSystem? _mapping;
    private int _massNavigationMoveOrderTypeId;
    private Entity _lastCommandSourceOwner = Entity.Null;
    private uint _lastCommandSourceRevision;
    private int _lastStructuralRevision = -1;
    private bool _lastHadCommandActors;
    private bool _initialized;

    public MassNavigationLargeWorldLocalOrderSourceSystem(
        World world,
        Dictionary<string, object> globals,
        OrderQueue orders,
        IModContext context)
    {
        _world = world;
        _globals = globals;
        _context = context;
        _helper = new LocalOrderSourceHelper(world, globals, orders);
    }

    public void Initialize() { }
    public void BeforeUpdate(in float dt) { }
    public void AfterUpdate(in float dt) { }
    public void Dispose() { }

    public void Update(in float dt)
    {
        EnsureInitialized();
        if (_mapping == null)
        {
            return;
        }

        if (TryGetSimulation(out MassNavigationSimulationRuntime simulation))
        {
            SyncCommandActors(simulation);
        }

        Entity actor = _helper.GetControlledActor();
        if (_helper.TrySetLocalPlayer(_mapping, actor))
        {
            _mapping.Update(dt);
        }
    }

    private void EnsureInitialized()
    {
        if (_initialized)
        {
            return;
        }

        _initialized = true;
        _mapping = _helper.TryCreateMapping(_context);
        if (_mapping == null)
        {
            return;
        }

        _helper.AfterOrderAccepted = ObserveAcceptedOrder;
    }

    private void SyncCommandActors(MassNavigationSimulationRuntime simulation)
    {
        if (!TryResolveCommandSourceOwner(out Entity owner) ||
            !_globals.TryGetValue(CoreServiceKeys.EntityCollectionStore.Name, out object? collectionsObj) ||
            collectionsObj is not EntityCollectionStore collections)
        {
            ClearCommandActorsIfNeeded(simulation);
            return;
        }

        if (!EntityCollectionContextRuntime.TryDescribeView(
                collections,
                owner,
                EntityCollectionKeys.CommandSource,
                out EntityCollectionView view))
        {
            ClearCommandActorsIfNeeded(simulation);
            _lastCommandSourceOwner = owner;
            return;
        }

        bool structuralChanged = _lastStructuralRevision != simulation.StructuralChangeRevision;
        if (_lastHadCommandActors &&
            _lastCommandSourceOwner == owner &&
            _lastCommandSourceRevision == view.Revision &&
            !structuralChanged)
        {
            return;
        }

        Span<Entity> commandActors = simulation.EnsureCommandActorScratch(view.Count);
        int written = EntityCollectionContextRuntime.Copy(
            collections,
            owner,
            EntityCollectionKeys.CommandSource,
            commandActors);
        if (written != view.Count)
        {
            throw new InvalidOperationException(
                $"MassNavigation large-world showcase expected {view.Count} command actor row(s), copied {written}.");
        }

        simulation.SetCommandActorSnapshot(commandActors[..written], view.Revision);
        simulation.ObserveCommandActorSyncTick();
        _lastCommandSourceOwner = owner;
        _lastCommandSourceRevision = view.Revision;
        _lastStructuralRevision = simulation.StructuralChangeRevision;
        _lastHadCommandActors = true;
    }

    private bool TryResolveCommandSourceOwner(out Entity owner)
    {
        owner = Entity.Null;
        if (!_globals.TryGetValue(CoreServiceKeys.LocalPlayerEntity.Name, out object? localObj) ||
            localObj is not Entity local ||
            local == Entity.Null ||
            !_world.IsAlive(local))
        {
            return false;
        }

        owner = local;
        return true;
    }

    private bool TryGetSimulation(out MassNavigationSimulationRuntime simulation)
    {
        simulation = default!;
        return _globals.TryGetValue(MassNavigationKeys.SimulationRuntime.Name, out object? simulationObj) &&
               simulationObj is MassNavigationSimulationRuntime runtime &&
               (simulation = runtime) != null;
    }

    private void ClearCommandActorsIfNeeded(MassNavigationSimulationRuntime simulation)
    {
        if (_lastHadCommandActors || simulation.CommandActorCount > 0)
        {
            simulation.ClearCommandActorSnapshot();
        }

        _lastCommandSourceOwner = Entity.Null;
        _lastCommandSourceRevision = 0;
        _lastStructuralRevision = simulation.StructuralChangeRevision;
        _lastHadCommandActors = false;
    }

    private void ObserveAcceptedOrder(in Order order)
    {
        if (!IsMassNavigationMoveOrder(in order) ||
            order.Args.Spatial.Kind != OrderSpatialKind.WorldCm ||
            order.Args.Spatial.Mode != OrderCollectionMode.Single ||
            !TryGetSimulation(out MassNavigationSimulationRuntime simulation))
        {
            return;
        }

        ReadOnlySpan<Entity> commandActors = simulation.CommandActors;
        simulation.FocusCommandTarget(
            new Vector2(order.Args.Spatial.WorldCm.X, order.Args.Spatial.WorldCm.Z),
            commandActors);
        simulation.MarkCommandApply();
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
}
