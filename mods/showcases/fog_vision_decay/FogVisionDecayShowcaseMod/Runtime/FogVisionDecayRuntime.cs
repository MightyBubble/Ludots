using System;
using System.Numerics;
using System.Threading.Tasks;
using Arch.Core;
using FogVisionDecayShowcaseMod.Input;
using FogVisionDecayShowcaseMod.UI;
using Ludots.Core.Components;
using Ludots.Core.Engine;
using Ludots.Core.Knowledge;
using Ludots.Core.Modding;
using Ludots.Core.Presentation.Minimap;
using Ludots.Core.Scripting;
using Ludots.UI;

namespace FogVisionDecayShowcaseMod.Runtime;

public sealed class FogVisionDecayShowcaseRuntime
{
    private readonly FogVisionDecayPanelController _panelController;
    private FogVisionDecayConfig? _config;
    private KnowledgeProjectionStore _knowledge = new(initialCapacity: 4, KnowledgeProjectionMaintenancePolicy.Manual);
    private KnowledgeProjectionResolver _resolver;
    private Entity _viewer;
    private Entity _source;
    private Entity[] _targets = Array.Empty<Entity>();
    private bool[] _liveNow = Array.Empty<bool>();
    private bool[] _livePrevious = Array.Empty<bool>();
    private bool[] _seenEver = Array.Empty<bool>();
    private Entity[] _copyTargets = Array.Empty<Entity>();
    private KnowledgeDisclosureRecord[] _copyRecords = Array.Empty<KnowledgeDisclosureRecord>();
    private MinimapKnowledgeViewerProvider? _previousMinimapKnowledgeViewerProvider;
    private bool _minimapKnowledgeViewerProviderInstalled;
    private bool _hasPreviousMinimapKnowledgeViewerProvider;
    private int _tick;
    private int _liveCount;
    private int _knownCount;
    private int _expiredCount;
    private int _seenEverCount;
    private int _lastExpiredCount;
    private int _lastCompactedCount;
    private long _lastFrameAllocatedBytes;
    private bool _patrolEnabled = true;
    private bool _scenarioReady;
    private string _status = "Load the fog vision decay showcase map.";

    public FogVisionDecayShowcaseRuntime()
    {
        _resolver = new KnowledgeProjectionResolver(_knowledge);
        _panelController = new FogVisionDecayPanelController(this);
    }

    public FogVisionDecaySnapshot Snapshot => BuildSnapshot();

    public Task HandleMapFocusedAsync(ScriptContext context)
    {
        GameEngine? engine = context.GetEngine();
        if (engine == null)
        {
            return Task.CompletedTask;
        }

        FogVisionDecayConfig config = EnsureConfig(engine);
        if (!FogVisionDecayIds.IsShowcaseMap(engine.CurrentMapSession?.MapId.Value) &&
            !FogVisionDecayIds.IsShowcaseMap(context.Get(CoreServiceKeys.MapId).Value))
        {
            ClearPanel(engine);
            return Task.CompletedTask;
        }

        ActivateInputContext(engine.GetService(CoreServiceKeys.InputHandler));
        EnsureScenario(engine, config);
        RefreshMinimap(engine);
        RefreshPanel(engine);
        return Task.CompletedTask;
    }

    public Task HandleMapUnloadedAsync(ScriptContext context)
    {
        GameEngine? engine = context.GetEngine();
        if (engine == null)
        {
            return Task.CompletedTask;
        }

        if (_config != null && FogVisionDecayIds.IsShowcaseMap(context.Get(CoreServiceKeys.MapId).Value))
        {
            DeactivateInputContext(engine.GetService(CoreServiceKeys.InputHandler));
            ClearPanel(engine);
            RestoreMinimapKnowledgeViewerProvider(engine);
            ResetScenario();
        }

        return Task.CompletedTask;
    }

    public void Advance(GameEngine engine)
    {
        if (!FogVisionDecayIds.IsShowcaseMap(engine.CurrentMapSession?.MapId.Value))
        {
            return;
        }

        FogVisionDecayConfig config = EnsureConfig(engine);
        EnsureScenario(engine, config);
        if (_patrolEnabled)
        {
            AdvanceSimulationOnly();
        }

        _status = _patrolEnabled
            ? $"Scout sweep {_tick:000}: live {_liveCount}, known {_knownCount}, expired {_expiredCount}."
            : $"Paused at sweep {_tick:000}: press N for one patrol step.";
        RefreshMinimap(engine);
    }

    public void AdvanceSimulationOnly()
    {
        if (!_scenarioReady || _config == null)
        {
            return;
        }

        long before = GC.GetAllocatedBytesForCurrentThread();
        _tick++;
        AdvanceKnowledgeCore(_config);
        _lastFrameAllocatedBytes = GC.GetAllocatedBytesForCurrentThread() - before;
    }

    public void ProbeKnowledgeQueryHotPathOnly()
    {
        if (!_scenarioReady)
        {
            return;
        }

        long before = GC.GetAllocatedBytesForCurrentThread();
        RefreshCounts(_tick);
        _lastFrameAllocatedBytes = GC.GetAllocatedBytesForCurrentThread() - before;
    }

    public void TogglePatrol()
    {
        _patrolEnabled = !_patrolEnabled;
        _status = _patrolEnabled ? "Scout patrol resumed." : "Scout patrol paused.";
    }

    public void StepPatrol(GameEngine engine)
    {
        FogVisionDecayConfig config = EnsureConfig(engine);
        EnsureScenario(engine, config);
        AdvanceSimulationOnly();
        _status = $"Manual sweep {_tick:000}: live {_liveCount}, known {_knownCount}, expired {_expiredCount}.";
        RefreshMinimap(engine);
    }

    public void Compact(GameEngine engine)
    {
        _lastCompactedCount = _knowledge.Compact();
        RefreshCounts(_tick);
        _status = $"Manual compact reclaimed {_lastCompactedCount} inactive knowledge records.";
        RefreshMinimap(engine);
    }

    public void RefreshPanel(GameEngine engine)
    {
        if (FogVisionDecayIds.IsShowcaseMap(engine.CurrentMapSession?.MapId.Value))
        {
            if (engine.GetService(CoreServiceKeys.UIRoot) is UIRoot root)
            {
                _panelController.MountOrRefresh(root, engine);
            }
        }
        else
        {
            ClearPanel(engine);
        }
    }

    public void RefreshMinimap(GameEngine engine)
    {
        RefreshMarkers(engine);
        if (engine.GetService(CoreServiceKeys.MinimapRuntime) is not MinimapRuntime minimap ||
            engine.GetService(CoreServiceKeys.MinimapMarkerBuffer) is not MinimapMarkerBuffer markers ||
            engine.GetService(CoreServiceKeys.MinimapScreenMarkerBuffer) is not MinimapScreenMarkerBuffer screenMarkers)
        {
            return;
        }

        minimap.Visible = true;
        minimap.UseRtsFullMapPreset();
        minimap.Refresh(engine, markers, screenMarkers);
        if (engine.GetService(CoreServiceKeys.ScreenOverlayBuffer) is Ludots.Core.Presentation.Hud.ScreenOverlayBuffer overlay)
        {
            minimap.Render(overlay);
        }
    }

    internal FogVisionDecayPanelState BuildPanelState()
    {
        FogVisionDecaySnapshot snapshot = BuildSnapshot();
        return new FogVisionDecayPanelState(
            Header: _config?.Header ?? "Fog Vision Decay",
            Summary: _config?.Summary ?? string.Empty,
            Controls: _config?.Controls ?? string.Empty,
            StatusLine: snapshot.Status,
            Metrics: new[]
            {
                $"Targets {snapshot.TargetCount:N0} | seen {snapshot.SeenEverCount:N0}",
                $"Live {snapshot.LiveCount:N0} | known ghosts {snapshot.KnownCount:N0} | expired {snapshot.ExpiredCount:N0}",
                $"Knowledge active {snapshot.ActiveRecordCount:N0} | physical {snapshot.PhysicalRecordCount:N0}",
                $"Capacity {snapshot.RecordCapacity:N0} / ceiling {snapshot.ConfiguredCapacityCeiling:N0}",
                $"Expired {snapshot.LastExpiredCount:N0} | compacted {snapshot.LastCompactedCount:N0}",
                $"Last frame alloc bytes {snapshot.LastFrameAllocatedBytes:N0}",
            },
            ContactLines: new[]
            {
                snapshot.PatrolLabel,
                _patrolEnabled ? "Patrol ON" : "Patrol PAUSED",
                "Minimap markers use the production KnowledgeProjectionResolver.",
            });
    }

    private FogVisionDecayConfig EnsureConfig(GameEngine engine)
    {
        if (_config != null)
        {
            return _config;
        }

        if (engine.ConfigPipeline == null)
        {
            throw new InvalidOperationException("Fog vision decay showcase requires ConfigPipeline before loading config.");
        }

        _config = new FogVisionDecayConfigLoader(engine.ConfigPipeline).Load(
            engine.ConfigCatalog,
            engine.ConfigConflictReport);
        return _config;
    }

    private void EnsureScenario(GameEngine engine, FogVisionDecayConfig config)
    {
        if (_scenarioReady)
        {
            return;
        }

        BuildScenarioEntities(engine, config);
        _knowledge = new KnowledgeProjectionStore(
            Math.Max(4, config.LiveWindowCount * 2),
            config.CreateMaintenancePolicy());
        _resolver = new KnowledgeProjectionResolver(_knowledge);
        engine.SetService(CoreServiceKeys.KnowledgeProjectionStore, _knowledge);
        engine.SetService(CoreServiceKeys.KnowledgeProjectionResolver, _resolver);
        _hasPreviousMinimapKnowledgeViewerProvider = engine.TryGetService(
            CoreServiceKeys.MinimapKnowledgeViewerProvider,
            out _previousMinimapKnowledgeViewerProvider);
        engine.SetService(CoreServiceKeys.MinimapKnowledgeViewerProvider, TryResolveMinimapKnowledgeViewer);
        _minimapKnowledgeViewerProviderInstalled = true;
        if (engine.GetService(CoreServiceKeys.MinimapRuntime) is MinimapRuntime minimap)
        {
            minimap.Visible = true;
            minimap.UseRtsFullMapPreset();
        }

        _scenarioReady = true;
        _status = "Scout patrol ready.";
    }

    private void BuildScenarioEntities(GameEngine engine, FogVisionDecayConfig config)
    {
        ResetScenario();
        World world = engine.World;
        _viewer = world.Create();
        _source = world.Create();
        world.Add(_viewer, new Name { Value = "Fog Viewer" });
        world.Add(_source, new Name { Value = "Scout Patrol" });
        world.Add(_viewer, new MapEntity { MapId = FogVisionDecayIds.ShowcaseMap });
        world.Add(_source, new MapEntity { MapId = FogVisionDecayIds.ShowcaseMap });

        _targets = new Entity[config.TargetCount];
        _liveNow = new bool[config.TargetCount];
        _livePrevious = new bool[config.TargetCount];
        _seenEver = new bool[config.TargetCount];
        _copyTargets = new Entity[config.TargetCount];
        _copyRecords = new KnowledgeDisclosureRecord[config.TargetCount];

        for (int i = 0; i < _targets.Length; i++)
        {
            Entity target = world.Create();
            world.Add(target, new Name { Value = $"Fog Contact {i:000}" });
            world.Add(target, new MapEntity { MapId = FogVisionDecayIds.ShowcaseMap });
            _targets[i] = target;
        }
    }

    private void AdvanceKnowledgeCore(FogVisionDecayConfig config)
    {
        Array.Clear(_liveNow, 0, _liveNow.Length);
        int start = (_tick * config.PatrolStride) % config.TargetCount;
        for (int i = 0; i < config.LiveWindowCount; i++)
        {
            int targetIndex = (start + i) % config.TargetCount;
            _liveNow[targetIndex] = true;
        }

        for (int i = 0; i < _targets.Length; i++)
        {
            if (_liveNow[i])
            {
                _seenEver[i] = true;
                UpsertKnowledge(i, KnowledgePresence.LiveVisible, KnowledgePositionAccess.Live, _tick + config.LiveExpiryOffsetTicks, config.ConfidencePermille);
            }
            else if (_livePrevious[i])
            {
                UpsertKnowledge(i, KnowledgePresence.Known, KnowledgePositionAccess.LastKnown, _tick + config.KnownTtlTicks, config.ConfidencePermille);
            }
        }

        for (int i = 0; i < _liveNow.Length; i++)
        {
            _livePrevious[i] = _liveNow[i];
        }

        KnowledgeProjectionMaintenanceResult result = _knowledge.RunMaintenance(_tick);
        _lastExpiredCount = result.ExpiredCount;
        _lastCompactedCount = result.CompactedCount;
        RefreshCounts(_tick);
    }

    private void UpsertKnowledge(
        int targetIndex,
        KnowledgePresence presence,
        KnowledgePositionAccess position,
        int expiryTick,
        int confidencePermille)
    {
        var empty = default(KnowledgeIdMask256);
        _knowledge.Upsert(
            _viewer,
            _targets[targetIndex],
            new KnowledgeDisclosureRecord(
                presence,
                position,
                empty,
                empty,
                empty,
                _viewer,
                _tick,
                expiryTick,
                confidencePermille,
                revision: 0));
    }

    private void RefreshCounts(int currentTick)
    {
        _liveCount = 0;
        _knownCount = 0;
        _seenEverCount = 0;
        int copied = _knowledge.CopyRecords(_viewer, currentTick, _copyTargets, _copyRecords);
        for (int i = 0; i < copied; i++)
        {
            if (_copyRecords[i].Presence == KnowledgePresence.LiveVisible)
            {
                _liveCount++;
            }
            else
            {
                _knownCount++;
            }
        }

        for (int i = 0; i < _seenEver.Length; i++)
        {
            if (_seenEver[i])
            {
                _seenEverCount++;
            }
        }

        _expiredCount = Math.Max(0, _seenEverCount - copied);
    }

    private void RefreshMarkers(GameEngine engine)
    {
        if (_config == null ||
            engine.GetService(CoreServiceKeys.MinimapMarkerBuffer) is not MinimapMarkerBuffer markers)
        {
            return;
        }

        markers.BeginFrame();
        var color = new Vector4(0.16f, 0.72f, 1f, 1f);
        int columns = Math.Max(1, _config.MarkerColumns);
        for (int i = 0; i < _targets.Length; i++)
        {
            int column = i % columns;
            int row = i / columns;
            float x = _config.OriginXCm + (column * _config.MarkerSpacingCm);
            float y = _config.OriginYCm + (row * _config.MarkerSpacingCm);
            markers.TryAdd(40_000 + i, _targets[i], x, y, in color, 7f);
        }
    }

    private void ClearPanel(GameEngine engine)
    {
        if (engine.GetService(CoreServiceKeys.UIRoot) is UIRoot root)
        {
            _panelController.ClearIfOwned(root);
        }
    }

    private void ActivateInputContext(Ludots.Core.Input.Runtime.PlayerInputHandler? input)
    {
        if (input == null || !input.HasContext(FogVisionDecayInputContexts.Showcase))
        {
            return;
        }

        input.PushContext(FogVisionDecayInputContexts.Showcase);
    }

    private void DeactivateInputContext(Ludots.Core.Input.Runtime.PlayerInputHandler? input)
    {
        input?.PopContext(FogVisionDecayInputContexts.Showcase);
    }

    private bool TryResolveMinimapKnowledgeViewer(GameEngine engine, out Entity viewer)
    {
        viewer = _viewer;
        return viewer != Entity.Null && engine.World.IsAlive(viewer);
    }

    private void ResetScenario()
    {
        _scenarioReady = false;
        _tick = 0;
        _liveCount = 0;
        _knownCount = 0;
        _expiredCount = 0;
        _seenEverCount = 0;
        _lastExpiredCount = 0;
        _lastCompactedCount = 0;
        _lastFrameAllocatedBytes = 0;
        _patrolEnabled = true;
        _viewer = Entity.Null;
        _source = Entity.Null;
        _targets = Array.Empty<Entity>();
        _liveNow = Array.Empty<bool>();
        _livePrevious = Array.Empty<bool>();
        _seenEver = Array.Empty<bool>();
        _copyTargets = Array.Empty<Entity>();
        _copyRecords = Array.Empty<KnowledgeDisclosureRecord>();
    }

    private void RestoreMinimapKnowledgeViewerProvider(GameEngine engine)
    {
        if (!_minimapKnowledgeViewerProviderInstalled)
        {
            return;
        }

        if (_hasPreviousMinimapKnowledgeViewerProvider && _previousMinimapKnowledgeViewerProvider != null)
        {
            engine.SetService(CoreServiceKeys.MinimapKnowledgeViewerProvider, _previousMinimapKnowledgeViewerProvider);
        }
        else
        {
            engine.RemoveService(CoreServiceKeys.MinimapKnowledgeViewerProvider);
        }

        _minimapKnowledgeViewerProviderInstalled = false;
        _hasPreviousMinimapKnowledgeViewerProvider = false;
        _previousMinimapKnowledgeViewerProvider = null;
    }

    private FogVisionDecaySnapshot BuildSnapshot()
    {
        FogVisionDecayConfig? config = _config;
        return new FogVisionDecaySnapshot(
            Tick: _tick,
            PatrolEnabled: _patrolEnabled,
            TargetCount: config?.TargetCount ?? 0,
            LiveCount: _liveCount,
            KnownCount: _knownCount,
            ExpiredCount: _expiredCount,
            SeenEverCount: _seenEverCount,
            ActiveRecordCount: _knowledge.RecordCount,
            PhysicalRecordCount: _knowledge.PhysicalRecordCount,
            RecordCapacity: _knowledge.RecordCapacity,
            ConfiguredCapacityCeiling: config?.CapacityCeiling ?? 0,
            LastExpiredCount: _lastExpiredCount,
            LastCompactedCount: _lastCompactedCount,
            LastFrameAllocatedBytes: _lastFrameAllocatedBytes,
            PatrolLabel: config == null ? "No patrol configured." : $"Sweep {_tick:000} stride {config.PatrolStride} TTL {config.KnownTtlTicks}",
            Status: _status);
    }
}
