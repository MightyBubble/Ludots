using Ludots.Core.CrowdSimulation.Movement;
using Ludots.Core.CrowdSimulation.Presentation;
using Ludots.Core.Presentation.Hud;

namespace CrowdSimulationUiMod.UI;

/// <summary>各面板的采样与写入。读数和上次一样就整段跳过。</summary>
internal sealed partial class CrowdHudPage
{
    private const int SparkSeries = 6;
    private const int FpsRing = 0;
    private const int SimRing = 1;
    private const int AvoidRing = 2;
    private const int HeadingRing = 3;
    private const int FlipRing = 4;
    private const int HpaRing = 5;

    private CrowdPathHud _path;
    private CrowdDeployHud _deploy;
    private CrowdPlayerHud _player;
    private CrowdInspectReadout _inspect;
    private CrowdNavReadout _nav;
    private bool _pathReady;
    private bool _deployReady;
    private bool _playerReady;
    private bool _inspectReady;
    private bool _navReady;
    private bool _faultShown;
    private string _faultLine = "";
    private double _faultTimeoutMs = double.NaN;
    private readonly int[] _units = new int[MaxPlayers + 1];
    private readonly int[] _unitsShown = new int[MaxPlayers + 1];
    private readonly int[] _typeUnits = new int[MaxUnitTypes];
    private readonly int[] _typeUnitsShown = new int[MaxUnitTypes];
    private bool _typeUnitsReady;
    private CrowdHudCatalog? _catalog;

    private double[][] _rings = null!;
    private int _histCount;
    private int _histHead;
    private bool _heapSeen;
    private long _heapBytes;
    private long _heapAt;
    private float _uiRedrawPeakMs;

    private void SamplePath()
    {
        var next = _path;
        if (!_feed.ReadPath(ref next)) return;
        if (_pathReady && next == _path) return;
        _path = next;
        _pathReady = true;
        CrowdHudFormat.PathDetail(W, in next);
        Flush(PathDetail);
        CrowdHudFormat.PathKeys(W, _feed.Text, in next);
        Flush(PathKeys);
    }

    private void SampleDeploy()
    {
        SyncCatalog();
        var next = _deploy;
        if (!_feed.ReadDeploy(ref next)) return;
        if (_deployReady && next == _deploy) return;
        _deploy = next;
        _deployReady = true;
        WriteDeploy(in next);
        WriteToolHint();
    }

    private void SamplePlayer()
    {
        var next = _player;
        if (!_feed.ReadPlayer(ref next, _units)) return;
        bool countsChanged = !_units.AsSpan().SequenceEqual(_unitsShown);
        if (_dvTab == DvTab.Layers) SampleTypeCounts();
        if (_playerReady && next == _player && !countsChanged)
        {
            AgeNotices(in next);
            return;
        }

        if (_playerReady && next.LocalPlayer != _player.LocalPlayer) ClearNotices();
        bool rebuildPlayers = !_playerReady || next.PlayerCount != _player.PlayerCount;
        _player = next;
        _units.AsSpan().CopyTo(_unitsShown);
        _playerReady = true;
        if (rebuildPlayers) BuildPlayers(next.PlayerCount);
        WritePlayer(in next);
        AgeNotices(in next);
        WriteToolHint();
    }

    private void SampleTypeCounts()
    {
        if (_catalog == null || !_feed.ReadUnitTypeCounts(_typeUnits)) return;
        if (_typeUnitsReady && _typeUnits.AsSpan().SequenceEqual(_typeUnitsShown)) return;
        _typeUnits.AsSpan().CopyTo(_typeUnitsShown);
        _typeUnitsReady = true;
        int n = Math.Min(_catalog.UnitTypes.Count, MaxUnitTypes);
        for (int t = 0; t < n; t++) Put(_typeCounts[t], CrowdHudFormat.Int(_typeUnits[t]));
    }

    private void SampleInspect()
    {
        var next = _inspect;
        if (!_feed.ReadInspect(ref next)) return;
        if (_inspectReady && next == _inspect) return;
        _inspect = next;
        _inspectReady = true;
        WriteInspect(in next);
    }

    private void SampleFault()
    {
        bool faulted = _feed.ReadPathFault(out string line, out double timeoutMs);
        if (faulted == _faultShown
            && (!faulted || (string.Equals(line, _faultLine, StringComparison.Ordinal) && timeoutMs.Equals(_faultTimeoutMs))))
            return;
        _faultShown = faulted;
        _faultLine = line;
        _faultTimeoutMs = timeoutMs;
        Mark(_fault.Show(faulted));
        if (faulted) CrowdHudFormat.Fault(W, line, timeoutMs);
        Flush(Fault);
    }

    private void SampleNav()
    {
        var next = _nav;
        if (!_feed.ReadNav(ref next)) return;
        if (_navReady && next == _nav) return;
        _nav = next;
        _navReady = true;
        WriteNav(in next);
    }

    private void SampleTelemetry(PresentationTimingDiagnostics? timing, double composeMs)
    {
        var r = default(CrowdTelemetryReadout);
        if (!_feed.ReadTelemetry(ref r)) return;
        double frameMs = double.NaN;
        double renderMs = double.NaN;
        if (timing != null)
        {
            float frame = timing.LastWallFrameMs > 0f ? timing.LastWallFrameMs : timing.LastFrameMs;
            if (frame > 0f)
            {
                frameMs = frame;
                renderMs = Math.Max(0d, frame - timing.LastTotalTickMs);
            }
        }

        double fps = double.IsNaN(frameMs) ? double.NaN : 1000d / frameMs;
        double uiPeak = timing != null ? _uiRedrawPeakMs : double.NaN;
        _uiRedrawPeakMs = 0f;
        double heapPerSec = SampleHeap();
        PushHistory(fps, in r);
        WriteTelemetry(in r, fps, frameMs, renderMs, uiPeak, composeMs, heapPerSec);
    }

    private void SyncCatalog()
    {
        CrowdHudCatalog? catalog = _feed.Catalog;
        if (ReferenceEquals(catalog, _catalog)) return;
        _catalog = catalog;
        _typeUnitsReady = false;
        BuildCatalogLists(catalog);
        if (catalog == null) return;
        W.Append(CrowdHudFormat.Inv, $"压测 {(int)Math.Round(catalog.UnitCapacity / 1000d)}K");
        Flush(StressLabel);
        if (_deployReady) WriteDeploy(in _deploy);
        if (_playerReady) WritePlayer(in _player);
        if (_inspectReady) WriteInspect(in _inspect);
    }

    private void WriteDeploy(in CrowdDeployHud d)
    {
        CrowdHudFormat.MouseHint(W, _feed.Text, in d);
        Flush(Hint);
        Put(Selected, d.Selected < 0 ? CrowdHudFormat.Int(double.NaN) : CrowdHudFormat.Int(d.Selected));
        Mark(_computing.Show(d.PendingNav));

        for (int i = 0; i < _tools.Length; i++) Mark(_tools[i].Set((int)d.Tool == i));
        bool spawning = d.Tool == CrowdClickTool.Spawn;
        Mark(_spawnTile.Set(spawning));
        Put(SpawnTileLabel, spawning ? "生成中" : "点地图生成");
        Mark(_structures.Show(d.Tool == CrowdClickTool.Structure));
        for (int i = 0; i < _structureChips.Count; i++) Mark(_structureChips[i].Set(i == d.TemplateIndex));
        Mark(_rebake.Show(d.Tool == CrowdClickTool.Structure || d.Tool == CrowdClickTool.Demolish));
        Mark(_rebakeEmpty.Show(!d.HasRebake));
        Mark(_rebakeRows.Show(d.HasRebake));
        if (d.HasRebake)
        {
            W.Append(CrowdHudFormat.Inv, $"{CrowdHudFormat.Int(d.RebakeTiles)} · {CrowdHudFormat.Int(d.RebakeContexts)} 个 nav 上下文");
            Flush(RebakeTiles);
            W.Append(CrowdHudFormat.Inv, $"{CrowdHudFormat.Int(d.RebakeCostOnly)} / {CrowdHudFormat.Int(d.RebakeContexts - d.RebakeCostOnly)} 个上下文");
            Flush(RebakeCost);
            W.Append(CrowdHudFormat.Inv, $"命中 {CrowdHudFormat.Int(d.RebakeHits)} · 新烘焙 {CrowdHudFormat.Int(d.RebakeMisses)}");
            Flush(RebakeCache);
            W.Append(CrowdHudFormat.Inv, $"{CrowdHudFormat.Int(d.RebakeOrders)} / {CrowdHudFormat.Int(d.RebakeEvicted)}");
            Flush(RebakeOrders);
            Put(RebakeStuck, CrowdHudFormat.Int(d.RebakeStuck));
            W.Append(CrowdHudFormat.Inv, $"{CrowdHudFormat.F1(d.RebakeMs)} ms");
            Flush(RebakeMs);
        }

        CrowdHudFormat.Validation(W, in d);
        Flush(Validation);
        Mark(_validation.Pick(ResultIndex(d.RuntimePresent && d.ValidationRan, d.RuntimePresent && d.ValidationFailed)));
        CrowdHudFormat.Fuzz(W, in d);
        Flush(FuzzLine);
        Mark(_fuzz.Pick(ResultIndex(d.RuntimePresent && d.FuzzRan, d.RuntimePresent && d.FuzzFailed)));
        CrowdHudFormat.Replay(W, in d);
        Flush(Replay);
        CrowdHudFormat.Counters(W, in d);
        Flush(Counters);
        Mark(_counterDetail.Show(d.Expanded));
        CrowdHudFormat.CounterDetail(W, in d);
        Flush(CounterDetail);

        if (d.SessionActive) W.Append(CrowdHudFormat.Inv, $"{d.FixedHz} Hz");
        else W.Append(CrowdHudFormat.Missing);
        Flush(TickRate);
        if (d.SessionActive) W.Append(CrowdHudFormat.Inv, $"{d.AvoidHz:0.##} Hz");
        else W.Append(CrowdHudFormat.Missing);
        Flush(AvoidRate);
        DrawRates(in d);
        for (int i = 0; i < _slices.Length; i++) Mark(_slices[i].Set(d.SessionActive && d.RebakeSlices == i + 1));

        Mark(_playIcon.Show(d.PlaybackHeld));
        Mark(_pauseIcon.Show(!d.PlaybackHeld));
        W.Append(CrowdHudFormat.Inv, $"{d.SpeedPermille / 1000d:0.##}x");
        Flush(Speed);
        Put(Units, d.SessionActive ? CrowdHudFormat.Int(d.Units) : CrowdHudFormat.Int(double.NaN));
    }

    private void DrawRates(in CrowdDeployHud d)
    {
        CrowdHudCatalog? c = _catalog;
        if (c == null || !d.SessionActive)
        {
            CrowdHudCanvas.DrawSlider(_tickSlider, double.NaN);
            CrowdHudCanvas.DrawSlider(_avoidSlider, double.NaN);
            return;
        }

        CrowdHudCanvas.DrawSlider(_tickSlider, 1d);
        double amax = d.FixedHz;
        CrowdHudCanvas.DrawSlider(_avoidSlider, amax > c.AvoidHzMin ? (d.AvoidHz - c.AvoidHzMin) / (amax - c.AvoidHzMin) : 0d);
    }

    private static int ResultIndex(bool ran, bool failed) => failed ? 2 : ran ? 1 : 0;

    private void WritePlayer(in CrowdPlayerHud p)
    {
        CrowdHudCatalog? c = _catalog;
        int clickIdx = ClickTemplateIndex(p.ClickTemplate);
        for (int i = 1; i <= p.PlayerCount && i < _units.Length; i++) Put(_playerCounts[i], CrowdHudFormat.Int(_unitsShown[i]));
        for (int i = 0; i < _playerChips.Count; i++) Mark(_playerChips[i].Set(i + 1 == p.LocalPlayer));
        Put(OrderMode, p.Mode switch
        {
            CrowdOrderMode.Preserve => "保持相对位置",
            CrowdOrderMode.Converge => "框内聚拢",
            CrowdOrderMode.Formation => "重新成阵",
            _ => CrowdHudFormat.Missing,
        });
        for (int i = 0; i < _formationChips.Count; i++) Mark(_formationChips[i].Set(i == p.ShapeIndex));
        Mark(_magicSwitch.Set(p.MagicBox));
        Mark(_losSwitch.Set(p.LineOfSight));
        Mark(_routeSwitch.Set(p.Route));
        Mark(_fogSwitch.Set(p.FogHide));

        for (int t = 0; t < _layerRows.Count; t++)
        {
            bool on = (p.LayerMask & (1 << t)) != 0;
            Mark(_layerRows[t].Set(!on));
            Mark(_layerEyes[t].Show(on));
            Mark(_layerEyesOff[t].Show(!on));
            Mark(_deployTypeChips[t].Set(on));
            bool spawnType = clickIdx >= 0 && c != null && (uint)clickIdx < (uint)c.ClickUnitType.Count && c.ClickUnitType[clickIdx] == t;
            Mark(_spawnTypeChips[t].Set(spawnType));
        }

        int windowLo = WindowLo(), windowHi = WindowHi();
        for (int r = 0; r < _deployRadiusChips.Count; r++)
        {
            bool inWindow = false;
            if (c != null && windowLo >= 0 && windowHi >= windowLo)
            {
                for (int t = windowLo; t <= windowHi; t++)
                {
                    if ((uint)t < (uint)c.ClickRadiusClass.Count && c.ClickRadiusClass[t] == r) inWindow = true;
                }
            }

            Mark(_deployRadiusChips[r].Set(inWindow));
            bool spawnRadius = clickIdx >= 0 && c != null && (uint)clickIdx < (uint)c.ClickRadiusClass.Count && c.ClickRadiusClass[clickIdx] == r;
            Mark(_spawnRadiusChips[r].Set(spawnRadius));
        }

        Put(DeployCount, CrowdHudFormat.Int(p.DeployCount));
        Put(SpawnCount, CrowdHudFormat.Int(p.ClickCount));
        CrowdHudCanvas.DrawSlider(_deployCountSlider, c == null ? double.NaN : Fraction(p.DeployCount, c.DeployCount));
        CrowdHudCanvas.DrawSlider(_spawnCountSlider, c == null ? double.NaN : Fraction(p.ClickCount, c.ClickCount));
        Put(SpawnHead, string.IsNullOrEmpty(p.ClickTemplate) ? CrowdHudFormat.Missing : p.ClickTemplate);
    }

    private static double Fraction(int value, CrowdHudRange range) =>
        range.Max > range.Min ? (value - range.Min) / (double)(range.Max - range.Min) : 0d;

    private void WriteToolHint()
    {
        if (!_deployReady) return;
        CrowdHudCatalog? c = _catalog;
        string? unit = null;
        int radiusCm = 0;
        int clickIndex = ClickTemplateIndex(_player.ClickTemplate);
        if (_playerReady && c != null && clickIndex >= 0
            && (uint)clickIndex < (uint)c.ClickRadiusClass.Count
            && (uint)c.ClickRadiusClass[clickIndex] < (uint)c.RadiusCm.Count)
        {
            unit = _player.ClickTemplate;
            radiusCm = c.RadiusCm[c.ClickRadiusClass[clickIndex]];
        }

        CrowdHudFormat.ToolHint(W, _feed.Text, in _deploy, _player.ClickCount, unit, radiusCm);
        Flush(ToolHint);
    }

    private void WriteInspect(in CrowdInspectReadout r)
    {
        int agentIndex = InspectAgentIndex();
        int radiusIndex = InspectRadiusIndex();
        for (int i = 0; i < _inspectLayerChips.Count; i++) Mark(_inspectLayerChips[i].Set(i == agentIndex));
        for (int i = 0; i < _inspectRadiusChips.Count; i++) Mark(_inspectRadiusChips[i].Set(i == radiusIndex));
        Put(InspectColor, CrowdHudFormat.ColorLabel(r.UnitColor));
        Put(InspectTerrain, r.TerrainTypes ? "开" : "关");
        if (r.Belief) W.Append(CrowdHudFormat.Inv, $"P{r.BeliefPlayer} · {CrowdHudFormat.Int(r.DivergentCells)} / {CrowdHudFormat.Int(r.Ghosts)}");
        else W.Append("关");
        Flush(InspectBelief);

        Mark(_overlaySwitches[0].Set(r.UnitColor == CrowdUnitColorMode.Team));
        Mark(_overlaySwitches[1].Set(r.UnitColor == CrowdUnitColorMode.Priority));
        Mark(_overlaySwitches[2].Set(r.Funnel));
        Mark(_overlaySwitches[3].Set(r.Density));
        Mark(_overlaySwitches[4].Set(r.Corridor));
        Mark(_overlaySwitches[5].Set(r.HpaGraph));
        Mark(_overlaySwitches[6].Set(r.NavPolygons));
        Mark(_overlaySwitches[7].Set(r.TileGrid));
        Mark(_overlaySwitches[8].Set(r.Flow));
        Mark(_overlaySwitches[9].Set(r.Blocked));
        Mark(_overlaySwitches[10].Set(r.Belief));
        Mark(_overlaySwitches[11].Set(r.Links));
    }

    internal void WriteNav(in CrowdNavReadout r)
    {
        if (r.HasTileCache)
        {
            W.Append(CrowdHudFormat.Inv, $"{r.TileCount}/{r.TileCapacity} · 命中 {CrowdHudFormat.Pct(r.TileHits, r.TileMisses)}");
            Flush(TileCacheHead);
            W.Append(CrowdHudFormat.Inv, $"hit {r.TileHits} · miss {r.TileMisses} · evict {r.TileEvicts}");
            Flush(TileCacheDetail);
        }
        else
        {
            Put(TileCacheHead, CrowdHudFormat.Missing);
            Put(TileCacheDetail, "");
        }

        CrowdHudCanvas.DrawRatio(_tileCacheBar, HitRatio(r.TileHits, r.TileMisses));
        if (r.HasFlowCache)
        {
            W.Append(CrowdHudFormat.Inv, $"{r.FlowCount}/{r.FlowCapacity} · 命中 {CrowdHudFormat.Pct(r.FlowHits, r.FlowMisses)}");
            Flush(FlowCacheHead);
            W.Append(CrowdHudFormat.Inv, $"hit {r.FlowHits} · miss {r.FlowMisses} · evict {r.FlowEvicts}");
            Flush(FlowCacheDetail);
        }
        else
        {
            Put(FlowCacheHead, CrowdHudFormat.Missing);
            Put(FlowCacheDetail, "");
        }

        CrowdHudCanvas.DrawRatio(_flowCacheBar, HitRatio(r.FlowHits, r.FlowMisses));
        Put(NavResident, CrowdHudFormat.Int(r.ResidentNavs));
        if (r.HasPath)
        {
            W.Append(CrowdHudFormat.Inv, $"{r.PlanRequests} / {r.PendingReplies}");
            Flush(NavRequests);
            W.Append(CrowdHudFormat.Inv, $"{CrowdHudFormat.F1(r.RoundTripLastMs)} / {CrowdHudFormat.F1(r.WorkerLastMs)} ms");
            Flush(NavRoundTrip);
        }
        else
        {
            Put(NavRequests, CrowdHudFormat.Missing);
            Put(NavRoundTrip, CrowdHudFormat.Missing);
        }

        Put(NavWaits, CrowdHudFormat.Int(r.WaitTicks));
        W.Append(CrowdHudFormat.Inv, $"{r.Tick} / {r.CommandLog}");
        Flush(NavTick);
    }

    private static double HitRatio(int hits, int misses) => hits + misses > 0 ? hits / (double)(hits + misses) : 0d;

    internal void WriteTelemetry(in CrowdTelemetryReadout r, double fps, double frameMs, double renderMs, double uiPeak, double composeMs, double heapPerSec)
    {
        CrowdHudAlerts a = _settings.Alerts;
        bool lowFps = !double.IsNaN(fps) && fps < a.LowFps;
        bool simHot = !double.IsNaN(r.SimMs) && r.SimMs > a.SimMs;
        bool avoidHot = !double.IsNaN(r.AvoidMs) && r.AvoidMs > a.AvoidMs;
        bool headingHot = r.HasHeading && r.HeadingDeg > a.HeadingDeg;
        bool flipHot = r.HasFlip && r.FlipPct > a.FlipPct;

        Alerted(Fps, FpsLow, CrowdHudFormat.Int(fps), lowFps);
        W.Append(CrowdHudFormat.Inv, $"FPS · {CrowdHudFormat.F1(frameMs)} ms");
        Flush(FrameMs);
        Alerted(SimMs, SimMsAlert, CrowdHudFormat.F2(r.SimMs), simHot);
        Alerted(AvoidMs, AvoidMsAlert, CrowdHudFormat.F2(r.AvoidMs), avoidHot);
        Put(HashMs, CrowdHudFormat.F2(r.HashMs));
        Put(RenderMs, CrowdHudFormat.F2(renderMs));
        W.Append(CrowdHudFormat.Inv, $"{CrowdHudFormat.F2(uiPeak)} / {CrowdHudFormat.F2(composeMs)}");
        Flush(UiMs);
        Put(HeapRate, CrowdHudFormat.Bytes(heapPerSec));
        Put(HeapUnit, double.IsFinite(heapPerSec) ? "/s" : "");
        Alerted(Heading, HeadingAlert, CrowdHudFormat.F2(r.HasHeading ? r.HeadingDeg : double.NaN), headingHot);
        Alerted(Flip, FlipAlert, CrowdHudFormat.F1(r.HasFlip ? r.FlipPct : double.NaN), flipHot);
        Put(FixedHz, CrowdHudFormat.Int(r.FixedHz));
        W.Append(CrowdHudFormat.Inv, $"Hz · {CrowdHudFormat.F2(r.FixedHz > 0 ? 1d / r.FixedHz : double.NaN)} s/tick");
        Flush(FixedUnit);

        Put(NavMeshMs, CrowdHudFormat.F2(r.NavMeshMs));
        Put(HpaMs, CrowdHudFormat.F2(r.HpaMs));
        Put(FlowMs, CrowdHudFormat.F1(r.FlowMs));
        Put(Extend, r.HasExtend ? CrowdHudFormat.Int(r.ExtendCount) : CrowdHudFormat.Int(double.NaN));
        Put(PlanMs, CrowdHudFormat.F1(r.PlanMs));
        Put(NavBakeMs, CrowdHudFormat.F1(r.NavBakeMs));
        Put(RebakeTelMs, CrowdHudFormat.F1(r.RebakeMs));
        W.Append(CrowdHudFormat.Inv, $"ms · {(_deploy.HasRebake ? _deploy.RebakeTiles : 0)} tiles · ×{_deploy.Rebakes}");
        Flush(RebakeTelUnit);

        int total = r.Idle + r.Moving + r.Arrived + r.Unreachable + r.Traversing;
        CrowdHudCanvas.DrawAgents(_agentsBar, r.Moving, r.Arrived, r.Unreachable, r.Idle, total);
        Put(AgentsMoving, CrowdHudFormat.Int(r.Moving));
        Put(AgentsArrived, CrowdHudFormat.Int(r.Arrived));
        Put(AgentsUnreachable, CrowdHudFormat.Int(r.Unreachable));
        Put(AgentsIdle, CrowdHudFormat.Int(r.Idle));

        Put(NavCell, CrowdHudFormat.F1(r.NavCellCm / 100d));
        W.Append(CrowdHudFormat.Inv, $"{(int)Math.Round(Math.Sqrt(r.TileCount))}²");
        Flush(Clusters);
        Put(WorldGen, CrowdHudFormat.Int(r.WorldBakeRecorded ? r.WorldBakeMs : double.NaN));
        Put(WorldGenUnit, r.WorldBakeRecorded && r.WorldCacheHits > 0 && r.WorldCacheMisses == 0 ? "ms · 缓存复用" : "ms");

        int n = _settings.History;
        int start = _histCount < n ? 0 : _histHead;
        CrowdHudCanvas.DrawSpark(_fpsSpark, _rings[FpsRing], start, _histCount, 32f, lowFps);
        CrowdHudCanvas.DrawSpark(_simSpark, _rings[SimRing], start, _histCount, 18f, simHot);
        CrowdHudCanvas.DrawSpark(_avoidSpark, _rings[AvoidRing], start, _histCount, 18f, avoidHot);
        CrowdHudCanvas.DrawSpark(_headingSpark, _rings[HeadingRing], start, _histCount, 18f, headingHot);
        CrowdHudCanvas.DrawSpark(_flipSpark, _rings[FlipRing], start, _histCount, 18f, flipHot);
        CrowdHudCanvas.DrawSpark(_hpaSpark, _rings[HpaRing], start, _histCount, 18f, false);
    }

    /// <summary>告警值写进红色那一格、常规格留空；两格挨着放，空的那格不占宽。</summary>
    private void Alerted<T>(Ludots.UI.Runtime.UiTextBinding normal, Ludots.UI.Runtime.UiTextBinding alert, T value, bool hot) where T : ISpanFormattable
    {
        Put(hot ? alert : normal, value);
        Put(hot ? normal : alert, "");
    }

    private void PushHistory(double fps, in CrowdTelemetryReadout r)
    {
        int n = _settings.History;
        _rings[FpsRing][_histHead] = Series(fps);
        _rings[SimRing][_histHead] = Series(r.SimMs);
        _rings[AvoidRing][_histHead] = Series(r.AvoidMs);
        _rings[HeadingRing][_histHead] = r.HasHeading ? r.HeadingDeg : 0;
        _rings[FlipRing][_histHead] = r.HasFlip ? r.FlipPct : 0;
        _rings[HpaRing][_histHead] = Series(r.HpaMs);
        _histHead = (_histHead + 1) % n;
        if (_histCount < n) _histCount++;
    }

    private void ResetTelemetry()
    {
        _histCount = 0;
        _histHead = 0;
        _heapSeen = false;
        _uiRedrawPeakMs = 0f;
    }

    /// <summary>仿真与呈现同进程，读的是本进程托管堆。</summary>
    private double SampleHeap()
    {
        long now = Environment.TickCount64;
        long bytes = GC.GetTotalMemory(false);
        if (!_heapSeen)
        {
            _heapSeen = true;
            _heapBytes = bytes;
            _heapAt = now;
            return double.NaN;
        }

        long dt = now - _heapAt;
        double perSec = dt > 0 ? (bytes - _heapBytes) * 1000d / dt : double.NaN;
        _heapBytes = bytes;
        _heapAt = now;
        return perSec;
    }

    private static double Series(double value) => double.IsFinite(value) ? value : 0;
}
