using System;
using System.Collections.Generic;
using System.Linq;
using Arch.Core;
using CoreInputMod.ViewMode;
using Ludots.Core.Components;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.Spawning;
using Ludots.Core.Input.Selection;
using Ludots.Core.Map;
using Ludots.Core.Mathematics;
using Ludots.Core.Mathematics.FixedPoint;
using Ludots.Core.Scripting;
using UxPrototypeMod.UI;

namespace UxPrototypeMod.Runtime;

internal sealed class UxPrototypeScenarioState
{
    private static readonly QueryDescription PrototypeEntityQuery = new QueryDescription()
        .WithAll<Name, WorldPositionCm, MapEntity>();

    private readonly Dictionary<int, ProductionLaneState> _production = new();
    private readonly List<ConstructionState> _construction = new();
    private readonly List<string> _events = new();
    private readonly Dictionary<int, List<WorldCmInt2>> _rallyPoints = new();
    private readonly Dictionary<string, float> _skillCooldowns = new(StringComparer.OrdinalIgnoreCase);

    private bool _initialized;
    private string _activeMapId = string.Empty;
    private int _calendarDay = 112;
    private float _dayProgress;
    private int _year = 206;
    private int _month = 5;
    private int _moonPhaseIndex = 2;
    private int _speedIndex = 1;
    private bool _paused;
    private bool _resourceDropdownOpen = true;
    private bool _settingsOpen;
    private bool _preferencesOpen;
    private bool _rosterCollapsed;
    private bool _gridLayout = true;
    private bool _globalExpanded = true;
    private bool _navmeshVisible = true;
    private bool _smartCastBatch = true;
    private bool _quickCastOnRelease = true;
    private bool _showTargetLines = true;
    private bool _showRangeIndicators = true;
    private bool _showTooltips = true;
    private bool _roadSnapping = true;
    private bool _obstacleFill = true;
    private bool _navmeshBakeRunning;
    private float _navmeshBakeProgress;
    private int _roadNodeCount = 6;
    private int _roadSegmentCount = 7;
    private int _obstacleCount = 3;
    private string _modeId = UxPrototypeIds.PlayModeId;
    private string _factionTab = UxPrototypeIds.FactionDiplomacy;
    private string _rosterTab = UxPrototypeIds.RosterEconomy;
    private string _globalTab = UxPrototypeIds.GlobalBuild;
    private string _objective = "Stabilize the frontier corridor, protect the central city, and keep all three production lines online.";
    private string? _activeBuildTemplateId;

    private int _food = 2180;
    private int _iron = 1240;
    private int _credits = 870;
    private int _energy = 135;

    public void EnsureInitialized(GameEngine engine, string mapId)
    {
        if (_initialized && string.Equals(_activeMapId, mapId, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        ResetForMap(mapId);
        SeedRallyPoints(engine, mapId);
        AddEvent("War council convened around the eastern pass.");
        AddEvent("Alliance envoy arrived from the southern fort.");
        AddEvent("Siege workshop reports one catapult frame ready.");
    }

    public void ResetForMap(string mapId)
    {
        _initialized = true;
        _activeMapId = mapId;
        _production.Clear();
        _construction.Clear();
        _rallyPoints.Clear();
        _events.Clear();
        _skillCooldowns.Clear();
        _calendarDay = 112;
        _dayProgress = 0.2f;
        _year = 206;
        _month = 5;
        _moonPhaseIndex = 2;
        _speedIndex = 1;
        _paused = false;
        _resourceDropdownOpen = true;
        _settingsOpen = false;
        _preferencesOpen = false;
        _rosterCollapsed = false;
        _gridLayout = true;
        _globalExpanded = true;
        _navmeshVisible = true;
        _smartCastBatch = true;
        _quickCastOnRelease = true;
        _showTargetLines = true;
        _showRangeIndicators = true;
        _showTooltips = true;
        _roadSnapping = true;
        _obstacleFill = true;
        _navmeshBakeRunning = false;
        _navmeshBakeProgress = 0f;
        _roadNodeCount = 6;
        _roadSegmentCount = 7;
        _obstacleCount = 3;
        _modeId = UxPrototypeIds.PlayModeId;
        _factionTab = UxPrototypeIds.FactionDiplomacy;
        _rosterTab = UxPrototypeIds.RosterEconomy;
        _globalTab = UxPrototypeIds.GlobalBuild;
        _objective = "Stabilize the frontier corridor, protect the central city, and keep all three production lines online.";
        _activeBuildTemplateId = null;
        _food = 2180;
        _iron = 1240;
        _credits = 870;
        _energy = 135;
    }

    public void Update(GameEngine engine, float dt)
    {
        string? mapId = engine.CurrentMapSession?.MapId.Value;
        if (!UxPrototypeIds.IsPrototypeMap(mapId))
        {
            return;
        }

        EnsureInitialized(engine, mapId!);
        float speedMultiplier = _paused ? 0f : _speedIndex switch
        {
            0 => 1f,
            1 => 2f,
            _ => 5f
        };

        if (speedMultiplier <= 0f)
        {
            return;
        }

        float scaledDt = dt * speedMultiplier;
        AdvanceCalendar(scaledDt);
        AdvanceProduction(engine, scaledDt);
        AdvanceConstruction(engine, scaledDt);
        AdvanceNavmeshBake(scaledDt);
        AdvanceEconomy(engine, scaledDt);
        AdvanceSkillCooldowns(scaledDt);
    }

    public void SwitchMode(string modeId)
    {
        _modeId = modeId;
        if (string.Equals(modeId, UxPrototypeIds.RoadEditorModeId, StringComparison.OrdinalIgnoreCase))
        {
            AddEvent("Road editor activated. Node snapping aligned to fortress routes.");
        }
        else if (string.Equals(modeId, UxPrototypeIds.ObstacleEditorModeId, StringComparison.OrdinalIgnoreCase))
        {
            AddEvent("Obstacle editor activated. Blocking polygon set ready for revision.");
        }
        else if (string.Equals(modeId, UxPrototypeIds.NavmeshModeId, StringComparison.OrdinalIgnoreCase))
        {
            AddEvent("Navmesh diagnostics opened for the tactical board.");
        }
    }

    public void TogglePause() => _paused = !_paused;
    public void SetSpeed(int speedIndex) => _speedIndex = Math.Clamp(speedIndex, 0, 2);
    public void ToggleResourceDropdown() => _resourceDropdownOpen = !_resourceDropdownOpen;
    public void ToggleSettings() => _settingsOpen = !_settingsOpen;
    public void TogglePreferences() => _preferencesOpen = !_preferencesOpen;
    public void ToggleRosterCollapsed() => _rosterCollapsed = !_rosterCollapsed;
    public void ToggleRosterLayout() => _gridLayout = !_gridLayout;
    public void ToggleGlobalExpanded() => _globalExpanded = !_globalExpanded;
    public void ToggleNavmeshVisible() => _navmeshVisible = !_navmeshVisible;
    public void ToggleSmartCastBatch() => _smartCastBatch = !_smartCastBatch;
    public void ToggleQuickCastOnRelease() => _quickCastOnRelease = !_quickCastOnRelease;
    public void ToggleTargetLines() => _showTargetLines = !_showTargetLines;
    public void ToggleRangeIndicators() => _showRangeIndicators = !_showRangeIndicators;
    public void ToggleTooltips() => _showTooltips = !_showTooltips;
    public void ToggleRoadSnapping() => _roadSnapping = !_roadSnapping;
    public void ToggleObstacleFill() => _obstacleFill = !_obstacleFill;
    public void SetFactionTab(string tab) => _factionTab = tab;
    public void SetRosterTab(string tab) => _rosterTab = tab;
    public void SetGlobalTab(string tab) => _globalTab = tab;

    public void StartNavmeshBake()
    {
        _navmeshBakeRunning = true;
        _navmeshBakeProgress = 0f;
        AddEvent("Navmesh bake queued for current editor payload.");
    }

    public void SaveLevel()
    {
        AddEvent($"Level payload saved with {_roadNodeCount} road nodes, {_roadSegmentCount} road segments, and {_obstacleCount} obstacle shapes.");
    }

    public void LoadLevel()
    {
        AddEvent("Level payload reloaded from the latest editor snapshot.");
    }

    public void AddRoadNode()
    {
        _roadNodeCount++;
        _roadSegmentCount = Math.Max(_roadSegmentCount, _roadNodeCount - 1);
        AddEvent("Road node inserted into the marching route draft.");
    }

    public void AddObstacle()
    {
        _obstacleCount++;
        AddEvent("Obstacle polygon appended to the blocking layer.");
    }

    public void QueueConstruction(GameEngine engine, string templateId)
    {
        QueueConstruction(engine, ResolveSelectedEntity(engine), templateId);
    }

    public void QueueConstruction(GameEngine engine, Entity targetEntity, string templateId)
    {
        string? selectedLabel = ResolveEntityLabel(engine, targetEntity);
        WorldCmInt2 anchor = ResolveEntityOrFallbackPosition(engine, targetEntity);
        WorldCmInt2 target = new(
            anchor.X + 450 + (_construction.Count * 150),
            anchor.Y + ((_construction.Count % 2 == 0) ? 280 : -280));

        _activeBuildTemplateId = templateId;
        _construction.Add(new ConstructionState(templateId, target, 0f, GetConstructionDuration(templateId), selectedLabel ?? "City"));
        _credits = Math.Max(0, _credits - 50);
        _iron = Math.Max(0, _iron - 35);
        AddEvent($"Construction queued for {LabelForTemplate(templateId)} near {selectedLabel ?? "City"}.");
    }

    public void QueueProduction(GameEngine engine, string templateId)
    {
        QueueProduction(engine, ResolveSelectedEntity(engine), templateId);
    }

    public void QueueProduction(GameEngine engine, Entity targetEntity, string templateId)
    {
        int producerId = targetEntity.Id;
        string producerName = ResolveEntityLabel(engine, targetEntity) ?? "City";
        if (producerId <= 0)
        {
            producerId = -1;
        }

        if (!_production.TryGetValue(producerId, out ProductionLaneState? lane))
        {
            lane = new ProductionLaneState(producerId, producerName);
            _production[producerId] = lane;
        }

        lane.Queue.Add(new ProductionItemState(templateId, 0f, GetProductionDuration(templateId)));
        _food = Math.Max(0, _food - 40);
        _credits = Math.Max(0, _credits - 20);
        AddEvent($"{producerName} queued {LabelForTemplate(templateId)}.");
    }

    public void TriggerAction(GameEngine engine, string actionId)
    {
        TriggerAction(engine, ResolveSelectedEntity(engine), actionId);
    }

    public void TriggerAction(GameEngine engine, Entity targetEntity, string actionId)
    {
        if (string.IsNullOrWhiteSpace(actionId))
        {
            return;
        }

        if (TryResolveBuildTemplate(actionId, out string? buildTemplate))
        {
            QueueConstruction(engine, targetEntity, buildTemplate!);
            return;
        }

        if (TryResolveUnitTemplate(actionId, out string? unitTemplate))
        {
            QueueProduction(engine, targetEntity, unitTemplate!);
            return;
        }

        TriggerSkill(engine, actionId);
    }

    public void CancelQueueEntry(GameEngine engine, string queueToken)
    {
        if (string.IsNullOrWhiteSpace(queueToken))
        {
            return;
        }

        string[] parts = queueToken.Split(':', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2)
        {
            return;
        }

        if (string.Equals(parts[0], "prod", StringComparison.OrdinalIgnoreCase) &&
            parts.Length >= 3 &&
            int.TryParse(parts[1], out int producerId) &&
            int.TryParse(parts[2], out int queueIndex) &&
            _production.TryGetValue(producerId, out ProductionLaneState? lane) &&
            queueIndex >= 0 &&
            queueIndex < lane.Queue.Count)
        {
            ProductionItemState removed = lane.Queue[queueIndex];
            lane.Queue.RemoveAt(queueIndex);
            RefundProduction(removed.TemplateId);
            AddEvent($"{lane.ProducerLabel} cancelled {LabelForTemplate(removed.TemplateId)}.");
            if (lane.Queue.Count == 0)
            {
                _production.Remove(producerId);
            }

            return;
        }

        if (string.Equals(parts[0], "construct", StringComparison.OrdinalIgnoreCase) &&
            int.TryParse(parts[1], out int constructionIndex) &&
            constructionIndex >= 0 &&
            constructionIndex < _construction.Count)
        {
            ConstructionState removed = _construction[constructionIndex];
            _construction.RemoveAt(constructionIndex);
            RefundConstruction(removed.TemplateId);
            if (_activeBuildTemplateId == removed.TemplateId)
            {
                _activeBuildTemplateId = null;
            }

            AddEvent($"{removed.BuilderLabel} cancelled {LabelForTemplate(removed.TemplateId)}.");
        }
    }

    public void CycleFactionOperation()
    {
        int index = Array.IndexOf(UxPrototypeIds.FactionTabs, _factionTab);
        _factionTab = UxPrototypeIds.FactionTabs[(index + 1 + UxPrototypeIds.FactionTabs.Length) % UxPrototypeIds.FactionTabs.Length];
        AddEvent($"Faction operation focus switched to {CultureName(_factionTab)}.");
    }

    public UxPrototypePanelState BuildSnapshot(GameEngine engine, ViewModeManager? viewModeManager)
    {
        string activeModeId = viewModeManager?.ActiveMode?.Id ?? _modeId;
        if (UxPrototypeIds.IsPrototypeMode(activeModeId))
        {
            _modeId = activeModeId;
        }

        Entity selectedEntity = ResolveSelectedEntity(engine);
        string? selectedLabel = ResolveEntityLabel(engine, selectedEntity);
        var counts = CountEntities(engine);
        var selectedQueue = BuildSelectedQueueSnapshot(engine);
        var minimapDots = BuildMinimapDots(engine, counts.MaxX, counts.MaxY, selectedEntity);

        return new UxPrototypePanelState(
            ActiveModeId: _modeId,
            ActiveModeLabel: ModeLabel(_modeId),
            Objective: _objective,
            EventQueue: new List<string>(_events),
            Food: _food,
            Iron: _iron,
            Credits: _credits,
            Energy: _energy,
            FoodDelta: counts.Farms * 3 + counts.Mines,
            IronDelta: counts.Mines * 2 + counts.Workshops,
            CreditsDelta: counts.Cities * 3 + counts.Farms * 2,
            EnergyDelta: counts.Workshops + counts.Forts,
            ResourceDropdownOpen: _resourceDropdownOpen,
            ResourceBreakdown: BuildResourceBreakdown(counts),
            Year: _year,
            Month: _month,
            Day: _calendarDay,
            DayProgress: _dayProgress,
            EraLabel: "Frontier Campaign",
            GanzhiLabel: "甲辰",
            MoonPhaseLabel: MoonPhaseLabel(_moonPhaseIndex),
            TimeSummary: $"{_year}Y / {_month:D2}M / {_calendarDay:D3}D",
            RosterTab: _rosterTab,
            RosterCollapsed: _rosterCollapsed,
            GridLayout: _gridLayout,
            RosterSummary: BuildRosterSummary(counts),
            RosterRows: BuildRosterRows(counts),
            FactionTab: _factionTab,
            FactionOperations: BuildFactionOperations(),
            GlobalTab: _globalTab,
            GlobalExpanded: _globalExpanded,
            GlobalEntries: BuildGlobalEntries(),
            SelectedEntityLabel: selectedLabel ?? "(none)",
            SelectedEntityType: ResolveSelectedType(selectedLabel),
            SelectedSummary: BuildSelectionSummary(selectedLabel, counts),
            SelectedSkills: BuildSkillRows(selectedLabel),
            SelectedQueue: selectedQueue,
            ProductionRows: BuildProductionRows(),
            RallySummary: BuildRallySummary(engine),
            ActiveBuildTemplateId: _activeBuildTemplateId,
            BuildEntries: BuildBuildEntries(),
            PreferencesOpen: _preferencesOpen,
            SettingsOpen: _settingsOpen,
            Paused: _paused,
            SpeedIndex: _speedIndex,
            SmartCastBatch: _smartCastBatch,
            QuickCastOnRelease: _quickCastOnRelease,
            ShowTargetLines: _showTargetLines,
            ShowRangeIndicators: _showRangeIndicators,
            ShowTooltips: _showTooltips,
            RoadSnapping: _roadSnapping,
            ObstacleFill: _obstacleFill,
            NavmeshVisible: _navmeshVisible,
            NavmeshBakeRunning: _navmeshBakeRunning,
            NavmeshBakeProgress: _navmeshBakeProgress,
            RoadNodeCount: _roadNodeCount,
            RoadSegmentCount: _roadSegmentCount,
            ObstacleCount: _obstacleCount,
            TooltipTitle: BuildTooltipTitle(selectedLabel),
            TooltipBody: BuildTooltipBody(selectedLabel),
            MinimapDots: minimapDots,
            MinimapSelectedLabel: selectedLabel ?? string.Empty);
    }

    public bool TryBuildEntityCommandSlots(GameEngine engine, Entity targetEntity, out string header, out List<SkillEntry> slots)
    {
        header = string.Empty;
        slots = new List<SkillEntry>();
        if (!engine.World.IsAlive(targetEntity))
        {
            return false;
        }

        string? label = ResolveEntityLabel(engine, targetEntity);
        header = label ?? $"Entity#{targetEntity.Id}";
        slots = BuildSkillRows(label);
        return slots.Count > 0;
    }

    public bool TryActivateEntityCommand(GameEngine engine, Entity targetEntity, int slotIndex)
    {
        if (!TryBuildEntityCommandSlots(engine, targetEntity, out _, out List<SkillEntry> slots))
        {
            return false;
        }

        if ((uint)slotIndex >= (uint)slots.Count)
        {
            return false;
        }

        SkillEntry slot = slots[slotIndex];
        if (!slot.Enabled)
        {
            return false;
        }

        TriggerAction(engine, targetEntity, slot.ActionId);
        return true;
    }

    private void AdvanceCalendar(float scaledDt)
    {
        _dayProgress += scaledDt * 0.018f;
        if (_dayProgress < 1f)
        {
            return;
        }

        _dayProgress -= 1f;
        _calendarDay++;
        _moonPhaseIndex = (_moonPhaseIndex + 1) % 8;
        if (_calendarDay > 360)
        {
            _calendarDay = 1;
            _year++;
        }

        _month = ((_calendarDay - 1) / 30) + 1;
    }

    private void AdvanceEconomy(GameEngine engine, float scaledDt)
    {
        var counts = CountEntities(engine);
        _food += (int)MathF.Floor(scaledDt * MathF.Max(1f, counts.Farms * 1.4f));
        _iron += (int)MathF.Floor(scaledDt * MathF.Max(0f, counts.Mines * 1.2f));
        _credits += (int)MathF.Floor(scaledDt * MathF.Max(1f, counts.Cities * 1.1f + counts.Farms * 0.8f));
        _energy += (int)MathF.Floor(scaledDt * MathF.Max(0f, counts.Workshops * 0.7f));
    }

    private void AdvanceProduction(GameEngine engine, float scaledDt)
    {
        if (engine.GetService(CoreServiceKeys.RuntimeEntitySpawnQueue) is not RuntimeEntitySpawnQueue spawnQueue)
        {
            return;
        }

        string mapId = engine.CurrentMapSession?.MapId.Value ?? _activeMapId;
        var completedLanes = new List<int>();

        foreach ((int producerId, ProductionLaneState lane) in _production)
        {
            if (lane.Queue.Count == 0)
            {
                completedLanes.Add(producerId);
                continue;
            }

            ProductionItemState item = lane.Queue[0];
            item = item with { Progress = item.Progress + scaledDt };
            lane.Queue[0] = item;
            if (item.Progress < item.Duration)
            {
                continue;
            }

            lane.Queue.RemoveAt(0);
            WorldCmInt2 spawnPoint = ResolveSpawnPoint(engine, lane.ProducerId);
            var request = new RuntimeEntitySpawnRequest
            {
                Kind = RuntimeEntitySpawnKind.Template,
                TemplateId = item.TemplateId,
                WorldPositionCm = Fix64Vec2.FromInt(spawnPoint.X, spawnPoint.Y),
                MapId = new MapId(mapId)
            };

            spawnQueue.TryEnqueue(in request);
            AddEvent($"{lane.ProducerLabel} completed {LabelForTemplate(item.TemplateId)}.");

            if (lane.Queue.Count == 0)
            {
                completedLanes.Add(producerId);
            }
        }

        for (int i = 0; i < completedLanes.Count; i++)
        {
            int key = completedLanes[i];
            if (_production.TryGetValue(key, out ProductionLaneState? lane) && lane.Queue.Count == 0)
            {
                _production.Remove(key);
            }
        }
    }

    private void AdvanceConstruction(GameEngine engine, float scaledDt)
    {
        if (engine.GetService(CoreServiceKeys.RuntimeEntitySpawnQueue) is not RuntimeEntitySpawnQueue spawnQueue)
        {
            return;
        }

        string mapId = engine.CurrentMapSession?.MapId.Value ?? _activeMapId;
        for (int i = _construction.Count - 1; i >= 0; i--)
        {
            ConstructionState item = _construction[i];
            item = item with { Progress = item.Progress + scaledDt };
            _construction[i] = item;
            if (item.Progress < item.Duration)
            {
                continue;
            }

            var request = new RuntimeEntitySpawnRequest
            {
                Kind = RuntimeEntitySpawnKind.Template,
                TemplateId = item.TemplateId,
                WorldPositionCm = Fix64Vec2.FromInt(item.Target.X, item.Target.Y),
                MapId = new MapId(mapId)
            };
            spawnQueue.TryEnqueue(in request);
            AddEvent($"{item.BuilderLabel} finished {LabelForTemplate(item.TemplateId)}.");
            _construction.RemoveAt(i);
            _activeBuildTemplateId = null;
        }
    }

    private void AdvanceNavmeshBake(float scaledDt)
    {
        if (!_navmeshBakeRunning)
        {
            return;
        }

        _navmeshBakeProgress = MathF.Min(1f, _navmeshBakeProgress + scaledDt * 0.15f);
        if (_navmeshBakeProgress < 1f)
        {
            return;
        }

        _navmeshBakeProgress = 1f;
        _navmeshBakeRunning = false;
        AddEvent("Navmesh bake completed with zero leaked islands.");
    }

    private void AdvanceSkillCooldowns(float scaledDt)
    {
        if (_skillCooldowns.Count == 0)
        {
            return;
        }

        var completed = new List<string>();
        foreach ((string actionId, float remaining) in _skillCooldowns)
        {
            float next = MathF.Max(0f, remaining - scaledDt);
            _skillCooldowns[actionId] = next;
            if (next <= 0f)
            {
                completed.Add(actionId);
            }
        }

        for (int i = 0; i < completed.Count; i++)
        {
            _skillCooldowns.Remove(completed[i]);
        }
    }

    private void SeedRallyPoints(GameEngine engine, string mapId)
    {
        _rallyPoints.Clear();
        MapId targetMapId = new(mapId);

        engine.World.Query(in PrototypeEntityQuery, (Entity entity, ref Name name, ref WorldPositionCm position, ref MapEntity mapEntity) =>
        {
            if (mapEntity.MapId != targetMapId)
            {
                return;
            }

            string label = name.Value;
            if (!label.Contains("City", StringComparison.OrdinalIgnoreCase) &&
                !label.Contains("Barracks", StringComparison.OrdinalIgnoreCase) &&
                !label.Contains("Stable", StringComparison.OrdinalIgnoreCase) &&
                !label.Contains("Workshop", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            WorldCmInt2 start = position.ToWorldCmInt2();
            _rallyPoints[entity.Id] = new List<WorldCmInt2>
            {
                new(start.X + 700, start.Y + 140),
                new(start.X + 1100, start.Y + 140),
                new(start.X + 1450, start.Y - 100)
            };
        });
    }

    private Entity ResolveSelectedEntity(GameEngine engine)
    {
        return SelectionContextRuntime.TryGetCurrentPrimary(engine.World, engine.GlobalContext, out Entity entity)
            ? entity
            : Entity.Null;
    }

    private string? ResolveSelectedLabel(GameEngine engine)
    {
        return ResolveEntityLabel(engine, ResolveSelectedEntity(engine));
    }

    private WorldCmInt2 ResolveSelectedOrFallbackPosition(GameEngine engine)
    {
        return ResolveEntityOrFallbackPosition(engine, ResolveSelectedEntity(engine));
    }

    private string? ResolveEntityLabel(GameEngine engine, Entity entity)
    {
        if (entity == Entity.Null || !engine.World.IsAlive(entity))
        {
            return null;
        }

        return engine.World.TryGet(entity, out Name name) ? name.Value : $"Entity#{entity.Id}";
    }

    private WorldCmInt2 ResolveEntityOrFallbackPosition(GameEngine engine, Entity entity)
    {
        if (entity != Entity.Null && engine.World.TryGet(entity, out WorldPositionCm position))
        {
            return position.ToWorldCmInt2();
        }

        WorldCmInt2 fallback = new(2600, 2600);
        engine.World.Query(in PrototypeEntityQuery, (Entity _, ref Name name, ref WorldPositionCm position, ref MapEntity mapEntity) =>
        {
            if (mapEntity.MapId != new MapId(_activeMapId))
            {
                return;
            }

            if (name.Value.Contains("City", StringComparison.OrdinalIgnoreCase))
            {
                fallback = position.ToWorldCmInt2();
            }
        });

        return fallback;
    }

    private WorldCmInt2 ResolveSpawnPoint(GameEngine engine, int producerId)
    {
        if (producerId > 0)
        {
            WorldCmInt2? found = null;
            engine.World.Query(in PrototypeEntityQuery, (Entity entity, ref Name _, ref WorldPositionCm producerPos, ref MapEntity mapEntity) =>
            {
                if (mapEntity.MapId != new MapId(_activeMapId) || entity.Id != producerId)
                {
                    return;
                }

                WorldCmInt2 point = producerPos.ToWorldCmInt2();
                found = new WorldCmInt2(point.X + 200, point.Y + 120);
            });

            if (found.HasValue)
            {
                return found.Value;
            }
        }

        return ResolveSelectedOrFallbackPosition(engine);
    }

    private ProductionQueueSnapshot BuildSelectedQueueSnapshot(GameEngine engine)
    {
        Entity selected = ResolveSelectedEntity(engine);
        if (selected != Entity.Null && _production.TryGetValue(selected.Id, out ProductionLaneState? lane))
        {
            return new ProductionQueueSnapshot(
                lane.ProducerLabel,
                lane.Queue.Select((item, index) => new ProductionQueueEntry(
                    $"prod:{selected.Id}:{index}",
                    LabelForTemplate(item.TemplateId),
                    ProgressPercent(item.Progress, item.Duration),
                    true)).ToList());
        }

        if (_construction.Count > 0)
        {
            return new ProductionQueueSnapshot(
                "Construction",
                _construction.Select((item, index) => new ProductionQueueEntry(
                    $"construct:{index}",
                    LabelForTemplate(item.TemplateId),
                    ProgressPercent(item.Progress, item.Duration),
                    true)).ToList());
        }

        return new ProductionQueueSnapshot("Idle", new List<ProductionQueueEntry>());
    }

    private List<ProductionRow> BuildProductionRows()
    {
        var rows = new List<ProductionRow>();
        foreach (ProductionLaneState lane in _production.Values.OrderBy(value => value.ProducerLabel, StringComparer.OrdinalIgnoreCase))
        {
            string summary = lane.Queue.Count == 0
                ? "Idle"
                : string.Join(" -> ", lane.Queue.Select(item => $"{LabelForTemplate(item.TemplateId)} {ProgressPercent(item.Progress, item.Duration)}%"));
            rows.Add(new ProductionRow(lane.ProducerLabel, summary));
        }

        foreach (ConstructionState item in _construction)
        {
            rows.Add(new ProductionRow(item.BuilderLabel, $"Building {LabelForTemplate(item.TemplateId)} {ProgressPercent(item.Progress, item.Duration)}%"));
        }

        if (rows.Count == 0)
        {
            rows.Add(new ProductionRow("Command Network", "No active queues."));
        }

        return rows;
    }

    private string BuildRallySummary(GameEngine engine)
    {
        Entity selected = ResolveSelectedEntity(engine);
        if (selected == Entity.Null || !_rallyPoints.TryGetValue(selected.Id, out List<WorldCmInt2>? points))
        {
            return "No rally chain assigned.";
        }

        return string.Join(" -> ", points.Select(point => $"({point.X},{point.Y})"));
    }

    private List<ResourceBreakdownEntry> BuildResourceBreakdown(EntityCountSummary counts)
    {
        return new List<ResourceBreakdownEntry>
        {
            new("Cities", counts.Cities, "+3 credits / +1 food"),
            new("Farms", counts.Farms, "+3 food / +2 credits"),
            new("Mines", counts.Mines, "+2 iron / +1 food"),
            new("Workshops", counts.Workshops, "+1 energy / siege slots"),
            new("Forts", counts.Forts, "+1 energy / build radius")
        };
    }

    private List<RosterRow> BuildRosterRows(EntityCountSummary counts)
    {
        return _rosterTab switch
        {
            UxPrototypeIds.RosterEconomy => new List<RosterRow>
            {
                new("Cities", counts.Cities, "Core command radius"),
                new("Farms", counts.Farms, "Food and credits"),
                new("Mines", counts.Mines, "Iron extraction"),
                new("Workers", counts.Workers, "Builder crews")
            },
            UxPrototypeIds.RosterProduction => new List<RosterRow>
            {
                new("Barracks", counts.Barracks, "Infantry lanes"),
                new("Stable", counts.Stables, "Cavalry lanes"),
                new("Workshop", counts.Workshops, "Siege lanes")
            },
            UxPrototypeIds.RosterDefense => new List<RosterRow>
            {
                new("Towers", counts.Towers, "Point denial"),
                new("Watchtowers", counts.Watchtowers, "Vision"),
                new("Forts", counts.Forts, "Build radius")
            },
            UxPrototypeIds.RosterTech => new List<RosterRow>
            {
                new("Research Slots", 3, "Queued"),
                new("Era Tags", 2, "Active"),
                new("Doctrine Cards", 4, "Unlocked")
            },
            UxPrototypeIds.RosterUnits => new List<RosterRow>
            {
                new("Soldiers", counts.Soldiers, "Frontline"),
                new("Archers", counts.Archers, "Ranged"),
                new("Mages", counts.Mages, "Spell"),
                new("Medics", counts.Medics, "Support"),
                new("Workers", counts.Workers, "Utility")
            },
            UxPrototypeIds.RosterSiege => new List<RosterRow>
            {
                new("Rams", counts.Rams, "Structure pressure"),
                new("Catapults", counts.Catapults, "Area bombard"),
                new("Well Columns", counts.WellColumns, "Tower lane")
            },
            UxPrototypeIds.RosterEquipment => new List<RosterRow>
            {
                new("Iron Reserves", _iron, "Forge stock"),
                new("Siege Kits", 6, "Ready"),
                new("Medic Packs", 4, "Issued")
            },
            _ => new List<RosterRow>
            {
                new("War Plans", 3, "Prepared"),
                new("Alliances", 1, "Conditional"),
                new("Event Chips", _events.Count, "Visible")
            }
        };
    }

    private List<FactionOperationEntry> BuildFactionOperations()
    {
        return _factionTab switch
        {
            UxPrototypeIds.FactionDiplomacy => new List<FactionOperationEntry>
            {
                new("Declare War", "Open northern hostilities."),
                new("Offer Alliance", "Reinforce the frontier with allies."),
                new("Send Envoy", "Raise favor with neutral houses.")
            },
            UxPrototypeIds.FactionPersonnel => new List<FactionOperationEntry>
            {
                new("Recruit Hero", "Pull a new named commander into the roster."),
                new("Assign Governor", "Redistribute city pressure."),
                new("Banquet", "Restore morale.")
            },
            UxPrototypeIds.FactionPublic => new List<FactionOperationEntry>
            {
                new("Tax Relief", "Trade income for stability."),
                new("Festival", "Lift support and loyalty."),
                new("Propaganda", "Accelerate war footing.")
            },
            UxPrototypeIds.FactionTech => new List<FactionOperationEntry>
            {
                new("Research", "Queue doctrine research."),
                new("Upgrade Unit", "Apply field retrofit."),
                new("Refit Siege", "Raise workshop output.")
            },
            _ => new List<FactionOperationEntry>
            {
                new("Buy Food", "Cover deficit."),
                new("Sell Materials", "Fund the next build chain."),
                new("Caravan", "Open a temporary route.")
            }
        };
    }

    private List<GlobalEntry> BuildGlobalEntries()
    {
        return _globalTab switch
        {
            UxPrototypeIds.GlobalBuild => UxPrototypeIds.BuildTemplates
                .Select(template => new GlobalEntry(
                    ToBuildActionId(template),
                    LabelForTemplate(template),
                    $"Build {LabelForTemplate(template)}",
                    template == _activeBuildTemplateId))
                .ToList(),
            UxPrototypeIds.GlobalUnits => UxPrototypeIds.UnitTemplates
                .Select(template => new GlobalEntry(
                    ToTrainActionId(template),
                    LabelForTemplate(template),
                    $"Queue {LabelForTemplate(template)}",
                    false))
                .ToList(),
            UxPrototypeIds.GlobalDefense => new List<GlobalEntry>
            {
                new("build:ux_tower", "Tower", "Preview point-defense placement", false),
                new("build:ux_watchtower", "Watchtower", "Preview vision tower placement", false),
                new("build:ux_fort", "Fort", "Expand build radius and anchor defense", false)
            },
            _ => new List<GlobalEntry>
            {
                new("editor:road", "Road Editor", "Insert and route road nodes", string.Equals(_modeId, UxPrototypeIds.RoadEditorModeId, StringComparison.OrdinalIgnoreCase)),
                new("editor:obstacle", "Obstacle Editor", "Edit blocking polygons", string.Equals(_modeId, UxPrototypeIds.ObstacleEditorModeId, StringComparison.OrdinalIgnoreCase)),
                new("editor:navmesh", "Navmesh Bake", "Rebuild tactical navigation mesh", string.Equals(_modeId, UxPrototypeIds.NavmeshModeId, StringComparison.OrdinalIgnoreCase))
            }
        };
    }

    private List<BuildEntry> BuildBuildEntries()
    {
        return UxPrototypeIds.BuildTemplates
            .Select(template => new BuildEntry(ToBuildActionId(template), LabelForTemplate(template), DescribeBuildTemplate(template), template == _activeBuildTemplateId))
            .ToList();
    }

    private List<SkillEntry> BuildSkillRows(string? selectedLabel)
    {
        string type = ResolveSelectedType(selectedLabel);
        return type switch
        {
            "Mage" => new List<SkillEntry>
            {
                CreateSkillEntry("fireball", "Q", "Fireball", "Area damage", true)
            },
            "Archer" => new List<SkillEntry>
            {
                CreateSkillEntry("volley", "Q", "Volley", "Channel barrage", true)
            },
            "Medic" => new List<SkillEntry>
            {
                CreateSkillEntry("heal", "Q", "Heal", "Restore nearby allies", true)
            },
            "Soldier" => new List<SkillEntry>
            {
                CreateSkillEntry("leap_slash", "Q", "Leap Slash", "Gap close", true)
            },
            "Heavy Cavalry" => new List<SkillEntry>
            {
                CreateSkillEntry("charge", "Q", "Charge", "Impact path", true)
            },
            "Horse Archer" => new List<SkillEntry>
            {
                CreateSkillEntry("volley", "Q", "Volley", "Ranged strafe burst", true)
            },
            "Catapult" => new List<SkillEntry>
            {
                CreateSkillEntry("siege_shot", "Q", "Siege Shot", "Long-range bombardment", true)
            },
            "City" => new List<SkillEntry>
            {
                CreateSkillEntry("train:ux_worker", "Q", "Train Worker", "Builder queue", true),
                CreateSkillEntry("build:ux_farm", "W", "Build Farm", "Economic expansion", true),
                CreateSkillEntry("build:ux_mine", "E", "Build Mine", "Iron extraction", true),
                CreateSkillEntry("build:ux_tower", "R", "Build Tower", "Immediate defense", true)
            },
            "Barracks" => new List<SkillEntry>
            {
                CreateSkillEntry("train:ux_soldier", "Q", "Train Soldier", "Lane 1", true),
                CreateSkillEntry("train:ux_archer", "W", "Train Archer", "Lane 2", true),
                CreateSkillEntry("train:ux_mage", "E", "Train Mage", "Lane 3", true),
                CreateSkillEntry("train:ux_medic", "R", "Train Medic", "Support slot", true)
            },
            "Stable" => new List<SkillEntry>
            {
                CreateSkillEntry("train:ux_heavy_cavalry", "Q", "Train Heavy Cavalry", "Charge line", true),
                CreateSkillEntry("train:ux_horse_archer", "W", "Train Horse Archer", "Skirmish line", true)
            },
            "Workshop" => new List<SkillEntry>
            {
                CreateSkillEntry("train:ux_ram", "Q", "Train Ram", "Structure pressure", true),
                CreateSkillEntry("train:ux_catapult", "W", "Train Catapult", "Siege pressure", true),
                CreateSkillEntry("train:ux_well_column", "E", "Train Well Column", "Sustain tower", true)
            },
            "Worker" => UxPrototypeIds.BuildTemplates
                .Select((template, index) => CreateSkillEntry(
                    ToBuildActionId(template),
                    index switch
                    {
                        0 => "Q",
                        1 => "W",
                        2 => "E",
                        3 => "R",
                        _ => string.Empty
                    },
                    $"Build {LabelForTemplate(template)}",
                    DescribeBuildTemplate(template),
                    true))
                .ToList(),
            "Fort" => new List<SkillEntry>
            {
                CreateSkillEntry("build:ux_watchtower", "Q", "Build Watchtower", "Extend vision", true),
                CreateSkillEntry("build:ux_tower", "W", "Build Tower", "Hold the choke", true),
                CreateSkillEntry("build:ux_fort", "E", "Reinforce Fort", "Add another anchor", true)
            },
            _ => new List<SkillEntry>
            {
                CreateSkillEntry("help:command", "Q", "Command", "Select a unit or building to unlock the hotbar.", false),
                CreateSkillEntry("help:queue", "W", "Queue", "Shift RMB queues movement on the battlefield.", false),
                CreateSkillEntry("help:global", "E", "Global", "Use the global panel to build and train.", false)
            }
        };
    }

    private string BuildSelectionSummary(string? selectedLabel, EntityCountSummary counts)
    {
        if (string.IsNullOrWhiteSpace(selectedLabel))
        {
            return $"Army {counts.Soldiers + counts.Archers + counts.Mages + counts.Medics + counts.HeavyCavalry + counts.HorseArchers} | Buildings {counts.Cities + counts.Barracks + counts.Stables + counts.Workshops + counts.Towers + counts.Watchtowers + counts.Forts}";
        }

        string type = ResolveSelectedType(selectedLabel);
        return type switch
        {
            "City" => "City selected. Build radius active. Queue workers or civic actions. Right-click can define rally chains for new units.",
            "Barracks" => "Barracks selected. Parallel infantry lanes online with rally display.",
            "Stable" => "Stable selected. Cavalry production and route chaining ready.",
            "Workshop" => "Workshop selected. Siege queue active and navmesh-sensitive.",
            "Worker" => "Worker selected. Can start structure placement and contribute to construction.",
            _ => $"{selectedLabel} selected. Use battlefield movement with Shift queue while this HUD mirrors prototype command state."
        };
    }

    private string BuildTooltipTitle(string? selectedLabel) =>
        string.IsNullOrWhiteSpace(selectedLabel) ? "Prototype HUD" : selectedLabel;

    private string BuildTooltipBody(string? selectedLabel)
    {
        string type = ResolveSelectedType(selectedLabel);
        return type switch
        {
            "City" => "Core structure. Extends build radius, hosts worker production, and anchors the minimap economy rollup.",
            "Barracks" => "Infantry production hub with soldier, archer, mage, and medic lanes.",
            "Stable" => "Mobile warfare hub with cavalry and horse archer output.",
            "Workshop" => "Siege manufacturing lane for rams, catapults, and well columns.",
            "Worker" => "Builder unit. In this mod it drives construction tasks through the global build panel.",
            "Ram" => "Siege ram. Applies direct structure pressure and anchors the frontline breach.",
            "Well Column" => "Support siege payload that sustains tower lanes and forward pressure.",
            _ => "This Ludots-native port keeps the prototype's HUD semantics while routing through existing selection, input, and runtime services."
        };
    }

    private List<MinimapDot> BuildMinimapDots(GameEngine engine, int maxX, int maxY, Entity selectedEntity)
    {
        var dots = new List<MinimapDot>();
        MapId mapId = new(_activeMapId);
        float safeMaxX = Math.Max(1f, maxX);
        float safeMaxY = Math.Max(1f, maxY);
        engine.World.Query(in PrototypeEntityQuery, (Entity entity, ref Name name, ref WorldPositionCm position, ref MapEntity mapEntity) =>
        {
            if (mapEntity.MapId != mapId)
            {
                return;
            }

            WorldCmInt2 world = position.ToWorldCmInt2();
            float x = world.X / safeMaxX;
            float y = world.Y / safeMaxY;
            string color = name.Value.Contains("Enemy", StringComparison.OrdinalIgnoreCase)
                ? "#E16E68"
                : name.Value.Contains("Deposit", StringComparison.OrdinalIgnoreCase)
                    ? "#E7C768"
                    : "#78C6FF";

            dots.Add(new MinimapDot(name.Value, x, y, color, entity == selectedEntity));
        });

        return dots;
    }

    private EntityCountSummary CountEntities(GameEngine engine)
    {
        var counts = new EntityCountSummary();
        MapId mapId = new(_activeMapId);
        engine.World.Query(in PrototypeEntityQuery, (Entity _, ref Name name, ref WorldPositionCm position, ref MapEntity mapEntity) =>
        {
            if (mapEntity.MapId != mapId)
            {
                return;
            }

            WorldCmInt2 world = position.ToWorldCmInt2();
            counts.MaxX = Math.Max(counts.MaxX, world.X);
            counts.MaxY = Math.Max(counts.MaxY, world.Y);
            string label = name.Value;
            if (label.Contains("City", StringComparison.OrdinalIgnoreCase)) counts.Cities++;
            else if (label.Contains("Barracks", StringComparison.OrdinalIgnoreCase)) counts.Barracks++;
            else if (label.Contains("Stable", StringComparison.OrdinalIgnoreCase)) counts.Stables++;
            else if (label.Contains("Workshop", StringComparison.OrdinalIgnoreCase)) counts.Workshops++;
            else if (label.Contains("Watchtower", StringComparison.OrdinalIgnoreCase)) counts.Watchtowers++;
            else if (label.Contains("Tower", StringComparison.OrdinalIgnoreCase)) counts.Towers++;
            else if (label.Contains("Fort", StringComparison.OrdinalIgnoreCase)) counts.Forts++;
            else if (label.Contains("Farm", StringComparison.OrdinalIgnoreCase)) counts.Farms++;
            else if (label.Contains("Mine", StringComparison.OrdinalIgnoreCase)) counts.Mines++;
            else if (label.Contains("Worker", StringComparison.OrdinalIgnoreCase)) counts.Workers++;
            else if (label.Contains("Soldier", StringComparison.OrdinalIgnoreCase)) counts.Soldiers++;
            else if (label.Contains("Archer", StringComparison.OrdinalIgnoreCase)) counts.Archers++;
            else if (label.Contains("Mage", StringComparison.OrdinalIgnoreCase)) counts.Mages++;
            else if (label.Contains("Medic", StringComparison.OrdinalIgnoreCase)) counts.Medics++;
            else if (label.Contains("Heavy Cavalry", StringComparison.OrdinalIgnoreCase)) counts.HeavyCavalry++;
            else if (label.Contains("Horse Archer", StringComparison.OrdinalIgnoreCase)) counts.HorseArchers++;
            else if (label.Contains("Ram", StringComparison.OrdinalIgnoreCase)) counts.Rams++;
            else if (label.Contains("Catapult", StringComparison.OrdinalIgnoreCase)) counts.Catapults++;
            else if (label.Contains("Well Column", StringComparison.OrdinalIgnoreCase)) counts.WellColumns++;
        });

        return counts;
    }

    private static string BuildRosterSummary(EntityCountSummary counts)
    {
        return $"Economy {counts.Farms + counts.Mines + counts.Cities} | Production {counts.Barracks + counts.Stables + counts.Workshops} | Field {counts.Soldiers + counts.Archers + counts.Mages + counts.Medics + counts.HeavyCavalry + counts.HorseArchers + counts.Rams + counts.Catapults + counts.WellColumns}";
    }

    private static string ResolveSelectedType(string? selectedLabel)
    {
        if (string.IsNullOrWhiteSpace(selectedLabel))
        {
            return "None";
        }

        string label = selectedLabel;
        if (label.Contains("Heavy Cavalry", StringComparison.OrdinalIgnoreCase)) return "Heavy Cavalry";
        if (label.Contains("Horse Archer", StringComparison.OrdinalIgnoreCase)) return "Horse Archer";
        if (label.Contains("Ram", StringComparison.OrdinalIgnoreCase)) return "Ram";
        if (label.Contains("Well Column", StringComparison.OrdinalIgnoreCase)) return "Well Column";
        if (label.Contains("Watchtower", StringComparison.OrdinalIgnoreCase)) return "Watchtower";
        if (label.Contains("Barracks", StringComparison.OrdinalIgnoreCase)) return "Barracks";
        if (label.Contains("Stable", StringComparison.OrdinalIgnoreCase)) return "Stable";
        if (label.Contains("Workshop", StringComparison.OrdinalIgnoreCase)) return "Workshop";
        if (label.Contains("Worker", StringComparison.OrdinalIgnoreCase)) return "Worker";
        if (label.Contains("Soldier", StringComparison.OrdinalIgnoreCase)) return "Soldier";
        if (label.Contains("Archer", StringComparison.OrdinalIgnoreCase)) return "Archer";
        if (label.Contains("Mage", StringComparison.OrdinalIgnoreCase)) return "Mage";
        if (label.Contains("Medic", StringComparison.OrdinalIgnoreCase)) return "Medic";
        if (label.Contains("Catapult", StringComparison.OrdinalIgnoreCase)) return "Catapult";
        if (label.Contains("Tower", StringComparison.OrdinalIgnoreCase)) return "Tower";
        if (label.Contains("Fort", StringComparison.OrdinalIgnoreCase)) return "Fort";
        if (label.Contains("Farm", StringComparison.OrdinalIgnoreCase)) return "Farm";
        if (label.Contains("Mine", StringComparison.OrdinalIgnoreCase)) return "Mine";
        if (label.Contains("City", StringComparison.OrdinalIgnoreCase)) return "City";
        return selectedLabel;
    }

    private static string LabelForTemplate(string templateId) => templateId switch
    {
        "ux_worker" => "Worker",
        "ux_soldier" => "Soldier",
        "ux_archer" => "Archer",
        "ux_mage" => "Mage",
        "ux_medic" => "Medic",
        "ux_heavy_cavalry" => "Heavy Cavalry",
        "ux_horse_archer" => "Horse Archer",
        "ux_ram" => "Ram",
        "ux_catapult" => "Catapult",
        "ux_well_column" => "Well Column",
        "ux_city" => "City",
        "ux_barracks" => "Barracks",
        "ux_stable" => "Stable",
        "ux_workshop" => "Workshop",
        "ux_tower" => "Tower",
        "ux_watchtower" => "Watchtower",
        "ux_fort" => "Fort",
        "ux_farm" => "Farm",
        "ux_mine" => "Mine",
        _ => templateId
    };

    private static string DescribeBuildTemplate(string templateId) => templateId switch
    {
        "ux_farm" => "Economic outpost that produces food and credits over time.",
        "ux_mine" => "Ore extractor tied to the iron line.",
        "ux_barracks" => "Infantry production lanes with four hotbar slots.",
        "ux_stable" => "Cavalry production and mobile flank pressure.",
        "ux_workshop" => "Siege construction and heavy logistics.",
        "ux_tower" => "Direct defensive pressure over a narrow lane.",
        "ux_watchtower" => "Vision and information advantage.",
        "ux_fort" => "Extended build radius and durable frontline anchor.",
        _ => "Prototype structure entry."
    };

    private static int GetProductionDuration(string templateId) => templateId switch
    {
        "ux_worker" => 3,
        "ux_soldier" => 4,
        "ux_archer" => 4,
        "ux_mage" => 5,
        "ux_medic" => 5,
        "ux_heavy_cavalry" => 6,
        "ux_horse_archer" => 6,
        "ux_ram" => 7,
        "ux_catapult" => 8,
        "ux_well_column" => 8,
        _ => 4
    };

    private static int GetConstructionDuration(string templateId) => templateId switch
    {
        "ux_farm" => 4,
        "ux_mine" => 5,
        "ux_barracks" => 7,
        "ux_stable" => 7,
        "ux_workshop" => 8,
        "ux_tower" => 5,
        "ux_watchtower" => 5,
        "ux_fort" => 9,
        _ => 6
    };

    private static string MoonPhaseLabel(int index) => index switch
    {
        0 => "New Moon",
        1 => "Waxing Crescent",
        2 => "First Quarter",
        3 => "Waxing Gibbous",
        4 => "Full Moon",
        5 => "Waning Gibbous",
        6 => "Last Quarter",
        _ => "Waning Crescent"
    };

    private static string ModeLabel(string modeId) => modeId switch
    {
        UxPrototypeIds.PlayModeId => "Play",
        UxPrototypeIds.RoadEditorModeId => "Road Editor",
        UxPrototypeIds.ObstacleEditorModeId => "Obstacle Editor",
        UxPrototypeIds.NavmeshModeId => "Navmesh",
        _ => modeId
    };

    private static int ProgressPercent(float progress, float duration)
    {
        if (duration <= 0f)
        {
            return 100;
        }

        return (int)MathF.Round(Math.Clamp(progress / duration, 0f, 1f) * 100f);
    }

    private static string CultureName(string id) =>
        string.IsNullOrWhiteSpace(id) ? "Unknown" : char.ToUpperInvariant(id[0]) + id[1..];

    private SkillEntry CreateSkillEntry(string actionId, string hotkey, string label, string summary, bool enabled)
    {
        float cooldown = _skillCooldowns.TryGetValue(actionId, out float remaining) ? remaining : 0f;
        bool active = cooldown > 0f;
        string countText = active ? $"{MathF.Ceiling(cooldown):0}s" : "Ready";
        return new SkillEntry(actionId, hotkey, label, summary, enabled && !active, active, countText);
    }

    private void TriggerSkill(GameEngine engine, string actionId)
    {
        switch (actionId)
        {
            case "fireball":
                StartCooldown(actionId, 4f);
                AddEvent("Mage cast Fireball at the current pointer locus.");
                break;
            case "volley":
                StartCooldown(actionId, 6f);
                AddEvent(_smartCastBatch
                    ? "Volley primed in batch mode for all valid ranged casters."
                    : "Volley channeled by the selected ranged unit.");
                break;
            case "heal":
                StartCooldown(actionId, 3f);
                AddEvent("Medic released Heal over the nearby formation.");
                break;
            case "charge":
                StartCooldown(actionId, 8f);
                AddEvent("Heavy Cavalry committed Charge along the frontline vector.");
                break;
            case "siege_shot":
                StartCooldown(actionId, 10f);
                AddEvent("Catapult released Siege Shot toward hostile fortifications.");
                break;
            case "leap_slash":
                StartCooldown(actionId, 5f);
                AddEvent("Soldier executed Leap Slash into melee range.");
                break;
            default:
                AddEvent($"Action {actionId} is not available in the current selection.");
                break;
        }
    }

    private void StartCooldown(string actionId, float seconds)
    {
        _skillCooldowns[actionId] = Math.Max(seconds, 0.5f);
    }

    private static bool TryResolveBuildTemplate(string actionId, out string? templateId)
    {
        templateId = actionId.StartsWith("build:", StringComparison.OrdinalIgnoreCase)
            ? actionId["build:".Length..]
            : null;
        return !string.IsNullOrWhiteSpace(templateId);
    }

    private static bool TryResolveUnitTemplate(string actionId, out string? templateId)
    {
        templateId = actionId.StartsWith("train:", StringComparison.OrdinalIgnoreCase)
            ? actionId["train:".Length..]
            : null;
        return !string.IsNullOrWhiteSpace(templateId);
    }

    private static string ToBuildActionId(string templateId) => $"build:{templateId}";

    private static string ToTrainActionId(string templateId) => $"train:{templateId}";

    private void RefundProduction(string templateId)
    {
        _food += 20;
        _credits += 10;
        if (templateId.Contains("cavalry", StringComparison.OrdinalIgnoreCase) ||
            templateId.Contains("catapult", StringComparison.OrdinalIgnoreCase) ||
            templateId.Contains("well_column", StringComparison.OrdinalIgnoreCase) ||
            templateId.Contains("ram", StringComparison.OrdinalIgnoreCase))
        {
            _credits += 20;
        }
    }

    private void RefundConstruction(string templateId)
    {
        _credits += 40;
        _iron += templateId switch
        {
            "ux_fort" => 60,
            "ux_workshop" => 50,
            "ux_stable" => 45,
            "ux_barracks" => 40,
            _ => 25
        };
    }

    private void AddEvent(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return;
        }

        _events.Insert(0, message);
        if (_events.Count > 6)
        {
            _events.RemoveRange(6, _events.Count - 6);
        }
    }

    private sealed class ProductionLaneState
    {
        public ProductionLaneState(int producerId, string producerLabel)
        {
            ProducerId = producerId;
            ProducerLabel = producerLabel;
        }

        public int ProducerId { get; }
        public string ProducerLabel { get; }
        public List<ProductionItemState> Queue { get; } = new();
    }

    private readonly record struct ProductionItemState(string TemplateId, float Progress, float Duration);
    private readonly record struct ConstructionState(string TemplateId, WorldCmInt2 Target, float Progress, float Duration, string BuilderLabel);

    private sealed class EntityCountSummary
    {
        public int Cities;
        public int Barracks;
        public int Stables;
        public int Workshops;
        public int Towers;
        public int Watchtowers;
        public int Forts;
        public int Farms;
        public int Mines;
        public int Workers;
        public int Soldiers;
        public int Archers;
        public int Mages;
        public int Medics;
        public int HeavyCavalry;
        public int HorseArchers;
        public int Rams;
        public int Catapults;
        public int WellColumns;
        public int MaxX;
        public int MaxY;
    }
}
