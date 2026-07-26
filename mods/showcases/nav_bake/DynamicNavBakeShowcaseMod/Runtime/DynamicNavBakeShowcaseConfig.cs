using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Nodes;
using Ludots.Core.Config;
using Ludots.Core.Spatial;

namespace DynamicNavBakeShowcaseMod.Runtime;

public enum DynamicNavBakeShowcaseSceneKind : byte
{
    Rts = 0,
    OpenWorld = 1
}

public sealed class DynamicNavBakeShowcaseConfig
{
    public string MapId { get; set; } = string.Empty;
    public string SceneKind { get; set; } = string.Empty;
    public int WidthInMacroTiles { get; set; }
    public int HeightInMacroTiles { get; set; }
    public int GridCellSizeCm { get; set; } = SpatialScaleDefaults.CellCm;
    public int ChunkSizeCells { get; set; } = SpatialScaleDefaults.TerrainChunkCells;
    public int ResidentWidthChunks { get; set; }
    public int ResidentHeightChunks { get; set; }
    public int CameraTargetXCm { get; set; }
    public int CameraTargetYCm { get; set; }
    public DynamicNavBakeShowcaseSquadConfig Squad { get; set; } = new();
    public DynamicNavBakeShowcaseGoalConfig Goal { get; set; } = new();
    public DynamicNavBakeShowcaseGateConfig Gate { get; set; } = new();
    public DynamicNavBakeShowcaseSideRouteConfig SideRouteWest { get; set; } = new();
    public DynamicNavBakeShowcaseSideRouteConfig SideRouteEast { get; set; } = new();
    public DynamicNavBakeShowcaseParkingConfig Parking { get; set; } = new();
    public int MovementSpeedCmPerSec { get; set; }
    public int WallPoolCapacity { get; set; }
    public int TerrainBrushHalfExtentCm { get; set; }
    public byte TerrainRaiseHeightLevel { get; set; } = 2;
    public int EvidenceSampleCount { get; set; }
    public DynamicNavBakeShowcaseBenchmarkConfig Benchmark { get; set; } = null!;
    public DynamicNavBakeShowcaseUiConfig Ui { get; set; } = new();
    public DynamicNavBakeShowcasePresentationConfig Presentation { get; set; } = null!;
    public DynamicNavBakeShowcaseRaylibAutoTimelineConfig RaylibAutoTimeline { get; set; } = null!;
    public DynamicNavBakeShowcaseOpenWorldConfig? OpenWorld { get; set; }

    public DynamicNavBakeShowcaseSceneKind ResolvedSceneKind => SceneKind switch
    {
        "rts" => DynamicNavBakeShowcaseSceneKind.Rts,
        "open_world" => DynamicNavBakeShowcaseSceneKind.OpenWorld,
        _ => throw new InvalidOperationException(
            $"DynamicNavBakeShowcaseConfig.sceneKind must be 'rts' or 'open_world', got '{SceneKind}'.")
    };

    public int WidthChunks => checked(WidthInMacroTiles * SpatialScaleDefaults.MacroTileCells / ChunkSizeCells);
    public int HeightChunks => checked(HeightInMacroTiles * SpatialScaleDefaults.MacroTileCells / ChunkSizeCells);
    public int ChunkSizeCm => checked(ChunkSizeCells * GridCellSizeCm);
    public int WorldWidthCm => checked(WidthChunks * ChunkSizeCm);
    public int WorldHeightCm => checked(HeightChunks * ChunkSizeCm);

    public static DynamicNavBakeShowcaseConfig Load(JsonObject configObject)
    {
        using JsonDocument document = JsonDocument.Parse(configObject.ToJsonString());
        ValidateRequiredProperties(document.RootElement);
        ValidateOpenWorldHotspotRequiredProperties(document.RootElement);
        ValidateBenchmarkRequiredProperties(document.RootElement);
        ValidatePresentationRequiredProperties(document.RootElement);
        ValidateRaylibAutoTimelineRequiredProperties(document.RootElement);
        var options = StrictJsonOptions.CreateCamelCase();
        DynamicNavBakeShowcaseConfig? config = document.RootElement.Deserialize<DynamicNavBakeShowcaseConfig>(options);
        if (config == null)
        {
            throw new InvalidOperationException("Failed to deserialize DynamicNavBakeShowcaseConfig.");
        }

        config.Validate();
        return config;
    }

    public void Validate()
    {
        RequireNonEmpty(MapId, nameof(MapId));
        _ = ResolvedSceneKind;
        if (WidthInMacroTiles <= 0 || HeightInMacroTiles <= 0)
        {
            throw new InvalidOperationException("DynamicNavBakeShowcaseConfig requires positive widthInMacroTiles and heightInMacroTiles.");
        }

        if (GridCellSizeCm <= 0 || ChunkSizeCells <= 0)
        {
            throw new InvalidOperationException("DynamicNavBakeShowcaseConfig requires positive gridCellSizeCm and chunkSizeCells.");
        }

        if (ResidentWidthChunks <= 0 || ResidentHeightChunks <= 0)
        {
            throw new InvalidOperationException("DynamicNavBakeShowcaseConfig requires positive residentWidthChunks and residentHeightChunks.");
        }

        if (ResidentWidthChunks > WidthChunks || ResidentHeightChunks > HeightChunks)
        {
            throw new InvalidOperationException("DynamicNavBakeShowcaseConfig resident window must fit inside the authored world.");
        }

        Squad.Validate();
        Goal.Validate();
        Gate.Validate();
        SideRouteWest.Validate("sideRouteWest");
        SideRouteEast.Validate("sideRouteEast");
        Parking.Validate();
        if (MovementSpeedCmPerSec <= 0)
        {
            throw new InvalidOperationException("DynamicNavBakeShowcaseConfig requires movementSpeedCmPerSec > 0.");
        }

        if (WallPoolCapacity < Gate.SegmentCount)
        {
            throw new InvalidOperationException("DynamicNavBakeShowcaseConfig wallPoolCapacity must cover gate.segmentCount.");
        }

        if (TerrainBrushHalfExtentCm <= 0)
        {
            throw new InvalidOperationException("DynamicNavBakeShowcaseConfig requires terrainBrushHalfExtentCm > 0.");
        }

        if (TerrainRaiseHeightLevel == 0)
        {
            throw new InvalidOperationException("DynamicNavBakeShowcaseConfig requires terrainRaiseHeightLevel > 0.");
        }

        if (EvidenceSampleCount <= 0)
        {
            throw new InvalidOperationException("DynamicNavBakeShowcaseConfig requires evidenceSampleCount > 0.");
        }

        if (Benchmark == null)
        {
            throw new InvalidOperationException(
                "DynamicNavBakeShowcaseConfig requires explicit 'benchmark' section (owner: DynamicNavBakeShowcaseConfig.benchmark).");
        }

        Benchmark.Validate(EvidenceSampleCount);
        if (Benchmark.PeakResidentTileCountMax != checked(ResidentWidthChunks * ResidentHeightChunks))
        {
            throw new InvalidOperationException(
                "DynamicNavBakeShowcaseConfig.benchmark.peakResidentTileCountMax must equal residentWidthChunks * residentHeightChunks.");
        }

        Ui.Validate();
        if (Presentation == null)
        {
            throw new InvalidOperationException(
                "DynamicNavBakeShowcaseConfig requires explicit 'presentation' section (owner: DynamicNavBakeShowcaseConfig.presentation).");
        }

        Presentation.Validate();
        if (RaylibAutoTimeline == null)
        {
            throw new InvalidOperationException(
                "DynamicNavBakeShowcaseConfig requires explicit 'raylibAutoTimeline' section (owner: DynamicNavBakeShowcaseConfig.raylibAutoTimeline).");
        }

        RaylibAutoTimeline.Validate();
        if (RaylibAutoTimeline.PlayerFraming.MinSquadMembersOnScreen > Squad.Count)
        {
            throw new InvalidOperationException(
                "DynamicNavBakeShowcaseConfig.raylibAutoTimeline.playerFraming.minSquadMembersOnScreen " +
                $"({RaylibAutoTimeline.PlayerFraming.MinSquadMembersOnScreen}) must be <= squad.count ({Squad.Count}).");
        }

        if (ResolvedSceneKind == DynamicNavBakeShowcaseSceneKind.OpenWorld)
        {
            if (OpenWorld == null)
            {
                throw new InvalidOperationException("DynamicNavBakeShowcaseConfig open_world scenes require openWorld section.");
            }

            OpenWorld.Validate(this);
            if (RaylibAutoTimeline.ResolvedFinalCaptureCompletionMode !=
                DynamicNavBakeShowcaseFinalCaptureCompletionMode.RouteReady)
            {
                throw new InvalidOperationException(
                    "DynamicNavBakeShowcaseConfig open_world scenes require " +
                    "raylibAutoTimeline.finalCaptureCompletionMode='route_ready' " +
                    "(continuous corridor march; final beat stays route-ready, not squad arrival).");
            }
        }
        else if (OpenWorld != null)
        {
            throw new InvalidOperationException("DynamicNavBakeShowcaseConfig rts scenes must not author openWorld.");
        }
        else if (RaylibAutoTimeline.ResolvedFinalCaptureCompletionMode !=
                 DynamicNavBakeShowcaseFinalCaptureCompletionMode.Arrival)
        {
            throw new InvalidOperationException(
                "DynamicNavBakeShowcaseConfig rts scenes require " +
                "raylibAutoTimeline.finalCaptureCompletionMode='arrival' " +
                "(final screenshot must wait for real squad arrival at authored goal slots).");
        }

        ValidateWorldPlacement();
    }

    private void ValidateWorldPlacement()
    {
        // World dimensions use checked arithmetic in WidthChunks/WorldWidthCm — truncation is already a hard fail.
        int worldWidth = WorldWidthCm;
        int worldHeight = WorldHeightCm;
        // Grid boards are authored as centered extents: [-half, +half).
        int minX = -worldWidth / 2;
        int minY = -worldHeight / 2;
        int maxX = minX + worldWidth;
        int maxY = minY + worldHeight;

        RequireInsideWorld(Squad.CenterXCm, Squad.CenterYCm, "squad.center", minX, minY, maxX, maxY);
        RequireInsideWorld(Goal.XCm, Goal.YCm, "goal", minX, minY, maxX, maxY);
        RequireInsideWorld(Gate.CenterXCm, Gate.CenterYCm, "gate.center", minX, minY, maxX, maxY);
        RequireInsideWorld(SideRouteWest.XCm, SideRouteWest.YCm, "sideRouteWest", minX, minY, maxX, maxY);
        RequireInsideWorld(SideRouteEast.XCm, SideRouteEast.YCm, "sideRouteEast", minX, minY, maxX, maxY);
        RequireInsideWorld(CameraTargetXCm, CameraTargetYCm, "cameraTarget", minX, minY, maxX, maxY);

        // SpatialPartitionUpdateSystem forbids WorldPosition outside board bounds.
        // Parking therefore stays inside the centered world extent but must not collide with
        // gameplay markers (same pattern as the RTS showcase parking at an unused in-bounds corner).
        RequireInsideWorld(Parking.XCm, Parking.YCm, "parking", minX, minY, maxX, maxY);
        RequireDistinctFromGameplay(Parking.XCm, Parking.YCm, "parking");
        // Dirty teleport fairness: parking must sit inside the initial resident window inset by
        // benchmark.dirtyComparisonBoundaryMarginChunks so neighbor dirty + triangle halo never
        // touch the RTS world edge (open-world exterior neighbor triangles would otherwise appear).
        ValidateParkingInsideDirtyComparisonInset();
    }

    private void ValidateParkingInsideDirtyComparisonInset()
    {
        int marginChunks = Benchmark.DirtyComparisonBoundaryMarginChunks;
        if (marginChunks <= 0)
        {
            throw new InvalidOperationException(
                "DynamicNavBakeShowcaseConfig.benchmark.dirtyComparisonBoundaryMarginChunks must be > 0.");
        }

        if (checked(2 * marginChunks) >= ResidentWidthChunks ||
            checked(2 * marginChunks) >= ResidentHeightChunks)
        {
            throw new InvalidOperationException(
                $"DynamicNavBakeShowcaseConfig.benchmark.dirtyComparisonBoundaryMarginChunks ({marginChunks}) " +
                $"must fit inside resident {ResidentWidthChunks}x{ResidentHeightChunks} chunks " +
                "(2 * dirtyComparisonBoundaryMarginChunks must be < each resident dimension).");
        }

        ResolveInitialResidentWorldBounds(
            this,
            out int residentMinX,
            out int residentMinY,
            out int residentMaxX,
            out int residentMaxY);
        int insetCm = checked(marginChunks * ChunkSizeCm);
        int insetMinX = checked(residentMinX + insetCm);
        int insetMinY = checked(residentMinY + insetCm);
        int insetMaxX = checked(residentMaxX - insetCm);
        int insetMaxY = checked(residentMaxY - insetCm);
        int parkingX = Parking.XCm;
        int parkingY = Parking.YCm;
        if (parkingX < insetMinX || parkingY < insetMinY ||
            parkingX >= insetMaxX || parkingY >= insetMaxY)
        {
            throw new InvalidOperationException(
                $"DynamicNavBakeShowcaseConfig.parking ({parkingX},{parkingY}) must lie inside the initial " +
                $"resident window inset by benchmark.dirtyComparisonBoundaryMarginChunks={marginChunks} " +
                $"[{insetMinX},{insetMaxX}) x [{insetMinY},{insetMaxY}) " +
                "so wall teleports dirty equivalent interior tiles on RTS and open-world scenes.");
        }
    }

    private void RequireDistinctFromGameplay(int xCm, int yCm, string label)
    {
        const int clearanceCm = 5000;
        if (Math.Abs(xCm - Gate.CenterXCm) < clearanceCm && Math.Abs(yCm - Gate.CenterYCm) < clearanceCm)
        {
            throw new InvalidOperationException($"DynamicNavBakeShowcaseConfig.{label} overlaps the gate playfield.");
        }

        if (Math.Abs(xCm - Squad.CenterXCm) < clearanceCm && Math.Abs(yCm - Squad.CenterYCm) < clearanceCm)
        {
            throw new InvalidOperationException($"DynamicNavBakeShowcaseConfig.{label} overlaps the squad spawn.");
        }

        if (Math.Abs(xCm - Goal.XCm) < clearanceCm && Math.Abs(yCm - Goal.YCm) < clearanceCm)
        {
            throw new InvalidOperationException($"DynamicNavBakeShowcaseConfig.{label} overlaps the goal.");
        }
    }

    private static void RequireInsideWorld(int xCm, int yCm, string label, int minX, int minY, int maxX, int maxY)
    {
        if (xCm < minX || yCm < minY || xCm >= maxX || yCm >= maxY)
        {
            throw new InvalidOperationException(
                $"DynamicNavBakeShowcaseConfig.{label} ({xCm},{yCm}) must lie inside world " +
                $"[{minX},{maxX}) x [{minY},{maxY}).");
        }
    }

    private static void ValidateRequiredProperties(JsonElement root)
    {
        RequireProperty(root, "mapId");
        RequireProperty(root, "sceneKind");
        RequireProperties(root, "widthInMacroTiles", "heightInMacroTiles", "gridCellSizeCm", "chunkSizeCells");
        RequireProperties(root, "residentWidthChunks", "residentHeightChunks", "cameraTargetXCm", "cameraTargetYCm");
        RequireProperty(root, "squad");
        RequireProperty(root, "goal");
        RequireProperty(root, "gate");
        RequireProperty(root, "sideRouteWest");
        RequireProperty(root, "sideRouteEast");
        RequireProperty(root, "parking");
        RequireProperties(root, "movementSpeedCmPerSec", "wallPoolCapacity", "terrainBrushHalfExtentCm", "terrainRaiseHeightLevel", "evidenceSampleCount", "benchmark", "ui", "presentation", "raylibAutoTimeline");
    }

    private static void ValidatePresentationRequiredProperties(JsonElement root)
    {
        JsonElement presentation = RequireProperty(root, "presentation");
        RequireProperties(
            presentation,
            "pathOverlayY",
            "localPathWidthMeters",
            "localPathBorderWidthMeters",
            "corridorPathWidthMeters",
            "corridorPathBorderWidthMeters",
            "fillColor",
            "edgeColor",
            "tileBoundsColor",
            "pendingColor",
            "rebuildingColor",
            "committedColor",
            "heightOffsetMeters",
            "navMeshEnabled",
            "navMeshLayer",
            "navMeshProfile",
            "drawFill",
            "drawEdges",
            "drawTileBounds",
            "drawTileStateIndication");
    }

    private static void ValidateRaylibAutoTimelineRequiredProperties(JsonElement root)
    {
        JsonElement timeline = RequireProperty(root, "raylibAutoTimeline");
        RequireProperties(
            timeline,
            "algorithmRequestEarliestFrame",
            "algorithmCommitDeadlineFrame",
            "initialScreenshotFrame",
            "dynamicActionFrame",
            "dynamicCommitDeadlineFrame",
            "dynamicScreenshotFrame",
            "finalActionFrame",
            "finalCommitDeadlineFrame",
            "finalScreenshotFrame",
            "autoExitFrame",
            "cameraTargetToleranceCm",
            "requiredQuiescentFixedTicks",
            "finalCaptureCompletionMode",
            "finalArrivalMemberToleranceCm",
            "finalArrivalRequiredStableFixedTicks",
            "playerFraming");
        JsonElement framing = RequireProperty(timeline, "playerFraming", "raylibAutoTimeline.playerFraming");
        RequireProperties(
            framing,
            "captureWidthPx",
            "captureHeightPx",
            "safeInsetLeftPx",
            "safeInsetTopPx",
            "safeInsetRightPx",
            "safeInsetBottomPx",
            "marginCm",
            "minDistanceCm",
            "maxDistanceCm",
            "baseDistanceCm",
            "minSquadMembersOnScreen",
            "minProjectedSquadSpanPx",
            "pathLookaheadCm",
            "coverageBuffer",
            "distanceToleranceCm");
    }

    private static void ValidateBenchmarkRequiredProperties(JsonElement root)
    {
        JsonElement benchmark = RequireProperty(root, "benchmark");
        RequireProperties(
            benchmark,
            "sampleWindowCount",
            "warmupSampleCount",
            "determinismWorkerCounts",
            "dirtyPublishP95RatioMax",
            "dirtyPublishP95FixedNoiseMs",
            "steadyStateThroughputRatioMin",
            "collectP95BudgetMs",
            "commitP95BudgetMs",
            "fixedStepBudgetMs",
            "layeredSpanSteadyStateAllocBytesMax",
            "peakResidentTileCountMax",
            "peakWorkerScratchBytesMax",
            "peakResidentBytesMax",
            "maxDirtyVisitedCandidateCount",
            "steadyStateTileBudgetPerSample",
            "dirtyComparisonBoundaryMarginChunks");
        JsonElement workers = RequireProperty(benchmark, "determinismWorkerCounts", "benchmark.determinismWorkerCounts");
        if (workers.ValueKind != JsonValueKind.Array || workers.GetArrayLength() < 2)
        {
            throw new InvalidOperationException(
                "DynamicNavBakeShowcaseConfig.benchmark.determinismWorkerCounts must be an array with at least two entries.");
        }
    }

    private static void ValidateOpenWorldHotspotRequiredProperties(JsonElement root)
    {
        if (!root.TryGetProperty("sceneKind", out JsonElement sceneKind) ||
            !string.Equals(sceneKind.GetString(), "open_world", StringComparison.Ordinal))
        {
            return;
        }

        JsonElement openWorld = RequireProperty(root, "openWorld");
        JsonElement hotspots = RequireProperty(openWorld, "hotspots");
        if (hotspots.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidOperationException("DynamicNavBakeShowcaseConfig.openWorld.hotspots must be an array.");
        }

        int index = 0;
        foreach (JsonElement hotspot in hotspots.EnumerateArray())
        {
            RequireProperty(hotspot, "wallCenterXCm", $"openWorld.hotspots[{index}].wallCenterXCm");
            RequireProperty(hotspot, "wallCenterYCm", $"openWorld.hotspots[{index}].wallCenterYCm");
            index++;
        }

        JsonElement autoCaptureMinimapRect = RequireProperty(
            openWorld,
            "autoCaptureMinimapRect",
            "openWorld.autoCaptureMinimapRect");
        RequireProperties(autoCaptureMinimapRect, "x", "y", "width", "height");
    }

    private static JsonElement RequireProperty(JsonElement root, string propertyName)
    {
        return RequireProperty(root, propertyName, propertyName);
    }

    private static JsonElement RequireProperty(JsonElement root, string propertyName, string pathLabel)
    {
        if (!root.TryGetProperty(propertyName, out JsonElement value))
        {
            throw new InvalidOperationException($"DynamicNavBakeShowcaseConfig requires explicit '{pathLabel}' property.");
        }

        return value;
    }

    private static void RequireProperties(JsonElement root, params string[] propertyNames)
    {
        for (int i = 0; i < propertyNames.Length; i++)
        {
            RequireProperty(root, propertyNames[i]);
        }
    }

    internal static void RequireNonEmpty(string value, string fieldName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException($"DynamicNavBakeShowcaseConfig requires non-empty {fieldName}.");
        }
    }

    internal static void ResolveGateSegmentWorldExtent(
        int centerXCm,
        int centerYCm,
        DynamicNavBakeShowcaseGateConfig gate,
        out int minXCm,
        out int minYCm,
        out int maxXCm,
        out int maxYCm)
    {
        if (gate == null)
        {
            throw new ArgumentNullException(nameof(gate));
        }

        int half = (gate.SegmentCount - 1) / 2;
        int minOffset = (0 - half) * gate.SegmentSpacingCm;
        int maxOffset = ((gate.SegmentCount - 1) - half) * gate.SegmentSpacingCm;
        minXCm = checked(centerXCm + minOffset - gate.NavRadiusCm);
        maxXCm = checked(centerXCm + maxOffset + gate.NavRadiusCm);
        minYCm = checked(centerYCm - gate.NavRadiusCm);
        maxYCm = checked(centerYCm + gate.NavRadiusCm);
    }

    internal static void ResolveHotspotResidentWorldBounds(
        DynamicNavBakeShowcaseConfig config,
        DynamicNavBakeShowcaseHotspotConfig hotspot,
        out int minXCm,
        out int minYCm,
        out int maxXCm,
        out int maxYCm)
    {
        if (config == null)
        {
            throw new ArgumentNullException(nameof(config));
        }

        if (hotspot == null)
        {
            throw new ArgumentNullException(nameof(hotspot));
        }

        int originX = checked(-config.WorldWidthCm / 2);
        int originY = checked(-config.WorldHeightCm / 2);
        minXCm = checked(originX + hotspot.ResidentOriginChunkX * config.ChunkSizeCm);
        minYCm = checked(originY + hotspot.ResidentOriginChunkZ * config.ChunkSizeCm);
        maxXCm = checked(minXCm + config.ResidentWidthChunks * config.ChunkSizeCm);
        maxYCm = checked(minYCm + config.ResidentHeightChunks * config.ChunkSizeCm);
    }

    /// <summary>
    /// Initial resident world bounds for dirty-comparison fairness (RTS full board at chunk 0,0;
    /// open-world initial hotspot resident window). Half-open [min,max).
    /// </summary>
    internal static void ResolveInitialResidentWorldBounds(
        DynamicNavBakeShowcaseConfig config,
        out int minXCm,
        out int minYCm,
        out int maxXCm,
        out int maxYCm)
    {
        if (config == null)
        {
            throw new ArgumentNullException(nameof(config));
        }

        if (config.ResolvedSceneKind == DynamicNavBakeShowcaseSceneKind.OpenWorld)
        {
            DynamicNavBakeShowcaseOpenWorldConfig openWorld = config.OpenWorld
                ?? throw new InvalidOperationException(
                    "DynamicNavBakeShowcaseConfig open_world scenes require openWorld section.");
            ResolveHotspotResidentWorldBounds(
                config,
                openWorld.Hotspots[openWorld.InitialHotspotIndex],
                out minXCm,
                out minYCm,
                out maxXCm,
                out maxYCm);
            return;
        }

        int originX = checked(-config.WorldWidthCm / 2);
        int originY = checked(-config.WorldHeightCm / 2);
        minXCm = originX;
        minYCm = originY;
        maxXCm = checked(originX + config.ResidentWidthChunks * config.ChunkSizeCm);
        maxYCm = checked(originY + config.ResidentHeightChunks * config.ChunkSizeCm);
    }
}

public sealed class DynamicNavBakeShowcaseSquadConfig
{
    public string TemplateId { get; set; } = string.Empty;
    public string ProfileId { get; set; } = string.Empty;
    public int Count { get; set; }
    public int CenterXCm { get; set; }
    public int CenterYCm { get; set; }
    public int Columns { get; set; }
    public int Rows { get; set; }
    public int SpacingXCm { get; set; }
    public int SpacingYCm { get; set; }

    public void Validate()
    {
        DynamicNavBakeShowcaseConfig.RequireNonEmpty(TemplateId, nameof(TemplateId));
        DynamicNavBakeShowcaseConfig.RequireNonEmpty(ProfileId, nameof(ProfileId));
        if (Count <= 0)
        {
            throw new InvalidOperationException("DynamicNavBakeShowcaseConfig.squad.count must be > 0.");
        }

        if (Columns <= 0 || Rows <= 0 || Columns * Rows < Count)
        {
            throw new InvalidOperationException("DynamicNavBakeShowcaseConfig.squad grid must cover count.");
        }

        if (SpacingXCm <= 0 || SpacingYCm <= 0)
        {
            throw new InvalidOperationException("DynamicNavBakeShowcaseConfig.squad spacing must be > 0.");
        }
    }
}

public sealed class DynamicNavBakeShowcaseGoalConfig
{
    public string TemplateId { get; set; } = string.Empty;
    public int XCm { get; set; }
    public int YCm { get; set; }

    public void Validate()
    {
        DynamicNavBakeShowcaseConfig.RequireNonEmpty(TemplateId, nameof(TemplateId));
    }
}

public sealed class DynamicNavBakeShowcaseGateConfig
{
    public string WallTemplateId { get; set; } = string.Empty;
    public int CenterXCm { get; set; }
    public int CenterYCm { get; set; }
    public int SegmentCount { get; set; }
    public int SegmentSpacingCm { get; set; }
    public int NavRadiusCm { get; set; }
    public int NavMinYcm { get; set; }
    public int NavMaxYcm { get; set; }

    public void Validate()
    {
        DynamicNavBakeShowcaseConfig.RequireNonEmpty(WallTemplateId, nameof(WallTemplateId));
        if (SegmentCount <= 0)
        {
            throw new InvalidOperationException("DynamicNavBakeShowcaseConfig.gate.segmentCount must be > 0.");
        }

        if (SegmentSpacingCm <= 0)
        {
            throw new InvalidOperationException("DynamicNavBakeShowcaseConfig.gate.segmentSpacingCm must be > 0.");
        }

        if (NavRadiusCm <= 0 || NavMaxYcm <= NavMinYcm)
        {
            throw new InvalidOperationException("DynamicNavBakeShowcaseConfig.gate requires positive navRadiusCm and navMaxYcm > navMinYcm.");
        }
    }
}

public sealed class DynamicNavBakeShowcaseSideRouteConfig
{
    public string MarkerTemplateId { get; set; } = string.Empty;
    public int XCm { get; set; }
    public int YCm { get; set; }

    public void Validate(string label)
    {
        DynamicNavBakeShowcaseConfig.RequireNonEmpty(MarkerTemplateId, $"{label}.markerTemplateId");
    }
}

public sealed class DynamicNavBakeShowcaseParkingConfig
{
    public int XCm { get; set; }
    public int YCm { get; set; }

    public void Validate()
    {
    }
}

public sealed class DynamicNavBakeShowcaseBenchmarkConfig
{
    /// <summary>
    /// Nearest-rank P95 needs enough samples that the 95th percentile is not the window maximum.
    /// </summary>
    public const int MinimumP95SampleWindowCount = 20;

    public int SampleWindowCount { get; set; }
    public int WarmupSampleCount { get; set; }
    public int[] DeterminismWorkerCounts { get; set; } = Array.Empty<int>();
    public double DirtyPublishP95RatioMax { get; set; }
    public double DirtyPublishP95FixedNoiseMs { get; set; }
    public double SteadyStateThroughputRatioMin { get; set; }
    public double CollectP95BudgetMs { get; set; }
    public double CommitP95BudgetMs { get; set; }
    public double FixedStepBudgetMs { get; set; }
    public long LayeredSpanSteadyStateAllocBytesMax { get; set; }
    public int PeakResidentTileCountMax { get; set; }
    public long PeakWorkerScratchBytesMax { get; set; }
    public long PeakResidentBytesMax { get; set; }
    public int MaxDirtyVisitedCandidateCount { get; set; }
    public int SteadyStateTileBudgetPerSample { get; set; }
    public int DirtyComparisonBoundaryMarginChunks { get; set; }

    public void Validate(int evidenceSampleCount)
    {
        if (SampleWindowCount < MinimumP95SampleWindowCount)
        {
            throw new InvalidOperationException(
                $"DynamicNavBakeShowcaseConfig.benchmark.sampleWindowCount ({SampleWindowCount}) " +
                $"must be >= MinimumP95SampleWindowCount ({MinimumP95SampleWindowCount}).");
        }

        if (WarmupSampleCount < 0)
        {
            throw new InvalidOperationException(
                "DynamicNavBakeShowcaseConfig.benchmark.warmupSampleCount must be >= 0.");
        }

        if (DirtyComparisonBoundaryMarginChunks <= 0)
        {
            throw new InvalidOperationException(
                "DynamicNavBakeShowcaseConfig.benchmark.dirtyComparisonBoundaryMarginChunks must be > 0.");
        }

        if (checked(SampleWindowCount + WarmupSampleCount) > evidenceSampleCount)
        {
            throw new InvalidOperationException(
                "DynamicNavBakeShowcaseConfig.benchmark sampleWindowCount + warmupSampleCount must be <= evidenceSampleCount.");
        }

        if (DeterminismWorkerCounts == null || DeterminismWorkerCounts.Length < 2)
        {
            throw new InvalidOperationException(
                "DynamicNavBakeShowcaseConfig.benchmark.determinismWorkerCounts must contain at least [1, N].");
        }

        bool hasOne = false;
        var seen = new HashSet<int>();
        for (int i = 0; i < DeterminismWorkerCounts.Length; i++)
        {
            int workers = DeterminismWorkerCounts[i];
            if (workers <= 0)
            {
                throw new InvalidOperationException(
                    $"DynamicNavBakeShowcaseConfig.benchmark.determinismWorkerCounts[{i}] must be > 0.");
            }

            if (!seen.Add(workers))
            {
                throw new InvalidOperationException(
                    $"DynamicNavBakeShowcaseConfig.benchmark.determinismWorkerCounts contains duplicate worker count {workers}.");
            }

            if (workers == 1)
            {
                hasOne = true;
            }
        }

        if (!hasOne)
        {
            throw new InvalidOperationException(
                "DynamicNavBakeShowcaseConfig.benchmark.determinismWorkerCounts must include 1.");
        }

        RequirePositiveRatio(DirtyPublishP95RatioMax, "dirtyPublishP95RatioMax");
        if (DirtyPublishP95FixedNoiseMs < 0d ||
            double.IsNaN(DirtyPublishP95FixedNoiseMs) ||
            double.IsInfinity(DirtyPublishP95FixedNoiseMs))
        {
            throw new InvalidOperationException(
                "DynamicNavBakeShowcaseConfig.benchmark.dirtyPublishP95FixedNoiseMs must be a finite value >= 0.");
        }

        RequirePositiveRatio(SteadyStateThroughputRatioMin, "steadyStateThroughputRatioMin");
        if (SteadyStateThroughputRatioMin > 1d)
        {
            throw new InvalidOperationException(
                "DynamicNavBakeShowcaseConfig.benchmark.steadyStateThroughputRatioMin must be <= 1.");
        }

        RequirePositiveMs(CollectP95BudgetMs, "collectP95BudgetMs");
        RequirePositiveMs(CommitP95BudgetMs, "commitP95BudgetMs");
        RequirePositiveMs(FixedStepBudgetMs, "fixedStepBudgetMs");
        if (DirtyPublishP95FixedNoiseMs > FixedStepBudgetMs)
        {
            throw new InvalidOperationException(
                "DynamicNavBakeShowcaseConfig.benchmark.dirtyPublishP95FixedNoiseMs must be <= fixedStepBudgetMs.");
        }

        if (CollectP95BudgetMs + CommitP95BudgetMs > FixedStepBudgetMs)
        {
            throw new InvalidOperationException(
                "DynamicNavBakeShowcaseConfig.benchmark collectP95BudgetMs + commitP95BudgetMs must be <= fixedStepBudgetMs.");
        }

        if (LayeredSpanSteadyStateAllocBytesMax < 0)
        {
            throw new InvalidOperationException(
                "DynamicNavBakeShowcaseConfig.benchmark.layeredSpanSteadyStateAllocBytesMax must be >= 0.");
        }

        if (PeakResidentTileCountMax <= 0)
        {
            throw new InvalidOperationException(
                "DynamicNavBakeShowcaseConfig.benchmark.peakResidentTileCountMax must be > 0.");
        }

        if (PeakWorkerScratchBytesMax <= 0)
        {
            throw new InvalidOperationException(
                "DynamicNavBakeShowcaseConfig.benchmark.peakWorkerScratchBytesMax must be > 0.");
        }

        if (PeakResidentBytesMax <= 0)
        {
            throw new InvalidOperationException(
                "DynamicNavBakeShowcaseConfig.benchmark.peakResidentBytesMax must be > 0.");
        }

        if (MaxDirtyVisitedCandidateCount <= 0)
        {
            throw new InvalidOperationException(
                "DynamicNavBakeShowcaseConfig.benchmark.maxDirtyVisitedCandidateCount must be > 0.");
        }

        if (SteadyStateTileBudgetPerSample <= 0)
        {
            throw new InvalidOperationException(
                "DynamicNavBakeShowcaseConfig.benchmark.steadyStateTileBudgetPerSample must be > 0.");
        }
    }

    private static void RequirePositiveRatio(double value, string field)
    {
        if (!(value > 0d) || double.IsNaN(value) || double.IsInfinity(value))
        {
            throw new InvalidOperationException(
                $"DynamicNavBakeShowcaseConfig.benchmark.{field} must be a finite value > 0.");
        }
    }

    private static void RequirePositiveMs(double value, string field)
    {
        if (!(value > 0d) || double.IsNaN(value) || double.IsInfinity(value))
        {
            throw new InvalidOperationException(
                $"DynamicNavBakeShowcaseConfig.benchmark.{field} must be a finite value > 0.");
        }
    }
}

public sealed class DynamicNavBakeShowcaseUiConfig
{
    public float AbsoluteLeft { get; set; }
    public float AbsoluteTop { get; set; }
    public float Width { get; set; }

    public void Validate()
    {
        if (!(Width > 0f))
        {
            throw new InvalidOperationException("DynamicNavBakeShowcaseConfig.ui.width must be > 0.");
        }
    }
}

/// <summary>
/// Authored world-spline and NavMesh presentation style for Dynamic NavBake overlays.
/// Events and the NavMesh projector must read these values; no adapter color hardcodes.
/// </summary>
public sealed class DynamicNavBakeShowcasePresentationConfig
{
    public float PathOverlayY { get; set; }
    public float LocalPathWidthMeters { get; set; }
    public float LocalPathBorderWidthMeters { get; set; }
    public float CorridorPathWidthMeters { get; set; }
    public float CorridorPathBorderWidthMeters { get; set; }
    public float[] FillColor { get; set; } = Array.Empty<float>();
    public float[] EdgeColor { get; set; } = Array.Empty<float>();
    public float[] TileBoundsColor { get; set; } = Array.Empty<float>();
    public float[] PendingColor { get; set; } = Array.Empty<float>();
    public float[] RebuildingColor { get; set; } = Array.Empty<float>();
    public float[] CommittedColor { get; set; } = Array.Empty<float>();
    public float HeightOffsetMeters { get; set; }
    public bool NavMeshEnabled { get; set; }
    public int NavMeshLayer { get; set; }
    public int NavMeshProfile { get; set; }
    public bool DrawFill { get; set; }
    public bool DrawEdges { get; set; }
    public bool DrawTileBounds { get; set; }
    public bool DrawTileStateIndication { get; set; }

    public void Validate()
    {
        RequireFinitePositive(PathOverlayY, "pathOverlayY");
        RequireFinitePositive(LocalPathWidthMeters, "localPathWidthMeters");
        RequireFinitePositive(LocalPathBorderWidthMeters, "localPathBorderWidthMeters");
        RequireFinitePositive(CorridorPathWidthMeters, "corridorPathWidthMeters");
        RequireFinitePositive(CorridorPathBorderWidthMeters, "corridorPathBorderWidthMeters");
        ValidateColor(FillColor, "fillColor");
        ValidateColor(EdgeColor, "edgeColor");
        ValidateColor(TileBoundsColor, "tileBoundsColor");
        ValidateColor(PendingColor, "pendingColor");
        ValidateColor(RebuildingColor, "rebuildingColor");
        ValidateColor(CommittedColor, "committedColor");
        if (!float.IsFinite(HeightOffsetMeters))
        {
            throw new InvalidOperationException(
                "DynamicNavBakeShowcaseConfig.presentation.heightOffsetMeters must be a finite value.");
        }

        if (NavMeshLayer < 0 || NavMeshProfile < 0)
        {
            throw new InvalidOperationException(
                "DynamicNavBakeShowcaseConfig.presentation.navMeshLayer/navMeshProfile must be nonnegative.");
        }
    }

    public Ludots.Core.Presentation.Navigation.NavMeshPresentationStyle ToNavMeshStyle()
    {
        return new Ludots.Core.Presentation.Navigation.NavMeshPresentationStyle(
            ToColor(FillColor),
            ToColor(EdgeColor),
            ToColor(TileBoundsColor),
            ToColor(PendingColor),
            ToColor(RebuildingColor),
            ToColor(CommittedColor),
            HeightOffsetMeters,
            DrawFill,
            DrawEdges,
            DrawTileBounds,
            DrawTileStateIndication);
    }

    private static void RequireFinitePositive(float value, string field)
    {
        if (!float.IsFinite(value) || value <= 0f)
        {
            throw new InvalidOperationException(
                $"DynamicNavBakeShowcaseConfig.presentation.{field} must be a finite value > 0.");
        }
    }

    private static void ValidateColor(float[] values, string field)
    {
        if (values == null || values.Length != 4)
        {
            throw new InvalidOperationException(
                $"DynamicNavBakeShowcaseConfig.presentation.{field} must be authored as [r,g,b,a].");
        }

        for (int i = 0; i < values.Length; i++)
        {
            if (!float.IsFinite(values[i]) || values[i] < 0f || values[i] > 1f)
            {
                throw new InvalidOperationException(
                    $"DynamicNavBakeShowcaseConfig.presentation.{field}[{i}] must be a finite value in [0, 1].");
            }
        }
    }

    private static Ludots.Core.Presentation.Navigation.NavMeshPresentationColor ToColor(float[] values)
        => new Ludots.Core.Presentation.Navigation.NavMeshPresentationColor(values[0], values[1], values[2], values[3]);
}

/// <summary>
/// Final auto-capture completion semantics after the restored formal route is observed.
/// </summary>
public enum DynamicNavBakeShowcaseFinalCaptureCompletionMode : byte
{
    RouteReady = 0,
    Arrival = 1
}

/// <summary>
/// Raylib auto-player frame contract for Dynamic NavBake showcase capture.
/// Frame indexes match RaylibHostLoop HostFrameIndex (set before engine.Tick each host frame).
/// Initial/dynamic beats stay in-motion route-ready; final completion is data-driven
/// (<see cref="FinalCaptureCompletionMode"/>).
/// </summary>
public sealed class DynamicNavBakeShowcaseRaylibAutoTimelineConfig
{
    public int AlgorithmRequestEarliestFrame { get; set; }
    public int AlgorithmCommitDeadlineFrame { get; set; }
    public int InitialScreenshotFrame { get; set; }
    public int DynamicActionFrame { get; set; }
    public int DynamicCommitDeadlineFrame { get; set; }
    public int DynamicScreenshotFrame { get; set; }
    public int FinalActionFrame { get; set; }
    public int FinalCommitDeadlineFrame { get; set; }
    public int FinalScreenshotFrame { get; set; }
    public int AutoExitFrame { get; set; }
    public int CameraTargetToleranceCm { get; set; }

    /// <summary>
    /// Distinct FixedSteps (Time.FixedTotalTime progress) that must observe nav-stable idle
    /// after a strictly newer committed generation before a topology/residency commit gate passes.
    /// Host frames without FixedStep must not advance this counter.
    /// </summary>
    public int RequiredQuiescentFixedTicks { get; set; }

    /// <summary>
    /// Authored final-capture completion mode: <c>route_ready</c> or <c>arrival</c>.
    /// </summary>
    public string FinalCaptureCompletionMode { get; set; } = string.Empty;

    /// <summary>
    /// Per-member WorldPositionCm tolerance (cm) around authored Goal + formation slot offset
    /// when <see cref="ResolvedFinalCaptureCompletionMode"/> is Arrival.
    /// </summary>
    public int FinalArrivalMemberToleranceCm { get; set; }

    /// <summary>
    /// Distinct FixedSteps that must observe all squad members idle and inside
    /// <see cref="FinalArrivalMemberToleranceCm"/> before arrival-mode final completion.
    /// </summary>
    public int FinalArrivalRequiredStableFixedTicks { get; set; }

    /// <summary>
    /// Deterministic player framing for auto-capture (squad + hotspot/obstacle + path lookahead).
    /// </summary>
    public DynamicNavBakeShowcasePlayerFramingConfig PlayerFraming { get; set; } = null!;

    public DynamicNavBakeShowcaseFinalCaptureCompletionMode ResolvedFinalCaptureCompletionMode =>
        FinalCaptureCompletionMode switch
        {
            "route_ready" => DynamicNavBakeShowcaseFinalCaptureCompletionMode.RouteReady,
            "arrival" => DynamicNavBakeShowcaseFinalCaptureCompletionMode.Arrival,
            _ => throw new InvalidOperationException(
                "DynamicNavBakeShowcaseConfig.raylibAutoTimeline.finalCaptureCompletionMode must be " +
                $"'route_ready' or 'arrival', got '{FinalCaptureCompletionMode}'.")
        };

    public void Validate()
    {
        RequireNonNegative(AlgorithmRequestEarliestFrame, "algorithmRequestEarliestFrame");
        RequireStrictlyIncreasing(
            ("algorithmRequestEarliestFrame", AlgorithmRequestEarliestFrame),
            ("algorithmCommitDeadlineFrame", AlgorithmCommitDeadlineFrame),
            ("initialScreenshotFrame", InitialScreenshotFrame),
            ("dynamicActionFrame", DynamicActionFrame),
            ("dynamicCommitDeadlineFrame", DynamicCommitDeadlineFrame),
            ("dynamicScreenshotFrame", DynamicScreenshotFrame),
            ("finalActionFrame", FinalActionFrame),
            ("finalCommitDeadlineFrame", FinalCommitDeadlineFrame),
            ("finalScreenshotFrame", FinalScreenshotFrame),
            ("autoExitFrame", AutoExitFrame));
        if (CameraTargetToleranceCm < 0)
        {
            throw new InvalidOperationException(
                "DynamicNavBakeShowcaseConfig.raylibAutoTimeline.cameraTargetToleranceCm must be >= 0.");
        }

        if (RequiredQuiescentFixedTicks < 2)
        {
            throw new InvalidOperationException(
                "DynamicNavBakeShowcaseConfig.raylibAutoTimeline.requiredQuiescentFixedTicks must be >= 2.");
        }

        _ = ResolvedFinalCaptureCompletionMode;
        if (FinalArrivalMemberToleranceCm <= 0)
        {
            throw new InvalidOperationException(
                "DynamicNavBakeShowcaseConfig.raylibAutoTimeline.finalArrivalMemberToleranceCm must be > 0.");
        }

        if (FinalArrivalRequiredStableFixedTicks < 2)
        {
            throw new InvalidOperationException(
                "DynamicNavBakeShowcaseConfig.raylibAutoTimeline.finalArrivalRequiredStableFixedTicks must be >= 2.");
        }

        if (PlayerFraming == null)
        {
            throw new InvalidOperationException(
                "DynamicNavBakeShowcaseConfig.raylibAutoTimeline requires explicit 'playerFraming' section.");
        }

        PlayerFraming.Validate();
    }

    private static void RequireNonNegative(int value, string field)
    {
        if (value < 0)
        {
            throw new InvalidOperationException(
                $"DynamicNavBakeShowcaseConfig.raylibAutoTimeline.{field} must be >= 0.");
        }
    }

    private static void RequireStrictlyIncreasing(params (string Name, int Value)[] frames)
    {
        for (int i = 1; i < frames.Length; i++)
        {
            if (frames[i].Value <= frames[i - 1].Value)
            {
                throw new InvalidOperationException(
                    "DynamicNavBakeShowcaseConfig.raylibAutoTimeline requires strictly increasing frames: " +
                    $"{frames[i - 1].Name}={frames[i - 1].Value} must be < {frames[i].Name}={frames[i].Value}.");
            }
        }
    }
}

/// <summary>
/// Data-driven auto-capture framing knobs (owner: raylibAutoTimeline.playerFraming).
/// </summary>
public sealed class DynamicNavBakeShowcasePlayerFramingConfig
{
    public int CaptureWidthPx { get; set; }
    public int CaptureHeightPx { get; set; }
    public int SafeInsetLeftPx { get; set; }
    public int SafeInsetTopPx { get; set; }
    public int SafeInsetRightPx { get; set; }
    public int SafeInsetBottomPx { get; set; }
    public float MarginCm { get; set; }
    public float MinDistanceCm { get; set; }
    public float MaxDistanceCm { get; set; }
    public float BaseDistanceCm { get; set; }
    public int MinSquadMembersOnScreen { get; set; }
    public float MinProjectedSquadSpanPx { get; set; }
    public float PathLookaheadCm { get; set; }
    public float CoverageBuffer { get; set; }
    public float DistanceToleranceCm { get; set; }

    public float AspectRatio => (float)CaptureWidthPx / CaptureHeightPx;
    public float SafeWidthFraction =>
        (float)(CaptureWidthPx - SafeInsetLeftPx - SafeInsetRightPx) / CaptureWidthPx;
    public float SafeHeightFraction =>
        (float)(CaptureHeightPx - SafeInsetTopPx - SafeInsetBottomPx) / CaptureHeightPx;
    public float SafeCenterNormalizedX =>
        (SafeInsetLeftPx + ((CaptureWidthPx - SafeInsetLeftPx - SafeInsetRightPx) * 0.5f)) / CaptureWidthPx;
    public float SafeCenterNormalizedY =>
        (SafeInsetTopPx + ((CaptureHeightPx - SafeInsetTopPx - SafeInsetBottomPx) * 0.5f)) / CaptureHeightPx;

    public void Validate()
    {
        if (CaptureWidthPx <= 0 || CaptureHeightPx <= 0)
        {
            throw new InvalidOperationException(
                "DynamicNavBakeShowcaseConfig.raylibAutoTimeline.playerFraming capture dimensions must be > 0.");
        }

        if (SafeInsetLeftPx < 0 || SafeInsetTopPx < 0 || SafeInsetRightPx < 0 || SafeInsetBottomPx < 0)
        {
            throw new InvalidOperationException(
                "DynamicNavBakeShowcaseConfig.raylibAutoTimeline.playerFraming safe insets must be >= 0.");
        }

        if (SafeInsetLeftPx + SafeInsetRightPx >= CaptureWidthPx ||
            SafeInsetTopPx + SafeInsetBottomPx >= CaptureHeightPx)
        {
            throw new InvalidOperationException(
                "DynamicNavBakeShowcaseConfig.raylibAutoTimeline.playerFraming safe insets must leave a non-empty capture area.");
        }

        if (!float.IsFinite(MarginCm) || MarginCm < 0f)
        {
            throw new InvalidOperationException(
                "DynamicNavBakeShowcaseConfig.raylibAutoTimeline.playerFraming.marginCm must be finite and >= 0.");
        }

        if (!float.IsFinite(MinDistanceCm) || MinDistanceCm <= 0f)
        {
            throw new InvalidOperationException(
                "DynamicNavBakeShowcaseConfig.raylibAutoTimeline.playerFraming.minDistanceCm must be finite and > 0.");
        }

        if (!float.IsFinite(MaxDistanceCm) || MaxDistanceCm < MinDistanceCm)
        {
            throw new InvalidOperationException(
                "DynamicNavBakeShowcaseConfig.raylibAutoTimeline.playerFraming.maxDistanceCm must be >= minDistanceCm.");
        }

        if (!float.IsFinite(BaseDistanceCm) || BaseDistanceCm < MinDistanceCm || BaseDistanceCm > MaxDistanceCm)
        {
            throw new InvalidOperationException(
                "DynamicNavBakeShowcaseConfig.raylibAutoTimeline.playerFraming.baseDistanceCm must lie within [minDistanceCm, maxDistanceCm].");
        }

        if (MinSquadMembersOnScreen <= 0)
        {
            throw new InvalidOperationException(
                "DynamicNavBakeShowcaseConfig.raylibAutoTimeline.playerFraming.minSquadMembersOnScreen must be > 0.");
        }

        if (!float.IsFinite(MinProjectedSquadSpanPx) || MinProjectedSquadSpanPx <= 0f)
        {
            throw new InvalidOperationException(
                "DynamicNavBakeShowcaseConfig.raylibAutoTimeline.playerFraming.minProjectedSquadSpanPx must be finite and > 0.");
        }

        if (!float.IsFinite(PathLookaheadCm) || PathLookaheadCm <= 0f)
        {
            throw new InvalidOperationException(
                "DynamicNavBakeShowcaseConfig.raylibAutoTimeline.playerFraming.pathLookaheadCm must be finite and > 0.");
        }

        if (!float.IsFinite(CoverageBuffer) || CoverageBuffer <= 0f)
        {
            throw new InvalidOperationException(
                "DynamicNavBakeShowcaseConfig.raylibAutoTimeline.playerFraming.coverageBuffer must be finite and > 0.");
        }

        if (!float.IsFinite(DistanceToleranceCm) || DistanceToleranceCm < 0f)
        {
            throw new InvalidOperationException(
                "DynamicNavBakeShowcaseConfig.raylibAutoTimeline.playerFraming.distanceToleranceCm must be finite and >= 0.");
        }
    }
}

public sealed class DynamicNavBakeShowcaseMinimapRectConfig
{
    public int X { get; set; }
    public int Y { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }

    public void Validate(DynamicNavBakeShowcasePlayerFramingConfig framing)
    {
        if (framing == null)
        {
            throw new ArgumentNullException(nameof(framing));
        }

        if (Width <= 0 || Height <= 0)
        {
            throw new InvalidOperationException(
                "DynamicNavBakeShowcaseConfig.openWorld.autoCaptureMinimapRect requires positive width and height.");
        }

        if (X < 0 || Y < 0)
        {
            throw new InvalidOperationException(
                "DynamicNavBakeShowcaseConfig.openWorld.autoCaptureMinimapRect x/y must be >= 0.");
        }

        if (checked(X + Width) > framing.CaptureWidthPx ||
            checked(Y + Height) > framing.CaptureHeightPx)
        {
            throw new InvalidOperationException(
                "DynamicNavBakeShowcaseConfig.openWorld.autoCaptureMinimapRect must fit inside " +
                $"playerFraming capture {framing.CaptureWidthPx}x{framing.CaptureHeightPx} " +
                $"(rect x={X}, y={Y}, width={Width}, height={Height}).");
        }
    }
}

public sealed class DynamicNavBakeShowcaseOpenWorldConfig
{
    public DynamicNavBakeShowcaseHotspotConfig[] Hotspots { get; set; } = Array.Empty<DynamicNavBakeShowcaseHotspotConfig>();
    public int InitialHotspotIndex { get; set; }
    public DynamicNavBakeShowcaseMinimapRectConfig AutoCaptureMinimapRect { get; set; } = null!;

    public void Validate(DynamicNavBakeShowcaseConfig config)
    {
        if (config == null)
        {
            throw new ArgumentNullException(nameof(config));
        }

        if (Hotspots.Length <= 0)
        {
            throw new InvalidOperationException("DynamicNavBakeShowcaseConfig.openWorld.hotspots must contain at least one hotspot.");
        }

        var ids = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < Hotspots.Length; i++)
        {
            Hotspots[i].Validate(i, config);
            if (!ids.Add(Hotspots[i].Id))
            {
                throw new InvalidOperationException($"DynamicNavBakeShowcaseConfig.openWorld contains duplicate hotspot id '{Hotspots[i].Id}'.");
            }
        }

        if ((uint)InitialHotspotIndex >= (uint)Hotspots.Length)
        {
            throw new InvalidOperationException("DynamicNavBakeShowcaseConfig.openWorld.initialHotspotIndex is out of range.");
        }

        if (AutoCaptureMinimapRect == null)
        {
            throw new InvalidOperationException(
                "DynamicNavBakeShowcaseConfig.openWorld requires explicit 'autoCaptureMinimapRect' section.");
        }

        DynamicNavBakeShowcasePlayerFramingConfig framing = config.RaylibAutoTimeline.PlayerFraming
            ?? throw new InvalidOperationException(
                "DynamicNavBakeShowcaseConfig.openWorld.autoCaptureMinimapRect requires raylibAutoTimeline.playerFraming.");
        AutoCaptureMinimapRect.Validate(framing);
        // Parking dirty-comparison inset is validated once for RTS and open-world via
        // DynamicNavBakeShowcaseConfig.ValidateParkingInsideDirtyComparisonInset (SSOT).
    }
}

public sealed class DynamicNavBakeShowcaseHotspotConfig
{
    public string Id { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public int CameraTargetXCm { get; set; }
    public int CameraTargetYCm { get; set; }
    public int WallCenterXCm { get; set; }
    public int WallCenterYCm { get; set; }
    public int ResidentOriginChunkX { get; set; }
    public int ResidentOriginChunkZ { get; set; }

    public void Validate(int index, DynamicNavBakeShowcaseConfig config)
    {
        if (config == null)
        {
            throw new ArgumentNullException(nameof(config));
        }

        DynamicNavBakeShowcaseConfig.RequireNonEmpty(Id, $"openWorld.hotspots[{index}].id");
        DynamicNavBakeShowcaseConfig.RequireNonEmpty(Label, $"openWorld.hotspots[{index}].label");
        if (ResidentOriginChunkX < 0 || ResidentOriginChunkZ < 0)
        {
            throw new InvalidOperationException($"DynamicNavBakeShowcaseConfig.openWorld.hotspots[{index}] resident origin must be non-negative.");
        }

        if (ResidentOriginChunkX + config.ResidentWidthChunks > config.WidthChunks ||
            ResidentOriginChunkZ + config.ResidentHeightChunks > config.HeightChunks)
        {
            throw new InvalidOperationException(
                $"DynamicNavBakeShowcaseConfig.openWorld.hotspots[{index}] resident origin ({ResidentOriginChunkX},{ResidentOriginChunkZ}) " +
                $"with resident {config.ResidentWidthChunks}x{config.ResidentHeightChunks} exceeds world chunks {config.WidthChunks}x{config.HeightChunks}.");
        }

        DynamicNavBakeShowcaseConfig.ResolveHotspotResidentWorldBounds(
            config,
            this,
            out int residentMinX,
            out int residentMinY,
            out int residentMaxX,
            out int residentMaxY);
        DynamicNavBakeShowcaseConfig.ResolveGateSegmentWorldExtent(
            WallCenterXCm,
            WallCenterYCm,
            config.Gate,
            out int spanMinX,
            out int spanMinY,
            out int spanMaxX,
            out int spanMaxY);

        int worldMinX = checked(-config.WorldWidthCm / 2);
        int worldMinY = checked(-config.WorldHeightCm / 2);
        int worldMaxX = checked(worldMinX + config.WorldWidthCm);
        int worldMaxY = checked(worldMinY + config.WorldHeightCm);

        if (WallCenterXCm < worldMinX || WallCenterYCm < worldMinY ||
            WallCenterXCm >= worldMaxX || WallCenterYCm >= worldMaxY)
        {
            throw new InvalidOperationException(
                $"DynamicNavBakeShowcaseConfig.openWorld.hotspots[{index}].wallCenter ({WallCenterXCm},{WallCenterYCm}) " +
                $"must lie inside world [{worldMinX},{worldMaxX}) x [{worldMinY},{worldMaxY}).");
        }

        if (WallCenterXCm < residentMinX || WallCenterYCm < residentMinY ||
            WallCenterXCm >= residentMaxX || WallCenterYCm >= residentMaxY)
        {
            throw new InvalidOperationException(
                $"DynamicNavBakeShowcaseConfig.openWorld.hotspots[{index}].wallCenter ({WallCenterXCm},{WallCenterYCm}) " +
                $"must lie inside authored resident window [{residentMinX},{residentMaxX}) x [{residentMinY},{residentMaxY}).");
        }

        if (spanMinX < worldMinX || spanMinY < worldMinY || spanMaxX >= worldMaxX || spanMaxY >= worldMaxY)
        {
            throw new InvalidOperationException(
                $"DynamicNavBakeShowcaseConfig.openWorld.hotspots[{index}] wall segment span " +
                $"[{spanMinX},{spanMaxX}] x [{spanMinY},{spanMaxY}] (center + gate segmentSpacing/navRadius) " +
                $"must fit inside world [{worldMinX},{worldMaxX}) x [{worldMinY},{worldMaxY}).");
        }

        if (spanMinX < residentMinX || spanMinY < residentMinY || spanMaxX >= residentMaxX || spanMaxY >= residentMaxY)
        {
            throw new InvalidOperationException(
                $"DynamicNavBakeShowcaseConfig.openWorld.hotspots[{index}] wall segment span " +
                $"[{spanMinX},{spanMaxX}] x [{spanMinY},{spanMaxY}] (center + gate segmentSpacing/navRadius) " +
                $"must fit inside authored resident window [{residentMinX},{residentMaxX}) x [{residentMinY},{residentMaxY}).");
        }
    }
}

public sealed class DynamicNavBakeShowcaseConfigLoader
{
    public const string ConfigDirectory = "Showcases/DynamicNavBake";

    private readonly ConfigPipeline _pipeline;

    public DynamicNavBakeShowcaseConfigLoader(ConfigPipeline pipeline)
    {
        _pipeline = pipeline ?? throw new ArgumentNullException(nameof(pipeline));
    }

    public DynamicNavBakeShowcaseConfig Load(
        ConfigCatalog catalog,
        ConfigConflictReport report,
        string mapId)
    {
        if (catalog == null)
        {
            throw new ArgumentNullException(nameof(catalog));
        }

        if (report == null)
        {
            throw new ArgumentNullException(nameof(report));
        }

        if (string.IsNullOrWhiteSpace(mapId) || !string.Equals(mapId.Trim(), mapId, StringComparison.Ordinal))
        {
            throw new ArgumentException("Dynamic NavBake showcase map id must be non-empty and trimmed.", nameof(mapId));
        }

        string relativePath = $"{ConfigDirectory}/{mapId}.json";
        if (!catalog.TryGet(relativePath, out ConfigCatalogEntry entry))
        {
            throw new InvalidOperationException($"DynamicNavBakeShowcaseConfig '{relativePath}' must be registered in config_catalog.json.");
        }

        if (entry.MergePolicy != ConfigMergePolicy.Replace)
        {
            throw new InvalidOperationException($"DynamicNavBakeShowcaseConfig '{relativePath}' must use Replace merge policy.");
        }

        JsonObject? merged = _pipeline.MergeFromCatalog(in entry, report) as JsonObject;
        if (merged == null)
        {
            throw new InvalidOperationException($"DynamicNavBakeShowcaseConfig requires '{relativePath}' through ConfigPipeline.");
        }

        return DynamicNavBakeShowcaseConfig.Load(merged);
    }
}
