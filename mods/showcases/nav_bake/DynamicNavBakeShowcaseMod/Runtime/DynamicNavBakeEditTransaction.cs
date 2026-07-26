using System;
using Ludots.Core.Mathematics;
using Ludots.Core.Navigation.NavMesh.Surface;

namespace DynamicNavBakeShowcaseMod.Runtime;

public enum DynamicNavBakeEditTool : byte
{
    Building = 0,
    Terrain = 1
}

public enum DynamicNavBakePlayerNavState : byte
{
    Waiting = 0,
    Baking = 1,
    RouteUpdated = 2
}

public enum DynamicNavBakeStagedEditKind : byte
{
    None = 0,
    BuildWall = 1,
    DemolishWall = 2,
    TerrainBlock = 3,
    TerrainRaise = 4,
    RestoreBuilding = 5,
    RestoreTerrain = 6
}

public enum DynamicNavBakePlacementLegality : byte
{
    None = 0,
    Legal = 1,
    IllegalOutsideResident = 2,
    IllegalOccupied = 3,
    IllegalNoPointer = 4,
    IllegalWrongToolState = 5
}

/// <summary>
/// Showcase-owned construction transaction. Staging never mutates ECS/obstacles/triangle SSOT.
/// Bake commits through wall pool or RuntimeNavTriangleSurfaceService, then dirty rebuild runs.
/// </summary>
public sealed class DynamicNavBakeEditTransaction
{
    private DynamicNavBakeEditTool _tool = DynamicNavBakeEditTool.Building;
    private DynamicNavBakeStagedEditKind _stagedKind = DynamicNavBakeStagedEditKind.None;
    private DynamicNavBakePlayerNavState _playerNavState = DynamicNavBakePlayerNavState.Waiting;
    private DynamicNavBakePlacementLegality _previewLegality = DynamicNavBakePlacementLegality.None;
    private bool _hasPreviewWorld;
    private int _previewXCm;
    private int _previewZCm;
    private bool _hasCommittedRestore;
    private DynamicNavBakeStagedEditKind _restoreKind = DynamicNavBakeStagedEditKind.None;
    private int _committedBuildingCenterXCm;
    private int _committedBuildingCenterZCm;
    private bool _committedBuildingWasBuilt;
    private NavTriangleSurfaceTileIndex? _terrainBeforeImage;
    private NavTriangleSurfaceTileIndex? _terrainCommittedImage;
    private WorldAabbCm _terrainCommittedDirty;
    private string _playerStatus = "Pick a tool, then place an edit.";

    public DynamicNavBakeEditTool Tool => _tool;
    public DynamicNavBakeStagedEditKind StagedKind => _stagedKind;
    public DynamicNavBakePlayerNavState PlayerNavState => _playerNavState;
    public DynamicNavBakePlacementLegality PreviewLegality => _previewLegality;
    public bool HasPreviewWorld => _hasPreviewWorld;
    public int PreviewXCm => _previewXCm;
    public int PreviewZCm => _previewZCm;
    public bool HasStagedEdit => _stagedKind != DynamicNavBakeStagedEditKind.None;
    public bool CanRestore => _hasCommittedRestore;
    public string PlayerStatus => _playerStatus;

    public void Reset()
    {
        _tool = DynamicNavBakeEditTool.Building;
        _stagedKind = DynamicNavBakeStagedEditKind.None;
        _playerNavState = DynamicNavBakePlayerNavState.Waiting;
        _previewLegality = DynamicNavBakePlacementLegality.None;
        _hasPreviewWorld = false;
        _previewXCm = 0;
        _previewZCm = 0;
        _hasCommittedRestore = false;
        _restoreKind = DynamicNavBakeStagedEditKind.None;
        _committedBuildingCenterXCm = 0;
        _committedBuildingCenterZCm = 0;
        _committedBuildingWasBuilt = false;
        _terrainBeforeImage = null;
        _terrainCommittedImage = null;
        _terrainCommittedDirty = default;
        _playerStatus = "Pick a tool, then place an edit.";
    }

    public void SetTool(DynamicNavBakeEditTool tool)
    {
        if (_stagedKind != DynamicNavBakeStagedEditKind.None)
        {
            throw new InvalidOperationException(
                "Cannot change construction tool while an edit is staged. Bake or clear the staged edit first.");
        }

        _tool = tool;
        ClearPreview();
        _playerStatus = tool == DynamicNavBakeEditTool.Building
            ? "Building tool ready. Aim at a legal spot, then Confirm."
            : "Terrain tool ready. Aim at a legal spot, then Confirm.";
    }

    public void SetPreview(int xCm, int zCm, DynamicNavBakePlacementLegality legality)
    {
        _hasPreviewWorld = true;
        _previewXCm = xCm;
        _previewZCm = zCm;
        _previewLegality = legality;
    }

    public void ClearPreview()
    {
        _hasPreviewWorld = false;
        _previewLegality = DynamicNavBakePlacementLegality.None;
    }

    public void StageBuilding(bool build, int centerXCm, int centerZCm)
    {
        RequireNoStaged();
        _stagedKind = build ? DynamicNavBakeStagedEditKind.BuildWall : DynamicNavBakeStagedEditKind.DemolishWall;
        _previewXCm = centerXCm;
        _previewZCm = centerZCm;
        _hasPreviewWorld = true;
        _previewLegality = DynamicNavBakePlacementLegality.Legal;
        _playerNavState = DynamicNavBakePlayerNavState.Waiting;
        _playerStatus = build
            ? "Building staged. Press Bake to apply."
            : "Demolish staged. Press Bake to apply.";
    }

    public void StageTerrain(NavTriangleSurfaceTerrainBrushKind kind, int centerXCm, int centerZCm)
    {
        RequireNoStaged();
        _stagedKind = kind == NavTriangleSurfaceTerrainBrushKind.Block
            ? DynamicNavBakeStagedEditKind.TerrainBlock
            : DynamicNavBakeStagedEditKind.TerrainRaise;
        _previewXCm = centerXCm;
        _previewZCm = centerZCm;
        _hasPreviewWorld = true;
        _previewLegality = DynamicNavBakePlacementLegality.Legal;
        _playerNavState = DynamicNavBakePlayerNavState.Waiting;
        _playerStatus = kind == NavTriangleSurfaceTerrainBrushKind.Block
            ? "Terrain block staged. Press Bake to apply."
            : "Terrain raise staged. Press Bake to apply.";
    }

    public void StageRestore()
    {
        RequireNoStaged();
        if (!_hasCommittedRestore)
        {
            throw new InvalidOperationException("No committed edit is available to restore.");
        }

        _stagedKind = _restoreKind;
        _playerNavState = DynamicNavBakePlayerNavState.Waiting;
        _playerStatus = "Restore staged. Press Bake to apply the before-image.";
    }

    public void ClearStaged()
    {
        _stagedKind = DynamicNavBakeStagedEditKind.None;
        _playerStatus = "Staged edit cleared.";
    }

    public void BeginBaking(string status)
    {
        if (_stagedKind == DynamicNavBakeStagedEditKind.None)
        {
            throw new InvalidOperationException("Bake requires a staged edit.");
        }

        _playerNavState = DynamicNavBakePlayerNavState.Baking;
        _playerStatus = status;
    }

    public void MarkRouteUpdated(string status)
    {
        _playerNavState = DynamicNavBakePlayerNavState.RouteUpdated;
        _playerStatus = status;
        _stagedKind = DynamicNavBakeStagedEditKind.None;
    }

    public void MarkBakeFailedKeepGeneration(string status)
    {
        _playerNavState = DynamicNavBakePlayerNavState.Waiting;
        _playerStatus = status;
        // Keep staged edit so the player can retry Bake without losing intent.
    }

    public void MarkCommittedBakeFailedKeepGeneration(string status)
    {
        _playerNavState = DynamicNavBakePlayerNavState.Waiting;
        _playerStatus = status;
        _stagedKind = DynamicNavBakeStagedEditKind.None;
    }

    public void RecordBuildingCommit(bool built, int centerXCm, int centerZCm)
    {
        _committedBuildingWasBuilt = built;
        _committedBuildingCenterXCm = centerXCm;
        _committedBuildingCenterZCm = centerZCm;
        _hasCommittedRestore = true;
        _restoreKind = built
            ? DynamicNavBakeStagedEditKind.RestoreBuilding
            : DynamicNavBakeStagedEditKind.RestoreBuilding;
        // Restore of a build is demolish; restore of a demolish is rebuild at same center.
        _restoreKind = DynamicNavBakeStagedEditKind.RestoreBuilding;
    }

    public void RecordTerrainCommit(
        NavTriangleSurfaceTileIndex beforeImage,
        NavTriangleSurfaceTileIndex committedImage,
        WorldAabbCm dirtyAabb)
    {
        _terrainBeforeImage = beforeImage ?? throw new ArgumentNullException(nameof(beforeImage));
        _terrainCommittedImage = committedImage ?? throw new ArgumentNullException(nameof(committedImage));
        _terrainCommittedDirty = dirtyAabb;
        _hasCommittedRestore = true;
        _restoreKind = DynamicNavBakeStagedEditKind.RestoreTerrain;
    }

    public bool TryGetCommittedBuilding(out bool wasBuilt, out int centerXCm, out int centerZCm)
    {
        wasBuilt = _committedBuildingWasBuilt;
        centerXCm = _committedBuildingCenterXCm;
        centerZCm = _committedBuildingCenterZCm;
        return _hasCommittedRestore && _restoreKind == DynamicNavBakeStagedEditKind.RestoreBuilding;
    }

    public bool TryGetTerrainBeforeImage(out NavTriangleSurfaceTileIndex beforeImage, out WorldAabbCm dirtyHint)
    {
        beforeImage = _terrainBeforeImage!;
        dirtyHint = _terrainCommittedDirty;
        return _terrainBeforeImage != null && _restoreKind == DynamicNavBakeStagedEditKind.RestoreTerrain;
    }

    public string FormatPlayerNavState()
        => _playerNavState switch
        {
            DynamicNavBakePlayerNavState.Waiting => "Waiting",
            DynamicNavBakePlayerNavState.Baking => "Baking",
            DynamicNavBakePlayerNavState.RouteUpdated => "Route Updated",
            _ => throw new InvalidOperationException($"Unknown player nav state '{_playerNavState}'.")
        };

    public string FormatTool()
        => _tool switch
        {
            DynamicNavBakeEditTool.Building => "Building",
            DynamicNavBakeEditTool.Terrain => "Terrain",
            _ => throw new InvalidOperationException($"Unknown edit tool '{_tool}'.")
        };

    public string FormatPreviewLegality()
        => _previewLegality switch
        {
            DynamicNavBakePlacementLegality.None => "No aim",
            DynamicNavBakePlacementLegality.Legal => "Legal",
            DynamicNavBakePlacementLegality.IllegalOutsideResident => "Illegal: outside loaded battle area",
            DynamicNavBakePlacementLegality.IllegalOccupied => "Illegal: slot already used",
            DynamicNavBakePlacementLegality.IllegalNoPointer => "Illegal: no ground aim",
            DynamicNavBakePlacementLegality.IllegalWrongToolState => "Illegal: clear staged edit first",
            _ => throw new InvalidOperationException($"Unknown placement legality '{_previewLegality}'.")
        };

    private void RequireNoStaged()
    {
        if (_stagedKind != DynamicNavBakeStagedEditKind.None)
        {
            throw new InvalidOperationException(
                "An edit is already staged. Bake it or clear it before staging another.");
        }
    }
}
