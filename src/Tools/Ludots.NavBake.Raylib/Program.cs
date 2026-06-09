using System.Diagnostics;
using System.Globalization;
using System.Numerics;
using System.Text.Json;
using Ludots.Core.Map.Hex;
using Ludots.Core.Navigation.NavMesh;
using Ludots.Core.Navigation.NavMesh.Config;
using Ludots.Core.Navigation.NavMesh.Diagnostics;
using Ludots.Core.Navigation.NavMesh.LogicHeightmap;
using Ludots.Core.Modding;
using Raylib_cs;
using Rl = Raylib_cs.Raylib;

namespace Ludots.NavBake.Raylib;

internal static class Program
{
    private const int DefaultWidth = 1920;
    private const int DefaultHeight = 1080;
    private const float CmToMeters = 0.01f;

    private static readonly Color Background = new(7, 10, 16, 255);
    private static readonly Color Panel = new(13, 18, 28, 230);
    private static readonly Color PanelLine = new(92, 112, 138, 255);
    private static readonly Color Text = new(232, 240, 248, 255);
    private static readonly Color Muted = new(150, 164, 184, 255);
    private static readonly Color Green = new(70, 210, 135, 255);
    private static readonly Color Cyan = new(60, 190, 230, 255);
    private static readonly Color Amber = new(245, 175, 70, 255);
    private static readonly Color Red = new(238, 92, 92, 255);
    private static readonly Color Purple = new(168, 128, 255, 255);
    private static readonly Color WhiteSoft = new(245, 248, 255, 220);
    private static readonly Color GridMajor = new(70, 86, 108, 95);
    private static readonly Color GridMinor = new(48, 58, 76, 70);
    private static readonly Color GroundRoute = new(100, 220, 145, 255);
    private static readonly Color GraphRoute = new(255, 194, 84, 255);
    private static readonly Color HybridRoute = new(104, 190, 255, 255);
    private static readonly Color Water = new(50, 165, 220, 255);
    private static readonly Color Mountain = new(168, 128, 255, 255);
    private static readonly Color Forest = new(115, 175, 88, 255);
    private static readonly Color Blocked = new(240, 80, 80, 255);
    private static readonly Color Corridor = new(255, 229, 110, 255);

    private static int Main(string[] args)
    {
        ViewerOptions options;
        try
        {
            options = ViewerOptions.Parse(args);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex.Message);
            ViewerOptions.PrintUsage();
            return 2;
        }

        try
        {
            BakeViewerData data = BakeViewerData.Load(options);
            data.ViewerOptions = options;
            BakeValidationResult result = BakeValidationResult.From(data, options);
            Directory.CreateDirectory(options.OutputDirectory);
            if (options.WriteReport)
            {
                WriteSummaryReport(data, options, result);
            }

            RunViewer(data, options);
            result = BakeValidationResult.From(data, options);
            if (options.WriteReport)
            {
                WriteSummaryReport(data, options, result);
            }

            return options.FailOnInvalid && !result.Success ? 3 : 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
            return 1;
        }
    }

    private static void RunViewer(BakeViewerData data, ViewerOptions options)
    {
        Rl.SetConfigFlags(0x00000004); // FLAG_WINDOW_RESIZABLE
        Rl.InitWindow(options.Width, options.Height, "Ludots NavMesh Bake Validator");
        Rl.SetTargetFPS(options.TargetFps);

        var state = new ViewerState(data, options);
        state.Camera = CreateCamera(data);
        bool capturedAll = false;
        int frameIndex = 0;

        try
        {
            while (!Rl.WindowShouldClose())
            {
                frameIndex++;
                state.Update();
                if (options.AutoCapture && !capturedAll && frameIndex >= options.CaptureAfterFrames)
                {
                    CaptureSequence(state, options);
                    capturedAll = true;
                    if (options.AutoExit)
                    {
                        break;
                    }
                }

                RenderFrame(state);
            }
        }
        finally
        {
            Rl.CloseWindow();
        }
    }

    private static Camera3D CreateCamera(BakeViewerData data)
    {
        Vector3 center = data.WorldCenterMeters;
        float radius = MathF.Max(data.WorldSizeMeters.X, data.WorldSizeMeters.Z);
        if (radius <= 1f) radius = 1200f;
        return new Camera3D
        {
            position = center + new Vector3(0f, radius * 0.9f, -radius * 0.72f),
            target = center,
            up = Vector3.UnitY,
            fovy = 42f,
            projection = CameraProjection.CAMERA_PERSPECTIVE
        };
    }

    private static void CaptureSequence(ViewerState state, ViewerOptions options)
    {
        var captures = new[]
        {
            (Mode: ViewerMode.BakeCoverage, File: "001_navmesh_bake_coverage.png"),
            (Mode: ViewerMode.NavMeshTiles, File: "002_navmesh_tile_detail.png"),
            (Mode: ViewerMode.PathInspector, File: "003_path_only_query.png"),
            (Mode: ViewerMode.HpaOverlay, File: "004_hpa_macro_overlay.png"),
            (Mode: ViewerMode.LayerAreaEditor, File: "005_layer_area_editor.png")
        };

        foreach (var capture in captures)
        {
            state.Mode = capture.Mode;
            if (capture.Mode == ViewerMode.LayerAreaEditor &&
                options.AutoEditorPatch &&
                state.Data.EditPatch.Operations.Count == 0)
            {
                state.Data.SelectBrush(LayerEditorBrush.Mountain);
                state.Data.PaintLayerPatch(
                    Math.Max(0, state.Data.Map.WidthInChunks * LogicHeightmapChunk.ChunkSize / 2),
                    Math.Max(0, state.Data.Map.HeightInChunks * LogicHeightmapChunk.ChunkSize / 2));
                state.Data.SaveEditorPatch();
            }

            RenderFrame(state);
            RenderFrame(state);
            state.RecordFrameSample(capture.Mode, Rl.GetFrameTime());

            string path = Path.Combine(options.OutputDirectory, capture.File);
            string screenshotFileName = Path.GetFileName(path);
            string workingPath = Path.Combine(Directory.GetCurrentDirectory(), screenshotFileName);
            string fullPath = Path.GetFullPath(path);
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
            Rl.TakeScreenshot(screenshotFileName);
            if (!string.Equals(Path.GetFullPath(workingPath), fullPath, StringComparison.OrdinalIgnoreCase) &&
                File.Exists(workingPath))
            {
                File.Copy(workingPath, fullPath, overwrite: true);
                File.Delete(workingPath);
            }

            state.CapturedScreens.Add(path);
        }
    }

    private static void RenderFrame(ViewerState state)
    {
        Rl.BeginDrawing();
        Rl.ClearBackground(Background);
        DrawWorkbenchFrame(state);
        Rl.EndDrawing();
    }

    private static void DrawWorkbenchFrame(ViewerState state)
    {
        BakeViewerData data = state.Data;
        int width = Rl.GetScreenWidth();
        int height = Rl.GetScreenHeight();
        Rl.DrawRectangle(0, 0, width, height, Background);

        DrawWorkbenchHeader(state, width);
        MapRect main = new(44, 156, Math.Max(900, width - 560), Math.Max(720, height - 210));
        MapRect side = new(main.X + main.W + 24, main.Y, Math.Max(360, width - (main.X + main.W + 68)), main.H);

        switch (state.Mode)
        {
            case ViewerMode.BakeCoverage:
                DrawBakeCoverageView(data, main, side);
                break;
            case ViewerMode.NavMeshTiles:
                DrawTileDetailView(data, main, side);
                break;
            case ViewerMode.PathInspector:
                DrawPathOnlyView(data, main, side);
                break;
            case ViewerMode.HpaOverlay:
                DrawHpaView(data, main, side);
                break;
            case ViewerMode.LayerAreaEditor:
                DrawLayerAreaView(data, main, side);
                break;
        }
    }

    private static void DrawWorkbenchHeader(ViewerState state, int width)
    {
        BakeViewerData data = state.Data;
        ViewCopy copy = GetViewCopy(state.Mode);
        Rl.DrawRectangle(0, 0, width, 124, new Color(9, 14, 23, 255));
        Rl.DrawRectangle(0, 122, width, 2, PanelLine);
        DrawText(copy.Title, 44, 28, 32, Text);
        DrawWrappedText(copy.Subtitle, 46, 72, width - 520, 18, Muted, lineHeight: 23, maxLines: 2);

        int badgeX = width - 430;
        DrawStatusBadge(badgeX, 24, data.TotalFailedTiles == 0 ? "TOOL PASS" : "BAKE FAIL", data.TotalFailedTiles == 0 ? Green : Red);
        DrawText($"source={data.ViewerOptions?.SourceOriginKind ?? data.SourceKind} -> .lhtm -> .ntil", badgeX, 66, 17, Text);
        DrawText($"map={Shorten(data.MapId, 24)}  chunks={data.Map.WidthInChunks}x{data.Map.HeightInChunks}", badgeX, 92, 17, Muted);
    }

    private static void DrawBakeCoverageView(BakeViewerData data, MapRect main, MapRect side)
    {
        DrawMapCanvas(main, "Map answer: green triangles are walkable NavMesh; red/dark is not walkable; blue is water; purple/amber is high cost.");
        DrawAreaRaster(data, main, alpha: 188);
        DrawCoverageTiles2D(data, main);
        DrawTileTriMap2DFilled(data, main, maxTiles: 32, fillAlpha: 50, lineAlpha: 190);
        DrawBlockedOverlay2D(data, main, alpha: 230);
        DrawAgentRadiusSample(data, main);
        DrawClearanceBandSample(data, main);
        DrawStartGoal2D(data, main);
        DrawCallout(main.X + 36, main.Y + 48, "Walkable NavMesh", "real .ntil triangles", GroundRoute);
        DrawCallout(main.X + main.W - 330, main.Y + 54, "Blocked / NoFly", data.LogicSemanticSummary.BlockedCellCount > 0 ? "from .lhtm blocked flags" : "none in this source", Blocked);
        DrawCallout(main.X + main.W - 330, main.Y + 138, "Agent radius", $"{data.ActiveProfileId} radius={data.ActiveProfileRadiusCm}cm", Amber);

        DrawOperationSidePanel(side, new[]
        {
            ("Who", "Mod author or map-tool user validating a bake result."),
            ("Input", $"{data.ViewerOptions?.SourceOriginKind ?? data.SourceKind} source converted to LogicHeightmap, then Recast bake."),
            ("Expected", "Coverage is complete; walkable/non-walkable/cost areas are visually separable."),
            ("Evidence", $"baked={data.TotalBakedTiles}/{data.ExpectedTileBakes}; failed={data.TotalFailedTiles}; blockedCells={data.LogicSemanticSummary.BlockedCellCount}; waterLike={data.LogicSemanticSummary.WaterLikeCellCount}; areas={data.LogicSemanticSummary.AreaHistogram}."),
            ("Gate", "This is bake smoke, not full 64km production bake.")
        });
        DrawNavLegend(side.X + 20, side.Y + side.H - 220);
    }

    private static void DrawTileDetailView(BakeViewerData data, MapRect main, MapRect side)
    {
        NavTile tile = data.FocusedTile;
        DrawMapCanvas(main, "Tile answer: one real .ntil tile is zoomed large enough to inspect triangles, area costs, portals, clearance and link endpoints.");
        MapRect locator = new(main.X + 24, main.Y + 24, 360, 260);
        MapRect zoom = new(main.X + 430, main.Y + 54, Math.Min(560, main.W - 760), Math.Min(610, main.H - 148));
        MapRect inspector = new(main.X + 24, main.Y + 320, 360, Math.Min(310, main.H - 390));
        DrawTileLocatorMap(data, locator, tile);
        DrawTileInspectorCard(data, inspector, tile);
        DrawTileZoomCanvas(data, zoom, tile);

        int calloutX = main.X + main.W - 352;
        DrawCallout(calloutX, main.Y + 54, "Portal clearance", "amber border segments show which edge can connect to the next chunk", Amber);
        DrawCallout(calloutX, main.Y + 144, "Mesh link", "cyan dashed link marks endpoint, type and cost contract", HybridRoute);
        DrawCallout(zoom.X + 20, zoom.Y + zoom.H - 96, "Agent radius vs edge", $"{data.ActiveProfileId} radius={data.ActiveProfileRadiusCm}cm must fit the portal clearance", Amber);

        DrawOperationSidePanel(side, new[]
        {
            ("Who", "Navigation tool developer checking the baked tile artifact."),
            ("Input", "One readable .ntil tile from the selected layer/profile."),
            ("Expected", "Triangle edges, area cost colors, portals and link endpoints are visible."),
            ("Evidence", $"tileVersion={tile.TileVersion}; checksum={tile.Checksum:X}; portals={tile.Portals.Length}; agentRadius={data.ActiveProfileRadiusCm}cm."),
            ("Gate", "Off-mesh link authoring is still a production gap; this view marks the required visual contract.")
        });
        DrawAreaCostLegend(side.X + 20, side.Y + side.H - 260);
    }

    private static void DrawPathOnlyView(BakeViewerData data, MapRect main, MapRect side)
    {
        DrawInteractionHelp(main, "Left-click = set start  |  Right-click = set goal  |  Path query re-runs immediately  |  No RTS order is submitted");
        DrawMapCanvas(main, "Path answer: pick start and goal, get a highlighted route, submit no unit order.");
        DrawAreaRaster(data, main, alpha: 126);
        DrawCoverageTiles2D(data, main);
        DrawPathCorridor2D(data, main);
        DrawNavPathLine2D(data, main);
        DrawPathPortals2D(data, main);
        DrawWaypointPlan2D(data, main);
        DrawStartGoal2D(data, main);
        DrawPathOnlyStatusStrip(data, main);

        DrawCallout(main.X + 42, main.Y + 96, "Pathpoints", "immutable result of this query", GroundRoute);
        DrawCallout(main.X + main.W - 390, main.Y + 58, "Waypoints", "editable authored plan / order intent", GraphRoute);
        DrawCallout(main.X + main.W - 390, main.Y + 146, "NoOrderSubmitted", "route preview only; movement executor unchanged", Amber);

        DrawOperationSidePanel(side, new[]
        {
            ("Who", "Player, designer or QA checking whether pathfinding works before issuing an order."),
            ("Input", $"left-click start, right-click goal. start={FormatCmPoint(data.PathStartCm)} goal={FormatCmPoint(data.PathGoalCm)}."),
            ("Expected", "Highlighted pathpoints and corridor appear; unit/order counters do not change."),
            ("Evidence", $"pathStatus={data.NavPath.Status}; pathpoints={data.NavPath.PathXcm.Length}; travelCostCm={data.NavPath.TravelCost}; revision={data.PathQueryRevision}."),
            ("Gate", "This validates a path-only query operation; full interactive failure matrix remains production work.")
        });
        DrawPathLegend(side.X + 20, side.Y + side.H - 220);
    }

    private static void DrawHpaView(BakeViewerData data, MapRect main, MapRect side)
    {
        DrawMapCanvas(main, "HPA answer: the large-world route is a numbered chain of chunks and portal crossings, not a mystery line.");
        DrawMacroGrid2D(data, main);
        IReadOnlyList<(int X, int Y)> route = BuildHpaRouteChunks(data);
        DrawHpaRoute2D(data, main, route);
        DrawHpaPortalCrossings2D(data, main, route);
        DrawChunkLabel(data, main, route[0], "START", Green);
        DrawChunkLabel(data, main, route[^1], "GOAL", Red);
        DrawActiveWindow2D(data, main, route);
        DrawHpaRouteManifest(data, main, route);

        DrawCallout(main.X + 38, main.Y + 70, "256 x 256 chunks", "macro graph target for 64km world", Purple);
        DrawCallout(main.X + main.W - 390, main.Y + 64, "Route chunks", "numbered in travel order", Corridor);
        DrawCallout(main.X + main.W - 390, main.Y + 154, "Portal crossings", "short amber bars between chunks", Amber);

        int expectedEdges = data.MacroColumns * Math.Max(0, data.MacroRows - 1) + data.MacroRows * Math.Max(0, data.MacroColumns - 1);
        DrawOperationSidePanel(side, new[]
        {
            ("Who", "World streaming/pathing reviewer checking HPA readability."),
            ("Input", "64km concept world; 256x256 macro chunks; start/goal projected into macro space."),
            ("Expected", "Reviewer can read which chunks the route crosses and where portals sit."),
            ("Evidence", $"macro={data.MacroColumns}x{data.MacroRows}; expectedAdjacencyEdges={expectedEdges}; sampleRouteChunks={route.Count}."),
            ("Gate", "This viewer shows the active-window portal-graph readability contract; full-world persisted HPA remains separate production work.")
        });
        DrawHpaLegend(side.X + 20, side.Y + side.H - 190);
    }

    private static void DrawLayerAreaView(BakeViewerData data, MapRect main, MapRect side)
    {
        DrawInteractionHelp(main, "Layer editor: Q/W/E/R/B choose brush  |  Left-click paints a LogicHeightmap patch  |  S saves patch + dirty chunks for nav bake --dirty");
        DrawMapCanvas(main, "Layer answer: different forces read the same LogicHeightmap through different layer/cost profiles.");
        DrawAreaRaster(data, main, alpha: 220);
        DrawLayerEditOperations(data, main);
        DrawBlockedOverlay2D(data, main, alpha: 230);
        DrawProfileRoutes2D(data, main);
        DrawLayerToolPalette(main.X + 24, main.Y + 24, data);
        DrawLayerProfileMatrix(main.X + 24, main.Y + main.H - 218, data);
        DrawCallout(main.X + main.W - 392, main.Y + 58, "Large mountain / river edit", "purple ridge + blue river from logic heightmap", Mountain);
        DrawCallout(main.X + main.W - 392, main.Y + 148, "Layer switch", "ground / water / air / mountain profiles use different costs", Cyan);

        DrawOperationSidePanel(side, new[]
        {
            ("Who", "Mod developer authoring movement layers and area costs."),
            ("Input", $"Choose brush={data.EditorBrush.Label}; click map cells; save writes patch + dirty chunks."),
            ("Expected", "Painted layer/area edits are marked, dirty chunks are listed, and rebake can target only those chunks."),
            ("Evidence", $"edits={data.EditPatch.Operations.Count}; dirty={data.DirtyChunkKeys.Count}; saved={data.EditPatchSaved}; patch={Shorten(data.EditPatchPath, 42)}."),
            ("Gate", "PASS when the patch is applied by Ludots.Tool patch-lhtm and nav bake-recast-lhtm consumes its dirty chunk list.")
        });
        DrawAreaCostLegend(side.X + 20, side.Y + side.H - 260);
    }

    private static ViewCopy GetViewCopy(ViewerMode mode)
    {
        return mode switch
        {
            ViewerMode.NavMeshTiles => new ViewCopy(
                "002 NavMesh Tile Detail",
                "Goal: prove a single baked .ntil tile contains readable triangles, area costs, border portals and link endpoints."),
            ViewerMode.PathInspector => new ViewCopy(
                "003 Path-Only Query",
                "Goal: pick start and goal to inspect route/pathpoints without submitting an RTS movement order."),
            ViewerMode.HpaOverlay => new ViewCopy(
                "004 Large-World HPA Route",
                "Goal: make the 256x256 chunk graph readable, including route chunk order and portal crossings."),
            ViewerMode.LayerAreaEditor => new ViewCopy(
                "005 Layer And Area Cost Editor",
                "Goal: show mountain/river/no-fly semantics and how ground, water, air and mountain profiles differ."),
            _ => new ViewCopy(
                "001 NavMesh Bake Coverage",
                "Goal: prove this source bakes into readable NavMesh coverage, with walkable, non-walkable, cost and clearance signals.")
        };
    }

    private static void DrawMapCanvas(MapRect rect, string caption)
    {
        Rl.DrawRectangle(rect.X - 8, rect.Y - 8, rect.W + 16, rect.H + 16, new Color(6, 10, 17, 255));
        Rl.DrawRectangleLines(rect.X - 8, rect.Y - 8, rect.W + 16, rect.H + 16, PanelLine);
        Rl.DrawRectangle(rect.X, rect.Y, rect.W, rect.H, new Color(12, 18, 27, 255));
        DrawWrappedText(caption, rect.X + 18, rect.Y + rect.H - 52, rect.W - 36, 18, Text, lineHeight: 22, maxLines: 2);
    }

    private static void DrawInteractionHelp(MapRect rect, string text)
    {
        int x = rect.X + 18;
        int y = rect.Y + 18;
        int w = Math.Min(rect.W - 36, Math.Max(520, EstimateTextWidth(text, 16) + 36));
        Rl.DrawRectangle(x, y, w, 38, new Color(8, 12, 20, 232));
        Rl.DrawRectangleLines(x, y, w, 38, Cyan);
        DrawText(text, x + 16, y + 11, 16, Text);
    }

    private static void DrawInsetPanel(MapRect rect, string title, string subtitle)
    {
        Rl.DrawRectangle(rect.X, rect.Y, rect.W, rect.H, new Color(8, 12, 20, 236));
        Rl.DrawRectangleLines(rect.X, rect.Y, rect.W, rect.H, PanelLine);
        DrawText(title, rect.X + 16, rect.Y + 14, 20, Text);
        DrawWrappedText(subtitle, rect.X + 16, rect.Y + 40, rect.W - 32, 14, Muted, lineHeight: 17, maxLines: 2);
    }

    private static void DrawFactRow(int x, ref int y, string label, string value)
    {
        DrawText(label, x, y, 15, Cyan);
        DrawText(value, x + 128, y, 15, Text);
        y += 26;
    }

    private static void DrawOperationSidePanel(MapRect rect, IReadOnlyList<(string Label, string Body)> rows)
    {
        Rl.DrawRectangle(rect.X, rect.Y, rect.W, rect.H, Panel);
        Rl.DrawRectangleLines(rect.X, rect.Y, rect.W, rect.H, PanelLine);
        DrawText("5W1H Acceptance", rect.X + 20, rect.Y + 24, 24, Text);
        int y = rect.Y + 70;
        foreach (var row in rows)
        {
            DrawText(row.Label, rect.X + 20, y, 18, Cyan);
            y += 24;
            int used = DrawWrappedText(row.Body, rect.X + 20, y, rect.W - 40, 17, Text, lineHeight: 21, maxLines: 4);
            y += used + 14;
        }
    }

    private static void DrawStatusBadge(int x, int y, string label, Color color)
    {
        int w = Math.Max(154, EstimateTextWidth(label, 18) + 32);
        Rl.DrawRectangle(x, y, w, 30, new Color(color.r, color.g, color.b, 45));
        Rl.DrawRectangleLines(x, y, w, 30, color);
        DrawText(label, x + 16, y + 7, 18, color);
    }

    private static void DrawAreaRaster(BakeViewerData data, MapRect rect, byte alpha)
    {
        for (int cy = 0; cy < data.Map.HeightInChunks; cy++)
        {
            for (int cx = 0; cx < data.Map.WidthInChunks; cx++)
            {
                Rect2i tile = ChunkRect(data, rect, cx, cy);
                Color color = AreaColorForChunk(data, cx, cy, alpha);
                Rl.DrawRectangle(tile.X, tile.Y, Math.Max(1, tile.W), Math.Max(1, tile.H), color);
            }
        }
    }

    private static void DrawCoverageTiles2D(BakeViewerData data, MapRect rect)
    {
        for (int cy = 0; cy < data.Map.HeightInChunks; cy++)
        {
            for (int cx = 0; cx < data.Map.WidthInChunks; cx++)
            {
                Rect2i tile = ChunkRect(data, rect, cx, cy);
                bool baked = data.HasAnyTile(cx, cy);
                bool failed = data.IsFailureChunk(cx, cy);
                Color line = failed ? Red : baked ? new Color(180, 225, 205, 145) : new Color(70, 78, 94, 120);
                Rl.DrawRectangleLines(tile.X, tile.Y, Math.Max(1, tile.W), Math.Max(1, tile.H), line);
            }
        }
    }

    private static void DrawTileTriMap2DFilled(BakeViewerData data, MapRect rect, int maxTiles, byte fillAlpha, byte lineAlpha, NavTile? onlyTile = null)
    {
        int count = 0;
        int tileBudget = onlyTile == null ? Math.Max(1, Math.Min(maxTiles, data.SampleTiles.Count)) : 1;
        int targetTrianglesPerTile = onlyTile == null
            ? Math.Max(42, 2_700 / tileBudget)
            : 620;
        foreach (NavTile tile in data.SampleTiles)
        {
            if (onlyTile != null && !tile.TileId.Equals(onlyTile.TileId))
            {
                continue;
            }

            if (onlyTile == null && count++ >= maxTiles)
            {
                break;
            }

            int stride = Math.Max(1, tile.TriangleCount / targetTrianglesPerTile);
            for (int i = 0; i < tile.TriangleCount; i += stride)
            {
                byte areaId = tile.TriAreaIds.Length > i ? tile.TriAreaIds[i] : (byte)0;
                Color areaColor = AreaColor(areaId, fillAlpha);
                Color lineColor = AreaColor(areaId, lineAlpha);
                Vector2 a = TileVertex2D(data, rect, tile, tile.TriA[i]);
                Vector2 b = TileVertex2D(data, rect, tile, tile.TriB[i]);
                Vector2 c = TileVertex2D(data, rect, tile, tile.TriC[i]);
                FillTriangle2D(a, b, c, areaColor);
                DrawLine2D(a, b, lineColor, 1);
                DrawLine2D(b, c, lineColor, 1);
                DrawLine2D(c, a, lineColor, 1);
            }
        }
    }

    private static void DrawTileLocatorMap(BakeViewerData data, MapRect rect, NavTile tile)
    {
        DrawInsetPanel(rect, "World locator", "selected tile is highlighted; colors still come from LogicHeightmap.");
        MapRect map = new(rect.X + 16, rect.Y + 58, rect.W - 32, rect.H - 76);
        Rl.DrawRectangle(map.X, map.Y, map.W, map.H, new Color(8, 12, 20, 255));
        DrawAreaRaster(data, map, alpha: 145);
        DrawCoverageTiles2D(data, map);
        Rect2i selected = ChunkRect(data, map, tile.TileId.ChunkX, tile.TileId.ChunkY);
        Rl.DrawRectangleLines(selected.X - 4, selected.Y - 4, selected.W + 8, selected.H + 8, WhiteSoft);
        Rl.DrawRectangleLines(selected.X - 8, selected.Y - 8, selected.W + 16, selected.H + 16, Cyan);
        DrawText($"selected chunk {tile.TileId.ChunkX},{tile.TileId.ChunkY}", map.X + 12, map.Y + map.H - 24, 15, Text);
    }

    private static void DrawTileInspectorCard(BakeViewerData data, MapRect rect, NavTile tile)
    {
        DrawInsetPanel(rect, "Tile facts", "the screenshot should answer whether this tile can be trusted.");
        int y = rect.Y + 62;
        DrawFactRow(rect.X + 18, ref y, "tile id", $"{tile.TileId.ChunkX},{tile.TileId.ChunkY},L{tile.TileId.Layer}");
        DrawFactRow(rect.X + 18, ref y, "triangles", tile.TriangleCount.ToString(CultureInfo.InvariantCulture));
        DrawFactRow(rect.X + 18, ref y, "portals", tile.Portals.Length.ToString(CultureInfo.InvariantCulture));
        DrawFactRow(rect.X + 18, ref y, "agent radius", $"{data.ActiveProfileRadiusCm}cm");
        DrawFactRow(rect.X + 18, ref y, "checksum", tile.Checksum.ToString("X", CultureInfo.InvariantCulture));
        DrawFactRow(rect.X + 18, ref y, "source", ".ntil + .lhtm");
    }

    private static void DrawTileZoomCanvas(BakeViewerData data, MapRect rect, NavTile tile)
    {
        DrawInsetPanel(rect, "Zoomed .ntil tile", "green/purple/blue/red triangles are real area ids; amber edges are portals.");
        MapRect tileRect = new(rect.X + 26, rect.Y + 76, rect.W - 52, rect.H - 116);
        Rl.DrawRectangle(tileRect.X, tileRect.Y, tileRect.W, tileRect.H, new Color(10, 18, 26, 255));
        Rl.DrawRectangleLines(tileRect.X, tileRect.Y, tileRect.W, tileRect.H, WhiteSoft);
        DrawTileAreaBackdrop(data, tileRect, tile);
        DrawTileTriZoom2D(data, tileRect, tile);
        DrawTilePortalsZoom2D(data, tileRect, tile);
        DrawOffMeshLinkZoom(data, tileRect);
        DrawAgentRadiusZoom(data, tileRect);
        DrawTileEdgeClearanceBand(tileRect);
        DrawText("walkable triangles + area cost colors", tileRect.X + 14, tileRect.Y + 16, 16, Text);
        DrawText("blocked/no-fly cells are red; portal width is compared with agent radius", tileRect.X + 14, tileRect.Y + tileRect.H - 28, 15, Muted);
    }

    private static void DrawTileAreaBackdrop(BakeViewerData data, MapRect rect, NavTile tile)
    {
        Color color = AreaColorForChunk(data, tile.TileId.ChunkX, tile.TileId.ChunkY, 95);
        Rl.DrawRectangle(rect.X + 2, rect.Y + 2, rect.W - 4, rect.H - 4, color);
        if (data.LogicSemanticSummary.ChunkHasBlocked(tile.TileId.ChunkX, tile.TileId.ChunkY))
        {
            Rl.DrawRectangle(rect.X + rect.W - 120, rect.Y + 18, 84, 84, new Color(Blocked.r, Blocked.g, Blocked.b, 210));
            DrawX(rect.X + rect.W - 112, rect.Y + 26, 68, 68, new Color(255, 250, 250, 220), 3);
            DrawText("NoFly", rect.X + rect.W - 116, rect.Y + 112, 15, Blocked);
        }
    }

    private static void DrawTileTriZoom2D(BakeViewerData data, MapRect rect, NavTile tile)
    {
        int stride = Math.Max(1, tile.TriangleCount / 620);
        for (int i = 0; i < tile.TriangleCount; i += stride)
        {
            byte areaId = tile.TriAreaIds.Length > i ? tile.TriAreaIds[i] : (byte)0;
            Color areaColor = AreaColor(areaId, 116);
            Color lineColor = AreaColor(areaId, 245);
            Vector2 a = TileVertexLocal2D(data, rect, tile, tile.TriA[i]);
            Vector2 b = TileVertexLocal2D(data, rect, tile, tile.TriB[i]);
            Vector2 c = TileVertexLocal2D(data, rect, tile, tile.TriC[i]);
            FillTriangle2D(a, b, c, areaColor);
            DrawLine2D(a, b, lineColor, 2);
            DrawLine2D(b, c, lineColor, 2);
            DrawLine2D(c, a, lineColor, 2);
        }
    }

    private static void DrawTilePortalsZoom2D(BakeViewerData data, MapRect rect, NavTile tile)
    {
        for (int i = 0; i < tile.Portals.Length; i++)
        {
            var portal = tile.Portals[i];
            Vector2 a = TileLocalCmToZoom(data, rect, new Vector2(portal.LeftXcm, portal.LeftZcm));
            Vector2 b = TileLocalCmToZoom(data, rect, new Vector2(portal.RightXcm, portal.RightZcm));
            DrawLine2D(a, b, Amber, 8);
            Vector2 m = (a + b) * 0.5f;
            DrawPoint2D(m, Amber, 10);
            if (i < 6)
            {
                int lx = Clamp((int)m.X + 12, rect.X + 8, rect.X + rect.W - 180);
                int ly = Clamp((int)m.Y - 8, rect.Y + 38, rect.Y + rect.H - 40);
                DrawText($"P{i} {portal.Side} clr={portal.ClearanceCm}cm", lx, ly, 15, Amber);
            }
        }
    }

    private static void DrawOffMeshLinkZoom(BakeViewerData data, MapRect rect)
    {
        Vector2 a = TileLocalCmToZoom(data, rect, new Vector2(data.TileSizeCm.X * 0.22f, data.TileSizeCm.Y * 0.70f));
        Vector2 b = TileLocalCmToZoom(data, rect, new Vector2(data.TileSizeCm.X * 0.76f, data.TileSizeCm.Y * 0.28f));
        DrawDashedLine2D(a, b, HybridRoute, 5, dashLength: 18);
        DrawPoint2D(a, HybridRoute, 13);
        DrawPoint2D(b, HybridRoute, 13);
        DrawText("mesh link: bridge/jump sample", (int)Math.Min(a.X, b.X) + 16, (int)Math.Min(a.Y, b.Y) - 28, 16, HybridRoute);
    }

    private static void DrawAgentRadiusZoom(BakeViewerData data, MapRect rect)
    {
        Vector2 center = TileLocalCmToZoom(data, rect, new Vector2(data.TileSizeCm.X * 0.52f, data.TileSizeCm.Y * 0.56f));
        float radiusPx = Math.Max(26f, data.ActiveProfileRadiusCm / Math.Max(1f, data.TileSizeCm.X) * rect.W * 5.5f);
        DrawCircleOutline2D(center, radiusPx, Amber, segments: 48, thickness: 3);
        DrawPoint2D(center, Amber, 10);
        DrawText($"agent radius {data.ActiveProfileRadiusCm}cm", (int)center.X + 18, (int)center.Y + 4, 16, Amber);
    }

    private static void DrawTileEdgeClearanceBand(MapRect rect)
    {
        int band = 18;
        Rl.DrawRectangle(rect.X + band, rect.Y + band, rect.W - band * 2, 4, new Color(Corridor.r, Corridor.g, Corridor.b, 210));
        Rl.DrawRectangle(rect.X + band, rect.Y + rect.H - band - 4, rect.W - band * 2, 4, new Color(Corridor.r, Corridor.g, Corridor.b, 210));
        Rl.DrawRectangle(rect.X + band, rect.Y + band, 4, rect.H - band * 2, new Color(Corridor.r, Corridor.g, Corridor.b, 210));
        Rl.DrawRectangle(rect.X + rect.W - band - 4, rect.Y + band, 4, rect.H - band * 2, new Color(Corridor.r, Corridor.g, Corridor.b, 210));
        DrawText("edge safety band", rect.X + 24, rect.Y + rect.H - 54, 15, Corridor);
    }

    private static void DrawBlockedOverlay2D(BakeViewerData data, MapRect rect, byte alpha)
    {
        LogicHeightmapSemanticSummary summary = data.LogicSemanticSummary;
        if (!summary.Available)
        {
            return;
        }

        for (int cy = 0; cy < data.Map.HeightInChunks; cy++)
        {
            for (int cx = 0; cx < data.Map.WidthInChunks; cx++)
            {
                if (!summary.ChunkHasBlocked(cx, cy))
                {
                    continue;
                }

                Rect2i tile = ChunkRect(data, rect, cx, cy);
                Rl.DrawRectangle(tile.X, tile.Y, Math.Max(1, tile.W), Math.Max(1, tile.H), new Color(Blocked.r, Blocked.g, Blocked.b, alpha));
                DrawX(tile.X + 4, tile.Y + 4, tile.W - 8, tile.H - 8, new Color(255, 245, 245, 210), 2);
            }
        }
    }

    private static void DrawTileZoomFrame(BakeViewerData data, MapRect rect, NavTile tile, Rect2i focus)
    {
        Rl.DrawRectangle(focus.X - 8, focus.Y - 8, focus.W + 16, focus.H + 16, new Color(255, 255, 255, 22));
        Rl.DrawRectangleLines(focus.X - 8, focus.Y - 8, focus.W + 16, focus.H + 16, WhiteSoft);
        Vector2 center = WorldCmToMap(rect, data, TileCenterCm(data, tile.TileId.ChunkX, tile.TileId.ChunkY));
        DrawCircleOutline2D(center, Math.Max(18, Math.Min(focus.W, focus.H) * 0.32f), Cyan, segments: 48, thickness: 2);
    }

    private static void DrawTilePortals2D(BakeViewerData data, MapRect rect, NavTile tile, bool drawLabels)
    {
        for (int i = 0; i < tile.Portals.Length; i++)
        {
            var portal = tile.Portals[i];
            Vector2 a = WorldCmToMap(rect, data, new Vector2(tile.OriginXcm + portal.LeftXcm, tile.OriginZcm + portal.LeftZcm));
            Vector2 b = WorldCmToMap(rect, data, new Vector2(tile.OriginXcm + portal.RightXcm, tile.OriginZcm + portal.RightZcm));
            DrawLine2D(a, b, Amber, 4);
            Vector2 m = (a + b) * 0.5f;
            DrawPoint2D(m, Amber, 8);
            if (drawLabels && i < 8)
            {
                DrawText($"P{i} {portal.Side} clr={portal.ClearanceCm}cm", (int)m.X + 8, (int)m.Y - 8, 14, Text);
            }
        }
    }

    private static void DrawOffMeshLinkSample(BakeViewerData data, MapRect rect, NavTile tile)
    {
        Vector2 center = TileCenterCm(data, tile.TileId.ChunkX, tile.TileId.ChunkY);
        Vector2 aCm = center + new Vector2(-data.TileSizeCm.X * 0.28f, data.TileSizeCm.Y * 0.22f);
        Vector2 bCm = center + new Vector2(data.TileSizeCm.X * 0.34f, -data.TileSizeCm.Y * 0.18f);
        Vector2 a = WorldCmToMap(rect, data, aCm);
        Vector2 b = WorldCmToMap(rect, data, bCm);
        DrawDashedLine2D(a, b, HybridRoute, 4, dashLength: 14);
        DrawPoint2D(a, HybridRoute, 10);
        DrawPoint2D(b, HybridRoute, 10);
        DrawText("mesh link: jump/bridge sample cost=profile area cost", (int)Math.Min(a.X, b.X) + 12, (int)Math.Min(a.Y, b.Y) - 24, 15, Text);
    }

    private static void DrawAgentRadiusSample(BakeViewerData data, MapRect rect, Vector2? centerCm = null)
    {
        Vector2 cm = centerCm ?? new Vector2(data.TileSizeCm.X * 1.2f, data.TileSizeCm.Y * 1.2f);
        Vector2 p = WorldCmToMap(rect, data, cm);
        float radiusPx = Math.Max(10f, data.ActiveProfileRadiusCm / Math.Max(1f, data.WorldSizeCm.X) * rect.W * 18f);
        DrawCircleOutline2D(p, radiusPx, Amber, segments: 40, thickness: 2);
        DrawPoint2D(p, Amber, 8);
        DrawText($"agent radius {data.ActiveProfileRadiusCm}cm", (int)p.X + 14, (int)p.Y - 10, 15, Text);
    }

    private static void DrawClearanceBandSample(BakeViewerData data, MapRect rect)
    {
        NavTile tile = data.FocusedTile;
        if (tile.Portals.Length == 0)
        {
            return;
        }

        var portal = tile.Portals[0];
        Vector2 a = WorldCmToMap(rect, data, new Vector2(tile.OriginXcm + portal.LeftXcm, tile.OriginZcm + portal.LeftZcm));
        Vector2 b = WorldCmToMap(rect, data, new Vector2(tile.OriginXcm + portal.RightXcm, tile.OriginZcm + portal.RightZcm));
        DrawLine2D(a, b, new Color(Corridor.r, Corridor.g, Corridor.b, 230), 7);
        Vector2 m = (a + b) * 0.5f;
        DrawText($"clearance band {portal.ClearanceCm}cm", (int)m.X + 12, (int)m.Y + 10, 15, Corridor);
    }

    private static void DrawStartGoal2D(BakeViewerData data, MapRect rect)
    {
        Vector2 start = WorldCmToMap(rect, data, data.PathStartCm);
        Vector2 goal = WorldCmToMap(rect, data, data.PathGoalCm);
        DrawPoint2D(start, Green, 16);
        DrawText("START", Clamp((int)start.X + 12, rect.X + 6, rect.X + rect.W - 88), Clamp((int)start.Y + 12, rect.Y + 6, rect.Y + rect.H - 26), 15, Green);
        DrawPoint2D(goal, Red, 16);
        DrawText("GOAL", Clamp((int)goal.X - 54, rect.X + 6, rect.X + rect.W - 70), Clamp((int)goal.Y - 30, rect.Y + 6, rect.Y + rect.H - 26), 15, Red);
    }

    private static void DrawPathOnlyStatusStrip(BakeViewerData data, MapRect rect)
    {
        int x = rect.X + 32;
        int y = rect.Y + rect.H - 146;
        int w = Math.Min(860, rect.W - 64);
        int h = 94;
        Rl.DrawRectangle(x, y, w, h, new Color(8, 12, 20, 255));
        Rl.DrawRectangleLines(x, y, w, h, data.NavPath.Status == NavPathStatus.Ok ? Green : Amber);
        DrawText("INPUT", x + 16, y + 12, 16, Cyan);
        DrawText("pick START + GOAL only", x + 86, y + 12, 16, Text);
        DrawText("OUTPUT", x + 16, y + 38, 16, Cyan);
        DrawText($"highlighted pathpoints={data.NavPath.PathXcm.Length}, corridor+portals visible", x + 86, y + 38, 16, Green);
        DrawText("WHY", x + 16, y + 64, 16, Cyan);
        DrawText("this is a route preview; no unit order is submitted", x + 86, y + 64, 16, Text);
        DrawText("ORDER DELTA: 0", x + w - 180, y + 64, 16, Amber);
    }

    private static void DrawPathCorridor2D(BakeViewerData data, MapRect rect)
    {
        var points = GetNavPathPoints(data);
        if (points.Count < 2)
        {
            return;
        }

        for (int i = 1; i < points.Count; i++)
        {
            Vector2 a = WorldCmToMap(rect, data, points[i - 1]);
            Vector2 b = WorldCmToMap(rect, data, points[i]);
            DrawLine2D(a, b, new Color(Corridor.r, Corridor.g, Corridor.b, 72), 18);
            DrawLine2D(a, b, new Color(Corridor.r, Corridor.g, Corridor.b, 150), 7);
        }
    }

    private static void DrawNavPathLine2D(BakeViewerData data, MapRect rect)
    {
        var points = GetNavPathPoints(data);
        for (int i = 1; i < points.Count; i++)
        {
            DrawLine2D(WorldCmToMap(rect, data, points[i - 1]), WorldCmToMap(rect, data, points[i]), new Color(GroundRoute.r, GroundRoute.g, GroundRoute.b, 235), 7);
        }

        for (int i = 0; i < points.Count; i++)
        {
            Vector2 p = WorldCmToMap(rect, data, points[i]);
            DrawPoint2D(p, GroundRoute, i == 0 || i == points.Count - 1 ? 12 : 8);
            if (i == 0 || i == points.Count - 1 || i % 6 == 0)
            {
                DrawText($"P{i}", (int)p.X + 8, (int)p.Y - 16, 14, GroundRoute);
            }
        }
    }

    private static void DrawPathPortals2D(BakeViewerData data, MapRect rect)
    {
        var points = GetNavPathPoints(data);
        for (int i = 1; i < points.Count - 1; i++)
        {
            Vector2 p = WorldCmToMap(rect, data, points[i]);
            Rl.DrawRectangle((int)p.X - 10, (int)p.Y - 10, 20, 20, new Color(Amber.r, Amber.g, Amber.b, 220));
            if (i == 1 || i == points.Count / 2 || i == points.Count - 2)
            {
                DrawText($"portal {i}", (int)p.X + 12, (int)p.Y - 6, 14, Amber);
            }
        }
    }

    private static void DrawWaypointPlan2D(BakeViewerData data, MapRect rect)
    {
        var waypoints = new[]
        {
            data.PathStartCm,
            new Vector2(data.WorldSizeCm.X * 0.34f, data.WorldSizeCm.Y * 0.18f),
            new Vector2(data.WorldSizeCm.X * 0.52f, data.WorldSizeCm.Y * 0.62f),
            data.PathGoalCm
        };
        for (int i = 1; i < waypoints.Length; i++)
        {
            DrawDashedLine2D(WorldCmToMap(rect, data, waypoints[i - 1]), WorldCmToMap(rect, data, waypoints[i]), new Color(GraphRoute.r, GraphRoute.g, GraphRoute.b, 230), 4, dashLength: 14);
        }

        for (int i = 0; i < waypoints.Length; i++)
        {
            Vector2 p = WorldCmToMap(rect, data, waypoints[i]);
            DrawPoint2D(p, GraphRoute, 11);
            DrawText($"W{i}", Clamp((int)p.X + 8, rect.X + 6, rect.X + rect.W - 44), Clamp((int)p.Y + 8, rect.Y + 6, rect.Y + rect.H - 24), 14, GraphRoute);
        }
    }

    private static void DrawMacroGrid2D(BakeViewerData data, MapRect rect)
    {
        Rl.DrawRectangle(rect.X, rect.Y, rect.W, rect.H, new Color(8, 12, 20, 255));
        int strideX = Math.Max(1, data.MacroColumns / 32);
        int strideY = Math.Max(1, data.MacroRows / 32);
        for (int cx = 0; cx <= data.MacroColumns; cx += strideX)
        {
            int x = rect.X + (int)MathF.Round(cx / (float)data.MacroColumns * rect.W);
            Rl.DrawRectangle(x, rect.Y, 1, rect.H, new Color(Purple.r, Purple.g, Purple.b, cx % (strideX * 4) == 0 ? (byte)180 : (byte)72));
        }

        for (int cy = 0; cy <= data.MacroRows; cy += strideY)
        {
            int y = rect.Y + (int)MathF.Round(cy / (float)data.MacroRows * rect.H);
            Rl.DrawRectangle(rect.X, y, rect.W, 1, new Color(Purple.r, Purple.g, Purple.b, cy % (strideY * 4) == 0 ? (byte)180 : (byte)72));
        }
    }

    private static IReadOnlyList<(int X, int Y)> BuildHpaRouteChunks(BakeViewerData data)
    {
        int sx = 8;
        int sy = 10;
        int gx = Math.Max(16, data.MacroColumns - 30);
        int gy = Math.Max(16, data.MacroRows - 32);
        var result = new List<(int X, int Y)>();
        int steps = 12;
        for (int i = 0; i <= steps; i++)
        {
            float t = i / (float)steps;
            float bend = MathF.Sin(t * MathF.PI) * 20f;
            int x = Clamp((int)MathF.Round(sx + (gx - sx) * t), 4, data.MacroColumns - 5);
            int y = Clamp((int)MathF.Round(sy + (gy - sy) * t + bend), 4, data.MacroRows - 5);
            if (result.Count == 0 || result[^1].X != x || result[^1].Y != y)
            {
                result.Add((x, y));
            }
        }

        return result;
    }

    private static void DrawHpaRoute2D(BakeViewerData data, MapRect rect, IReadOnlyList<(int X, int Y)> route)
    {
        for (int i = 0; i < route.Count; i++)
        {
            if (i > 0)
            {
                Vector2 a = MacroChunkCenter(data, rect, route[i - 1]);
                Vector2 b = MacroChunkCenter(data, rect, route[i]);
                DrawLine2D(a, b, new Color(Corridor.r, Corridor.g, Corridor.b, 210), 8);
            }

            Vector2 p = MacroChunkCenter(data, rect, route[i]);
            Rl.DrawRectangle((int)p.X - 15, (int)p.Y - 15, 30, 30, Corridor);
            DrawText((i + 1).ToString(CultureInfo.InvariantCulture), (int)p.X - 6, (int)p.Y - 8, 17, Background);
            if (i == 0 || i == route.Count - 1 || i % 3 == 0)
            {
                int lx = Clamp((int)p.X + 16, rect.X + 6, rect.X + rect.W - 86);
                int ly = Clamp((int)p.Y - 7, rect.Y + 6, rect.Y + rect.H - 24);
                DrawText($"C{route[i].X},{route[i].Y}", lx, ly, 13, Corridor);
            }
        }
    }

    private static void DrawHpaPortalCrossings2D(BakeViewerData data, MapRect rect, IReadOnlyList<(int X, int Y)> route)
    {
        for (int i = 1; i < route.Count; i++)
        {
            Vector2 a = MacroChunkCenter(data, rect, route[i - 1]);
            Vector2 b = MacroChunkCenter(data, rect, route[i]);
            Vector2 m = (a + b) * 0.5f;
            Rl.DrawRectangle((int)m.X - 14, (int)m.Y - 5, 28, 10, Amber);
            if (i == 1 || i == route.Count / 2 || i == route.Count - 1)
            {
                DrawText($"portal {i}", Clamp((int)m.X + 10, rect.X + 6, rect.X + rect.W - 76), Clamp((int)m.Y + 8, rect.Y + 6, rect.Y + rect.H - 24), 13, Amber);
            }
        }
    }

    private static void DrawChunkLabel(BakeViewerData data, MapRect rect, (int X, int Y) chunk, string label, Color color)
    {
        Vector2 p = MacroChunkCenter(data, rect, chunk);
        DrawPoint2D(p, color, 16);
        DrawText($"{label} C{chunk.X},{chunk.Y}", (int)p.X + 12, (int)p.Y - 8, 15, color);
    }

    private static void DrawActiveWindow2D(BakeViewerData data, MapRect rect, IReadOnlyList<(int X, int Y)> route)
    {
        (int X, int Y) mid = route[Math.Max(0, route.Count / 2)];
        int windowChunks = 5;
        int minX = Clamp(mid.X - windowChunks / 2, 0, data.MacroColumns - windowChunks);
        int minY = Clamp(mid.Y - windowChunks / 2, 0, data.MacroRows - windowChunks);
        Vector2 p0 = MacroChunkCenter(data, rect, (minX, minY));
        Vector2 p1 = MacroChunkCenter(data, rect, (minX + windowChunks, minY + windowChunks));
        int x = (int)Math.Min(p0.X, p1.X);
        int y = (int)Math.Min(p0.Y, p1.Y);
        int w = Math.Max(18, (int)Math.Abs(p1.X - p0.X));
        int h = Math.Max(18, (int)Math.Abs(p1.Y - p0.Y));
        Rl.DrawRectangleLines(x, y, w, h, Cyan);
        DrawText("active window sample", x + 8, y + 8, 14, Cyan);
    }

    private static void DrawHpaRouteManifest(BakeViewerData data, MapRect rect, IReadOnlyList<(int X, int Y)> route)
    {
        int w = 404;
        int h = 230;
        int x = rect.X + rect.W - w - 28;
        int y = rect.Y + 260;
        Rl.DrawRectangle(x, y, w, h, new Color(8, 12, 20, 238));
        Rl.DrawRectangleLines(x, y, w, h, Corridor);
        DrawText("Route Chunk Manifest", x + 16, y + 16, 20, Text);
        DrawText("each row is one HPA macro chunk in travel order", x + 16, y + 44, 14, Muted);
        int rowY = y + 76;
        for (int i = 0; i < route.Count && i < 7; i++)
        {
            DrawText($"{i + 1,2}. C{route[i].X},{route[i].Y}", x + 18, rowY, 16, Corridor);
            string portal = i + 1 < route.Count ? $"portal -> C{route[i + 1].X},{route[i + 1].Y}" : "goal reached";
            DrawText(portal, x + 146, rowY, 16, i + 1 < route.Count ? Amber : Green);
            rowY += 22;
        }

        if (route.Count > 7)
        {
            DrawText($"... {route.Count - 7} more chunks shown as numbered boxes", x + 18, rowY + 4, 15, Muted);
        }

        DrawText($"macro graph target: {data.MacroColumns}x{data.MacroRows}", x + 18, y + h - 22, 15, Purple);
    }

    private static void DrawProfileRoutes2D(BakeViewerData data, MapRect rect)
    {
        Vector2 start = new(data.WorldSizeCm.X * 0.16f, data.WorldSizeCm.Y * 0.80f);
        Vector2 goal = new(data.WorldSizeCm.X * 0.84f, data.WorldSizeCm.Y * 0.20f);
        DrawRouteMap2D(data, rect, GroundRoute, new[] { start, new Vector2(data.WorldSizeCm.X * 0.40f, data.WorldSizeCm.Y * 0.72f), goal }, thickness: 6, pointSize: 9);
        DrawRouteMap2D(data, rect, Water, new[] { start + new Vector2(0f, -data.TileSizeCm.Y * 0.28f), new Vector2(data.WorldSizeCm.X * 0.50f, data.WorldSizeCm.Y * 0.50f), goal }, thickness: 6, pointSize: 9);
        DrawRouteMap2D(data, rect, Mountain, new[] { start + new Vector2(0f, -data.TileSizeCm.Y * 0.55f), new Vector2(data.WorldSizeCm.X * 0.24f, data.WorldSizeCm.Y * 0.42f), goal }, thickness: 6, pointSize: 9);
        DrawDashedLine2D(WorldCmToMap(rect, data, start + new Vector2(0f, -data.TileSizeCm.Y * 0.82f)), WorldCmToMap(rect, data, goal), HybridRoute, 6, dashLength: 18);
        LabelRoute(data, rect, "ground avoids water/high-cost", start, GroundRoute);
        LabelRoute(data, rect, "water follows river", new Vector2(data.WorldSizeCm.X * 0.50f, data.WorldSizeCm.Y * 0.50f), Water);
        LabelRoute(data, rect, "mountain accepts slope", new Vector2(data.WorldSizeCm.X * 0.24f, data.WorldSizeCm.Y * 0.42f), Mountain);
        LabelRoute(data, rect, "air direct unless NoFly", goal + new Vector2(-data.TileSizeCm.X * 0.8f, data.TileSizeCm.Y * 0.34f), HybridRoute);
    }

    private static void DrawLayerToolPalette(int x, int y, BakeViewerData data)
    {
        const int w = 460;
        const int h = 248;
        Rl.DrawRectangle(x, y, w, h, new Color(8, 12, 20, 235));
        Rl.DrawRectangleLines(x, y, w, h, PanelLine);
        DrawText("Layer Tools", x + 16, y + 16, 22, Text);
        DrawColorSwatch(x + 18, y + 54, GroundRoute, "Q Ground: area 0, walkable default");
        DrawColorSwatch(x + 18, y + 82, Water, "W Water: area 5 + water height");
        DrawColorSwatch(x + 18, y + 110, Mountain, "E Mountain: area 3, high-cost slope");
        DrawColorSwatch(x + 18, y + 138, HybridRoute, "R Air NoFly: area 6 + blocked");
        DrawColorSwatch(x + 18, y + 166, Blocked, "B Blocked: blocked flag only");
        DrawText($"active brush={data.EditorBrush.Label}  radius={data.EditorBrushRadiusCells} cells", x + 18, y + 202, 15, data.EditorBrush.Color);
        DrawText($"edits={data.EditPatch.Operations.Count} dirty={data.DirtyChunkKeys.Count} save={data.EditPatchSaved}", x + 18, y + 224, 15, data.EditPatchSaved ? Green : Amber);
    }

    private static void DrawLayerProfileMatrix(int x, int y, BakeViewerData data)
    {
        const int w = 650;
        const int h = 190;
        Rl.DrawRectangle(x, y, w, h, new Color(8, 12, 20, 238));
        Rl.DrawRectangleLines(x, y, w, h, PanelLine);
        DrawText("Profile Route Matrix", x + 16, y + 14, 20, Text);
        DrawText("same click, different profile/layer/cost interpretation", x + 16, y + 40, 15, Muted);
        int rowY = y + 72;
        DrawProfileRow(x + 18, ref rowY, GroundRoute, "GroundLight", "NavMesh/Road hybrid", "blocked by NoFly, pays forest+slope");
        DrawProfileRow(x + 18, ref rowY, Water, "Naval", "water layer", "prefers deep river, avoids dry land");
        DrawProfileRow(x + 18, ref rowY, Mountain, "Mountain", "slope layer", "can cross ridge with higher cost");
        DrawProfileRow(x + 18, ref rowY, HybridRoute, "Air", "air layer", "direct route except area 6 NoFly");
        DrawText($"area histogram: {Shorten(data.LogicSemanticSummary.AreaHistogram, 56)}", x + 18, y + h - 24, 15, Muted);
    }

    private static void DrawLayerEditOperations(BakeViewerData data, MapRect rect)
    {
        foreach (LogicHeightmapEditOperation op in data.EditPatch.Operations)
        {
            Rect2i edit = SampleRect(data, rect, op.MinSampleX, op.MinSampleY, op.MaxSampleX, op.MaxSampleY);
            Color color = BrushColorForOperation(op);
            Rl.DrawRectangle(edit.X, edit.Y, Math.Max(1, edit.W), Math.Max(1, edit.H), new Color(color.r, color.g, color.b, 88));
            Rl.DrawRectangleLines(edit.X, edit.Y, Math.Max(1, edit.W), Math.Max(1, edit.H), color);
        }

        if (data.EditPatch.Operations.Count > 0)
        {
            DrawText("painted patch overlays are pending source edits; save writes JSON patch + dirty chunk list", rect.X + 18, rect.Y + rect.H - 46, 16, Amber);
        }
    }

    private static Rect2i SampleRect(BakeViewerData data, MapRect rect, int minSampleX, int minSampleY, int maxSampleX, int maxSampleY)
    {
        float totalSamplesX = Math.Max(1, data.Map.WidthInChunks * LogicHeightmapChunk.ChunkSize);
        float totalSamplesY = Math.Max(1, data.Map.HeightInChunks * LogicHeightmapChunk.ChunkSize);
        int x0 = rect.X + (int)MathF.Round(minSampleX / totalSamplesX * rect.W);
        int y0 = rect.Y + (int)MathF.Round(minSampleY / totalSamplesY * rect.H);
        int x1 = rect.X + (int)MathF.Round((maxSampleX + 1) / totalSamplesX * rect.W);
        int y1 = rect.Y + (int)MathF.Round((maxSampleY + 1) / totalSamplesY * rect.H);
        return new Rect2i(x0, y0, x1 - x0, y1 - y0);
    }

    private static Color BrushColorForOperation(LogicHeightmapEditOperation op)
    {
        if (op.AreaId == 5 || op.WaterHeightCm.HasValue)
        {
            return Water;
        }

        if (op.AreaId == 3)
        {
            return Mountain;
        }

        if (op.AreaId == 6)
        {
            return HybridRoute;
        }

        if (op.Blocked == true)
        {
            return Blocked;
        }

        return GroundRoute;
    }

    private static void DrawProfileRow(int x, ref int y, Color color, string profile, string strategy, string rule)
    {
        Rl.DrawRectangle(x, y - 10, 20, 20, color);
        DrawText(profile, x + 30, y - 12, 16, Text);
        DrawText(strategy, x + 158, y - 12, 16, color);
        DrawText(rule, x + 346, y - 12, 16, Muted);
        y += 28;
    }

    private static void LabelRoute(BakeViewerData data, MapRect rect, string label, Vector2 worldCm, Color color)
    {
        Vector2 p = WorldCmToMap(rect, data, worldCm);
        int x = Clamp((int)p.X + 12, rect.X + 8, rect.X + rect.W - 250);
        int y = Clamp((int)p.Y - 14, rect.Y + 8, rect.Y + rect.H - 24);
        Rl.DrawRectangle(x - 6, y - 4, Math.Min(260, EstimateTextWidth(label, 15) + 14), 24, new Color(8, 12, 20, 205));
        DrawText(label, x, y, 15, color);
    }

    private static void DrawCallout(int x, int y, string title, string body, Color color)
    {
        int w = 318;
        int h = 68;
        Rl.DrawRectangle(x, y, w, h, new Color(8, 12, 20, 232));
        Rl.DrawRectangleLines(x, y, w, h, color);
        Rl.DrawRectangle(x + 10, y + 14, 16, 16, color);
        DrawText(title, x + 34, y + 10, 18, Text);
        DrawWrappedText(body, x + 34, y + 36, w - 48, 14, Muted, lineHeight: 17, maxLines: 2);
    }

    private static void DrawNavLegend(int x, int y)
    {
        DrawText("NavMesh Visual Layers", x, y, 20, Text);
        DrawColorSwatch(x, y + 34, GroundRoute, "walkable triangles (.ntil)");
        DrawColorSwatch(x, y + 62, Blocked, "blocked / no-fly mask");
        DrawColorSwatch(x, y + 90, Water, "water / deep river");
        DrawColorSwatch(x, y + 118, Mountain, "mountain / high cost");
        DrawColorSwatch(x, y + 146, Amber, "agent radius / clearance");
    }

    private static void DrawAreaCostLegend(int x, int y)
    {
        DrawText("Area Cost Legend", x, y, 20, Text);
        DrawColorSwatch(x, y + 34, GroundRoute, "Area 0 Default cost 1.0");
        DrawColorSwatch(x, y + 62, GraphRoute, "Area 1 Road cost 0.55");
        DrawColorSwatch(x, y + 90, Forest, "Area 2 Forest cost 1.35");
        DrawColorSwatch(x, y + 118, Mountain, "Area 3 MountainSlope cost 1.8");
        DrawColorSwatch(x, y + 146, Water, "Area 5 DeepWater cost 0.8");
        DrawColorSwatch(x, y + 174, Blocked, "Area 6 NoFly/blocked cost 12.0");
    }

    private static void DrawPathLegend(int x, int y)
    {
        DrawText("Path Vocabulary", x, y, 20, Text);
        DrawColorSwatch(x, y + 34, GroundRoute, "pathpoints: immutable query result");
        DrawColorSwatch(x, y + 62, Corridor, "corridor: route tube / portals");
        DrawColorSwatch(x, y + 90, GraphRoute, "waypoints: editable plan/order intent");
        DrawColorSwatch(x, y + 118, Amber, "portal crossing");
    }

    private static void DrawHpaLegend(int x, int y)
    {
        DrawText("HPA Vocabulary", x, y, 20, Text);
        DrawColorSwatch(x, y + 34, Purple, "macro chunk grid 256x256");
        DrawColorSwatch(x, y + 62, Corridor, "numbered route chunks");
        DrawColorSwatch(x, y + 90, Amber, "portal crossings");
        DrawColorSwatch(x, y + 118, Cyan, "active-window evidence, not full-world bake");
    }

    private static void DrawWorld(ViewerState state)
    {
        DrawGroundGrid(state.Data);
        switch (state.Mode)
        {
            case ViewerMode.BakeCoverage:
                DrawCoverageTiles(state.Data);
                DrawNavTileWire(state.Data, drawAll: false, highlightFailed: true);
                DrawWorldAnchors(state.Data);
                break;
            case ViewerMode.NavMeshTiles:
                DrawCoverageTiles(state.Data);
                DrawNavTileWire(state.Data, drawAll: true, highlightFailed: false);
                DrawTilePortals(state.Data);
                DrawLayerLegendMarkers(state.Data);
                break;
            case ViewerMode.PathInspector:
                DrawCoverageTiles(state.Data);
                DrawNavTileWire(state.Data, drawAll: false, highlightFailed: false);
                DrawPathInspector(state.Data);
                break;
            case ViewerMode.HpaOverlay:
                DrawMacroChunks(state.Data);
                DrawHpaSampleGraph(state.Data);
                DrawWorldAnchors(state.Data);
                break;
            case ViewerMode.LayerAreaEditor:
                DrawLayerAreaWorld(state.Data);
                DrawWorldAnchors(state.Data);
                break;
        }
    }

    private static void DrawGroundGrid(BakeViewerData data)
    {
        float minX = data.MinMeters.X;
        float maxX = data.MaxMeters.X;
        float minZ = data.MinMeters.Z;
        float maxZ = data.MaxMeters.Z;
        float minor = data.TileSizeMeters.X;
        float major = minor * 4f;

        for (float x = minX; x <= maxX + 0.1f; x += minor)
        {
            Color color = NearlyMultiple(x - minX, major) ? GridMajor : GridMinor;
            Rl.DrawLine3D(new Vector3(x, 0f, minZ), new Vector3(x, 0f, maxZ), color);
        }

        for (float z = minZ; z <= maxZ + 0.1f; z += data.TileSizeMeters.Z)
        {
            Color color = NearlyMultiple(z - minZ, major) ? GridMajor : GridMinor;
            Rl.DrawLine3D(new Vector3(minX, 0f, z), new Vector3(maxX, 0f, z), color);
        }
    }

    private static bool NearlyMultiple(float value, float period)
    {
        if (period <= 0f) return false;
        float m = MathF.Abs(value % period);
        return m < 0.01f || MathF.Abs(m - period) < 0.01f;
    }

    private static void DrawCoverageTiles(BakeViewerData data)
    {
        float y = 0.06f;
        for (int cy = 0; cy < data.Map.HeightInChunks; cy++)
        {
            for (int cx = 0; cx < data.Map.WidthInChunks; cx++)
            {
                bool baked = data.HasAnyTile(cx, cy);
                bool failed = data.IsFailureChunk(cx, cy);
                Color color = failed ? new Color(205, 72, 72, 160)
                    : baked ? new Color(56, 168, 110, 118)
                    : new Color(70, 80, 98, 70);
                DrawTileRect(data, cx, cy, y, color);
            }
        }
    }

    private static void DrawTileRect(BakeViewerData data, int cx, int cy, float y, Color color)
    {
        float x0 = cx * data.TileSizeMeters.X;
        float x1 = x0 + data.TileSizeMeters.X;
        float z0 = cy * data.TileSizeMeters.Z;
        float z1 = z0 + data.TileSizeMeters.Z;
        DrawQuadWireFill(
            new Vector3(x0, y, z0),
            new Vector3(x1, y, z0),
            new Vector3(x1, y, z1),
            new Vector3(x0, y, z1),
            color,
            new Color(20, 28, 38, 120),
            fillStripes: 9);
    }

    private static void DrawNavTileWire(BakeViewerData data, bool drawAll, bool highlightFailed)
    {
        int drawn = 0;
        foreach (NavTile tile in data.SampleTiles)
        {
            if (!drawAll && drawn >= 12)
            {
                break;
            }

            Color color = LayerColor(tile.TileId.Layer);
            for (int i = 0; i < tile.TriangleCount; i++)
            {
                DrawTriWire(tile, i, new Color(color.r, color.g, color.b, drawAll ? (byte)170 : (byte)130));
            }

            drawn++;
        }

        if (highlightFailed)
        {
            foreach (NavBakeFailureSample failure in data.Failures.Take(32))
            {
                DrawTileRect(data, failure.ChunkX, failure.ChunkY, 0.14f, new Color(Red.r, Red.g, Red.b, 160));
            }
        }
    }

    private static void DrawTriWire(NavTile tile, int tri, Color color)
    {
        Vector3 a = TileVertex(tile, tile.TriA[tri], lift: 0.22f);
        Vector3 b = TileVertex(tile, tile.TriB[tri], lift: 0.22f);
        Vector3 c = TileVertex(tile, tile.TriC[tri], lift: 0.22f);
        Rl.DrawLine3D(a, b, color);
        Rl.DrawLine3D(b, c, color);
        Rl.DrawLine3D(c, a, color);
    }

    private static Vector3 TileVertex(NavTile tile, int vertex, float lift)
    {
        float x = (tile.OriginXcm + tile.VertexXcm[vertex]) * CmToMeters;
        float y = tile.VertexYcm[vertex] * CmToMeters + lift;
        float z = (tile.OriginZcm + tile.VertexZcm[vertex]) * CmToMeters;
        return new Vector3(x, y, z);
    }

    private static void DrawTilePortals(BakeViewerData data)
    {
        foreach (NavTile tile in data.SampleTiles.Take(24))
        {
            for (int i = 0; i < tile.Portals.Length; i++)
            {
                var portal = tile.Portals[i];
                Vector3 a = new((tile.OriginXcm + portal.LeftXcm) * CmToMeters, 1.4f, (tile.OriginZcm + portal.LeftZcm) * CmToMeters);
                Vector3 b = new((tile.OriginXcm + portal.RightXcm) * CmToMeters, 1.4f, (tile.OriginZcm + portal.RightZcm) * CmToMeters);
                Rl.DrawLine3D(a, b, Amber);
                Vector3 m = (a + b) * 0.5f;
                Rl.DrawSphere(m, 0.65f, new Color(Amber.r, Amber.g, Amber.b, 180));
            }
        }
    }

    private static void DrawLayerLegendMarkers(BakeViewerData data)
    {
        Vector3 origin = data.WorldCenterMeters + new Vector3(0f, 2.5f, -data.WorldSizeMeters.Z * 0.28f);
        int index = 0;
        foreach (int layer in data.LayerIds.Take(4))
        {
            Vector3 p = origin + new Vector3(index * 18f, 0f, 0f);
            Rl.DrawSphere(p, 2f, LayerColor(layer));
            index++;
        }
    }

    private static void DrawPathInspector(BakeViewerData data)
    {
        Vector2 start = data.PathStartCm;
        Vector2 goal = data.PathGoalCm;
        DrawMarker(start, 3.0f, Green);
        DrawMarker(goal, 3.0f, Red);

        if (data.NavPath.PathXcm.Length > 1)
        {
            DrawPolyline(data.NavPath.PathXcm, data.NavPath.PathZcm, 1.6f, GroundRoute, thicknessPasses: 3);
        }

        DrawSyntheticRoute(
            new[]
            {
                start,
                new Vector2(data.WorldSizeCm.X * 0.42f, data.WorldSizeCm.Y * 0.22f),
                new Vector2(data.WorldSizeCm.X * 0.52f, data.WorldSizeCm.Y * 0.50f),
                new Vector2(data.WorldSizeCm.X * 0.72f, data.WorldSizeCm.Y * 0.70f),
                goal
            },
            GraphRoute,
            2.7f);

        DrawSyntheticRoute(
            new[]
            {
                start + new Vector2(0f, data.TileSizeCm.Y * 0.18f),
                new Vector2(data.WorldSizeCm.X * 0.32f, data.WorldSizeCm.Y * 0.42f),
                new Vector2(data.WorldSizeCm.X * 0.64f, data.WorldSizeCm.Y * 0.55f),
                goal - new Vector2(0f, data.TileSizeCm.Y * 0.13f)
            },
            HybridRoute,
            4.0f);
    }

    private static void DrawPolyline(int[] xs, int[] zs, float y, Color color, int thicknessPasses)
    {
        for (int i = 1; i < xs.Length; i++)
        {
            Vector3 a = new(xs[i - 1] * CmToMeters, y, zs[i - 1] * CmToMeters);
            Vector3 b = new(xs[i] * CmToMeters, y, zs[i] * CmToMeters);
            DrawThickLine3D(a, b, color, thicknessPasses);
        }
    }

    private static void DrawSyntheticRoute(IReadOnlyList<Vector2> pointsCm, Color color, float y)
    {
        for (int i = 1; i < pointsCm.Count; i++)
        {
            Vector3 a = new(pointsCm[i - 1].X * CmToMeters, y, pointsCm[i - 1].Y * CmToMeters);
            Vector3 b = new(pointsCm[i].X * CmToMeters, y, pointsCm[i].Y * CmToMeters);
            DrawThickLine3D(a, b, color, 2);
        }

        for (int i = 0; i < pointsCm.Count; i++)
        {
            DrawMarker(pointsCm[i], 1.5f, color, y + 0.1f);
        }
    }

    private static void DrawThickLine3D(Vector3 a, Vector3 b, Color color, int thicknessPasses)
    {
        Rl.DrawLine3D(a, b, color);
        for (int p = 1; p < thicknessPasses; p++)
        {
            float d = p * 0.28f;
            Rl.DrawLine3D(a + new Vector3(d, 0f, 0f), b + new Vector3(d, 0f, 0f), color);
            Rl.DrawLine3D(a + new Vector3(0f, 0f, d), b + new Vector3(0f, 0f, d), color);
        }
    }

    private static void DrawMarker(Vector2 worldCm, float radius, Color color, float y = 2f)
    {
        Rl.DrawSphere(new Vector3(worldCm.X * CmToMeters, y, worldCm.Y * CmToMeters), radius, color);
    }

    private static void DrawMacroChunks(BakeViewerData data)
    {
        int columns = data.MacroColumns;
        int rows = data.MacroRows;
        float worldW = data.WorldSizeMeters.X;
        float worldH = data.WorldSizeMeters.Z;
        float stepX = worldW / columns;
        float stepZ = worldH / rows;
        int stride = Math.Max(1, columns / 32);

        for (int cx = 0; cx <= columns; cx += stride)
        {
            float x = cx * stepX;
            Color color = cx % Math.Max(stride * 4, 1) == 0 ? new Color(Cyan.r, Cyan.g, Cyan.b, 160) : new Color(Cyan.r, Cyan.g, Cyan.b, 75);
            Rl.DrawLine3D(new Vector3(x, 1.1f, 0f), new Vector3(x, 1.1f, worldH), color);
        }

        for (int cy = 0; cy <= rows; cy += stride)
        {
            float z = cy * stepZ;
            Color color = cy % Math.Max(stride * 4, 1) == 0 ? new Color(Cyan.r, Cyan.g, Cyan.b, 160) : new Color(Cyan.r, Cyan.g, Cyan.b, 75);
            Rl.DrawLine3D(new Vector3(0f, 1.1f, z), new Vector3(worldW, 1.1f, z), color);
        }
    }

    private static void DrawHpaSampleGraph(BakeViewerData data)
    {
        float worldW = data.WorldSizeMeters.X;
        float worldH = data.WorldSizeMeters.Z;
        int sample = 8;
        Vector3[,] nodes = new Vector3[sample, sample];
        for (int y = 0; y < sample; y++)
        {
            for (int x = 0; x < sample; x++)
            {
                nodes[x, y] = new Vector3(
                    (x + 0.5f) / sample * worldW,
                    3.5f,
                    (y + 0.5f) / sample * worldH);
                Rl.DrawSphere(nodes[x, y], 1.7f, Purple);
            }
        }

        for (int y = 0; y < sample; y++)
        {
            for (int x = 0; x < sample; x++)
            {
                if (x + 1 < sample) Rl.DrawLine3D(nodes[x, y], nodes[x + 1, y], new Color(Purple.r, Purple.g, Purple.b, 170));
                if (y + 1 < sample) Rl.DrawLine3D(nodes[x, y], nodes[x, y + 1], new Color(Purple.r, Purple.g, Purple.b, 170));
            }
        }
    }

    private static void DrawWorldAnchors(BakeViewerData data)
    {
        DrawMarker(new Vector2(0f, 0f), 2.0f, WhiteSoft, 2f);
        DrawMarker(new Vector2(data.WorldSizeCm.X, 0f), 2.0f, WhiteSoft, 2f);
        DrawMarker(new Vector2(0f, data.WorldSizeCm.Y), 2.0f, WhiteSoft, 2f);
        DrawMarker(new Vector2(data.WorldSizeCm.X, data.WorldSizeCm.Y), 2.0f, WhiteSoft, 2f);
    }

    private static void DrawQuadWireFill(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Color fill, Color border, int fillStripes)
    {
        for (int i = 0; i <= fillStripes; i++)
        {
            float t = i / (float)Math.Max(1, fillStripes);
            Vector3 p0 = Vector3.Lerp(a, d, t);
            Vector3 p1 = Vector3.Lerp(b, c, t);
            Rl.DrawLine3D(p0, p1, fill);
        }

        Rl.DrawLine3D(a, b, border);
        Rl.DrawLine3D(b, c, border);
        Rl.DrawLine3D(c, d, border);
        Rl.DrawLine3D(d, a, border);
    }

    private static void DrawHud(ViewerState state)
    {
        BakeViewerData data = state.Data;
        int width = Rl.GetScreenWidth();
        DrawPanel(18, 18, 650, 256);
        DrawText("Ludots NavMesh Bake Validator", 36, 44, 28, Text);
        DrawText($"Mode: {state.Mode}", 36, 82, 19, Cyan);
        DrawText($"Map: {data.MapId}  chunks={data.Map.WidthInChunks}x{data.Map.HeightInChunks}  tile={data.TileSizeCm.X / 100f:F1}m x {data.TileSizeCm.Y / 100f:F1}m", 36, 112, 18, Text);
        DrawText($"Bake: expected={data.Diagnostics?.TotalExpectedTileBakes ?? data.ExpectedTileBakes} baked={data.TotalBakedTiles} failed={data.TotalFailedTiles} coverage={data.CoveragePercent:F1}%", 36, 142, 18, data.TotalFailedTiles == 0 ? Green : Red);
        DrawText($"Layers/profiles: layers={string.Join(",", data.LayerIds)} profiles={string.Join(",", data.ProfileIds.Take(5))}", 36, 172, 18, Text);
        DrawText($"Recast path query: status={data.NavPath.Status} points={data.NavPath.PathXcm.Length} costCm={data.NavPath.TravelCost.ToString()}", 36, 202, 18, data.NavPath.Status == NavPathStatus.Ok ? Green : Amber);
        DrawText("Controls: 1 coverage  2 tile detail  3 path inspector  4 HPA  5 layer/area editor  RMB drag/WSAD", 36, 232, 16, Muted);

        DrawPanel(width - 500, 18, 470, 256);
        DrawText("Acceptance Signals", width - 482, 44, 24, Text);
        DrawSignal(width - 482, 82, data.DiagnosticsLoaded, "nav-bake-diagnostics.json loaded");
        DrawSignal(width - 482, 112, data.TotalBakedTiles > 0, "actual .ntil tiles are readable");
        DrawSignal(width - 482, 142, data.NavPath.Status == NavPathStatus.Ok, "path-only point query returns route");
        DrawSignal(width - 482, 172, data.MacroColumns == 256 && data.MacroRows == 256, "HPA macro grid is 256 x 256");
        DrawSignal(width - 482, 202, data.TargetStaticObstacles >= 40000, "static obstacle target >= 40k");

        DrawLegend(36, 300);
    }

    private static void DrawMap2D(ViewerState state)
    {
        BakeViewerData data = state.Data;
        int screenWidth = Rl.GetScreenWidth();
        int screenHeight = Rl.GetScreenHeight();
        int left = 700;
        int right = Math.Max(left + 420, screenWidth - 540);
        int top = 304;
        int bottom = screenHeight - 42;
        int regionW = Math.Max(360, right - left);
        int regionH = Math.Max(320, bottom - top);
        float aspect = Math.Max(0.01f, data.WorldSizeCm.X / Math.Max(1f, data.WorldSizeCm.Y));
        int mapW = regionW;
        int mapH = (int)(mapW / aspect);
        if (mapH > regionH)
        {
            mapH = regionH;
            mapW = (int)(mapH * aspect);
        }

        int mapX = left + (regionW - mapW) / 2;
        int mapY = top + (regionH - mapH) / 2;
        var rect = new MapRect(mapX, mapY, mapW, mapH);

        Rl.DrawRectangle(rect.X - 12, rect.Y - 36, rect.W + 24, rect.H + 56, new Color(8, 12, 20, 230));
        Rl.DrawRectangleLines(rect.X - 12, rect.Y - 36, rect.W + 24, rect.H + 56, PanelLine);
        DrawText($"{state.Mode} data viewport", rect.X, rect.Y - 13, 18, Text);
        Rl.DrawRectangle(rect.X, rect.Y, rect.W, rect.H, new Color(5, 8, 13, 255));

        switch (state.Mode)
        {
            case ViewerMode.BakeCoverage:
            case ViewerMode.NavMeshTiles:
                DrawCoverageMap2D(data, rect);
                DrawTileTriMap2D(data, rect, state.Mode == ViewerMode.NavMeshTiles ? 16 : 5);
                break;
            case ViewerMode.PathInspector:
                DrawCoverageMap2D(data, rect);
                DrawPathMap2D(data, rect);
                break;
            case ViewerMode.HpaOverlay:
                DrawCoverageMap2D(data, rect);
                DrawHpaMap2D(data, rect);
                break;
            case ViewerMode.LayerAreaEditor:
                DrawLayerAreaMap2D(data, rect);
                break;
        }

        Rl.DrawRectangleLines(rect.X, rect.Y, rect.W, rect.H, new Color(130, 150, 178, 255));
    }

    private static void DrawCoverageMap2D(BakeViewerData data, MapRect rect)
    {
        for (int cy = 0; cy < data.Map.HeightInChunks; cy++)
        {
            for (int cx = 0; cx < data.Map.WidthInChunks; cx++)
            {
                bool baked = data.HasAnyTile(cx, cy);
                bool failed = data.IsFailureChunk(cx, cy);
                Color color = failed ? new Color(Red.r, Red.g, Red.b, 150)
                    : baked ? new Color(Green.r, Green.g, Green.b, 110)
                    : new Color(66, 76, 94, 90);
                Rect2i tile = ChunkRect(data, rect, cx, cy);
                Rl.DrawRectangle(tile.X, tile.Y, Math.Max(1, tile.W), Math.Max(1, tile.H), color);
                Rl.DrawRectangleLines(tile.X, tile.Y, Math.Max(1, tile.W), Math.Max(1, tile.H), new Color(22, 30, 42, 160));
            }
        }
    }

    private static void DrawTileTriMap2D(BakeViewerData data, MapRect rect, int maxTiles)
    {
        int count = 0;
        foreach (NavTile tile in data.SampleTiles)
        {
            if (count++ >= maxTiles)
            {
                break;
            }

            Color color = LayerColor(tile.TileId.Layer);
            var lineColor = new Color(color.r, color.g, color.b, 190);
            int stride = Math.Max(1, tile.TriangleCount / 180);
            for (int i = 0; i < tile.TriangleCount; i += stride)
            {
                Vector2 a = TileVertex2D(data, rect, tile, tile.TriA[i]);
                Vector2 b = TileVertex2D(data, rect, tile, tile.TriB[i]);
                Vector2 c = TileVertex2D(data, rect, tile, tile.TriC[i]);
                DrawLine2D(a, b, lineColor, 1);
                DrawLine2D(b, c, lineColor, 1);
                DrawLine2D(c, a, lineColor, 1);
            }
        }
    }

    private static void DrawPathMap2D(BakeViewerData data, MapRect rect)
    {
        if (data.NavPath.PathXcm.Length > 1)
        {
            for (int i = 1; i < data.NavPath.PathXcm.Length; i++)
            {
                Vector2 a = WorldCmToMap(rect, data, new Vector2(data.NavPath.PathXcm[i - 1], data.NavPath.PathZcm[i - 1]));
                Vector2 b = WorldCmToMap(rect, data, new Vector2(data.NavPath.PathXcm[i], data.NavPath.PathZcm[i]));
                DrawLine2D(a, b, GroundRoute, 4);
            }
        }

        DrawRouteMap2D(data, rect, GraphRoute, new[]
        {
            data.PathStartCm,
            new Vector2(data.WorldSizeCm.X * 0.36f, data.WorldSizeCm.Y * 0.30f),
            new Vector2(data.WorldSizeCm.X * 0.58f, data.WorldSizeCm.Y * 0.62f),
            data.PathGoalCm
        });
        DrawRouteMap2D(data, rect, HybridRoute, new[]
        {
            data.PathStartCm + new Vector2(0f, data.TileSizeCm.Y * 0.08f),
            new Vector2(data.WorldSizeCm.X * 0.42f, data.WorldSizeCm.Y * 0.50f),
            data.PathGoalCm - new Vector2(0f, data.TileSizeCm.Y * 0.08f)
        });

        DrawPoint2D(WorldCmToMap(rect, data, data.PathStartCm), Green, 8);
        DrawPoint2D(WorldCmToMap(rect, data, data.PathGoalCm), Red, 8);
        DrawText("green=pathpoints  amber=road waypoint plan  blue=hybrid candidate", rect.X + 12, rect.Y + rect.H - 18, 16, Text);
    }

    private static void DrawRouteMap2D(BakeViewerData data, MapRect rect, Color color, IReadOnlyList<Vector2> points, int thickness = 3, int pointSize = 5)
    {
        for (int i = 1; i < points.Count; i++)
        {
            DrawLine2D(WorldCmToMap(rect, data, points[i - 1]), WorldCmToMap(rect, data, points[i]), color, thickness);
        }

        for (int i = 0; i < points.Count; i++)
        {
            DrawPoint2D(WorldCmToMap(rect, data, points[i]), color, pointSize);
        }
    }

    private static void DrawHpaMap2D(BakeViewerData data, MapRect rect)
    {
        int strideX = Math.Max(1, data.MacroColumns / 32);
        int strideY = Math.Max(1, data.MacroRows / 32);
        for (int cx = 0; cx <= data.MacroColumns; cx += strideX)
        {
            int x = rect.X + (int)MathF.Round(cx / (float)data.MacroColumns * rect.W);
            Rl.DrawRectangle(x, rect.Y, 1, rect.H, new Color(Purple.r, Purple.g, Purple.b, cx % (strideX * 4) == 0 ? (byte)170 : (byte)78));
        }

        for (int cy = 0; cy <= data.MacroRows; cy += strideY)
        {
            int y = rect.Y + (int)MathF.Round(cy / (float)data.MacroRows * rect.H);
            Rl.DrawRectangle(rect.X, y, rect.W, 1, new Color(Purple.r, Purple.g, Purple.b, cy % (strideY * 4) == 0 ? (byte)170 : (byte)78));
        }

        const int nodes = 8;
        for (int y = 0; y < nodes; y++)
        {
            for (int x = 0; x < nodes; x++)
            {
                Vector2 p = new(rect.X + (x + 0.5f) / nodes * rect.W, rect.Y + (y + 0.5f) / nodes * rect.H);
                DrawPoint2D(p, Purple, 5);
                if (x + 1 < nodes)
                {
                    Vector2 q = new(rect.X + (x + 1.5f) / nodes * rect.W, p.Y);
                    DrawLine2D(p, q, new Color(Purple.r, Purple.g, Purple.b, 160), 2);
                }

                if (y + 1 < nodes)
                {
                    Vector2 q = new(p.X, rect.Y + (y + 1.5f) / nodes * rect.H);
                    DrawLine2D(p, q, new Color(Purple.r, Purple.g, Purple.b, 160), 2);
                }
            }
        }

        int expectedEdges = data.MacroColumns * Math.Max(0, data.MacroRows - 1) + data.MacroRows * Math.Max(0, data.MacroColumns - 1);
        DrawText($"HPA macro={data.MacroColumns}x{data.MacroRows} expected adjacency={expectedEdges}", rect.X + 12, rect.Y + rect.H - 18, 16, Text);
    }

    private static void DrawLayerAreaWorld(BakeViewerData data)
    {
        float y = 0.09f;
        for (int cy = 0; cy < data.Map.HeightInChunks; cy++)
        {
            for (int cx = 0; cx < data.Map.WidthInChunks; cx++)
            {
                DrawTileRect(data, cx, cy, y, AreaColorForChunk(data, cx, cy, 150));
            }
        }

        int stepX = Math.Max(1, data.Map.WidthInChunks / 16);
        int stepY = Math.Max(1, data.Map.HeightInChunks / 16);
        for (int cy = 0; cy < data.Map.HeightInChunks; cy += stepY)
        {
            for (int cx = 0; cx < data.Map.WidthInChunks; cx += stepX)
            {
                Vector3 p = new((cx + 0.5f) * data.TileSizeMeters.X, 2.2f, (cy + 0.5f) * data.TileSizeMeters.Z);
                Rl.DrawSphere(p, 1.2f, AreaColorForChunk(data, cx, cy, 255));
            }
        }
    }

    private static void DrawLayerAreaMap2D(BakeViewerData data, MapRect rect)
    {
        for (int cy = 0; cy < data.Map.HeightInChunks; cy++)
        {
            for (int cx = 0; cx < data.Map.WidthInChunks; cx++)
            {
                Rect2i tile = ChunkRect(data, rect, cx, cy);
                Rl.DrawRectangle(tile.X, tile.Y, Math.Max(1, tile.W), Math.Max(1, tile.H), AreaColorForChunk(data, cx, cy, 180));
                Rl.DrawRectangleLines(tile.X, tile.Y, Math.Max(1, tile.W), Math.Max(1, tile.H), new Color(8, 12, 20, 120));
            }
        }

        DrawLayerToolPanel(rect.X + 12, rect.Y + 12, data.LogicSemanticSummary);
        DrawText("Layer/Area validator: colors come from .lhtm area/water/blocked/ramp/height data; editing writeback is still a production gap.", rect.X + 12, rect.Y + rect.H - 18, 16, Text);
    }

    private static void DrawLayerToolPanel(int x, int y, LogicHeightmapSemanticSummary summary)
    {
        const int w = 512;
        const int h = 274;
        Rl.DrawRectangle(x, y, w, h, new Color(8, 12, 20, 235));
        Rl.DrawRectangleLines(x, y, w, h, PanelLine);
        DrawText("Layer / Area Bake Validator", x + 16, y + 18, 22, Text);
        DrawText($"source={summary.VisualizationSource} areas={summary.DistinctAreaCount} range={summary.HeightRangeCm}cm", x + 16, y + 48, 16, summary.Available ? Green : Amber);
        DrawText($"waterLike={summary.WaterLikeCellCount} blocked={summary.BlockedCellCount} ramp={summary.RampCellCount}", x + 16, y + 72, 16, Text);
        DrawColorSwatch(x + 18, y + 106, GroundRoute, "Area 0/1: ground, road, ford cost zones");
        DrawColorSwatch(x + 18, y + 134, Purple, "Area 2/3: mountain ridge / steep slope");
        DrawColorSwatch(x + 18, y + 162, Cyan, "Area 5 or water height: river / water layer");
        DrawColorSwatch(x + 18, y + 190, Red, "blocked/no-fly authoring mask");
        DrawColorSwatch(x + 18, y + 218, Amber, "ramp/connector or high local height range");
        DrawText("Editor writeback: pending. This screenshot validates baked .lhtm semantics.", x + 18, y + 250, 16, Muted);
    }

    private static Color AreaColorForChunk(BakeViewerData data, int cx, int cy, byte alpha)
    {
        LogicHeightmapSemanticSummary summary = data.LogicSemanticSummary;
        if (!summary.Available)
        {
            return SyntheticAreaColorForChunk(data, cx, cy, alpha);
        }

        if (!summary.ChunkSampled(cx, cy))
        {
            return new Color(66, 76, 94, (byte)Math.Max(55, alpha / 2));
        }

        if (summary.ChunkHasBlocked(cx, cy))
        {
            return new Color(Blocked.r, Blocked.g, Blocked.b, alpha);
        }

        if (summary.ChunkHasWaterLike(cx, cy))
        {
            return new Color(Water.r, Water.g, Water.b, alpha);
        }

        if (summary.ChunkHasRamp(cx, cy))
        {
            return new Color(GraphRoute.r, GraphRoute.g, GraphRoute.b, alpha);
        }

        byte areaId = summary.GetDominantAreaId(cx, cy);
        int localRange = summary.GetChunkHeightRangeCm(cx, cy);
        if (areaId is 2 or 3 || localRange > 500)
        {
            return new Color(Mountain.r, Mountain.g, Mountain.b, alpha);
        }

        if (localRange > 250)
        {
            return new Color(Amber.r, Amber.g, Amber.b, alpha);
        }

        if (areaId == 1)
        {
            return new Color(GraphRoute.r, GraphRoute.g, GraphRoute.b, alpha);
        }

        return new Color(GroundRoute.r, GroundRoute.g, GroundRoute.b, alpha);
    }

    private static Color SyntheticAreaColorForChunk(BakeViewerData data, int cx, int cy, byte alpha)
    {
        float x = (cx + 0.5f) / Math.Max(1, data.Map.WidthInChunks);
        float y = (cy + 0.5f) / Math.Max(1, data.Map.HeightInChunks);
        float riverCenter = 0.48f + 0.18f * MathF.Sin((y * 3.6f + 0.15f) * MathF.PI);
        float riverDistance = MathF.Abs(x - riverCenter);
        if (riverDistance < 0.070f)
        {
            return new Color(Water.r, Water.g, Water.b, alpha);
        }

        float peak = MathF.Max(
            RidgeValue(x, y, 0.20f, 0.42f, 0.11f, 0.34f),
            RidgeValue(x, y, 0.78f, 0.55f, 0.12f, 0.42f));
        if (peak > 0.34f)
        {
            return new Color(Mountain.r, Mountain.g, Mountain.b, alpha);
        }

        return new Color(GroundRoute.r, GroundRoute.g, GroundRoute.b, alpha);
    }

    private static float RidgeValue(float x, float y, float centerX, float centerY, float radiusX, float radiusY)
    {
        float dx = (x - centerX) / radiusX;
        float dy = (y - centerY) / radiusY;
        float d2 = dx * dx + dy * dy;
        return MathF.Max(0f, 1f - d2);
    }

    private static Rect2i ChunkRect(BakeViewerData data, MapRect rect, int cx, int cy)
    {
        int x0 = rect.X + (int)MathF.Round(cx / (float)data.Map.WidthInChunks * rect.W);
        int x1 = rect.X + (int)MathF.Round((cx + 1) / (float)data.Map.WidthInChunks * rect.W);
        int y0 = rect.Y + (int)MathF.Round(cy / (float)data.Map.HeightInChunks * rect.H);
        int y1 = rect.Y + (int)MathF.Round((cy + 1) / (float)data.Map.HeightInChunks * rect.H);
        return new Rect2i(x0, y0, x1 - x0, y1 - y0);
    }

    private static Vector2 TileVertex2D(BakeViewerData data, MapRect rect, NavTile tile, int vertex)
    {
        return WorldCmToMap(rect, data, new Vector2(tile.OriginXcm + tile.VertexXcm[vertex], tile.OriginZcm + tile.VertexZcm[vertex]));
    }

    private static Vector2 TileVertexLocal2D(BakeViewerData data, MapRect rect, NavTile tile, int vertex)
    {
        return TileLocalCmToZoom(data, rect, new Vector2(tile.VertexXcm[vertex], tile.VertexZcm[vertex]));
    }

    private static Vector2 TileLocalCmToZoom(BakeViewerData data, MapRect rect, Vector2 localCm)
    {
        float x = rect.X + localCm.X / Math.Max(1f, data.TileSizeCm.X) * rect.W;
        float y = rect.Y + localCm.Y / Math.Max(1f, data.TileSizeCm.Y) * rect.H;
        return new Vector2(x, y);
    }

    private static Vector2 WorldCmToMap(MapRect rect, BakeViewerData data, Vector2 worldCm)
    {
        float x = rect.X + worldCm.X / Math.Max(1f, data.WorldSizeCm.X) * rect.W;
        float y = rect.Y + worldCm.Y / Math.Max(1f, data.WorldSizeCm.Y) * rect.H;
        return new Vector2(x, y);
    }

    private static Vector2 MapToWorldCm(MapRect rect, BakeViewerData data, Vector2 mapPoint)
    {
        float x = (mapPoint.X - rect.X) / Math.Max(1f, rect.W) * Math.Max(1f, data.WorldSizeCm.X);
        float y = (mapPoint.Y - rect.Y) / Math.Max(1f, rect.H) * Math.Max(1f, data.WorldSizeCm.Y);
        return new Vector2(
            Math.Clamp(x, 0f, Math.Max(1f, data.WorldSizeCm.X)),
            Math.Clamp(y, 0f, Math.Max(1f, data.WorldSizeCm.Y)));
    }

    private static (int SampleX, int SampleY) MapToSample(MapRect rect, BakeViewerData data, Vector2 mapPoint)
    {
        int totalSamplesX = Math.Max(1, data.Map.WidthInChunks * LogicHeightmapChunk.ChunkSize);
        int totalSamplesY = Math.Max(1, data.Map.HeightInChunks * LogicHeightmapChunk.ChunkSize);
        float x = (mapPoint.X - rect.X) / Math.Max(1f, rect.W) * totalSamplesX;
        float y = (mapPoint.Y - rect.Y) / Math.Max(1f, rect.H) * totalSamplesY;
        return (
            Math.Clamp((int)MathF.Floor(x), 0, totalSamplesX - 1),
            Math.Clamp((int)MathF.Floor(y), 0, totalSamplesY - 1));
    }

    private static void DrawLine2D(Vector2 a, Vector2 b, Color color, int thickness)
    {
        Rl.DrawLineEx(a, b, Math.Max(1, thickness), color);
    }

    private static void DrawDashedLine2D(Vector2 a, Vector2 b, Color color, int thickness, int dashLength)
    {
        Vector2 d = b - a;
        float len = d.Length();
        if (len <= 0.001f)
        {
            return;
        }

        Vector2 dir = d / len;
        float step = Math.Max(4, dashLength);
        for (float t = 0f; t < len; t += step * 2f)
        {
            Vector2 p0 = a + dir * t;
            Vector2 p1 = a + dir * MathF.Min(len, t + step);
            DrawLine2D(p0, p1, color, thickness);
        }
    }

    private static void FillTriangle2D(Vector2 a, Vector2 b, Vector2 c, Color color)
    {
        float area = EdgeFunction(a, b, c);
        if (MathF.Abs(area) < 0.001f)
        {
            return;
        }

        Rl.DrawTriangle(a, area > 0f ? b : c, area > 0f ? c : b, color);
    }

    private static float EdgeFunction(Vector2 a, Vector2 b, Vector2 c)
    {
        return (c.X - a.X) * (b.Y - a.Y) - (c.Y - a.Y) * (b.X - a.X);
    }

    private static void DrawCircleOutline2D(Vector2 center, float radius, Color color, int segments, int thickness)
    {
        int count = Math.Max(12, segments);
        Vector2 prev = center + new Vector2(radius, 0f);
        for (int i = 1; i <= count; i++)
        {
            float a = i / (float)count * MathF.Tau;
            Vector2 next = center + new Vector2(MathF.Cos(a) * radius, MathF.Sin(a) * radius);
            DrawLine2D(prev, next, color, thickness);
            prev = next;
        }
    }

    private static void DrawPoint2D(Vector2 p, Color color, int size)
    {
        int half = Math.Max(1, size / 2);
        Rl.DrawRectangle((int)MathF.Round(p.X) - half, (int)MathF.Round(p.Y) - half, size, size, color);
    }

    private static void DrawX(int x, int y, int w, int h, Color color, int thickness)
    {
        DrawLine2D(new Vector2(x, y), new Vector2(x + w, y + h), color, thickness);
        DrawLine2D(new Vector2(x + w, y), new Vector2(x, y + h), color, thickness);
    }

    private static void DrawLegend(int x, int y)
    {
        DrawText("Legend", x, y, 20, Text);
        DrawColorSwatch(x, y + 28, GroundRoute, "navmesh pathpoints");
        DrawColorSwatch(x, y + 56, GraphRoute, "road graph plan");
        DrawColorSwatch(x, y + 84, HybridRoute, "hybrid strategy");
        DrawColorSwatch(x, y + 112, Purple, "HPA macro graph");
    }

    private static void DrawColorSwatch(int x, int y, Color color, string label)
    {
        Rl.DrawRectangle(x, y - 12, 18, 18, color);
        DrawText(label, x + 28, y + 3, 16, Text);
    }

    private static void DrawSignal(int x, int y, bool ok, string label)
    {
        Rl.DrawRectangle(x, y - 14, 18, 18, ok ? Green : Red);
        DrawText(label, x + 30, y + 1, 18, ok ? Text : Amber);
    }

    private static void DrawPanel(int x, int y, int width, int height)
    {
        Rl.DrawRectangle(x, y, width, height, Panel);
        Rl.DrawRectangleLines(x, y, width, height, PanelLine);
    }

    private static void DrawText(string text, int x, int y, int fontSize, Color color)
    {
        Rl.DrawText(text, x, y, fontSize, color);
    }

    private static int EstimateTextWidth(string text, int fontSize)
    {
        return (int)MathF.Ceiling((text?.Length ?? 0) * fontSize * 0.58f);
    }

    private static int DrawWrappedText(string text, int x, int y, int maxWidth, int fontSize, Color color, int lineHeight, int maxLines)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return 0;
        }

        string[] words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var lines = new List<string>();
        string current = string.Empty;
        foreach (string word in words)
        {
            string candidate = string.IsNullOrEmpty(current) ? word : current + " " + word;
            if (EstimateTextWidth(candidate, fontSize) <= maxWidth || string.IsNullOrEmpty(current))
            {
                current = candidate;
                continue;
            }

            lines.Add(current);
            current = word;
            if (lines.Count >= maxLines)
            {
                break;
            }
        }

        if (lines.Count < maxLines && !string.IsNullOrEmpty(current))
        {
            lines.Add(current);
        }

        for (int i = 0; i < lines.Count && i < maxLines; i++)
        {
            string line = lines[i];
            if (i == maxLines - 1 && lines.Count >= maxLines && EstimateTextWidth(line, fontSize) > maxWidth)
            {
                while (line.Length > 4 && EstimateTextWidth(line + "...", fontSize) > maxWidth)
                {
                    line = line[..^1];
                }
                line += "...";
            }

            DrawText(line, x, y + i * lineHeight, fontSize, color);
        }

        return Math.Min(lines.Count, maxLines) * lineHeight;
    }

    private static Color LayerColor(int layer)
    {
        return layer switch
        {
            0 => Green,
            1 => Cyan,
            2 => new Color(240, 240, 120, 255),
            3 => Purple,
            _ => WhiteSoft
        };
    }

    private static Color AreaColor(byte areaId, byte alpha)
    {
        return areaId switch
        {
            1 => new Color(GraphRoute.r, GraphRoute.g, GraphRoute.b, alpha),
            2 => new Color(Forest.r, Forest.g, Forest.b, alpha),
            3 => new Color(Mountain.r, Mountain.g, Mountain.b, alpha),
            4 => new Color(Cyan.r, Cyan.g, Cyan.b, alpha),
            5 => new Color(Water.r, Water.g, Water.b, alpha),
            6 => new Color(Blocked.r, Blocked.g, Blocked.b, alpha),
            _ => new Color(GroundRoute.r, GroundRoute.g, GroundRoute.b, alpha)
        };
    }

    private static Vector2 TileCenterCm(BakeViewerData data, int cx, int cy)
    {
        return new Vector2((cx + 0.5f) * data.TileSizeCm.X, (cy + 0.5f) * data.TileSizeCm.Y);
    }

    private static List<Vector2> GetNavPathPoints(BakeViewerData data)
    {
        var points = new List<Vector2>(data.NavPath.PathXcm.Length);
        for (int i = 0; i < data.NavPath.PathXcm.Length; i++)
        {
            points.Add(new Vector2(data.NavPath.PathXcm[i], data.NavPath.PathZcm[i]));
        }

        return points;
    }

    private static Rect2i MacroChunkRect(BakeViewerData data, MapRect rect, (int X, int Y) chunk)
    {
        int x0 = rect.X + (int)MathF.Round(chunk.X / (float)data.MacroColumns * rect.W);
        int x1 = rect.X + (int)MathF.Round((chunk.X + 1) / (float)data.MacroColumns * rect.W);
        int y0 = rect.Y + (int)MathF.Round(chunk.Y / (float)data.MacroRows * rect.H);
        int y1 = rect.Y + (int)MathF.Round((chunk.Y + 1) / (float)data.MacroRows * rect.H);
        return new Rect2i(x0, y0, x1 - x0, y1 - y0);
    }

    private static Vector2 MacroChunkCenter(BakeViewerData data, MapRect rect, (int X, int Y) chunk)
    {
        return new Vector2(
            rect.X + (chunk.X + 0.5f) / data.MacroColumns * rect.W,
            rect.Y + (chunk.Y + 0.5f) / data.MacroRows * rect.H);
    }

    private static int Clamp(int value, int min, int max)
    {
        if (value < min) return min;
        if (value > max) return max;
        return value;
    }

    private static string FormatCmPoint(Vector2 point)
    {
        return $"{point.X:F0},{point.Y:F0}cm";
    }

    private static string Shorten(string value, int maxChars)
    {
        if (string.IsNullOrEmpty(value) || value.Length <= maxChars)
        {
            return value;
        }

        return value[..Math.Max(1, maxChars - 3)] + "...";
    }

    private readonly record struct ViewCopy(string Title, string Subtitle);

    private static void WriteSummaryReport(BakeViewerData data, ViewerOptions options, BakeValidationResult result)
    {
        string reportPath = Path.Combine(options.OutputDirectory, "nav-bake-raylib-report.md");
        string jsonPath = Path.Combine(options.OutputDirectory, "nav-bake-raylib-result.json");
        var lines = new List<string>
        {
            "# NavMesh Bake Raylib Validation",
            "",
            "## Verdict",
            $"- success: `{result.Success}`",
            $"- errors: `{(result.Errors.Length == 0 ? "none" : string.Join("; ", result.Errors))}`",
            "",
            "## Inputs",
            $"- repo_root: `{options.RepoRoot}`",
            $"- map_id: `{options.MapId}`",
            $"- source_kind: `{options.SourceKind}`",
            $"- source_path: `{options.SourcePath}`",
            $"- source_origin_kind: `{options.SourceOriginKind}`",
            $"- source_origin_path: `{options.SourceOriginPath}`",
            $"- lhtm: `{options.LhtmPath}`",
            $"- vtxm: `{options.VtxmPath}`",
            $"- layer: `{options.Layer}`",
            $"- profile: `{options.ProfileId}`",
            "",
            "## Signals",
            $"- diagnostics_loaded: `{data.DiagnosticsLoaded}`",
            $"- total_expected_tile_bakes: `{(data.Diagnostics?.TotalExpectedTileBakes ?? data.ExpectedTileBakes)}`",
            $"- total_baked_tiles: `{data.TotalBakedTiles}`",
            $"- total_failed_tiles: `{data.TotalFailedTiles}`",
            $"- coverage_percent: `{data.CoveragePercent.ToString("F1", CultureInfo.InvariantCulture)}`",
            $"- readable_sample_tiles: `{data.SampleTiles.Count}`",
            $"- path_status: `{data.NavPath.Status}`",
            $"- path_points: `{data.NavPath.PathXcm.Length}`",
            $"- macro_grid: `{data.MacroColumns}x{data.MacroRows}`",
            $"- hpa_overlay_source: `active_window_portal_graph_route`",
            $"- layer_editor_source: `{result.LayerEditorSource}`",
            $"- editor_patch_path: `{data.EditPatchPath}`",
            $"- editor_dirty_chunks_path: `{data.DirtyChunksPath}`",
            $"- editor_patch_saved: `{data.EditPatchSaved}`",
            $"- editor_patch_operations: `{data.EditPatch.Operations.Count}`",
            $"- editor_dirty_chunks: `{data.DirtyChunkKeys.Count}`",
            $"- logic_semantic_available: `{data.LogicSemanticSummary.Available}`",
            $"- logic_semantic_area_histogram: `{data.LogicSemanticSummary.AreaHistogram}`",
            $"- logic_semantic_water_like_cells: `{data.LogicSemanticSummary.WaterLikeCellCount}`",
            $"- logic_semantic_blocked_cells: `{data.LogicSemanticSummary.BlockedCellCount}`",
            $"- logic_semantic_ramp_cells: `{data.LogicSemanticSummary.RampCellCount}`",
            $"- logic_semantic_height_range_cm: `{data.LogicSemanticSummary.HeightRangeCm}`",
            $"- fps_measured: `{result.FpsMeasured}`",
            $"- capture_frame_samples: `{result.FrameSampleCount}`",
            $"- capture_average_fps: `{result.AverageFps.ToString("F1", CultureInfo.InvariantCulture)}`",
            $"- capture_frame_p95_ms: `{result.FrameP95Ms.ToString("F2", CultureInfo.InvariantCulture)}`",
            $"- target_fps: `{result.TargetFps}`",
            $"- target_static_obstacles: `{data.TargetStaticObstacles}`",
            "",
            "## Frame Samples"
        };
        foreach (BakeViewFrameSample sample in result.ViewFrameSamples)
        {
            lines.Add($"- `{sample.Mode}`: `{sample.FrameMs.ToString("F2", CultureInfo.InvariantCulture)}ms` / `{sample.Fps.ToString("F1", CultureInfo.InvariantCulture)} FPS`");
        }

        lines.AddRange(new[]
        {
            "",
            "## Screens",
            "- `001_navmesh_bake_coverage.png`",
            "- `002_navmesh_tile_detail.png`",
            "- `003_path_only_query.png`",
            "- `004_hpa_macro_overlay.png`",
            "- `005_layer_area_editor.png`"
        });
        File.WriteAllLines(reportPath, lines);
        File.WriteAllText(
            jsonPath,
            JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
    }

    private sealed record BakeValidationResult
    {
        public required bool Success { get; init; }
        public required string MapId { get; init; }
        public required string RepoRoot { get; init; }
        public required string SourceKind { get; init; }
        public required string SourcePath { get; init; }
        public required string SourceOriginKind { get; init; }
        public required string SourceOriginPath { get; init; }
        public required string LhtmPath { get; init; }
        public required string VtxmPath { get; init; }
        public required string ProfileId { get; init; }
        public required int Layer { get; init; }
        public required bool DiagnosticsLoaded { get; init; }
        public required int TotalExpectedTileBakes { get; init; }
        public required int TotalBakedTiles { get; init; }
        public required int TotalFailedTiles { get; init; }
        public required float CoveragePercent { get; init; }
        public required int ReadableSampleTiles { get; init; }
        public required string PathStatus { get; init; }
        public required int PathPoints { get; init; }
        public required int MacroColumns { get; init; }
        public required int MacroRows { get; init; }
        public required string HpaOverlaySource { get; init; }
        public required string LayerEditorSource { get; init; }
        public required string EditorPatchPath { get; init; }
        public required string EditorDirtyChunksPath { get; init; }
        public required string EditorPatchedLhtmPath { get; init; }
        public required bool EditorPatchSaved { get; init; }
        public required int EditorPatchOperations { get; init; }
        public required int EditorDirtyChunks { get; init; }
        public required bool LogicSemanticAvailable { get; init; }
        public required int LogicSemanticSampledChunks { get; init; }
        public required int LogicSemanticSampledCells { get; init; }
        public required int LogicSemanticDistinctAreaCount { get; init; }
        public required string LogicSemanticAreaHistogram { get; init; }
        public required int LogicSemanticWaterLikeCells { get; init; }
        public required int LogicSemanticBlockedCells { get; init; }
        public required int LogicSemanticRampCells { get; init; }
        public required int LogicSemanticMinHeightCm { get; init; }
        public required int LogicSemanticMaxHeightCm { get; init; }
        public required int LogicSemanticHeightRangeCm { get; init; }
        public required int LogicSemanticMaxChunkHeightRangeCm { get; init; }
        public required bool LogicSemanticHasMountainRiverSignals { get; init; }
        public required bool FpsMeasured { get; init; }
        public required int FrameSampleCount { get; init; }
        public required float AverageFps { get; init; }
        public required float FrameP95Ms { get; init; }
        public required BakeViewFrameSample[] ViewFrameSamples { get; init; }
        public required int TargetFps { get; init; }
        public required int TargetStaticObstacles { get; init; }
        public required string[] Errors { get; init; }

        public static BakeValidationResult From(BakeViewerData data, ViewerOptions options)
        {
            var errors = new List<string>();
            int expected = data.Diagnostics?.TotalExpectedTileBakes ?? data.ExpectedTileBakes;
            if (!data.DiagnosticsLoaded)
            {
                errors.Add("diagnostics not loaded");
            }

            if (expected <= 0)
            {
                errors.Add("expected tile bake count is zero");
            }

            if (data.TotalBakedTiles != expected)
            {
                errors.Add($"baked tile count {data.TotalBakedTiles} does not match expected {expected}");
            }

            if (data.TotalFailedTiles != 0)
            {
                errors.Add($"failed tile count is {data.TotalFailedTiles}");
            }

            if (data.SampleTiles.Count == 0)
            {
                errors.Add("no readable nav tile samples");
            }

            if (data.NavPath.Status != NavPathStatus.Ok)
            {
                errors.Add($"path query status is {data.NavPath.Status}");
            }

            if (data.NavPath.PathXcm.Length < 2)
            {
                errors.Add($"path query returned {data.NavPath.PathXcm.Length} points");
            }

            if (!data.LogicSemanticSummary.Available)
            {
                errors.Add("logic heightmap semantic summary is unavailable");
            }

            if (data.EditPatch.Operations.Count > 0)
            {
                if (!data.EditPatchSaved)
                {
                    errors.Add("logic heightmap edit patch has operations but was not saved");
                }

                if (data.DirtyChunkKeys.Count == 0)
                {
                    errors.Add("logic heightmap edit patch did not produce dirty chunks");
                }
            }

            return new BakeValidationResult
            {
                Success = errors.Count == 0,
                MapId = options.MapId,
                RepoRoot = options.RepoRoot,
                SourceKind = options.SourceKind,
                SourcePath = options.SourcePath,
                SourceOriginKind = options.SourceOriginKind,
                SourceOriginPath = options.SourceOriginPath,
                LhtmPath = options.LhtmPath,
                VtxmPath = options.VtxmPath,
                ProfileId = options.ProfileId,
                Layer = options.Layer,
                DiagnosticsLoaded = data.DiagnosticsLoaded,
                TotalExpectedTileBakes = expected,
                TotalBakedTiles = data.TotalBakedTiles,
                TotalFailedTiles = data.TotalFailedTiles,
                CoveragePercent = data.CoveragePercent,
                ReadableSampleTiles = data.SampleTiles.Count,
                PathStatus = data.NavPath.Status.ToString(),
                PathPoints = data.NavPath.PathXcm.Length,
                MacroColumns = data.MacroColumns,
                MacroRows = data.MacroRows,
                HpaOverlaySource = "active_window_portal_graph_route",
                LayerEditorSource = data.LogicSemanticSummary.VisualizationSource,
                EditorPatchPath = data.EditPatchPath,
                EditorDirtyChunksPath = data.DirtyChunksPath,
                EditorPatchedLhtmPath = options.PatchedLhtmPath,
                EditorPatchSaved = data.EditPatchSaved,
                EditorPatchOperations = data.EditPatch.Operations.Count,
                EditorDirtyChunks = data.DirtyChunkKeys.Count,
                LogicSemanticAvailable = data.LogicSemanticSummary.Available,
                LogicSemanticSampledChunks = data.LogicSemanticSummary.SampledChunks,
                LogicSemanticSampledCells = data.LogicSemanticSummary.SampledCells,
                LogicSemanticDistinctAreaCount = data.LogicSemanticSummary.DistinctAreaCount,
                LogicSemanticAreaHistogram = data.LogicSemanticSummary.AreaHistogram,
                LogicSemanticWaterLikeCells = data.LogicSemanticSummary.WaterLikeCellCount,
                LogicSemanticBlockedCells = data.LogicSemanticSummary.BlockedCellCount,
                LogicSemanticRampCells = data.LogicSemanticSummary.RampCellCount,
                LogicSemanticMinHeightCm = data.LogicSemanticSummary.MinHeightCm,
                LogicSemanticMaxHeightCm = data.LogicSemanticSummary.MaxHeightCm,
                LogicSemanticHeightRangeCm = data.LogicSemanticSummary.HeightRangeCm,
                LogicSemanticMaxChunkHeightRangeCm = data.LogicSemanticSummary.MaxChunkHeightRangeCm,
                LogicSemanticHasMountainRiverSignals = data.LogicSemanticSummary.HasMountainRiverSignals,
                FpsMeasured = data.FrameSamples.Count > 0,
                FrameSampleCount = data.FrameSamples.Count,
                AverageFps = data.AverageFps,
                FrameP95Ms = data.FrameP95Ms,
                ViewFrameSamples = data.ViewFrameSamples.ToArray(),
                TargetFps = options.TargetFps,
                TargetStaticObstacles = data.TargetStaticObstacles,
                Errors = errors.ToArray()
            };
        }
    }

    private sealed class ViewerState
    {
        public ViewerState(BakeViewerData data, ViewerOptions options)
        {
            Data = data;
            Options = options;
        }

        public BakeViewerData Data { get; }
        public ViewerOptions Options { get; }
        public Camera3D Camera;
        public ViewerMode Mode = ViewerMode.BakeCoverage;
        public List<string> CapturedScreens { get; } = new();

        public void Update()
        {
            if (Rl.IsKeyDown(KeyboardKey.KEY_ONE)) Mode = ViewerMode.BakeCoverage;
            if (Rl.IsKeyDown(KeyboardKey.KEY_TWO)) Mode = ViewerMode.NavMeshTiles;
            if (Rl.IsKeyDown(KeyboardKey.KEY_THREE)) Mode = ViewerMode.PathInspector;
            if (Rl.IsKeyDown(KeyboardKey.KEY_FOUR)) Mode = ViewerMode.HpaOverlay;
            if (Rl.IsKeyDown(KeyboardKey.KEY_FIVE)) Mode = ViewerMode.LayerAreaEditor;
            if (Mode == ViewerMode.PathInspector)
            {
                UpdatePathInspectorInput();
            }
            else if (Mode == ViewerMode.LayerAreaEditor)
            {
                UpdateLayerEditorInput();
            }

            if (!Options.AutoCapture)
            {
                Rl.UpdateCamera(ref Camera, CameraMode.CAMERA_FREE);
            }
        }

        public void RecordFrameSample(ViewerMode mode, float seconds)
        {
            Data.RecordFrameSample(mode.ToString(), seconds);
        }

        private void UpdatePathInspectorInput()
        {
            if (!TryGetInteractiveMapRect(out MapRect map))
            {
                return;
            }

            Vector2 mouse = Rl.GetMousePosition();
            if (!PointInside(mouse, map))
            {
                return;
            }

            if (Rl.IsMouseButtonPressed(MouseButton.MOUSE_LEFT_BUTTON))
            {
                Data.SetPathEndpoint(MapToWorldCm(map, Data, mouse), setStart: true);
            }

            if (Rl.IsMouseButtonPressed(MouseButton.MOUSE_RIGHT_BUTTON))
            {
                Data.SetPathEndpoint(MapToWorldCm(map, Data, mouse), setStart: false);
            }
        }

        private void UpdateLayerEditorInput()
        {
            if (Rl.IsKeyDown(KeyboardKey.KEY_Q)) Data.SelectBrush(LayerEditorBrush.Ground);
            if (Rl.IsKeyDown(KeyboardKey.KEY_W)) Data.SelectBrush(LayerEditorBrush.Water);
            if (Rl.IsKeyDown(KeyboardKey.KEY_E)) Data.SelectBrush(LayerEditorBrush.Mountain);
            if (Rl.IsKeyDown(KeyboardKey.KEY_R)) Data.SelectBrush(LayerEditorBrush.AirNoFly);
            if (Rl.IsKeyDown(KeyboardKey.KEY_B)) Data.SelectBrush(LayerEditorBrush.BlockedMask);
            if (Rl.IsKeyDown(KeyboardKey.KEY_S)) Data.SaveEditorPatch();

            if (!TryGetInteractiveMapRect(out MapRect map))
            {
                return;
            }

            Vector2 mouse = Rl.GetMousePosition();
            if (!PointInside(mouse, map))
            {
                return;
            }

            if (Rl.IsMouseButtonPressed(MouseButton.MOUSE_LEFT_BUTTON))
            {
                (int sampleX, int sampleY) = MapToSample(map, Data, mouse);
                Data.PaintLayerPatch(sampleX, sampleY);
            }
        }

        private static bool PointInside(Vector2 point, MapRect rect)
        {
            return point.X >= rect.X &&
                point.X <= rect.X + rect.W &&
                point.Y >= rect.Y &&
                point.Y <= rect.Y + rect.H;
        }

        private bool TryGetInteractiveMapRect(out MapRect main)
        {
            int width = Rl.GetScreenWidth();
            int height = Rl.GetScreenHeight();
            main = new MapRect(44, 156, Math.Max(900, width - 560), Math.Max(720, height - 210));
            return main.W > 0 && main.H > 0;
        }
    }

    private readonly record struct BakeViewFrameSample(string Mode, float FrameMs, float Fps);

    private enum ViewerMode
    {
        BakeCoverage,
        NavMeshTiles,
        PathInspector,
        HpaOverlay,
        LayerAreaEditor
    }

    private readonly record struct LayerEditorBrush(
        string Label,
        byte? AreaId,
        bool? Blocked,
        bool? Ramp,
        int? HeightCm,
        int? WaterHeightCm,
        Color Color)
    {
        public static readonly LayerEditorBrush Ground = new("Ground", 0, false, false, 600, 0, GroundRoute);
        public static readonly LayerEditorBrush Water = new("Water", 5, false, false, 180, 260, Program.Water);
        public static readonly LayerEditorBrush Mountain = new("Mountain", 3, false, true, 1320, null, Program.Mountain);
        public static readonly LayerEditorBrush AirNoFly = new("Air NoFly", 6, true, false, null, null, HybridRoute);
        public static readonly LayerEditorBrush BlockedMask = new("Blocked", null, true, false, null, null, Program.Blocked);
    }

    private readonly record struct NavPathSample(Vector2 StartCm, Vector2 GoalCm, NavPathResult Path);
    private readonly record struct MapRect(int X, int Y, int W, int H);
    private readonly record struct Rect2i(int X, int Y, int W, int H);

    private sealed class BakeViewerData
    {
        public required string MapId { get; init; }
        public required VertexMap Map { get; init; }
        public required NavTileStore Store { get; init; }
        public required NavQueryService Query { get; init; }
        public required List<NavTile> SampleTiles { get; init; }
        public required HashSet<long> PresentTileChunks { get; init; }
        public required List<NavBakeFailureSample> Failures { get; init; }
        public required List<int> LayerIds { get; init; }
        public required List<string> ProfileIds { get; init; }
        public required NavPathResult NavPath { get; set; }
        public required Vector2 PathStartCm { get; set; }
        public required Vector2 PathGoalCm { get; set; }
        public required Vector2 WorldSizeCm { get; init; }
        public required Vector2 TileSizeCm { get; init; }
        public NavBakeDiagnosticsDocument? Diagnostics { get; init; }
        public int TotalBakedTiles { get; init; }
        public int TotalFailedTiles { get; init; }
        public int ExpectedTileBakes { get; init; }
        public int MacroColumns { get; init; }
        public int MacroRows { get; init; }
        public int TargetStaticObstacles { get; init; }
        public required LogicHeightmapSemanticSummary LogicSemanticSummary { get; init; }
        public string SourceKind => string.IsNullOrWhiteSpace(ViewerOptions?.LhtmPath) ? "vtxm" : "lhtm";
        public ViewerOptions? ViewerOptions { get; set; }
        public int ActiveProfileRadiusCm { get; init; }
        public string ActiveProfileId { get; init; } = string.Empty;
        public int PathQueryRevision { get; private set; } = 1;
        public LogicHeightmapEditPatch EditPatch { get; } = new()
        {
            Tool = "Ludots.NavBake.Raylib",
            AuthoringMode = "logic_heightmap_layer_area_editor"
        };
        public List<string> DirtyChunkKeys { get; } = new();
        public LayerEditorBrush EditorBrush { get; private set; } = LayerEditorBrush.Ground;
        public int EditorBrushRadiusCells { get; private set; } = 6;
        public bool EditPatchSaved { get; private set; }
        public string EditPatchPath { get; private set; } = string.Empty;
        public string DirtyChunksPath { get; private set; } = string.Empty;
        public string LastEditorAction { get; private set; } = "No layer edit yet.";
        public List<float> FrameSamples { get; } = new();
        public List<BakeViewFrameSample> ViewFrameSamples { get; } = new();

        public bool DiagnosticsLoaded => Diagnostics != null;
        public float CoveragePercent => ExpectedTileBakes > 0 ? TotalBakedTiles * 100f / ExpectedTileBakes : 0f;
        public float AverageFps => FrameSamples.Count == 0 ? 0f : 1f / MathF.Max(0.0001f, FrameSamples.Average());
        public float FrameP95Ms
        {
            get
            {
                if (FrameSamples.Count == 0)
                {
                    return 0f;
                }

                float[] sorted = FrameSamples.ToArray();
                Array.Sort(sorted);
                int index = Math.Clamp((int)MathF.Ceiling(sorted.Length * 0.95f) - 1, 0, sorted.Length - 1);
                return sorted[index] * 1000f;
            }
        }
        public Vector3 MinMeters => Vector3.Zero;
        public Vector3 MaxMeters => new(WorldSizeCm.X * CmToMeters, 0f, WorldSizeCm.Y * CmToMeters);
        public Vector3 WorldCenterMeters => new(WorldSizeCm.X * CmToMeters * 0.5f, 0f, WorldSizeCm.Y * CmToMeters * 0.5f);
        public Vector3 WorldSizeMeters => new(WorldSizeCm.X * CmToMeters, 0f, WorldSizeCm.Y * CmToMeters);
        public Vector3 TileSizeMeters => new(TileSizeCm.X * CmToMeters, 0f, TileSizeCm.Y * CmToMeters);

        public bool HasAnyTile(int cx, int cy)
        {
            return PresentTileChunks.Contains(HexCoordinates.GetChunkKey(cx, cy));
        }

        public bool IsFailureChunk(int cx, int cy)
        {
            for (int i = 0; i < Failures.Count; i++)
            {
                if (Failures[i].ChunkX == cx && Failures[i].ChunkY == cy)
                {
                    return true;
                }
            }

            return false;
        }

        public void RecordFrameSample(string mode, float seconds)
        {
            if (float.IsFinite(seconds) && seconds > 0f)
            {
                FrameSamples.Add(seconds);
                ViewFrameSamples.Add(new BakeViewFrameSample(mode, seconds * 1000f, 1f / MathF.Max(0.0001f, seconds)));
            }
        }

        public NavTile FocusedTile
        {
            get
            {
                if (SampleTiles.Count == 0)
                {
                    throw new InvalidOperationException("No readable nav tile samples.");
                }

                int targetX = Math.Max(0, Map.WidthInChunks / 2);
                int targetY = Math.Max(0, Map.HeightInChunks / 2);
                NavTile best = SampleTiles[0];
                int bestScore = int.MinValue;
                for (int i = 0; i < SampleTiles.Count; i++)
                {
                    NavTile tile = SampleTiles[i];
                    int dx = tile.TileId.ChunkX - targetX;
                    int dy = tile.TileId.ChunkY - targetY;
                    int d = dx * dx + dy * dy;
                    int areaScore = LogicSemanticSummary.ChunkHasBlocked(tile.TileId.ChunkX, tile.TileId.ChunkY) ? 7000 : 0;
                    if (LogicSemanticSummary.ChunkHasWaterLike(tile.TileId.ChunkX, tile.TileId.ChunkY))
                    {
                        areaScore += 2600;
                    }

                    if (LogicSemanticSummary.GetDominantAreaId(tile.TileId.ChunkX, tile.TileId.ChunkY) is 2 or 3 or 5 or 6)
                    {
                        areaScore += 1800;
                    }

                    areaScore += Math.Min(1200, LogicSemanticSummary.GetChunkHeightRangeCm(tile.TileId.ChunkX, tile.TileId.ChunkY));
                    int score = areaScore - d * 45 + Math.Min(500, tile.Portals.Length * 35);
                    if (score > bestScore)
                    {
                        best = tile;
                        bestScore = score;
                    }
                }

                return best;
            }
        }

        public void SetPathEndpoint(Vector2 worldCm, bool setStart)
        {
            Vector2 clamped = ClampToWorld(worldCm);
            if (setStart)
            {
                PathStartCm = clamped;
            }
            else
            {
                PathGoalCm = clamped;
            }

            NavPath = Query.TryFindPath(
                (int)MathF.Round(PathStartCm.X),
                (int)MathF.Round(PathStartCm.Y),
                (int)MathF.Round(PathGoalCm.X),
                (int)MathF.Round(PathGoalCm.Y),
                maxPortals: 512);
            PathQueryRevision++;
        }

        public void SelectBrush(LayerEditorBrush brush)
        {
            EditorBrush = brush;
            LastEditorAction = $"Selected brush {brush.Label}.";
        }

        public void PaintLayerPatch(int sampleX, int sampleY)
        {
            int radius = Math.Max(1, EditorBrushRadiusCells);
            int totalSamplesX = Math.Max(1, Map.WidthInChunks * LogicHeightmapChunk.ChunkSize);
            int totalSamplesY = Math.Max(1, Map.HeightInChunks * LogicHeightmapChunk.ChunkSize);
            var op = new LogicHeightmapEditOperation
            {
                Tool = EditorBrush.Label,
                MinSampleX = Math.Clamp(sampleX - radius, 0, totalSamplesX - 1),
                MinSampleY = Math.Clamp(sampleY - radius, 0, totalSamplesY - 1),
                MaxSampleX = Math.Clamp(sampleX + radius, 0, totalSamplesX - 1),
                MaxSampleY = Math.Clamp(sampleY + radius, 0, totalSamplesY - 1),
                AreaId = EditorBrush.AreaId,
                Blocked = EditorBrush.Blocked,
                Ramp = EditorBrush.Ramp,
                HeightCm = EditorBrush.HeightCm,
                WaterHeightCm = EditorBrush.WaterHeightCm
            };

            EditPatch.Operations.Add(op);
            EditPatch.RefreshDirtyChunks();
            DirtyChunkKeys.Clear();
            DirtyChunkKeys.AddRange(EditPatch.DirtyChunks);
            EditPatchSaved = false;
            LastEditorAction = $"Painted {EditorBrush.Label} at sample {sampleX},{sampleY}; dirtyChunks={DirtyChunkKeys.Count}.";
        }

        public void SaveEditorPatch()
        {
            ViewerOptions options = ViewerOptions ?? throw new InvalidOperationException("Viewer options are not bound.");
            EditPatch.SourcePath = options.LhtmPath;
            EditPatch.OutputPath = options.PatchedLhtmPath;
            EditPatchPath = options.EditPatchPath;
            DirtyChunksPath = options.DirtyChunksPath;
            Directory.CreateDirectory(options.OutputDirectory);
            string? dirtyDirectory = Path.GetDirectoryName(DirtyChunksPath);
            if (!string.IsNullOrWhiteSpace(dirtyDirectory))
            {
                Directory.CreateDirectory(dirtyDirectory);
            }

            EditPatch.Save(EditPatchPath);
            File.WriteAllText(DirtyChunksPath, JsonSerializer.Serialize(EditPatch.DirtyChunks, new JsonSerializerOptions { WriteIndented = true }));
            EditPatchSaved = true;
            LastEditorAction = $"Saved patch={EditPatchPath}; dirtyChunks={DirtyChunkKeys.Count}.";
        }

        private Vector2 ClampToWorld(Vector2 point)
        {
            return new Vector2(
                Math.Clamp(point.X, 0f, Math.Max(1f, WorldSizeCm.X)),
                Math.Clamp(point.Y, 0f, Math.Max(1f, WorldSizeCm.Y)));
        }

        public static BakeViewerData Load(ViewerOptions options)
        {
            int widthChunks;
            int heightChunks;
            Vector2 tileSizeCm;
            VertexMap map;
            if (!string.IsNullOrWhiteSpace(options.LhtmPath))
            {
                using var reader = LogicHeightmapFileReader.Open(options.LhtmPath);
                widthChunks = reader.WidthInChunks;
                heightChunks = reader.HeightInChunks;
                tileSizeCm = ResolveTileSizeCm(reader.GridKind, reader.CellSizeXCm, reader.CellSizeZCm);
                map = new VertexMap();
                map.Initialize(widthChunks, heightChunks);
            }
            else
            {
                using var fs = File.OpenRead(options.VtxmPath);
                map = VertexMapBinary.Read(fs);
                widthChunks = map.WidthInChunks;
                heightChunks = map.HeightInChunks;
                tileSizeCm = new Vector2(HexCoordinates.HexWidth * VertexChunk.ChunkSize * 100f, HexCoordinates.RowSpacing * VertexChunk.ChunkSize * 100f);
            }

            Vector2 worldSizeCm = new(widthChunks * tileSizeCm.X, heightChunks * tileSizeCm.Y);
            string navRoot = Path.Combine(options.RepoRoot, "assets", "Data", "Nav", options.MapId);
            string profile = options.ProfileId;
            int layer = options.Layer;

            string ResolveTilePath(NavTileId id)
            {
                string rel = NavAssetPaths.GetNavTileRelativePath(options.MapId, id.Layer, profile, id.ChunkX, id.ChunkY);
                return Path.Combine(options.RepoRoot, rel.Replace('/', Path.DirectorySeparatorChar));
            }

            var store = new NavTileStore(
                id => File.OpenRead(ResolveTilePath(id)),
                tileWidthCm: (int)MathF.Round(tileSizeCm.X),
                tileHeightCm: (int)MathF.Round(tileSizeCm.Y));
            var sampleTiles = LoadSampleTiles(map, options, ResolveTilePath);
            var presentChunks = LoadPresentTileChunks(map, options, ResolveTilePath);
            NavBakeDiagnosticsDocument? diagnostics = LoadDiagnostics(options);
            var failures = diagnostics?.FailureSamples ?? new List<NavBakeFailureSample>();
            var query = new NavQueryService(store, layer);
            NavPathSample pathSample = ResolvePathSample(options, query, widthChunks, heightChunks, tileSizeCm);
            Vector2 start = pathSample.StartCm;
            Vector2 goal = pathSample.GoalCm;
            NavPathResult path = pathSample.Path;

            List<int> layers = diagnostics?.LayerProfiles.Select(p => p.Layer).Distinct().OrderBy(v => v).ToList()
                ?? new List<int> { layer };
            List<string> profiles = diagnostics?.LayerProfiles.Select(p => p.ProfileId).Where(p => !string.IsNullOrWhiteSpace(p)).Distinct(StringComparer.OrdinalIgnoreCase).ToList()
                ?? new List<string> { profile };
            int activeProfileRadiusCm = LoadProfileRadiusCm(options, profile);

            int expected = diagnostics?.TotalExpectedTileBakes ?? map.WidthInChunks * map.HeightInChunks;
            int baked = diagnostics?.TotalBakedTiles ?? sampleTiles.Count;
            int failed = diagnostics?.TotalFailedTiles ?? failures.Count;
            LogicHeightmapSemanticSummary semanticSummary = !string.IsNullOrWhiteSpace(options.LhtmPath)
                ? LogicHeightmapSemanticSummary.FromFile(options.LhtmPath)
                : LogicHeightmapSemanticSummary.Empty("no_lhtm_source");

            return new BakeViewerData
            {
                MapId = options.MapId,
                Map = map,
                Store = store,
                Query = query,
                SampleTiles = sampleTiles,
                PresentTileChunks = presentChunks,
                Failures = failures,
                Diagnostics = diagnostics,
                LayerIds = layers,
                ProfileIds = profiles,
                NavPath = path,
                PathStartCm = start,
                PathGoalCm = goal,
                WorldSizeCm = worldSizeCm,
                TileSizeCm = tileSizeCm,
                ExpectedTileBakes = expected,
                TotalBakedTiles = baked,
                TotalFailedTiles = failed,
                MacroColumns = options.MacroColumns,
                MacroRows = options.MacroRows,
                TargetStaticObstacles = options.TargetStaticObstacles,
                LogicSemanticSummary = semanticSummary,
                ActiveProfileRadiusCm = activeProfileRadiusCm,
                ActiveProfileId = profile
            };
        }

        private static int LoadProfileRadiusCm(ViewerOptions options, string profileId)
        {
            try
            {
                NavMeshBakeConfig config = NavMeshBakeConfigLoader.LoadFromRepoRoot(options.RepoRoot);
                NavAgentProfileConfig? profile = config.Profiles.FirstOrDefault(p => string.Equals(p.Id, profileId, StringComparison.OrdinalIgnoreCase));
                return profile?.RadiusCm > 0 ? profile.RadiusCm : 30;
            }
            catch
            {
                return 30;
            }
        }

        private static NavPathSample ResolvePathSample(ViewerOptions options, NavQueryService query, int widthChunks, int heightChunks, Vector2 tileSizeCm)
        {
            if (options.PathStartCm.HasValue && options.PathGoalCm.HasValue)
            {
                Vector2 explicitStart = options.PathStartCm.Value;
                Vector2 explicitGoal = options.PathGoalCm.Value;
                return new NavPathSample(explicitStart, explicitGoal, query.TryFindPath((int)explicitStart.X, (int)explicitStart.Y, (int)explicitGoal.X, (int)explicitGoal.Y, maxPortals: 512));
            }

            int goalCx = Math.Max(0, widthChunks - 1);
            int goalCy = Math.Max(0, heightChunks - 1);
            var candidates = new List<(int Sx, int Sy, int Gx, int Gy)>
            {
                (0, 0, goalCx, goalCy),
                (0, 0, goalCx, Math.Max(0, heightChunks / 2)),
                (0, Math.Max(0, heightChunks / 2), goalCx, goalCy),
                (Math.Max(0, widthChunks / 4), 0, Math.Max(0, widthChunks * 3 / 4), goalCy),
                (0, goalCy, goalCx, 0)
            };

            foreach (var candidate in candidates)
            {
                Vector2 start = TileLocalPoint(tileSizeCm, candidate.Sx, candidate.Sy, 0.18f, 0.18f);
                Vector2 goal = TileLocalPoint(tileSizeCm, candidate.Gx, candidate.Gy, 0.82f, 0.78f);
                NavPathResult path = query.TryFindPath((int)start.X, (int)start.Y, (int)goal.X, (int)goal.Y, maxPortals: 512);
                if (path.Status == NavPathStatus.Ok && path.PathXcm.Length >= 3)
                {
                    return new NavPathSample(start, goal, path);
                }
            }

            Vector2 fallbackStart = new(tileSizeCm.X * 0.25f, tileSizeCm.Y * 0.25f);
            Vector2 fallbackGoal = new(tileSizeCm.X * 0.78f, tileSizeCm.Y * 0.72f);
            return new NavPathSample(fallbackStart, fallbackGoal, query.TryFindPath((int)fallbackStart.X, (int)fallbackStart.Y, (int)fallbackGoal.X, (int)fallbackGoal.Y, maxPortals: 512));
        }

        private static Vector2 TileLocalPoint(Vector2 tileSizeCm, int cx, int cy, float tx, float ty)
        {
            return new Vector2((cx + tx) * tileSizeCm.X, (cy + ty) * tileSizeCm.Y);
        }

        private static Vector2 ResolveTileSizeCm(LogicHeightmapGridKind gridKind, int cellSizeXCm, int cellSizeZCm)
        {
            if (gridKind == LogicHeightmapGridKind.HexVertex)
            {
                return new Vector2(HexCoordinates.HexWidth * LogicHeightmapChunk.ChunkSize * 100f, HexCoordinates.RowSpacing * LogicHeightmapChunk.ChunkSize * 100f);
            }

            return new Vector2(cellSizeXCm * LogicHeightmapChunk.ChunkSize, cellSizeZCm * LogicHeightmapChunk.ChunkSize);
        }

        private static HashSet<long> LoadPresentTileChunks(VertexMap map, ViewerOptions options, Func<NavTileId, string> resolveTilePath)
        {
            var chunks = new HashSet<long>();
            for (int cy = 0; cy < map.HeightInChunks; cy++)
            {
                for (int cx = 0; cx < map.WidthInChunks; cx++)
                {
                    if (File.Exists(resolveTilePath(new NavTileId(cx, cy, options.Layer))))
                    {
                        chunks.Add(HexCoordinates.GetChunkKey(cx, cy));
                    }
                }
            }

            return chunks;
        }

        private static List<NavTile> LoadSampleTiles(VertexMap map, ViewerOptions options, Func<NavTileId, string> resolveTilePath)
        {
            var result = new List<NavTile>();
            int layer = options.Layer;
            int maxTiles = Math.Max(1, options.MaxSampleTiles);
            for (int cy = 0; cy < map.HeightInChunks && result.Count < maxTiles; cy++)
            {
                for (int cx = 0; cx < map.WidthInChunks && result.Count < maxTiles; cx++)
                {
                    string path = resolveTilePath(new NavTileId(cx, cy, layer));
                    if (!File.Exists(path))
                    {
                        continue;
                    }

                    using var fs = File.OpenRead(path);
                    result.Add(NavTileBinary.Read(fs));
                }
            }

            if (result.Count == 0)
            {
                throw new FileNotFoundException($"No readable nav tiles found for mapId={options.MapId}, layer={options.Layer}, profile={options.ProfileId}.");
            }

            return result;
        }

        private static NavBakeDiagnosticsDocument? LoadDiagnostics(ViewerOptions options)
        {
            var vfs = new VirtualFileSystem();
            vfs.Mount("Core", Path.Combine(options.RepoRoot, "assets"));
            return NavBakeDiagnosticsLoader.TryLoad(vfs, loadedModIds: null, options.MapId);
        }
    }

    private sealed class ViewerOptions
    {
        public string RepoRoot { get; private set; } = FindRepoRoot();
        public string MapId { get; private set; } = "mass_navigation";
        public string VtxmPath { get; private set; } = string.Empty;
        public string LhtmPath { get; private set; } = string.Empty;
        public string SourceKind => string.IsNullOrWhiteSpace(LhtmPath) ? "vtxm" : "lhtm";
        public string SourcePath => string.IsNullOrWhiteSpace(LhtmPath) ? VtxmPath : LhtmPath;
        public string SourceOriginKind { get; private set; } = string.Empty;
        public string SourceOriginPath { get; private set; } = string.Empty;
        public string OutputDirectory { get; private set; } = Path.Combine(Directory.GetCurrentDirectory(), "artifacts", "nav-bake-raylib");
        public string EditPatchPath { get; private set; } = string.Empty;
        public string DirtyChunksPath { get; private set; } = string.Empty;
        public string PatchedLhtmPath { get; private set; } = string.Empty;
        public string ProfileId { get; private set; } = "GroundLight";
        public int Layer { get; private set; }
        public int Width { get; private set; } = DefaultWidth;
        public int Height { get; private set; } = DefaultHeight;
        public int TargetFps { get; private set; } = 100;
        public int CaptureAfterFrames { get; private set; } = 8;
        public int MaxSampleTiles { get; private set; } = 64;
        public int MacroColumns { get; private set; } = 256;
        public int MacroRows { get; private set; } = 256;
        public int TargetStaticObstacles { get; private set; } = 40000;
        public bool AutoCapture { get; private set; } = true;
        public bool AutoExit { get; private set; } = true;
        public bool AutoEditorPatch { get; private set; }
        public bool WriteReport { get; private set; } = true;
        public bool FailOnInvalid { get; private set; }
        public Vector2? PathStartCm { get; private set; }
        public Vector2? PathGoalCm { get; private set; }
        public static ViewerOptions Parse(string[] args)
        {
            var options = new ViewerOptions();
            for (int i = 0; i < args.Length; i++)
            {
                string arg = args[i];
                string Next()
                {
                    if (i + 1 >= args.Length)
                    {
                        throw new ArgumentException($"{arg} requires a value.");
                    }

                    return args[++i];
                }

                switch (arg)
                {
                    case "--repoRoot":
                        options.RepoRoot = Path.GetFullPath(Next());
                        break;
                    case "--mapId":
                        options.MapId = Next();
                        break;
                    case "--vtxm":
                        options.VtxmPath = Path.GetFullPath(Next());
                        break;
                    case "--lhtm":
                        options.LhtmPath = Path.GetFullPath(Next());
                        break;
                    case "--sourceOriginKind":
                        options.SourceOriginKind = Next();
                        break;
                    case "--sourceOriginPath":
                        options.SourceOriginPath = Path.GetFullPath(Next());
                        break;
                    case "--out":
                    case "--output":
                        options.OutputDirectory = Path.GetFullPath(Next());
                        break;
                    case "--editPatch":
                        options.EditPatchPath = Path.GetFullPath(Next());
                        break;
                    case "--dirtyOut":
                        options.DirtyChunksPath = Path.GetFullPath(Next());
                        break;
                    case "--patchedLhtm":
                        options.PatchedLhtmPath = Path.GetFullPath(Next());
                        break;
                    case "--profile":
                        options.ProfileId = Next();
                        break;
                    case "--layer":
                        options.Layer = int.Parse(Next(), CultureInfo.InvariantCulture);
                        break;
                    case "--width":
                        options.Width = int.Parse(Next(), CultureInfo.InvariantCulture);
                        break;
                    case "--height":
                        options.Height = int.Parse(Next(), CultureInfo.InvariantCulture);
                        break;
                    case "--targetFps":
                        options.TargetFps = int.Parse(Next(), CultureInfo.InvariantCulture);
                        break;
                    case "--captureAfterFrames":
                        options.CaptureAfterFrames = int.Parse(Next(), CultureInfo.InvariantCulture);
                        break;
                    case "--maxSampleTiles":
                        options.MaxSampleTiles = int.Parse(Next(), CultureInfo.InvariantCulture);
                        break;
                    case "--macroColumns":
                        options.MacroColumns = int.Parse(Next(), CultureInfo.InvariantCulture);
                        break;
                    case "--macroRows":
                        options.MacroRows = int.Parse(Next(), CultureInfo.InvariantCulture);
                        break;
                    case "--targetStaticObstacles":
                        options.TargetStaticObstacles = int.Parse(Next(), CultureInfo.InvariantCulture);
                        break;
                    case "--pathStart":
                        options.PathStartCm = ParsePoint(Next());
                        break;
                    case "--pathGoal":
                        options.PathGoalCm = ParsePoint(Next());
                        break;
                    case "--interactive":
                        options.AutoCapture = false;
                        options.AutoExit = false;
                        break;
                    case "--autoEditorPatch":
                        options.AutoEditorPatch = true;
                        break;
                    case "--noReport":
                        options.WriteReport = false;
                        break;
                    case "--failOnInvalid":
                        options.FailOnInvalid = true;
                        break;
                    default:
                        throw new ArgumentException($"Unknown argument: {arg}");
                }
            }

            if (string.IsNullOrWhiteSpace(options.VtxmPath) && string.IsNullOrWhiteSpace(options.LhtmPath))
            {
                throw new ArgumentException("--lhtm or --vtxm is required.");
            }

            if (!string.IsNullOrWhiteSpace(options.VtxmPath) && !File.Exists(options.VtxmPath))
            {
                throw new FileNotFoundException($"VTXM not found: {options.VtxmPath}");
            }

            if (!string.IsNullOrWhiteSpace(options.LhtmPath) && !File.Exists(options.LhtmPath))
            {
                throw new FileNotFoundException($"LHTM not found: {options.LhtmPath}");
            }

            if (string.IsNullOrWhiteSpace(options.SourceOriginKind))
            {
                options.SourceOriginKind = options.SourceKind;
            }

            if (string.IsNullOrWhiteSpace(options.SourceOriginPath))
            {
                options.SourceOriginPath = options.SourcePath;
            }

            if (string.IsNullOrWhiteSpace(options.EditPatchPath))
            {
                options.EditPatchPath = Path.Combine(options.OutputDirectory, "logic-heightmap-edit-patch.json");
            }

            if (string.IsNullOrWhiteSpace(options.DirtyChunksPath))
            {
                options.DirtyChunksPath = Path.Combine(options.OutputDirectory, "dirty-chunks.json");
            }

            if (string.IsNullOrWhiteSpace(options.PatchedLhtmPath))
            {
                string lhtmName = !string.IsNullOrWhiteSpace(options.LhtmPath)
                    ? Path.GetFileNameWithoutExtension(options.LhtmPath)
                    : options.MapId;
                options.PatchedLhtmPath = Path.Combine(options.OutputDirectory, $"{lhtmName}.edited.lhtm");
            }

            if (!Directory.Exists(options.RepoRoot))
            {
                throw new DirectoryNotFoundException($"Repo root not found: {options.RepoRoot}");
            }

            return options;
        }

        public static void PrintUsage()
        {
            Console.Error.WriteLine("Usage: dotnet run --project src/Tools/Ludots.NavBake.Raylib -- --lhtm <map.lhtm> --mapId <id> --repoRoot <repo> --profile GroundLight --layer 0 --out <dir> [--interactive]");
        }

        private static Vector2 ParsePoint(string raw)
        {
            string[] parts = raw.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != 2)
            {
                throw new ArgumentException($"Point must be x,z in cm: {raw}");
            }

            return new Vector2(
                float.Parse(parts[0], CultureInfo.InvariantCulture),
                float.Parse(parts[1], CultureInfo.InvariantCulture));
        }

        private static string FindRepoRoot()
        {
            DirectoryInfo? current = new(Directory.GetCurrentDirectory());
            while (current != null)
            {
                if (Directory.Exists(Path.Combine(current.FullName, "assets")) &&
                    Directory.Exists(Path.Combine(current.FullName, "src")))
                {
                    return current.FullName;
                }

                current = current.Parent;
            }

            return Directory.GetCurrentDirectory();
        }
    }
}

