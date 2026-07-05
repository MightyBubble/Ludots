using System;
using Arch.Core;
using Arch.System;
using CoreInputMod.Systems;
using Ludots.Core.Engine;
using Ludots.Core.EntityCollections;
using Ludots.Core.Gameplay.Components;
using Ludots.Core.Gameplay.GAS.Orders;
using Ludots.Core.Input.Orders;
using Ludots.Core.Input.Runtime;
using Ludots.Core.MassNavigation;
using Ludots.Core.MassNavigation.Input;
using Ludots.Core.MassNavigation.Runtime;
using Ludots.Core.Modding;
using Ludots.Core.Scripting;

namespace MassNavigationMod.Systems;

internal sealed class MassNavigationLocalOrderSourceSystem : ISystem<float>
{
    private readonly GameEngine _engine;
    private readonly OrderQueue _orders;
    private readonly IModContext _context;
    private readonly LocalOrderSourceHelper _helper;
    private InputOrderMappingSystem? _mapping;
    private bool _initialized;
    private int _lastMarkedOrderId;

    public MassNavigationLocalOrderSourceSystem(GameEngine engine, OrderQueue orders, IModContext context)
    {
        _engine = engine ?? throw new ArgumentNullException(nameof(engine));
        _orders = orders ?? throw new ArgumentNullException(nameof(orders));
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _helper = new LocalOrderSourceHelper(engine.World, engine.GlobalContext, orders);
    }

    public void Initialize()
    {
    }

    public void BeforeUpdate(in float dt)
    {
    }

    public void Update(in float dt)
    {
        EnsureInitialized();
        if (!TryGetRuntime(out MassNavigationSimulationRuntime simulation))
        {
            return;
        }

        SyncCommandSource(simulation);
        HandleLocalControlInput(dt, simulation);

        if (_mapping == null ||
            _engine.GetService(CoreServiceKeys.UiCaptured))
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
        if (_mapping == null)
        {
            return;
        }

        _mapping.SetOrderSubmitHandler(SubmitMoveOrder);
    }

    private bool TryGetRuntime(out MassNavigationSimulationRuntime simulation)
    {
        simulation = default!;
        if (!MassNavigationIds.IsCurrentNavigationRuntimeReady(_engine) ||
            _engine.GetService(MassNavigationKeys.SimulationRuntime) is not MassNavigationSimulationRuntime runtime)
        {
            return false;
        }

        simulation = runtime;
        return true;
    }

    private void SyncCommandSource(MassNavigationSimulationRuntime simulation)
    {
        if (_engine.GetService(CoreServiceKeys.EntityCollectionStore) is EntityCollectionStore collections)
        {
            MassNavigationCommandSourceSync.SyncIfChanged(_engine.World, _engine.GlobalContext, collections, simulation);
        }
    }

    private void HandleLocalControlInput(float dt, MassNavigationSimulationRuntime simulation)
    {
        if (_engine.GetService(CoreServiceKeys.UiCaptured) ||
            _engine.GetService(CoreServiceKeys.AuthoritativeInput) is not IInputActionReader input)
        {
            return;
        }

        if (input.PressedThisFrame(MassNavigationInputActions.ResetScene))
        {
            simulation.RequestSceneReset();
        }

        float deltaRadians = 0f;
        if (input.IsDown(MassNavigationInputActions.RotateLeft))
        {
            deltaRadians -= simulation.Config.Semantics.Group.FormationRotationSpeedRadiansPerSecond * dt;
        }

        if (input.IsDown(MassNavigationInputActions.RotateRight))
        {
            deltaRadians += simulation.Config.Semantics.Group.FormationRotationSpeedRadiansPerSecond * dt;
        }

        if (MathF.Abs(deltaRadians) > simulation.Config.Semantics.Group.FormationRotationEpsilonRadians)
        {
            simulation.RotateCommandSourcesFormation(_engine.World, deltaRadians, ResolveLocalPlayerId());
        }
    }

    private void SubmitMoveOrder(in Order order)
    {
        if (!TryGetRuntime(out MassNavigationSimulationRuntime simulation))
        {
            return;
        }

        if (order.Args.Spatial.Kind != OrderSpatialKind.WorldCm ||
            order.Args.Spatial.Mode != OrderCollectionMode.Single)
        {
            throw new InvalidOperationException("MassNavigation local order source requires a single WorldCm command target.");
        }

        var value = order;
        float worldXCm = value.Args.Spatial.WorldCm.X;
        float worldYCm = value.Args.Spatial.WorldCm.Z;
        if (!simulation.ContainsWorldPoint(worldXCm, worldYCm))
        {
            simulation.RejectCommandOutsideWorld(worldXCm, worldYCm);
            return;
        }

        value.Args.I0 = (int)simulation.FormationMode;
        value.Args.F0 = simulation.NavGroupRuntime.CommandSourceRotationRadians;
        value.Args.Selection = new OrderSelectionReference { Container = Entity.Null };

        if (!_orders.TryEnqueueAssigned(ref value))
        {
            simulation.RejectCommandOrderSubmit(worldXCm, worldYCm);
            return;
        }

        if (value.OrderId != _lastMarkedOrderId)
        {
            simulation.FocusCommandTarget(
                new System.Numerics.Vector2(worldXCm, worldYCm),
                simulation.CommandSourceEntities);
            simulation.MarkCommandApply();
            _lastMarkedOrderId = value.OrderId;
        }
    }

    private int ResolveLocalPlayerId()
    {
        if (!_engine.GlobalContext.TryGetValue(CoreServiceKeys.LocalPlayerEntity.Name, out object? localObj) ||
            localObj is not Entity local ||
            !_engine.World.IsAlive(local))
        {
            throw new InvalidOperationException("MassNavigation local order source requires LocalPlayerEntity before rotating formations.");
        }

        if (!_engine.World.TryGet(local, out PlayerOwner owner))
        {
            throw new InvalidOperationException("MassNavigation local order source LocalPlayerEntity must author PlayerOwner before rotating formations.");
        }

        return owner.PlayerId;
    }
}
