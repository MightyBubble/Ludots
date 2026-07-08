using System.Numerics;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.Camera;
using Ludots.Core.Map.Authoring;
using Ludots.Core.Mathematics;
using Ludots.Core.Navigation.NavMesh;
using Ludots.Core.Presentation.Hud;
using Ludots.Core.Presentation.Rendering;
using Ludots.Core.Scripting;
using Ludots.WebUI.DataPlane;
using LiveMapEditorMod.Runtime;
using LiveMapEditorMod.UI;

namespace LiveMapEditorMod.Systems;

internal sealed class LiveMapEditorRealWindowUatDriver
{
    private const int NavAgentStartRelFrame = 210;
    private const int NavAgentEndRelFrame = 305;
    private const int TransportAgentStartRelFrame = 460;
    private const int TransportAgentEndRelFrame = 570;
    private static readonly Vector4 PanelFill = new(0.02f, 0.05f, 0.08f, 0.78f);
    private static readonly Vector4 PanelBorder = new(0.24f, 0.82f, 1f, 0.74f);
    private static readonly Vector4 TitleColor = new(0.94f, 0.98f, 1f, 1f);
    private static readonly Vector4 TextColor = new(0.76f, 0.88f, 0.96f, 0.98f);
    private static readonly Vector4 WarningColor = new(1f, 0.72f, 0.24f, 0.98f);
    private readonly GameEngine _engine;
    private readonly LiveMapEditorRuntime _runtime;
    private readonly LiveMapEditorPanelController _panelController;
    private readonly int _startFrame;
    private readonly string _reportPath;
    private readonly bool[] _steps = new bool[16];
    private readonly string _createdHexMapId;
    private string _stageTitle = "Waiting";
    private string _stageDetail = "real-window UAT is armed";
    private string _cefButtonStatus = "not checked";
    private string _lastAction = "none";
    private string _navStats = "nav pending";
    private string _transportStats = "transport pending";
    private string _mapStats = "map pending";
    private int _failureCount;
    private WorldCmInt2? _navAgentMarker;
    private WorldCmInt2? _transportAgentMarker;

    private LiveMapEditorRealWindowUatDriver(
        GameEngine engine,
        LiveMapEditorRuntime runtime,
        LiveMapEditorPanelController panelController,
        int startFrame,
        string reportPath)
    {
        _engine = engine;
        _runtime = runtime;
        _panelController = panelController;
        _startFrame = startFrame;
        _reportPath = reportPath;
        _createdHexMapId = $"real_window_hex_{DateTime.UtcNow:yyyyMMddHHmmss}";
        Log("armed real-window UAT");
    }

    public static LiveMapEditorRealWindowUatDriver? TryCreate(
        GameEngine engine,
        LiveMapEditorRuntime runtime,
        LiveMapEditorPanelController panelController)
    {
        if (!ReadEnvBoolOrDefault("LUDOTS_LIVE_MAP_EDITOR_REAL_WINDOW_UAT", defaultValue: false))
        {
            return null;
        }

        int startFrame = Math.Max(1, ReadEnvIntOrDefault("LUDOTS_LIVE_MAP_EDITOR_REAL_WINDOW_UAT_START_FRAME", 120));
        string reportPath = Environment.GetEnvironmentVariable("LUDOTS_LIVE_MAP_EDITOR_UAT_REPORT_PATH") ?? string.Empty;
        return new LiveMapEditorRealWindowUatDriver(engine, runtime, panelController, startFrame, reportPath);
    }

    public void Update(int frameIndex, float dt)
    {
        if (frameIndex < _startFrame)
        {
            return;
        }

        int rel = frameIndex - _startFrame;
        UpdateStageText(rel);
        ApplyCamera(rel);
        RunScheduledSteps(rel);
        UpdateAgentMarkers(rel);
    }

    public void DrawScreen(ScreenOverlayBuffer screen)
    {
        screen.AddRect(358, 72, 564, 128, PanelFill, PanelBorder);
        screen.AddText(376, 88, $"REAL WINDOW UAT | {_stageTitle}", 16, TitleColor);
        screen.AddText(376, 114, _stageDetail, 13, TextColor);
        screen.AddText(376, 136, $"CEF Paint button: {_cefButtonStatus}", 13, _cefButtonStatus == "pass" ? TextColor : WarningColor);
        screen.AddText(376, 158, $"{_navStats} | {_transportStats}", 13, TextColor);
        screen.AddText(376, 180, $"{_mapStats} | failures={_failureCount} | last={_lastAction}", 13, _failureCount == 0 ? TextColor : WarningColor);
    }

    public void DrawGround(GroundOverlayBuffer ground)
    {
        if (_navAgentMarker.HasValue)
        {
            DrawAgentMarker(ground, _navAgentMarker.Value, new Vector4(0.24f, 1f, 0.42f, 0.9f), radiusM: 0.85f);
        }

        if (_transportAgentMarker.HasValue)
        {
            DrawAgentMarker(ground, _transportAgentMarker.Value, new Vector4(1f, 0.86f, 0.18f, 0.94f), radiusM: 1.05f);
        }
    }

    private void RunScheduledSteps(int rel)
    {
        RunStep(0, rel, 0, "open CEF editor panel", () =>
        {
            _panelController.Show();
            _runtime.SetViewToggle("grid", true);
            _runtime.SetViewToggle("chunks", true);
            _runtime.SetViewToggle("navmesh", true);
            _runtime.SetViewToggle("path", true);
            _runtime.SetViewToggle("transport", true);
            _runtime.SetViewToggle("minimap", true);
        });

        RunStep(1, rel, 50, "verify synthetic CEF Paint button click", () =>
        {
            if (!_runtime.PanelOpen)
            {
                throw new InvalidOperationException("panel is not open");
            }

            if (!string.Equals(_runtime.Tool, "paint", StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"expected tool=paint after CEF click, got '{_runtime.Tool}'");
            }

            _cefButtonStatus = "pass";
        }, failure =>
        {
            _cefButtonStatus = "fail";
            _runtime.SetTool("paint");
            Log($"CEF Paint button check failed but UAT continues: {failure}");
        });

        RunStep(2, rel, 90, "edit small grid terrain and heightmap", () =>
        {
            _runtime.SetTool("paint");
            _runtime.SetBrush(
                radiusCells: 4,
                mode: "set",
                target: "all",
                heightLevel: 8,
                waterHeightLevel: 10,
                areaId: 2,
                cost: 1.25f,
                blocked: false,
                water: true,
                ramp: true);
            RequireOk("paint ridge A", _runtime.PaintTerrain(_engine, 22, 22, 4));
            RequireOk("paint ridge B", _runtime.PaintTerrain(_engine, 31, 24, 3));
            RequireOk("paint ridge C", _runtime.PaintTerrain(_engine, 40, 30, 4));
            RequireOk("bucket water", _runtime.BucketFillWater(_engine, 22, 22, 11));
            _runtime.SetBrush(
                radiusCells: 1,
                mode: "set",
                target: "blocked",
                heightLevel: null,
                waterHeightLevel: null,
                areaId: null,
                cost: null,
                blocked: true,
                water: null,
                ramp: null);
            RequireOk("paint blocker", _runtime.PaintTerrain(_engine, 30, 27, 1));
            _stageDetail = "small grid edited: brush height/water/blocked cells pushed into Raylib heightmap";
        });

        RunStep(3, rel, 150, "bake CDT navmesh and query nav path", () =>
        {
            RequireOk("estimate dirty+n", _runtime.EstimateNavBake(_engine, "dirty+n", true));
            RequireOk("rebake full CDT", _runtime.RebuildNav(_engine, "full", 64, true, false));
            RequireOk("path options", _runtime.SetPathOptions(_engine, "Small", 0, 512));
            RequireOk("query nav path", _runtime.QueryPath(_engine, 1200, 1200, 5600, 5600));
            RefreshNavStats();
            if (_runtime.Nav.PathStatus != NavPathStatus.Ok || _runtime.Nav.PathXcm.Length <= 1)
            {
                throw new InvalidOperationException($"nav path failed: {_runtime.Nav.PathStatus} points={_runtime.Nav.PathXcm.Length}");
            }
        });

        RunStep(4, rel, 340, "edit graph and query transport route", () =>
        {
            _runtime.SetTool("transport");
            _runtime.Transport.SetMode("node");
            RequireOk("transport add node A", _runtime.Transport.AddNode(
                _engine,
                id: "uat_port_a",
                kind: "Port",
                tags: "Transport.Area.Water",
                xCm: 9000,
                yCm: 9400,
                hasPickedWorld: false,
                pickedWorld: default));
            RequireOk("transport add node B", _runtime.Transport.AddNode(
                _engine,
                id: "uat_port_b",
                kind: "Port",
                tags: "Transport.Area.Water",
                xCm: 16000,
                yCm: 9400,
                hasPickedWorld: false,
                pickedWorld: default));
            RequireOk("transport begin segment", _runtime.Transport.BeginSegment(_engine));
            RequireOk("transport append A", _runtime.Transport.AppendSegmentPoint(_engine, false, 9000, 9400, false, default));
            RequireOk("transport append middle", _runtime.Transport.AppendSegmentPoint(_engine, false, 12800, 10300, false, default));
            RequireOk("transport append B", _runtime.Transport.AppendSegmentPoint(_engine, false, 16000, 9400, false, default));
            RequireOk("transport commit segment", _runtime.Transport.CommitSegment(
                _engine,
                id: "uat_shallow_lane_recording",
                areaId: "Transport.Area.Shallow",
                tags: "Transport.Area.Water,Transport.Area.River",
                direction: "Bidirectional",
                flowDirection: "None",
                depthCm: 140,
                widthCm: 420,
                laneCount: 1,
                visualWidthMeters: 2.6f,
                sampleStepCm: 400));
            RequireOk("transport query route", _runtime.Transport.QueryRoute(
                _engine,
                agentTypeId: "Transport.ShallowBoat",
                startXcm: 9000,
                startYcm: 9400,
                goalXcm: 16000,
                goalYcm: 9400,
                hasPickedWorld: false,
                pickedWorld: default));
            RefreshTransportStats();
            if (!string.Equals(_runtime.Transport.RouteStatus.ToString(), "Found", StringComparison.Ordinal) ||
                _runtime.Transport.RoutePathXcm.Length <= 1)
            {
                throw new InvalidOperationException(
                    $"transport route failed: {_runtime.Transport.RouteStatus} points={_runtime.Transport.RoutePathXcm.Length}");
            }
        });

        RunStep(5, rel, 610, "create medium hex map and huge preview", () =>
        {
            RequireOk("preview medium hex", _runtime.PreviewBoardAllocation("createMap", 512f, 512f, 100));
            RequireOk("create medium hex map", _runtime.CreateMap(
                _engine,
                mapId: _createdHexMapId,
                boardName: "hex_main",
                topology: "HexGrid",
                widthMeters: 512f,
                heightMeters: 512f,
                cellSizeCm: 100,
                hexEdgeLengthCm: 350,
                navigationEnabled: false,
                loadAfterCreate: false));
            RequireOk("preview huge grid map", _runtime.PreviewBoardAllocation("addBoard", 40960f, 40960f, 100));
            RefreshMapStats();
        });

        RunStep(6, rel, 720, "final nav and route validation", () =>
        {
            RefreshNavStats();
            RefreshTransportStats();
            RefreshMapStats();
            if (_failureCount > 0)
            {
                throw new InvalidOperationException("one or more UAT steps failed; see report");
            }
        });
    }

    private void RunStep(int step, int rel, int triggerRel, string name, Action action, Action<string>? onFailure = null)
    {
        if (_steps[step] || rel < triggerRel)
        {
            return;
        }

        _steps[step] = true;
        try
        {
            action();
            _lastAction = $"{name}: ok";
            Log(_lastAction);
        }
        catch (Exception ex)
        {
            _failureCount++;
            _lastAction = $"{name}: failed";
            Log($"{_lastAction}: {ex.Message}");
            onFailure?.Invoke(ex.Message);
        }
    }

    private void UpdateStageText(int rel)
    {
        if (rel < 60)
        {
            _stageTitle = "CEF Button Probe";
            _stageDetail = "synthetic mouse click targets the visible Paint button in the real CEF panel";
            return;
        }

        if (rel < 150)
        {
            _stageTitle = "Small Grid Heightmap";
            _stageDetail = "brush edits height, water, area and blocked flags on the focused grid terrain";
            return;
        }

        if (rel < 340)
        {
            _stageTitle = "Navmesh And Agent";
            _stageDetail = "CDT navmesh is baked, triangles are validated, and the green agent follows the path";
            return;
        }

        if (rel < 610)
        {
            _stageTitle = "Graph Edit And Route";
            _stageDetail = "transport graph nodes/segment are authored, baked, and the yellow agent follows the route";
            return;
        }

        if (rel < 720)
        {
            _stageTitle = "Medium Hex And Huge Preview";
            _stageDetail = "medium HexGrid map is created; huge grid allocation preview is shown in the panel";
            return;
        }

        _stageTitle = "Final Streaming Sweep";
        _stageDetail = "camera pans smoothly while grid, heightmap, navmesh, path and graph overlays stay visible";
    }

    private void ApplyCamera(int rel)
    {
        if (_engine.GameSession?.Camera == null)
        {
            return;
        }

        Vector2 target;
        float yaw = 180f;
        float pitch = 50f;
        float distanceCm = 18000f;
        if (rel < 150)
        {
            float t = Clamp01(rel / 150f);
            target = Vector2.Lerp(new Vector2(12800f, 12800f), new Vector2(3300f, 3000f), Smooth(t));
            distanceCm = 15000f;
        }
        else if (rel < 340)
        {
            float t = Clamp01((rel - 150) / 190f);
            target = Vector2.Lerp(new Vector2(2500f, 2500f), new Vector2(5600f, 5600f), Smooth(t));
            yaw = 178f + MathF.Sin(t * MathF.PI) * 18f;
            distanceCm = 12000f;
        }
        else if (rel < 610)
        {
            float t = Clamp01((rel - 340) / 270f);
            target = Vector2.Lerp(new Vector2(9000f, 9400f), new Vector2(16000f, 9400f), Smooth(t));
            yaw = 170f + t * 34f;
            distanceCm = 19000f;
        }
        else
        {
            float t = Clamp01((rel - 610) / 210f);
            target = new Vector2(
                12800f + MathF.Sin(t * MathF.PI * 2f) * 8800f,
                12800f + MathF.Cos(t * MathF.PI * 2f) * 6600f);
            yaw = 160f + t * 80f;
            distanceCm = 22000f;
        }

        _engine.GameSession.Camera.ApplyPose(new CameraPoseRequest
        {
            TargetCm = target,
            Yaw = yaw,
            Pitch = pitch,
            DistanceCm = distanceCm,
            FovYDeg = 55f
        });
    }

    private void UpdateAgentMarkers(int rel)
    {
        _navAgentMarker = rel >= NavAgentStartRelFrame && rel <= NavAgentEndRelFrame
            ? SamplePolyline(_runtime.Nav.PathXcm, _runtime.Nav.PathZcm, (rel - NavAgentStartRelFrame) / (float)(NavAgentEndRelFrame - NavAgentStartRelFrame))
            : null;

        _transportAgentMarker = rel >= TransportAgentStartRelFrame && rel <= TransportAgentEndRelFrame
            ? SamplePolyline(_runtime.Transport.RoutePathXcm, _runtime.Transport.RoutePathYcm, (rel - TransportAgentStartRelFrame) / (float)(TransportAgentEndRelFrame - TransportAgentStartRelFrame))
            : null;
    }

    private void RefreshNavStats()
    {
        (int tiles, int triangles, int broken) = CountNavTriangles();
        _navStats = $"nav tiles={tiles} tris={triangles} broken={broken} path={_runtime.Nav.PathStatus}/{_runtime.Nav.PathXcm.Length}";
        Log(_navStats);
        if (tiles <= 0 || triangles <= 0 || broken > 0)
        {
            throw new InvalidOperationException(_navStats);
        }
    }

    private void RefreshTransportStats()
    {
        _transportStats =
            $"graph nodes={_runtime.Transport.Asset?.Nodes.Count ?? 0} segs={_runtime.Transport.Asset?.Segments.Count ?? 0} route={_runtime.Transport.RouteStatus}/{_runtime.Transport.RoutePathXcm.Length}";
        Log(_transportStats);
    }

    private void RefreshMapStats()
    {
        BoardAllocationPreview? preview = _runtime.MapLifecycle.AddBoardPreview;
        _mapStats = preview == null
            ? $"hex={_createdHexMapId}"
            : $"hex={_createdHexMapId} huge={preview.WidthMacroTiles}x{preview.HeightMacroTiles} macro";
        Log(_mapStats);
    }

    private (int Tiles, int Triangles, int Broken) CountNavTriangles()
    {
        NavQueryServiceRegistry? registry = _engine.GetService(CoreServiceKeys.NavQueryServices);
        if (registry == null)
        {
            return (0, 0, 0);
        }

        int tiles = 0;
        int triangles = 0;
        int broken = 0;
        IReadOnlyList<KeyValuePair<NavQueryServiceKey, NavTileStore>> stores = registry.SnapshotStores();
        for (int storeIndex = 0; storeIndex < stores.Count; storeIndex++)
        {
            NavTile[] snapshot = stores[storeIndex].Value.SnapshotLoadedTiles();
            for (int tileIndex = 0; tileIndex < snapshot.Length; tileIndex++)
            {
                NavTile tile = snapshot[tileIndex];
                tiles++;
                for (int i = 0; i < tile.TriangleCount; i++)
                {
                    triangles++;
                    int a = tile.TriA[i];
                    int b = tile.TriB[i];
                    int c = tile.TriC[i];
                    if ((uint)a >= (uint)tile.VertexCount ||
                        (uint)b >= (uint)tile.VertexCount ||
                        (uint)c >= (uint)tile.VertexCount)
                    {
                        broken++;
                        continue;
                    }

                    long area2 =
                        ((long)tile.VertexXcm[b] - tile.VertexXcm[a]) * (tile.VertexZcm[c] - tile.VertexZcm[a]) -
                        ((long)tile.VertexZcm[b] - tile.VertexZcm[a]) * (tile.VertexXcm[c] - tile.VertexXcm[a]);
                    if (area2 == 0)
                    {
                        broken++;
                    }
                }
            }
        }

        return (tiles, triangles, broken);
    }

    private static void RequireOk(string label, WebUiCommandResult result)
    {
        if (!result.Success)
        {
            throw new InvalidOperationException($"{label}: {result.ErrorCode} {result.Message}");
        }
    }

    private void Log(string message)
    {
        if (string.IsNullOrWhiteSpace(_reportPath))
        {
            return;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(_reportPath))!);
        File.AppendAllText(_reportPath, $"{DateTime.UtcNow:O} {message}{Environment.NewLine}");
    }

    private static void DrawAgentMarker(GroundOverlayBuffer ground, WorldCmInt2 world, Vector4 color, float radiusM)
    {
        ground.TryAdd(new GroundOverlayItem
        {
            Shape = GroundOverlayShape.Ring,
            Center = WorldUnits.WorldCmToVisualMeters(world, 0.52f),
            Radius = radiusM,
            InnerRadius = radiusM * 0.55f,
            FillColor = new Vector4(color.X, color.Y, color.Z, 0.22f),
            BorderColor = color,
            BorderWidth = 0.08f
        });
    }

    private static WorldCmInt2? SamplePolyline(int[] xs, int[] ys, float t)
    {
        int count = Math.Min(xs.Length, ys.Length);
        if (count == 0)
        {
            return null;
        }

        if (count == 1)
        {
            return new WorldCmInt2(xs[0], ys[0]);
        }

        float scaled = Clamp01(t) * (count - 1);
        int index = Math.Min(count - 2, (int)MathF.Floor(scaled));
        float local = scaled - index;
        int x = (int)MathF.Round(xs[index] + ((xs[index + 1] - xs[index]) * local));
        int y = (int)MathF.Round(ys[index] + ((ys[index + 1] - ys[index]) * local));
        return new WorldCmInt2(x, y);
    }

    private static float Smooth(float t)
    {
        t = Clamp01(t);
        return t * t * (3f - (2f * t));
    }

    private static float Clamp01(float value)
        => Math.Clamp(value, 0f, 1f);

    private static int ReadEnvIntOrDefault(string key, int defaultValue)
        => int.TryParse(Environment.GetEnvironmentVariable(key), out int value) ? value : defaultValue;

    private static bool ReadEnvBoolOrDefault(string key, bool defaultValue)
    {
        string? value = Environment.GetEnvironmentVariable(key);
        if (string.IsNullOrWhiteSpace(value))
        {
            return defaultValue;
        }

        return value.Equals("1", StringComparison.OrdinalIgnoreCase) ||
               value.Equals("true", StringComparison.OrdinalIgnoreCase) ||
               value.Equals("yes", StringComparison.OrdinalIgnoreCase) ||
               value.Equals("on", StringComparison.OrdinalIgnoreCase);
    }
}
