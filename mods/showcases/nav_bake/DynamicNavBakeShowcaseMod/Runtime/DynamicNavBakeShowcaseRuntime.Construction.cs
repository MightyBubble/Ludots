using System;
using Arch.Core;
using Ludots.Core.Engine;
using Ludots.Core.Input.Runtime;
using Ludots.Core.Mathematics;
using Ludots.Core.Navigation.NavMesh;
using Ludots.Core.Navigation.NavMesh.Bake;
using Ludots.Core.Navigation.NavMesh.Config;
using Ludots.Core.Navigation.NavMesh.Surface;
using Ludots.Core.Scripting;

namespace DynamicNavBakeShowcaseMod.Runtime;

internal sealed partial class DynamicNavBakeShowcaseRuntime
{
    public DynamicNavBakeEditTransaction EditTransaction => _editTransaction;

    public bool TryEnterConstructionMode(GameEngine engine, out string error)
    {
        error = string.Empty;
        if (!IsActive)
        {
            error = "Showcase is not active.";
            return false;
        }

        if (_constructionMode)
        {
            return true;
        }

        if (_editTransaction.HasStagedEdit || _editBakeAwaitingCompletion)
        {
            error = "Navigation is still updating. Wait before entering construction.";
            _lastStatus = error;
            RefreshPanel(engine);
            return false;
        }

        try
        {
            _editTransaction.SetTool(DynamicNavBakeEditTool.Building);
            SetConstructionMode(engine, enabled: true);
            _lastStatus = "建造模式：绿色可放置，红色不可放。左键落地，右键或 Esc 取消。";
            RefreshPanel(engine);
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            _lastStatus = error;
            RefreshPanel(engine);
            return false;
        }
    }

    public bool TryExitConstructionMode(GameEngine engine, out string error)
    {
        error = string.Empty;
        if (!_constructionMode)
        {
            return true;
        }

        if (_editTransaction.HasStagedEdit)
        {
            _editTransaction.ClearStaged();
        }

        _editTransaction.ClearPreview();
        SetConstructionMode(engine, enabled: false);
        _lastStatus = "已退出建造模式。左键框选，右键移动。";
        RefreshPanel(engine);
        return true;
    }

    public bool TryPlaceBuildingAtPreview(GameEngine engine, out string error)
    {
        error = string.Empty;
        if (!_constructionMode)
        {
            error = "Enter construction mode before placing a building.";
            _lastStatus = error;
            RefreshPanel(engine);
            return false;
        }

        if (!TryConfirmStageAtPreview(engine, out error))
        {
            return false;
        }

        if (!TryBakeStagedEdit(engine, out error))
        {
            return false;
        }

        SetConstructionMode(engine, enabled: false);
        _editTransaction.ClearPreview();
        if (string.IsNullOrEmpty(_lastStatus))
        {
            _lastStatus = "建筑已放置，导航正在更新。";
        }

        RefreshPanel(engine);
        return true;
    }

    public bool TrySetEditTool(GameEngine engine, DynamicNavBakeEditTool tool, out string error)
    {
        error = string.Empty;
        if (!IsActive)
        {
            error = "Showcase is not active.";
            return false;
        }

        try
        {
            _editTransaction.SetTool(tool);
            _lastStatus = _editTransaction.PlayerStatus;
            RefreshPanel(engine);
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            _lastStatus = error;
            RefreshPanel(engine);
            return false;
        }
    }

    public bool TryUpdatePlacementPreview(GameEngine engine, out string error)
    {
        error = string.Empty;
        if (!IsActive || !_entitiesBound)
        {
            return true;
        }

        if (!_constructionMode)
        {
            if (_editTransaction.HasPreviewWorld && !_editTransaction.HasStagedEdit)
            {
                _editTransaction.ClearPreview();
            }

            return true;
        }

        if (_editTransaction.HasStagedEdit)
        {
            return true;
        }

        if (!TryResolvePointerWorld(engine, out int xCm, out int zCm))
        {
            _editTransaction.ClearPreview();
            return true;
        }

        DynamicNavBakePlacementLegality legality = EvaluatePlacementLegality(engine, xCm, zCm);
        _editTransaction.SetPreview(xCm, zCm, legality);
        return true;
    }

    public bool TryConfirmStageAtPreview(GameEngine engine, out string error)
    {
        error = string.Empty;
        if (!IsActive)
        {
            error = "Showcase is not active.";
            return false;
        }

        EnsureEntitiesBound(engine);
        if (!_editTransaction.HasPreviewWorld ||
            _editTransaction.PreviewLegality != DynamicNavBakePlacementLegality.Legal)
        {
            error = _editTransaction.HasPreviewWorld
                ? _editTransaction.FormatPreviewLegality()
                : "Aim at a legal ground position before Confirm.";
            _lastStatus = error;
            RefreshPanel(engine);
            return false;
        }

        int xCm = _editTransaction.PreviewXCm;
        int zCm = _editTransaction.PreviewZCm;
        try
        {
            if (_editTransaction.Tool == DynamicNavBakeEditTool.Building)
            {
                if (_constructionMode && WallDeployedCount > 0)
                {
                    error = FormatLegalityError(DynamicNavBakePlacementLegality.IllegalOccupied);
                    _lastStatus = error;
                    RefreshPanel(engine);
                    return false;
                }

                bool build = WallDeployedCount == 0;
                _editTransaction.StageBuilding(build, xCm, zCm);
            }
            else
            {
                _editTransaction.StageTerrain(NavTriangleSurfaceTerrainBrushKind.Raise, xCm, zCm);
            }

            _lastStatus = _editTransaction.PlayerStatus;
            RefreshPanel(engine);
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            _lastStatus = error;
            RefreshPanel(engine);
            return false;
        }
    }

    public bool TryStageBuildingAtGate(GameEngine engine, bool build, out string error)
    {
        error = string.Empty;
        if (!IsActive)
        {
            error = "Showcase is not active.";
            return false;
        }

        EnsureEntitiesBound(engine);
        ResolveActiveWallCenter(out int centerXCm, out int centerYCm, out _);
        DynamicNavBakePlacementLegality legality = EvaluatePlacementLegality(engine, centerXCm, centerYCm);
        if (legality != DynamicNavBakePlacementLegality.Legal)
        {
            error = FormatLegalityError(legality);
            _lastStatus = error;
            RefreshPanel(engine);
            return false;
        }

        try
        {
            _editTransaction.SetTool(DynamicNavBakeEditTool.Building);
            _editTransaction.StageBuilding(build, centerXCm, centerYCm);
            _lastStatus = _editTransaction.PlayerStatus;
            RefreshPanel(engine);
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            _lastStatus = error;
            RefreshPanel(engine);
            return false;
        }
    }

    public bool TryStageTerrainAt(
        GameEngine engine,
        int centerXCm,
        int centerZCm,
        NavTriangleSurfaceTerrainBrushKind kind,
        out string error)
    {
        error = string.Empty;
        if (!IsActive)
        {
            error = "Showcase is not active.";
            return false;
        }

        EnsureEntitiesBound(engine);
        DynamicNavBakePlacementLegality legality = EvaluatePlacementLegality(engine, centerXCm, centerZCm);
        if (legality != DynamicNavBakePlacementLegality.Legal)
        {
            error = FormatLegalityError(legality);
            _lastStatus = error;
            RefreshPanel(engine);
            return false;
        }

        try
        {
            _editTransaction.SetTool(DynamicNavBakeEditTool.Terrain);
            _editTransaction.StageTerrain(kind, centerXCm, centerZCm);
            _lastStatus = _editTransaction.PlayerStatus;
            RefreshPanel(engine);
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            _lastStatus = error;
            RefreshPanel(engine);
            return false;
        }
    }

    public bool TryStageTerrainAtHotspot(
        GameEngine engine,
        NavTriangleSurfaceTerrainBrushKind kind,
        out string error)
    {
        ResolveActiveWallCenter(out int centerXCm, out int centerZCm, out _);
        return TryStageTerrainAt(engine, centerXCm, centerZCm, kind, out error);
    }

    public bool TryBakeStagedEdit(GameEngine engine, out string error)
    {
        error = string.Empty;
        if (!IsActive)
        {
            error = "Showcase is not active.";
            return false;
        }

        if (!_editTransaction.HasStagedEdit)
        {
            error = "Nothing is staged. Confirm a placement first.";
            _lastStatus = error;
            RefreshPanel(engine);
            return false;
        }

        EnsureEntitiesBound(engine);
        RuntimeIncrementalNavMeshRebuildQueue queue = RequireQueue(engine);
        if (queue.Status != RuntimeNavMeshRebuildStatus.Idle || queue.HasResidentWindowTransition)
        {
            error = "Navigation is already baking. Wait for the current update to finish.";
            _lastStatus = error;
            RefreshPanel(engine);
            return false;
        }

        ulong generationBefore = ReadLatestGeneration(engine);
        RuntimeNavMeshTelemetryService telemetry = engine.GetService(CoreServiceKeys.RuntimeNavMeshTelemetry)
            ?? throw new InvalidOperationException(
                "DynamicNavBake edit Bake requires CoreServiceKeys.RuntimeNavMeshTelemetry.");
        _editTransaction.BeginBaking("Baking navigation for your edit…");
        _lastStatus = _editTransaction.PlayerStatus;
        RefreshPanel(engine);

        try
        {
            switch (_editTransaction.StagedKind)
            {
                case DynamicNavBakeStagedEditKind.BuildWall:
                    CommitBuilding(engine, build: true);
                    break;
                case DynamicNavBakeStagedEditKind.DemolishWall:
                    CommitBuilding(engine, build: false);
                    break;
                case DynamicNavBakeStagedEditKind.TerrainBlock:
                    CommitTerrain(engine, NavTriangleSurfaceTerrainBrushKind.Block);
                    break;
                case DynamicNavBakeStagedEditKind.TerrainRaise:
                    CommitTerrain(engine, NavTriangleSurfaceTerrainBrushKind.Raise);
                    break;
                case DynamicNavBakeStagedEditKind.RestoreBuilding:
                    CommitRestoreBuilding(engine);
                    break;
                case DynamicNavBakeStagedEditKind.RestoreTerrain:
                    CommitRestoreTerrain(engine);
                    break;
                default:
                    throw new InvalidOperationException(
                        $"Unsupported staged edit kind '{_editTransaction.StagedKind}'.");
            }

            _editBakeGenerationBefore = generationBefore;
            _editBakeFailedBatchCountBefore = telemetry.FailedBatchCount;
            _editBakeAwaitingCompletion = true;
            // Structural bake must not cancel live marches — units keep orders and repath
            // after the new nav generation commits (see AdvanceEditBake → RequestFormalRouteRepath).
            ClearShowcasePathOverlayOnly();
            _lastStatus = "Baking navigation for your edit…";
            RefreshPanel(engine);
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            _editTransaction.MarkBakeFailedKeepGeneration(
                $"Bake failed; previous navigation kept. {error}");
            _lastStatus = _editTransaction.PlayerStatus;
            RefreshPanel(engine);
            return false;
        }
    }

    public bool TryStageRestore(GameEngine engine, out string error)
    {
        error = string.Empty;
        if (!IsActive)
        {
            error = "Showcase is not active.";
            return false;
        }

        try
        {
            _editTransaction.StageRestore();
            _lastStatus = _editTransaction.PlayerStatus;
            RefreshPanel(engine);
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            _lastStatus = error;
            RefreshPanel(engine);
            return false;
        }
    }

    public bool TrySwitchShowcaseMap(GameEngine engine, string mapId, out string error)
    {
        error = string.Empty;
        if (string.IsNullOrWhiteSpace(mapId) || !DynamicNavBakeShowcaseIds.IsShowcaseMap(mapId))
        {
            error = $"Map '{mapId}' is not a Dynamic NavBake showcase map.";
            return false;
        }

        string? current = engine.CurrentMapSession?.MapId.Value;
        if (string.Equals(current, mapId, StringComparison.Ordinal))
        {
            _lastStatus = $"Already on map '{mapId}'.";
            RefreshPanel(engine);
            return true;
        }

        try
        {
            // Showcase map switching is exclusive. Leaving the previous large world resident would
            // keep its entities outside the next map's spatial bounds when focus returns to RTS.
            Unbind(engine);
            if (!string.IsNullOrEmpty(current))
            {
                engine.UnloadMap(current);
            }

            engine.LoadMap(mapId);
            _lastStatus = $"Switched to map '{mapId}'.";
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            _lastStatus = error;
            return false;
        }
    }

    private void CommitBuilding(GameEngine engine, bool build)
    {
        DynamicNavBakeShowcaseWallPool pool = RequireWallPool();
        int centerXCm = _editTransaction.PreviewXCm;
        int centerYCm = _editTransaction.PreviewZCm;
        if (build)
        {
            if (!pool.TryBuildAll(engine, ActiveConfig, centerXCm, centerYCm, out string buildError))
            {
                throw new InvalidOperationException(buildError);
            }

            _editTransaction.RecordBuildingCommit(built: true, centerXCm, centerYCm);
        }
        else
        {
            if (!pool.TryDemolishAll(engine, ActiveConfig, out string demolishError))
            {
                throw new InvalidOperationException(demolishError);
            }

            _editTransaction.RecordBuildingCommit(built: false, centerXCm, centerYCm);
        }
    }

    private void CommitRestoreBuilding(GameEngine engine)
    {
        if (!_editTransaction.TryGetCommittedBuilding(out bool wasBuilt, out int centerXCm, out int centerZCm))
        {
            throw new InvalidOperationException("Restore building requires a committed building before-image.");
        }

        DynamicNavBakeShowcaseWallPool pool = RequireWallPool();
        if (wasBuilt)
        {
            if (!pool.TryDemolishAll(engine, ActiveConfig, out string demolishError))
            {
                throw new InvalidOperationException(demolishError);
            }

            _editTransaction.RecordBuildingCommit(built: false, centerXCm, centerZCm);
        }
        else
        {
            if (!pool.TryBuildAll(engine, ActiveConfig, centerXCm, centerZCm, out string buildError))
            {
                throw new InvalidOperationException(buildError);
            }

            _editTransaction.RecordBuildingCommit(built: true, centerXCm, centerZCm);
        }
    }

    private void CommitTerrain(GameEngine engine, NavTriangleSurfaceTerrainBrushKind kind)
    {
        RuntimeNavTriangleSurfaceService surfaceService = engine.GetService(CoreServiceKeys.RuntimeNavTriangleSurface)
            ?? throw new InvalidOperationException(
                "Terrain bake requires CoreServiceKeys.RuntimeNavTriangleSurface.");
        NavMeshBakeConfig bakeConfig = engine.GetService(CoreServiceKeys.NavMeshBakeConfig)
            ?? throw new InvalidOperationException("Terrain bake requires NavMeshBakeConfig.");

        var spec = new NavTriangleSurfaceTerrainBrushSpec(
            _editTransaction.PreviewXCm,
            _editTransaction.PreviewZCm,
            ActiveConfig.TerrainBrushHalfExtentCm,
            kind,
            ActiveConfig.TerrainEditCellSizeCm,
            bakeConfig.RuntimeIncremental?.HeightScaleMeters
                ?? throw new InvalidOperationException("Terrain bake requires runtimeIncremental.heightScaleMeters."),
            baseHeightLevel: 0,
            ActiveConfig.TerrainRaiseHeightLevel,
            targetMinYcm: ActiveConfig.Gate.NavMinYcm,
            targetMaxYcm: ActiveConfig.Gate.NavMaxYcm);

        RuntimeNavTriangleSurfaceEditTransaction transaction =
            engine.GetService(CoreServiceKeys.RuntimeNavTriangleSurfaceEditTransaction)
            ?? throw new InvalidOperationException(
                "Terrain bake requires CoreServiceKeys.RuntimeNavTriangleSurfaceEditTransaction.");
        transaction.StageBrush(in spec);
        NavTriangleSurfaceTileIndex before = transaction.StagedBefore;
        NavTriangleSurfaceTileIndex after = transaction.StagedAfter;
        WorldAabbCm dirty = transaction.StagedDirtyAabb;
        transaction.Commit();
        if (!ReferenceEquals(surfaceService.Published, after))
        {
            throw new InvalidOperationException(
                "Core terrain edit transaction committed without publishing its staged after-image.");
        }

        _editTransaction.RecordTerrainCommit(before, after, dirty);
    }

    private void CommitRestoreTerrain(GameEngine engine)
    {
        if (!_editTransaction.TryGetTerrainBeforeImage(out _, out _))
        {
            throw new InvalidOperationException("Restore terrain requires a committed terrain before-image.");
        }

        RuntimeNavTriangleSurfaceEditTransaction transaction =
            engine.GetService(CoreServiceKeys.RuntimeNavTriangleSurfaceEditTransaction)
            ?? throw new InvalidOperationException(
                "Terrain restore requires CoreServiceKeys.RuntimeNavTriangleSurfaceEditTransaction.");
        transaction.StageExactRestore();
        NavTriangleSurfaceTileIndex current = transaction.StagedBefore;
        NavTriangleSurfaceTileIndex restored = transaction.StagedAfter;
        WorldAabbCm dirty = transaction.StagedDirtyAabb;
        transaction.Commit();
        _editTransaction.RecordTerrainCommit(current, restored, dirty);
    }

    private void AdvanceEditBake(GameEngine engine, RuntimeIncrementalNavMeshRebuildQueue queue)
    {
        if (!_editBakeAwaitingCompletion)
        {
            return;
        }

        RuntimeNavMeshTelemetryService telemetry = engine.GetService(CoreServiceKeys.RuntimeNavMeshTelemetry)
            ?? throw new InvalidOperationException(
                "DynamicNavBake edit Bake requires CoreServiceKeys.RuntimeNavMeshTelemetry while in flight.");
        if (telemetry.FailedBatchCount > _editBakeFailedBatchCountBefore)
        {
            _editBakeAwaitingCompletion = false;
            _editTransaction.MarkCommittedBakeFailedKeepGeneration(
                "Bake failed; the previous navigation generation remains active. Restore or try another edit.");
            _lastStatus = _editTransaction.PlayerStatus;
            RefreshPanel(engine);
            return;
        }

        bool busy = queue.Status != RuntimeNavMeshRebuildStatus.Idle ||
                    queue.HasResidentWindowTransition ||
                    telemetry.HasOpenGeneration;
        if (busy || ReadLatestGeneration(engine) == _editBakeGenerationBefore)
        {
            _lastStatus = "Baking navigation for your edit…";
            return;
        }

        _editBakeAwaitingCompletion = false;
        _editTransaction.MarkRouteUpdated("Route Updated — navigation reflects your edit.");
        _lastStatus = _editTransaction.PlayerStatus;
        RequestFormalRouteRepath(engine);
        if (_squadDeployed)
        {
            RecomputePath(engine);
        }

        RefreshPanel(engine);
    }

    private DynamicNavBakePlacementLegality EvaluatePlacementLegality(GameEngine engine, int xCm, int zCm)
    {
        RuntimeIncrementalNavMeshRebuildQueue queue = RequireQueue(engine);
        if (ActiveConfig.ResolvedSceneKind == DynamicNavBakeShowcaseSceneKind.OpenWorld)
        {
            if (queue.CommittedResidentWindowCount <= 0 ||
                !queue.IsWorldPointInCommittedResidentWindow(xCm, zCm))
            {
                return DynamicNavBakePlacementLegality.IllegalOutsideResident;
            }
        }
        else
        {
            int minX = ActiveConfig.WorldOriginXCm;
            int minY = ActiveConfig.WorldOriginZCm;
            int maxX = ActiveConfig.WorldMaxXCm;
            int maxY = ActiveConfig.WorldMaxZCm;
            if (xCm < minX || zCm < minY || xCm >= maxX || zCm >= maxY)
            {
                return DynamicNavBakePlacementLegality.IllegalOutsideResident;
            }
        }

        // Player construction mode only places buildings; an occupied slot is illegal (red preview).
        // Harness demolish / restore APIs bypass this by calling Stage* with an explicit build flag.
        if (_constructionMode &&
            _editTransaction.Tool == DynamicNavBakeEditTool.Building &&
            WallDeployedCount > 0)
        {
            return DynamicNavBakePlacementLegality.IllegalOccupied;
        }

        return DynamicNavBakePlacementLegality.Legal;
    }

    private void SetConstructionMode(GameEngine engine, bool enabled)
    {
        _constructionMode = enabled;
        engine.GlobalContext[CoreServiceKeys.CommandSourceAcquisitionSuppressed.Name] = enabled;
    }

    private static bool TryResolvePointerWorld(GameEngine engine, out int xCm, out int zCm)
    {
        xCm = 0;
        zCm = 0;
        if (engine.GetService(CoreServiceKeys.AuthoritativeInput) is not IInputActionReader input)
        {
            return false;
        }

        if (!AuthoritativeGroundPointerHelper.TryRead(input, out WorldCmInt2 worldCm))
        {
            return false;
        }

        xCm = worldCm.X;
        zCm = worldCm.Y;
        return true;
    }

    private static string FormatLegalityError(DynamicNavBakePlacementLegality legality)
        => legality switch
        {
            DynamicNavBakePlacementLegality.IllegalOutsideResident => "Placement is outside the loaded battle area.",
            DynamicNavBakePlacementLegality.IllegalOccupied => "Placement slot is already occupied.",
            DynamicNavBakePlacementLegality.IllegalNoPointer => "No ground aim available.",
            DynamicNavBakePlacementLegality.IllegalWrongToolState => "Clear the staged edit before placing another.",
            _ => $"Placement rejected ({legality})."
        };
}
