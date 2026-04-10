using System.Threading.Tasks;
using System.Numerics;
using Arch.Core;
using CoreInputMod;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.Camera;
using Ludots.Core.Gameplay.Components;
using Ludots.Core.Input.Selection;
using Ludots.Core.Modding;
using Ludots.Core.Presentation.Assets;
using Ludots.Core.Scripting;
using MassNavPlaygroundMod.Systems;
using MassNavPlaygroundMod.UI;

namespace MassNavPlaygroundMod.Runtime;

internal sealed class MassNavPlaygroundRuntime
{
    private const string MassNavCameraProfileId = "Camera.Profile.MassNavTactical";
    private static readonly Vector2 MassNavCameraTargetCm = Vector2.Zero;
    private const float MassNavCameraDistanceCm = 32000f;

    private static readonly QueryDescription LocalPlayerQuery = new QueryDescription().WithAll<PlayerOwner>();

    private readonly IModContext _context;
    private bool _systemsInstalled;
    private bool _scenarioSpawned;
    private readonly MassNavPlaygroundPanelController _panelController = new();

    public MassNavPlaygroundRuntime(IModContext context)
    {
        _context = context;
    }

    public void EnsureSystemsInstalled(GameEngine engine)
    {
        if (_systemsInstalled)
        {
            return;
        }

        var simulation = new MassNavSimulationRuntime();
        engine.SetService(MassNavPlaygroundKeys.SimulationRuntime, simulation);
        engine.RegisterSystem(new MassNavSelectionSyncSystem(engine, simulation), SystemGroup.InputCollection);
        engine.RegisterSystem(new MassNavCommandBridgeSystem(engine, simulation), SystemGroup.InputCollection);
        var meshes = engine.GetService(CoreServiceKeys.PresentationMeshAssetRegistry)
            ?? throw new System.InvalidOperationException("MassNavPlaygroundMod requires PresentationMeshAssetRegistry.");
        engine.RegisterPresentationSystem(new MassNavPrimitivePresentationSystem(engine, simulation, meshes));
        engine.RegisterPresentationSystem(new MassNavHudPresentationSystem(engine, simulation));
        engine.RegisterPresentationSystem(new MassNavPanelPresentationSystem(engine, _panelController, simulation));
        _systemsInstalled = true;
        _context.Log("[MassNavPlaygroundMod] Installed mass-nav façade runtime skeleton.");
    }

    public Task HandleMapFocusedAsync(ScriptContext context)
    {
        var engine = context.GetEngine();
        if (engine == null)
        {
            return Task.CompletedTask;
        }

        if (!MassNavPlaygroundIds.IsPlaygroundMap(context.Get(CoreServiceKeys.MapId).Value))
        {
            return Task.CompletedTask;
        }

        EnsureSystemsInstalled(engine);
        EnsureLocalPlayerEntity(engine);
        EnsureTacticalCamera(engine);
        EnsureScenario(engine);
        return Task.CompletedTask;
    }

    public Task HandleMapUnloadedAsync(ScriptContext context)
    {
        _scenarioSpawned = false;
        return Task.CompletedTask;
    }

    private void EnsureScenario(GameEngine engine)
    {
        if (_scenarioSpawned &&
            engine.GetService(MassNavPlaygroundKeys.SimulationRuntime) is { } existing &&
            existing.AgentState.TotalAgents > 0)
        {
            return;
        }

        MassNavSimulationRuntime simulation = engine.GetService(MassNavPlaygroundKeys.SimulationRuntime)
            ?? throw new System.InvalidOperationException("MassNavPlaygroundMod requires simulation runtime.");
        MassNavScenarioBootstrap.SpawnDefaultScenario(engine.World, simulation.AgentState);
        _scenarioSpawned = true;
    }

    private static void EnsureLocalPlayerEntity(GameEngine engine)
    {
        SelectionRuntime selection = engine.GetService(CoreServiceKeys.SelectionRuntime)
            ?? throw new System.InvalidOperationException("MassNavPlaygroundMod requires SelectionRuntime.");

        if (engine.GlobalContext.TryGetValue(CoreServiceKeys.LocalPlayerEntity.Name, out var localObj) &&
            localObj is Entity local &&
            engine.World.IsAlive(local))
        {
            EnsureSelectionOwner(engine.World, local, selection, engine.GlobalContext);
            return;
        }

        Entity owner = Entity.Null;
        engine.World.Query(in LocalPlayerQuery, (Entity entity, ref PlayerOwner playerOwner) =>
        {
            if (owner == Entity.Null && playerOwner.PlayerId == 1)
            {
                owner = entity;
            }
        });

        if (owner == Entity.Null)
        {
            owner = engine.World.Create(new PlayerOwner { PlayerId = 1 }, default(SelectionDragState));
        }

        engine.GlobalContext[CoreServiceKeys.LocalPlayerEntity.Name] = owner;
        EnsureSelectionOwner(engine.World, owner, selection, engine.GlobalContext);
    }

    private static void EnsureSelectionOwner(World world, Entity owner, SelectionRuntime selection, System.Collections.Generic.Dictionary<string, object> globals)
    {
        if (!world.Has<SelectionDragState>(owner))
        {
            world.Add(owner, default(SelectionDragState));
        }

        selection.TryGetOrCreateSelectionEntity(owner, SelectionSetKeys.LivePrimary, out _);
        selection.TryBindView(owner, SelectionViewKeys.Primary, owner, SelectionSetKeys.LivePrimary);
        globals[CoreServiceKeys.SelectionViewViewerEntity.Name] = owner;
        globals[CoreServiceKeys.SelectionViewKey.Name] = SelectionViewKeys.Primary;
    }

    private static void EnsureTacticalCamera(GameEngine engine)
    {
        RequestTacticalCameraReset(engine);
    }

    internal static void RequestTacticalCameraReset(GameEngine engine)
    {
        engine.GlobalContext[CoreServiceKeys.VirtualCameraRequest.Name] = new VirtualCameraRequest
        {
            Id = MassNavCameraProfileId,
            BlendDurationSeconds = 0f,
            ResetRuntimeState = true,
            SnapToFollowTargetWhenAvailable = false
        };
        engine.SetService(CoreServiceKeys.CameraPoseRequest, new CameraPoseRequest
        {
            VirtualCameraId = MassNavCameraProfileId,
            TargetCm = MassNavCameraTargetCm,
            DistanceCm = MassNavCameraDistanceCm
        });
    }
}
