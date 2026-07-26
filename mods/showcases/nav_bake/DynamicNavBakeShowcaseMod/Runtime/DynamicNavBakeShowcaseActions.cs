using System;
using System.Numerics;
using Arch.Core;
using DynamicNavBakeShowcaseMod;
using Ludots.Core.Engine;
using Ludots.Core.Navigation.NavMesh;
using Ludots.Core.Navigation.NavMesh.Bake;
using Ludots.Core.Navigation.NavMesh.Surface;

namespace DynamicNavBakeShowcaseMod.Runtime;

public sealed class DynamicNavBakeShowcaseActions
{
    private readonly DynamicNavBakeShowcaseRuntime _runtime;

    internal DynamicNavBakeShowcaseActions(DynamicNavBakeShowcaseRuntime runtime)
    {
        _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
    }

    internal DynamicNavBakeShowcaseRuntime Runtime => _runtime;

    public bool IsActive => _runtime.IsActive;
    public DynamicNavBakeShowcaseConfig ActiveConfig => _runtime.ActiveConfig;
    public string LastStatus => _runtime.LastStatus;
    public NavPathStatus LastPathStatus => _runtime.LastPathStatus;
    public int LastPathPointCount => _runtime.LastPathPointCount;
    public int LastCoarseCorridorNodeCount => _runtime.LastCoarseCorridorNodeCount;
    public int OpenWorldCorridorCursor => _runtime.OpenWorldCorridorCursor;
    public bool MoveCommandActive => _runtime.MoveCommandActive;
    public int PresentationPathRevision => _runtime.PresentationPathRevision;
    public DynamicNavBakePathOrchestrationState PathOrchestrationState => _runtime.PathOrchestrationState;
    public bool SquadDeployed => _runtime.SquadDeployed;
    public bool ConstructionMode => _runtime.ConstructionMode;
    public int WallDeployedCount => _runtime.WallDeployedCount;
    public DynamicNavBakeEditTool SelectedEditTool => _runtime.EditTransaction.Tool;
    public DynamicNavBakePlayerNavState PlayerNavState => _runtime.EditTransaction.PlayerNavState;
    public bool HasStagedEdit => _runtime.EditTransaction.HasStagedEdit;
    public bool CanRestore => _runtime.EditTransaction.CanRestore;
    public ReadOnlySpan<Entity> SquadEntities => _runtime.SquadEntities;

    /// <summary>
    /// Successful <see cref="TryCommandMoveToGoal"/> submissions this session (allocation-free).
    /// </summary>
    public int FormalMoveCommandSubmitCount => _runtime.FormalMoveCommandSubmitCount;

    public Vector2 ResolveAuthoredCameraTargetCm() => _runtime.ResolveAuthoredCameraTargetCm();

    public void EnsureAutoCaptureCameraActive(GameEngine engine)
        => _runtime.EnsureAutoCaptureCameraActive(engine);

    public void ApplyAutoCapturePlayerFraming(GameEngine engine)
        => _runtime.ApplyAutoCapturePlayerFraming(engine);

    public DynamicNavBakeShowcasePlayerFramingPose ResolvePlayerFramingPose(GameEngine engine)
        => _runtime.ResolvePlayerFramingPose(engine);

    public int CountSquadMembersInsidePlayerFraming(GameEngine engine)
        => _runtime.CountSquadMembersInsidePlayerFraming(engine);

    public DynamicNavBakeShowcasePlayerFramingVisibility CaptureSquadPlayerFramingVisibility(GameEngine engine)
        => _runtime.CaptureSquadPlayerFramingVisibility(engine);

    public bool TrySwitchAlgorithm(GameEngine engine, NavBakeAlgorithmKind algorithm, out string error)
        => _runtime.TrySwitchAlgorithm(engine, algorithm, out error);

    public bool TrySwitchMap(GameEngine engine, string mapId, out string error)
        => _runtime.TrySwitchShowcaseMap(engine, mapId, out error);

    public bool TryEnterConstructionMode(GameEngine engine, out string error)
        => _runtime.TryEnterConstructionMode(engine, out error);

    public bool TryExitConstructionMode(GameEngine engine, out string error)
        => _runtime.TryExitConstructionMode(engine, out error);

    public bool TryPlaceBuildingAtPreview(GameEngine engine, out string error)
        => _runtime.TryPlaceBuildingAtPreview(engine, out error);

    public bool TrySetEditTool(GameEngine engine, DynamicNavBakeEditTool tool, out string error)
        => _runtime.TrySetEditTool(engine, tool, out error);

    public bool TryConfirmPlacement(GameEngine engine, out string error)
        => _runtime.TryConfirmStageAtPreview(engine, out error);

    public bool TryStageBuilding(GameEngine engine, out string error)
        => _runtime.TryStageBuildingAtGate(engine, build: true, out error);

    public bool TryStageTerrainRaise(GameEngine engine, out string error)
        => _runtime.TryStageTerrainAtHotspot(engine, NavTriangleSurfaceTerrainBrushKind.Raise, out error);

    public bool TryBake(GameEngine engine, out string error)
        => _runtime.TryBakeStagedEdit(engine, out error);

    public bool TryRestore(GameEngine engine, out string error)
        => _runtime.TryStageRestore(engine, out error);

    public bool TrySetNavMeshVisible(GameEngine engine, bool visible, out string error)
        => _runtime.TrySetNavMeshVisible(engine, visible, out error);

    public bool TryBuildWall(GameEngine engine, out string error)
        => _runtime.TryBuildWall(engine, out error);

    public bool TryDemolishWall(GameEngine engine, out string error)
        => _runtime.TryDemolishWall(engine, out error);

    public bool TryDeploySquad(GameEngine engine, out string error)
        => _runtime.TryDeploySquad(engine, out error);

    public bool TryDeploySquadNonBlocking(GameEngine engine, out string error)
        => _runtime.TryDeploySquadNonBlocking(engine, out error);

    public bool TryCommandMoveToGoal(GameEngine engine, out string error)
        => _runtime.TryCommandMoveToGoal(engine, out error);

    public bool TryNextHotspot(GameEngine engine, out string error)
        => _runtime.TryNextHotspot(engine, out error);

    public bool TryReturn(GameEngine engine, out string error)
        => _runtime.TryReturn(engine, out error);

    public void DrainUntilIdle(GameEngine engine, int maxTicks)
        => _runtime.DrainUntilIdle(engine, maxTicks);

    public DynamicNavBakeShowcaseEvidence CaptureEvidence(GameEngine engine)
        => _runtime.CaptureEvidence(engine);

    /// <summary>
    /// Allocation-free formal player-route observation for host-frame readiness / screenshot gates.
    /// </summary>
    public DynamicNavBakeShowcaseFormalPlayerRouteSnapshot CaptureFormalPlayerRouteSnapshot(GameEngine engine)
        => _runtime.CaptureFormalPlayerRouteSnapshot(engine);

    /// <summary>
    /// Allocation-free read-only arrival observation over pre-bound authored squad entities.
    /// </summary>
    public DynamicNavBakeShowcaseSquadArrivalSnapshot CaptureSquadArrivalSnapshot(GameEngine engine)
        => _runtime.CaptureSquadArrivalSnapshot(engine);

    public static DynamicNavBakeShowcaseActions Require(GameEngine engine)
    {
        if (engine.GlobalContext.TryGetValue(DynamicNavBakeShowcaseIds.RuntimeServiceKey, out object? value) &&
            value is DynamicNavBakeShowcaseActions actions)
        {
            return actions;
        }

        throw new InvalidOperationException("DynamicNavBakeShowcaseActions is not registered for the active map.");
    }
}
