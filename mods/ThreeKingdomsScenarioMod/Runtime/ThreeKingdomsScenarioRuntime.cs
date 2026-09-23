using System;
using System.Numerics;
using System.Threading.Tasks;
using Arch.Core;
using EntityCommandPanelMod.UI;
using EntityInfoPanelsMod;
using EntityInfoPanelsMod.Commands;
using Ludots.Core.Components;
using Ludots.Core.Config;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.Camera;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Input.Selection;
using Ludots.Core.Scripting;
using Ludots.UI;
using MinimapControlMod;
using MinimapControlMod.Runtime;
using RtsDemoMod.Runtime;
using ThreeKingdomsScenarioMod.UI;

namespace ThreeKingdomsScenarioMod.Runtime;

internal sealed class ThreeKingdomsScenarioRuntime
{
    private readonly ThreeKingdomsScenarioPanelController _panelController = new();

    public Task HandleMapFocusedAsync(ScriptContext context)
    {
        if (context.GetEngine() is not GameEngine engine)
        {
            return Task.CompletedTask;
        }

        if (!ThreeKingdomsScenarioIds.IsScenarioMap(engine.CurrentMapSession?.MapId.Value))
        {
            CloseEntityInfoPanels(context);
            SetMinimapVisible(context, visible: false);
            ClearPanelIfOwned(context);
            return Task.CompletedTask;
        }

        EnsureEntityInfoPanels(context, engine);
        SetMinimapVisible(context, visible: true);
        engine.GlobalContext[EntityCommandPanelShowcaseTheme.ContextKey] = EntityCommandPanelShowcaseTheme.Sc2Id;
        EnsurePlayableSelectionAndCamera(engine);
        RefreshPanel(engine);
        return Task.CompletedTask;
    }

    public Task HandleMapUnloadedAsync(ScriptContext context)
    {
        string mapId = context.Get(CoreServiceKeys.MapId).Value;
        if (!ThreeKingdomsScenarioIds.IsScenarioMap(mapId))
        {
            return Task.CompletedTask;
        }

        CloseEntityInfoPanels(context);
        SetMinimapVisible(context, visible: false);
        ClearPanelIfOwned(context);
        return Task.CompletedTask;
    }

    public void RefreshPanel(GameEngine engine)
    {
        if (!ThreeKingdomsScenarioIds.IsScenarioMap(engine.CurrentMapSession?.MapId.Value))
        {
            if (engine.GetService(MinimapControlServiceKeys.Runtime) is MinimapControlRuntime minimapRuntime)
            {
                minimapRuntime.Visible = false;
            }

            ClearPanelIfOwned(engine);
            return;
        }

        if (engine.GetService(MinimapControlServiceKeys.Runtime) is MinimapControlRuntime runtime)
        {
            runtime.Visible = true;
        }

        engine.GlobalContext[EntityCommandPanelShowcaseTheme.ContextKey] = EntityCommandPanelShowcaseTheme.Sc2Id;
    }

    private static ThreeKingdomsScenarioHudState BuildHudState(GameEngine engine)
    {
        Entity selected = SelectionContextRuntime.TryGetCurrentPrimary(engine.World, engine.GlobalContext, out Entity current)
            ? current
            : Entity.Null;

        string selectionLabel = "(no selection)";
        string selectionType = "Scenario";
        string routeSummary = "Road columns hold the siege lane while field units pivot between wall, gate, trench, and tunnel objectives.";
        string garrisonSummary = "Garrisons, ladders, gates, and tunnel mouths are all relation-driven holds.";
        string siegeSummary = "Break the gate, secure the wall, and keep the crossings open.";

        if (selected != Entity.Null && engine.World.IsAlive(selected))
        {
            selectionLabel = engine.World.TryGet(selected, out Name name)
                ? name.Value
                : $"Entity #{selected.Id}";

            selectionType = engine.World.Has<AbilityStateBuffer>(selected)
                ? "Command Entity"
                : "Observer";
        }

        return new ThreeKingdomsScenarioHudState(
            ScenarioTitle: "Three Kingdoms Siege",
            SelectionLabel: selectionLabel,
            SelectionType: selectionType,
            CommandHint: "Use the command deck to board, capture, climb, deploy ladders, and seize the gate.",
            ModeSummary: siegeSummary,
            RouteSummary: routeSummary,
            GarrisonSummary: garrisonSummary,
            SiegeSummary: siegeSummary);
    }

    private static void EnsureEntityInfoPanels(ScriptContext context, GameEngine engine)
    {
        if (engine.GetService(EntityInfoPanelServiceKeys.HandleStore) is not EntityInfoPanelHandleStore handles)
        {
            return;
        }

        OpenOrUpdate(
            context,
            handles,
            ThreeKingdomsScenarioIds.SelectionCollectionHandleKey,
            new EntityInfoPanelRequest(
                EntityInfoPanelKind.EntityCollectionInspector,
                EntityInfoPanelSurface.Ui,
                EntityInfoPanelTarget.CurrentSelectionView(),
                new EntityInfoPanelLayout(EntityInfoPanelAnchor.BottomLeft, 16f, 18f, 360f, 264f),
                EntityInfoGasDetailFlags.None,
                true));

        CloseIfPresent(context, handles, ThreeKingdomsScenarioIds.SelectionInsightHandleKey);
    }

    private static EntityInfoPanelTarget? TryResolveSelectedTarget(GameEngine engine)
    {
        if (!SelectionContextRuntime.TryGetCurrentPrimary(engine.World, engine.GlobalContext, out Entity selected) ||
            selected == Entity.Null ||
            !engine.World.IsAlive(selected))
        {
            return null;
        }

        return EntityInfoPanelTarget.Fixed(selected);
    }

    private static bool OpenOrUpdate(
        ScriptContext context,
        EntityInfoPanelHandleStore handles,
        string handleKey,
        EntityInfoPanelRequest request)
    {
        if (handles.TryGet(handleKey, out _))
        {
            new UpdateEntityInfoPanelCommand
            {
                HandleSlotKey = handleKey,
                Visible = true,
                Layout = request.Layout,
                Target = request.Target,
                GasDetailFlags = request.GasDetailFlags
            }.ExecuteAsync(context).GetAwaiter().GetResult();
            return false;
        }

        new OpenEntityInfoPanelCommand
        {
            HandleSlotKey = handleKey,
            Request = request
        }.ExecuteAsync(context).GetAwaiter().GetResult();
        return true;
    }

    private static void CloseEntityInfoPanels(ScriptContext context)
    {
        if (context.Get(EntityInfoPanelServiceKeys.HandleStore) is not EntityInfoPanelHandleStore handles)
        {
            return;
        }

        CloseIfPresent(context, handles, ThreeKingdomsScenarioIds.SelectionCollectionHandleKey);
        CloseIfPresent(context, handles, ThreeKingdomsScenarioIds.SelectionInsightHandleKey);
    }

    private static void CloseIfPresent(ScriptContext context, EntityInfoPanelHandleStore handles, string handleKey)
    {
        if (!handles.TryGet(handleKey, out _))
        {
            return;
        }

        new CloseEntityInfoPanelCommand
        {
            HandleSlotKey = handleKey
        }.ExecuteAsync(context).GetAwaiter().GetResult();
    }

    private static void SetMinimapVisible(ScriptContext context, bool visible)
    {
        if (context.Get(MinimapControlServiceKeys.Runtime) is MinimapControlRuntime runtime)
        {
            runtime.Visible = visible;
        }
    }

    private static void EnsurePlayableSelectionAndCamera(GameEngine engine)
    {
        Entity vanguard = FindNamedEntity(engine, "Road Vanguard", requireCommandSurface: true);
        if (vanguard == Entity.Null)
        {
            return;
        }

        RtsShowcaseSelectionHelper.TrySelectAndFocus(engine, vanguard, snapCamera: true);
        ApplyScenarioOpeningCamera(engine, vanguard);
    }

    private static void ApplyScenarioOpeningCamera(GameEngine engine, Entity vanguard)
    {
        if (!engine.World.TryGet(vanguard, out WorldPositionCm vanguardPosition))
        {
            return;
        }

        Vector2 targetCm = vanguardPosition.Value.ToVector2();
        Entity mainGate = FindNamedEntity(engine, "Main Gate");
        if (mainGate != Entity.Null && engine.World.TryGet(mainGate, out WorldPositionCm gatePosition))
        {
            Vector2 gateCm = gatePosition.Value.ToVector2();
            targetCm = Vector2.Lerp(targetCm, gateCm, 0.58f);
        }

        CameraConfig? mapCamera = engine.CurrentMapSession?.MapConfig?.DefaultCamera;
        var pose = new CameraPoseRequest
        {
            TargetCm = targetCm,
            Yaw = mapCamera?.Yaw,
            Pitch = mapCamera?.Pitch,
            DistanceCm = ResolveOpeningDistance(mapCamera?.DistanceCm),
            FovYDeg = mapCamera?.FovYDeg
        };

        engine.GameSession.Camera.ApplyPose(pose);
        engine.GlobalContext[CoreServiceKeys.CameraPoseRequest.Name] = pose;
    }

    private static float ResolveOpeningDistance(float? configuredDistanceCm)
    {
        if (!configuredDistanceCm.HasValue || configuredDistanceCm.Value <= 0f)
        {
            return 12000f;
        }

        return MathF.Max(10000f, configuredDistanceCm.Value * 0.72f);
    }

    private static Entity FindNamedEntity(GameEngine engine, string entityName, bool requireCommandSurface = false)
    {
        Entity resolved = Entity.Null;
        if (requireCommandSurface)
        {
            var commandQuery = new QueryDescription().WithAll<Name, AbilityStateBuffer>();
            engine.World.Query(in commandQuery, (Entity entity, ref Name name, ref AbilityStateBuffer _) =>
            {
                if (resolved != Entity.Null ||
                    !string.Equals(name.Value, entityName, StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }

                resolved = entity;
            });

            return resolved;
        }

        var namedQuery = new QueryDescription().WithAll<Name>();
        engine.World.Query(in namedQuery, (Entity entity, ref Name name) =>
        {
            if (resolved != Entity.Null ||
                !string.Equals(name.Value, entityName, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            resolved = entity;
        });

        return resolved;
    }

    private void ClearPanelIfOwned(ScriptContext context)
    {
        if (context.Get(CoreServiceKeys.UIRoot) is not UIRoot root)
        {
            return;
        }

        _panelController.ClearIfOwned(root);
    }

    private void ClearPanelIfOwned(GameEngine engine)
    {
        if (engine.GetService(CoreServiceKeys.UIRoot) is not UIRoot root)
        {
            return;
        }

        _panelController.ClearIfOwned(root);
    }
}
