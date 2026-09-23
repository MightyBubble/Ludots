using Arch.Core;
using Ludots.Core.Components;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.Components;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.Gameplay.Items;
using Ludots.Core.Gameplay.Teams;
using Ludots.Core.Input.Selection;
using Ludots.Core.Mathematics;
using Ludots.Core.Modding;
using Ludots.Core.NodeLibraries.GASGraph;
using Ludots.Core.NodeLibraries.GASGraph.Host;
using Ludots.Core.Scripting;
using Ludots.UI;
using SanguoGrandStrategyMod.UI;

namespace SanguoGrandStrategyMod.Runtime;

public sealed class SanguoGrandStrategyRuntime
{
    private const string CityEconomyGraphId = "sanguo.graph.cityEconomyQuery";
    private const string WeiFrontierGraphId = "sanguo.graph.weiFrontierQuery";

    private readonly IModContext _context;
    private readonly SanguoGrandStrategyPanelController _panelController;
    private readonly FactionRuntimeState[] _factions;
    private readonly UnitTypeDefinition[] _unitTypes;
    private readonly CityDefinition[] _cityDefinitions;
    private readonly List<CityRuntimeState> _cities = new(300);
    private readonly List<string> _log = new(16);
    private readonly List<Entity> _itemScratch = new(8);
    private readonly object _sync = new();
    private GameEngine? _engine;
    private IGraphRuntimeApi? _graphApi;
    private bool _scenarioReady;
    private bool _commanderEquipmentReady;
    private int _selectedCityIndex;
    private int _selectedUnitTypeIndex;
    private float _seconds;
    private int _turn = 1;
    private int _graphExecutions;
    private uint _graphSeed = 0xA341316Cu;
    private Entity _commanderEntity = Entity.Null;
    private Entity _equipmentContainer = Entity.Null;
    private SanguoGrandStrategyGraphView _graphSummary = new(0, 0, 0, 0, 0, 0, 0, "n/a", "n/a");
    private PendingBattle? _pendingBattle;
    private string _webUiStatus = "WebUI DataPlane: waiting for host browser runtime.";

    public SanguoGrandStrategyRuntime(IModContext context)
    {
        _context = context;
        _panelController = new SanguoGrandStrategyPanelController(this);
        _factions = SanguoScenarioData.CreateFactions().Select(static f => new FactionRuntimeState(f)).ToArray();
        _unitTypes = SanguoScenarioData.CreateUnitTypes();
        _cityDefinitions = SanguoScenarioData.CreateCities();
    }

    public int CityCount => _cityDefinitions.Length;
    public int UnitTypeCount => _unitTypes.Length;
    public int FactionCount => _factions.Length;
    public string WebUiStatus => _webUiStatus;

    public Task HandleMapFocusedAsync(ScriptContext context)
    {
        GameEngine? engine = context.GetEngine();
        if (engine == null)
        {
            return Task.CompletedTask;
        }

        if (!SanguoGrandStrategyIds.IsShowcaseMap(engine.CurrentMapSession?.MapId.Value))
        {
            ClearPanel(engine);
            return Task.CompletedTask;
        }

        _engine = engine;
        EnsureScenario(engine);
        RefreshPanel(engine);
        return Task.CompletedTask;
    }

    public Task HandleMapUnloadedAsync(ScriptContext context)
    {
        if (!context.TryGet(CoreServiceKeys.MapId, out Ludots.Core.Map.MapId mapId) ||
            !SanguoGrandStrategyIds.IsShowcaseMap(mapId.Value))
        {
            return Task.CompletedTask;
        }

        GameEngine? engine = context.GetEngine();
        if (engine != null)
        {
            ClearPanel(engine);
        }

        ResetScenario();
        return Task.CompletedTask;
    }

    public void Tick(GameEngine engine, float dt)
    {
        if (!SanguoGrandStrategyIds.IsShowcaseMap(engine.CurrentMapSession?.MapId.Value))
        {
            return;
        }

        EnsureScenario(engine);
        lock (_sync)
        {
            SyncCitiesFromAttributes(engine);
            ExecuteScenarioGraphs(engine);
            RecalculateFactions();
        }

        _seconds += MathF.Max(0f, dt);
        if (_seconds < 1f)
        {
            return;
        }

        _seconds = 0f;
        lock (_sync)
        {
            RunEconomyTurn(engine);
            if ((_turn % 3) == 0)
            {
                RunAiTurn(engine);
            }

            if (_pendingBattle is { TurnsRemaining: > 0 } battle)
            {
                _pendingBattle = battle with { TurnsRemaining = battle.TurnsRemaining - 1 };
            }

            _turn++;
            RecalculateFactions();
            ExecuteScenarioGraphs(engine);
        }
    }

    public void RefreshPanel(GameEngine engine)
    {
        if (!SanguoGrandStrategyIds.IsShowcaseMap(engine.CurrentMapSession?.MapId.Value))
        {
            ClearPanel(engine);
            return;
        }

        if (engine.GetService(CoreServiceKeys.UIRoot) is UIRoot root)
        {
            _panelController.MountOrRefresh(root, engine);
        }
    }

    public void SetWebUiStatus(string status)
    {
        if (string.IsNullOrWhiteSpace(status))
        {
            return;
        }

        lock (_sync)
        {
            _webUiStatus = status.Trim();
        }
    }

    public void SelectNextCity(GameEngine engine)
    {
        lock (_sync)
        {
            EnsureScenario(engine);
            _selectedCityIndex = (_selectedCityIndex + 1) % Math.Max(1, _cities.Count);
            SelectCurrentCityInCore(engine);
            AddLog($"Selected {SelectedCity.Name}.");
        }
    }

    public void SelectPreviousCity(GameEngine engine)
    {
        lock (_sync)
        {
            EnsureScenario(engine);
            _selectedCityIndex = (_selectedCityIndex - 1 + Math.Max(1, _cities.Count)) % Math.Max(1, _cities.Count);
            SelectCurrentCityInCore(engine);
            AddLog($"Selected {SelectedCity.Name}.");
        }
    }

    public void SelectNextUnitType(GameEngine engine)
    {
        lock (_sync)
        {
            EnsureScenario(engine);
            _selectedUnitTypeIndex = (_selectedUnitTypeIndex + 1) % _unitTypes.Length;
            AddLog($"Doctrine focus: {SelectedUnitType.Name}.");
        }
    }

    public bool SelectCityById(GameEngine engine, string cityId)
    {
        if (string.IsNullOrWhiteSpace(cityId))
        {
            return false;
        }

        lock (_sync)
        {
            EnsureScenario(engine);
            int index = _cities.FindIndex(city => string.Equals(city.Definition.Id, cityId, StringComparison.Ordinal));
            if (index < 0)
            {
                return false;
            }

            _selectedCityIndex = index;
            SelectCurrentCityInCore(engine);
            AddLog($"Selected {_cities[index].Name} from WebUI.");
            return true;
        }
    }

    public void Conscript(GameEngine engine)
    {
        lock (_sync)
        {
            EnsureScenario(engine);
            CityRuntimeState city = SelectedCity;
            if (city.Food < 600 || city.Gold < 160)
            {
                AddLog($"{city.Name} cannot conscript: food or gold is short.");
                return;
            }

            PublishCityEffect(engine, city, "Effect.Sanguo.Conscript");
            AddLog($"{city.Name} queued conscription through GAS effect pipeline (+800 troops).");
        }
    }

    public void TrainElite(GameEngine engine)
    {
        lock (_sync)
        {
            EnsureScenario(engine);
            CityRuntimeState city = SelectedCity;
            UnitTypeDefinition unit = SelectedUnitType;
            if (city.Food < unit.CostFood || city.Gold < unit.CostGold)
            {
                AddLog($"{city.Name} cannot train {unit.Name}: cost {unit.CostGold} gold / {unit.CostFood} food.");
                return;
            }

            PublishCityEffect(engine, city, "Effect.Sanguo.TrainElite");
            AddLog($"{city.Name} queued {unit.Name} training through GAS effect pipeline.");
        }
    }

    public void Develop(GameEngine engine)
    {
        lock (_sync)
        {
            EnsureScenario(engine);
            CityRuntimeState city = SelectedCity;
            if (city.Gold < 220)
            {
                AddLog($"{city.Name} lacks gold for development.");
                return;
            }

            PublishCityEffect(engine, city, "Effect.Sanguo.Develop");
            AddLog($"{city.Name} queued market and workshop development through GAS.");
        }
    }

    public void Tax(GameEngine engine)
    {
        lock (_sync)
        {
            EnsureScenario(engine);
            CityRuntimeState city = SelectedCity;
            PublishCityEffect(engine, city, "Effect.Sanguo.Tax");
            AddLog($"{city.Name} queued emergency tax through GAS effect pipeline.");
        }
    }

    public void Harvest(GameEngine engine)
    {
        lock (_sync)
        {
            EnsureScenario(engine);
            CityRuntimeState city = SelectedCity;
            PublishCityEffect(engine, city, "Effect.Sanguo.Harvest");
            AddLog($"{city.Name} queued harvest logistics through GAS effect pipeline.");
        }
    }

    public void March(GameEngine engine)
    {
        lock (_sync)
        {
            EnsureScenario(engine);
            CityRuntimeState source = SelectedCity;
            CityRuntimeState? target = FindNearestHostile(source);
            if (target == null)
            {
                AddLog("No hostile city is in range.");
                return;
            }

            UnitTypeDefinition unit = SelectedUnitType;
            int marchingTroops = Math.Min(source.Troops / 3, 4500 + unit.Tier * 400);
            if (marchingTroops < 600)
            {
                AddLog($"{source.Name} needs at least 600 troops to march.");
                return;
            }

            PublishCityEffect(engine, source, "Effect.Sanguo.March");
            source.Troops -= marchingTroops;
            _pendingBattle = new PendingBattle(source.Definition.Id, target.Definition.Id, source.TeamId, marchingTroops, unit, TurnsRemaining: 2);
            SyncCity(engine, source);
            RecalculateFactions();
            AddLog($"{source.Name} sent {marchingTroops} {unit.Name} toward {target.Name}; march logistics queued in GAS.");
        }
    }

    public void ResolveBattle(GameEngine engine)
    {
        lock (_sync)
        {
            EnsureScenario(engine);
            if (_pendingBattle == null)
            {
                AddLog("No pending campaign battle.");
                return;
            }

            PendingBattle battle = _pendingBattle;
            CityRuntimeState? target = FindCity(battle.TargetCityId);
            if (target == null || target.TeamId == battle.AttackerTeamId)
            {
                _pendingBattle = null;
                AddLog("Campaign target is no longer valid.");
                return;
            }

            int attackScore = battle.Troops + battle.UnitType.Attack * 85 + battle.UnitType.Mobility * 45;
            int defenseScore = target.Troops / 2 + target.Defense * 70 + target.Morale * 35;
            if (attackScore > defenseScore)
            {
                int survivors = Math.Max(500, battle.Troops - target.Troops / 4);
                int oldTeam = target.TeamId;
                target.TeamId = battle.AttackerTeamId;
                target.Troops = survivors;
                target.Morale = 45;
                target.Loyalty = 35;
                target.Defense = Clamp(target.Defense - 5, 1, 160);
                SyncCity(engine, target);
                RecalculateFactions();
                AddLog($"{target.Name} fell from Team {oldTeam} to Team {battle.AttackerTeamId}; {survivors} troops garrisoned.");
            }
            else
            {
                target.Troops = Math.Max(500, target.Troops - battle.Troops / 5);
                target.Morale = Clamp(target.Morale + 5, 1, 100);
                SyncCity(engine, target);
                RecalculateFactions();
                AddLog($"{target.Name} held. Attack {attackScore} vs defense {defenseScore}.");
            }

            _pendingBattle = null;
        }
    }

    public void Research(GameEngine engine)
    {
        lock (_sync)
        {
            EnsureScenario(engine);
            CityRuntimeState city = SelectedCity;
            if (city.Gold < 240)
            {
                AddLog($"{city.Name} lacks gold for doctrine research.");
                return;
            }

            PublishCityEffect(engine, city, "Effect.Sanguo.Research");
            AddLog($"{city.Name} queued doctrine research through GAS effect pipeline.");
        }
    }

    public void Diplomacy(GameEngine engine)
    {
        lock (_sync)
        {
            EnsureScenario(engine);
            FactionRuntimeState target = _factions
                .Where(static faction => faction.Definition.TeamId != 1)
                .OrderBy(static faction => faction.TrustWithPlayer)
                .First();
            target.TrustWithPlayer = Clamp(target.TrustWithPlayer + 12, 0, 100);
            target.Stance = target.TrustWithPlayer >= 70 ? "Friendly" : target.TrustWithPlayer <= 25 ? "Hostile" : "Neutral";
            PublishCityEffect(engine, SelectedCity, "Effect.Sanguo.Diplomacy");
            TeamManager.SetRelationshipSymmetric(
                1,
                target.Definition.TeamId,
                target.Stance == "Friendly" ? TeamRelationship.Friendly : target.Stance == "Hostile" ? TeamRelationship.Hostile : TeamRelationship.Neutral);
            AddLog($"Diplomacy improved {target.Definition.Name} trust to {target.TrustWithPlayer} ({target.Stance}).");
        }
    }

    internal SanguoGrandStrategyPanelState BuildPanelState()
    {
        lock (_sync)
        {
            CityRuntimeState city = _cities.Count == 0 ? new CityRuntimeState(_cityDefinitions[0], Entity.Null) : SelectedCity;
            UnitTypeDefinition unit = SelectedUnitType;
            return new SanguoGrandStrategyPanelState(
                Header: "Sanguo Grand Strategy",
                Summary: $"Turn {_turn} | {_cityDefinitions.Length} cities | {_unitTypes.Length} unit types | {_factions.Length} factions",
                WebUiStatus: _webUiStatus,
                SelectedCityLine: $"{city.Name} [{FactionName(city.TeamId)}] {city.Region}",
                SelectedUnitLine: $"{unit.Name} atk {unit.Attack} def {unit.Defense} cost {unit.CostGold}G/{unit.CostFood}F",
                EquipmentLine: BuildEquipmentLine(),
                CommanderLine: BuildCommanderLine(),
                PendingBattleLine: BuildPendingBattleLine(),
                OverviewLines: BuildOverviewLines(),
                CityLines: BuildCityLines(city),
                FactionLines: BuildFactionLines(),
                UnitLines: BuildUnitLines(),
                LogLines: _log.Count == 0 ? new[] { "Scenario loaded." } : _log.ToArray());
        }
    }

    public SanguoGrandStrategySnapshot BuildDataPlaneSnapshot()
    {
        lock (_sync)
        {
            CityRuntimeState selected = _cities.Count == 0 ? new CityRuntimeState(_cityDefinitions[0], Entity.Null) : SelectedCity;
            UnitTypeDefinition unit = SelectedUnitType;
            return new SanguoGrandStrategySnapshot(
                _turn,
                _cityDefinitions.Length,
                _unitTypes.Length,
                _factions.Length,
                selected.Definition.Id,
                unit.Id,
                _webUiStatus,
                BuildEquipmentLine(),
                BuildCommanderLine(),
                _graphSummary,
                _factions.Select(static faction => new SanguoGrandStrategyFactionView(
                    faction.Definition.TeamId,
                    faction.Definition.Name,
                    faction.Definition.Color,
                    faction.Cities,
                    faction.Population,
                    faction.Troops,
                    faction.Food,
                    faction.Gold,
                    faction.Production,
                    faction.Stance,
                    faction.TrustWithPlayer)).ToArray(),
                _cities.Select(city => new SanguoGrandStrategyCityView(
                    city.Definition.Id,
                    city.Definition.Name,
                    city.Definition.Region,
                    city.TeamId,
                    city.Definition.X,
                    city.Definition.Y,
                    city.Population,
                    city.Troops,
                    city.Food,
                    city.Gold,
                    city.Production,
                    city.Defense,
                    city.Morale,
                    city.Loyalty,
                    ReferenceEquals(city, selected))).ToArray(),
                _log.ToArray());
        }
    }

    private void EnsureScenario(GameEngine engine)
    {
        if (_scenarioReady)
        {
            return;
        }

        var session = engine.CurrentMapSession
            ?? throw new InvalidOperationException("Sanguo scenario requires a focused map session.");
        if (!SanguoGrandStrategyIds.IsShowcaseMap(session.MapId.Value))
        {
            return;
        }

        _cities.Clear();
        for (int i = 0; i < _cityDefinitions.Length; i++)
        {
            CityDefinition definition = _cityDefinitions[i];
            if (!session.EntityIndex.TryGet(definition.Id, out Entity entity) || !engine.World.IsAlive(entity))
            {
                throw new InvalidOperationException($"Sanguo map is missing city entity '{definition.Id}'.");
            }

            EnsureCityComponents(engine.World, entity);
            var state = new CityRuntimeState(definition, entity);
            _cities.Add(state);
            SyncCity(engine, state);
        }

        int firstPlayerCity = _cities.FindIndex(static city => city.TeamId == 1);
        _selectedCityIndex = firstPlayerCity >= 0 ? firstPlayerCity : 0;
        _selectedUnitTypeIndex = 0;
        _turn = 1;
        _seconds = 0f;
        _pendingBattle = null;
        _scenarioReady = true;
        SetupLocalSelection(engine);
        EnsureCommanderEquipment(engine);
        RecalculateFactions();
        ExecuteScenarioGraphs(engine);
        AddLog("Scenario ready: 300 seamless China-map cities and 100 unit types loaded.");
    }

    private void ResetScenario()
    {
        lock (_sync)
        {
            _engine = null;
            _scenarioReady = false;
            _cities.Clear();
            _log.Clear();
            _pendingBattle = null;
            _turn = 1;
            _seconds = 0f;
            _selectedCityIndex = 0;
            _selectedUnitTypeIndex = 0;
            _commanderEntity = Entity.Null;
            _equipmentContainer = Entity.Null;
            _commanderEquipmentReady = false;
            _graphApi = null;
            _graphExecutions = 0;
            _graphSummary = new SanguoGrandStrategyGraphView(0, 0, 0, 0, 0, 0, 0, "n/a", "n/a");
        }
    }

    private void RunEconomyTurn(GameEngine engine)
    {
        for (int i = 0; i < _cities.Count; i++)
        {
            CityRuntimeState city = _cities[i];
            city.Food += Math.Max(50, city.Production * 5 + city.Population / 700);
            city.Gold += Math.Max(25, city.Production * 3 + city.Loyalty * 2);
            city.Population += Math.Max(12, city.Loyalty + city.Morale - 80);
            city.Supply = Clamp(city.Supply + city.Production / 20, 0, 140);
            if (city.Food < city.Troops / 2)
            {
                city.Morale = Clamp(city.Morale - 1, 1, 100);
            }

            SyncCity(engine, city);
        }

        if ((_turn % 5) == 0)
        {
            AddLog($"Turn {_turn}: seasonal income resolved across {_cities.Count} cities.");
        }
    }

    private void RunAiTurn(GameEngine engine)
    {
        for (int teamId = 2; teamId <= _factions.Length; teamId++)
        {
            CityRuntimeState? city = _cities
                .Where(c => c.TeamId == teamId)
                .OrderByDescending(static c => c.Production + c.Defense)
                .FirstOrDefault();
            if (city == null)
            {
                continue;
            }

            if (city.Gold > 500 && city.Food > 900)
            {
                city.Gold -= 180;
                city.Food -= 360;
                city.Troops += 480 + city.Training * 2;
                city.Training = Clamp(city.Training + 1, 1, 140);
            }
            else
            {
                city.Production += 1;
                city.Food += 500;
            }

            SyncCity(engine, city);
        }
    }

    private void SyncCitiesFromAttributes(GameEngine engine)
    {
        World world = engine.World;
        for (int i = 0; i < _cities.Count; i++)
        {
            CityRuntimeState city = _cities[i];
            if (!world.IsAlive(city.Entity) || !world.Has<AttributeBuffer>(city.Entity))
            {
                continue;
            }

            if (world.Has<Team>(city.Entity))
            {
                city.TeamId = world.Get<Team>(city.Entity).Id;
            }

            city.Population = Math.Max(1000, ReadAttribute(world, city.Entity, "Population"));
            city.Troops = Math.Max(0, ReadAttribute(world, city.Entity, "Troops"));
            city.Food = Math.Max(0, ReadAttribute(world, city.Entity, "Food"));
            city.Gold = Math.Max(0, ReadAttribute(world, city.Entity, "Gold"));
            city.Production = Math.Max(1, ReadAttribute(world, city.Entity, "Production"));
            city.Defense = Clamp(ReadAttribute(world, city.Entity, "Defense"), 1, 160);
            city.Morale = Clamp(ReadAttribute(world, city.Entity, "Morale"), 1, 100);
            city.Loyalty = Clamp(ReadAttribute(world, city.Entity, "Loyalty"), 1, 100);
            city.Training = Clamp(ReadAttribute(world, city.Entity, "Training"), 1, 140);
            city.Supply = Clamp(ReadAttribute(world, city.Entity, "Supply"), 0, 140);
            city.Command = Clamp(ReadAttribute(world, city.Entity, "Command"), 1, 160);
            city.TechProgress = Math.Max(0, ReadAttribute(world, city.Entity, "TechProgress"));
            SyncCity(engine, city);
        }
    }

    private void PublishCityEffect(GameEngine engine, CityRuntimeState city, string effectId)
    {
        if (!engine.World.IsAlive(city.Entity))
        {
            throw new InvalidOperationException($"Cannot publish Sanguo effect '{effectId}' for dead city '{city.Definition.Id}'.");
        }

        int templateId = EffectTemplateIdRegistry.GetId(effectId);
        if (templateId <= 0)
        {
            throw new InvalidOperationException($"Sanguo effect template '{effectId}' is not registered.");
        }

        EffectRequestQueue queue = engine.GetService(CoreServiceKeys.EffectRequestQueue)
            ?? throw new InvalidOperationException("EffectRequestQueue service is missing.");
        queue.Publish(new EffectRequest
        {
            Source = city.Entity,
            Target = city.Entity,
            TemplateId = templateId
        });
    }

    private void ExecuteScenarioGraphs(GameEngine engine)
    {
        Entity owner = ResolveGraphOwner(engine);
        if (owner == Entity.Null)
        {
            return;
        }

        int cityEconomyGraphId = GraphIdRegistry.GetId(CityEconomyGraphId);
        int weiFrontierGraphId = GraphIdRegistry.GetId(WeiFrontierGraphId);
        if (cityEconomyGraphId <= 0 || weiFrontierGraphId <= 0)
        {
            throw new InvalidOperationException("Sanguo graph programs are not registered.");
        }

        GraphReturnWriter writer = engine.GetService(CoreServiceKeys.GraphReturnWriter)
            ?? throw new InvalidOperationException("GraphReturnWriter service is missing.");
        IGraphRuntimeApi api = _graphApi ??= GasGraphRuntimeApi.CreateProduction(
            engine.World,
            engine.SpatialQueries,
            engine.SpatialCoords,
            engine.EventBus,
            engine.GetService(CoreServiceKeys.EffectRequestQueue),
            engine.GlobalContext);

        IntVector2 targetPos = default;
        writer.ExecuteAndWrite(cityEconomyGraphId, owner, owner, Entity.Null, Entity.Null, targetPos, NextGraphSeed(), api);
        writer.ExecuteAndWrite(weiFrontierGraphId, owner, owner, Entity.Null, Entity.Null, targetPos, NextGraphSeed(), api);
        _graphExecutions++;
        RefreshGraphSummary(engine, owner);
    }

    private Entity ResolveGraphOwner(GameEngine engine)
    {
        if (_commanderEntity != Entity.Null && engine.World.IsAlive(_commanderEntity))
        {
            return _commanderEntity;
        }

        Entity owner = engine.GetService(CoreServiceKeys.LocalPlayerEntity);
        return owner != Entity.Null && engine.World.IsAlive(owner) ? owner : Entity.Null;
    }

    private void RefreshGraphSummary(GameEngine engine, Entity owner)
    {
        GraphOutputValueStore values = engine.GetService(CoreServiceKeys.GraphOutputValueStore)
            ?? throw new InvalidOperationException("GraphOutputValueStore service is missing.");
        Entity bestProduction = ReadGraphEntity(values, owner, "sanguo.summary.bestProductionCity");
        Entity weiStrongest = ReadGraphEntity(values, owner, "sanguo.summary.weiStrongestCity");
        _graphSummary = new SanguoGrandStrategyGraphView(
            _graphExecutions,
            ReadGraphInt(values, owner, "sanguo.summary.cityCount"),
            ReadGraphInt(values, owner, "sanguo.summary.weiFrontierCount"),
            (int)ReadGraphFloat(values, owner, "sanguo.summary.population"),
            (int)ReadGraphFloat(values, owner, "sanguo.summary.food"),
            (int)ReadGraphFloat(values, owner, "sanguo.summary.gold"),
            (int)ReadGraphFloat(values, owner, "sanguo.summary.weiTroops"),
            CityName(bestProduction),
            CityName(weiStrongest));
    }

    private int ReadGraphInt(GraphOutputValueStore values, Entity owner, string key)
    {
        return values.TryGet(owner, key, out GraphOutputValueHandle handle) &&
               values.TryGetView(handle, out GraphOutputValueView view)
            ? view.IntValue
            : 0;
    }

    private float ReadGraphFloat(GraphOutputValueStore values, Entity owner, string key)
    {
        return values.TryGet(owner, key, out GraphOutputValueHandle handle) &&
               values.TryGetView(handle, out GraphOutputValueView view)
            ? view.FloatValue
            : 0f;
    }

    private Entity ReadGraphEntity(GraphOutputValueStore values, Entity owner, string key)
    {
        return values.TryGet(owner, key, out GraphOutputValueHandle handle) &&
               values.TryGetView(handle, out GraphOutputValueView view)
            ? view.EntityValue
            : Entity.Null;
    }

    private uint NextGraphSeed()
    {
        _graphSeed ^= _graphSeed << 13;
        _graphSeed ^= _graphSeed >> 17;
        _graphSeed ^= _graphSeed << 5;
        return _graphSeed == 0u ? 1u : _graphSeed;
    }

    private void EnsureCommanderEquipment(GameEngine engine)
    {
        if (_commanderEquipmentReady &&
            engine.World.IsAlive(_commanderEntity) &&
            engine.World.IsAlive(_equipmentContainer))
        {
            return;
        }

        Entity owner = engine.GetService(CoreServiceKeys.LocalPlayerEntity);
        if (owner == Entity.Null || !engine.World.IsAlive(owner))
        {
            owner = engine.World.Create(new PlayerOwner { PlayerId = 1 });
            engine.SetService(CoreServiceKeys.LocalPlayerEntity, owner);
            engine.SetService(CoreServiceKeys.LocalPlayerId, 1);
        }

        _commanderEntity = owner;
        EnsureCommanderAttributes(engine.World, owner);

        InventoryRuntimeService inventory = engine.GetService(CoreServiceKeys.InventoryRuntimeService)
            ?? throw new InvalidOperationException("InventoryRuntimeService service is missing.");
        ItemLayoutRegistry layouts = engine.GetService(CoreServiceKeys.ItemLayoutRegistry)
            ?? throw new InvalidOperationException("ItemLayoutRegistry service is missing.");
        int layoutId = layouts.GetId("sanguo_layout_commander");
        if (layoutId <= 0)
        {
            throw new InvalidOperationException("Sanguo commander equipment layout is not registered.");
        }

        _equipmentContainer = inventory.CreateContainer(owner, layoutId, ItemContainerPurpose.Equipment);
        EquipFreshItem(engine, inventory, "itm_sanguo_green_dragon_blade", "weapon");
        EquipFreshItem(engine, inventory, "itm_sanguo_mingguang_armor", "armor");
        EquipFreshItem(engine, inventory, "itm_sanguo_red_hare", "mount");
        EquipFreshItem(engine, inventory, "itm_sanguo_prime_minister_seal", "seal");
        EquipFreshItem(engine, inventory, "itm_sanguo_art_of_war", "manual");
        _commanderEquipmentReady = true;
        AddLog("Commander equipment equipped through InventoryRuntimeService; passive item effects are live.");
    }

    private void EquipFreshItem(GameEngine engine, InventoryRuntimeService inventory, string itemId, string slotId)
    {
        ItemDefinitionRegistry definitions = engine.GetService(CoreServiceKeys.ItemDefinitionRegistry)
            ?? throw new InvalidOperationException("ItemDefinitionRegistry service is missing.");
        int definitionId = definitions.GetId(itemId);
        if (definitionId <= 0)
        {
            throw new InvalidOperationException($"Sanguo item definition '{itemId}' is not registered.");
        }

        Entity item = inventory.CreateItem(definitionId);
        if (!inventory.TryMoveItemToNamedSlot(item, _equipmentContainer, slotId))
        {
            throw new InvalidOperationException($"Could not equip Sanguo item '{itemId}' into slot '{slotId}'.");
        }
    }

    private static void EnsureCommanderAttributes(World world, Entity owner)
    {
        if (!world.Has<AttributeBuffer>(owner))
        {
            world.Add(owner, new AttributeBuffer());
        }

        SetAttributeBase(world, owner, "Command", 34);
        SetAttributeBase(world, owner, "Defense", 18);
        SetAttributeBase(world, owner, "Morale", 58);
        SetAttributeBase(world, owner, "Supply", 72);
        SetAttributeBase(world, owner, "Training", 24);
        SetAttributeBase(world, owner, "TechProgress", 0);
        SetAttributeBase(world, owner, "Loyalty", 70);
        SetAttributeBase(world, owner, "Food", 0);
        SetAttributeBase(world, owner, "Production", 0);
    }

    private void RecalculateFactions()
    {
        for (int i = 0; i < _factions.Length; i++)
        {
            FactionRuntimeState faction = _factions[i];
            faction.Cities = 0;
            faction.Population = 0;
            faction.Troops = 0;
            faction.Food = 0;
            faction.Gold = 0;
            faction.Production = 0;
            if (faction.Definition.TeamId == 1)
            {
                faction.Stance = "Player";
            }
            else
            {
                faction.Stance = faction.TrustWithPlayer >= 70 ? "Friendly" : faction.TrustWithPlayer <= 25 ? "Hostile" : "Neutral";
            }
        }

        for (int i = 0; i < _cities.Count; i++)
        {
            CityRuntimeState city = _cities[i];
            FactionRuntimeState? faction = FindFaction(city.TeamId);
            if (faction == null)
            {
                continue;
            }

            faction.Cities++;
            faction.Population += city.Population;
            faction.Troops += city.Troops;
            faction.Food += city.Food;
            faction.Gold += city.Gold;
            faction.Production += city.Production;
        }
    }

    private void SetupLocalSelection(GameEngine engine)
    {
        World world = engine.World;
        Entity owner = engine.GetService(CoreServiceKeys.LocalPlayerEntity);
        if (!world.IsAlive(owner))
        {
            owner = world.Create(new PlayerOwner { PlayerId = 1 });
            engine.SetService(CoreServiceKeys.LocalPlayerEntity, owner);
            engine.SetService(CoreServiceKeys.LocalPlayerId, 1);
        }

        var selection = engine.GetService(CoreServiceKeys.SelectionRuntime);
        if (selection == null || _cities.Count == 0)
        {
            return;
        }

        selection.TryBindView(owner, SelectionViewKeys.Primary, owner, SelectionSetKeys.LivePrimary);
        engine.GlobalContext[CoreServiceKeys.SelectionViewViewerEntity.Name] = owner;
        engine.GlobalContext[CoreServiceKeys.SelectionViewKey.Name] = SelectionViewKeys.Primary;
        SelectCurrentCityInCore(engine);
    }

    private void SelectCurrentCityInCore(GameEngine engine)
    {
        if (_cities.Count == 0)
        {
            return;
        }

        var selection = engine.GetService(CoreServiceKeys.SelectionRuntime);
        Entity owner = engine.GetService(CoreServiceKeys.LocalPlayerEntity);
        Entity target = SelectedCity.Entity;
        if (selection == null || !engine.World.IsAlive(owner) || !engine.World.IsAlive(target))
        {
            return;
        }

        Span<Entity> next = stackalloc Entity[1];
        next[0] = target;
        selection.ReplaceSelection(owner, SelectionSetKeys.LivePrimary, next);
    }

    private void SyncCity(GameEngine engine, CityRuntimeState city)
    {
        World world = engine.World;
        if (!world.IsAlive(city.Entity))
        {
            return;
        }

        if (world.Has<Team>(city.Entity))
        {
            world.Set(city.Entity, new Team { Id = city.TeamId });
        }
        else
        {
            world.Add(city.Entity, new Team { Id = city.TeamId });
        }

        if (world.Has<PlayerOwner>(city.Entity))
        {
            world.Set(city.Entity, new PlayerOwner { PlayerId = city.TeamId });
        }
        else
        {
            world.Add(city.Entity, new PlayerOwner { PlayerId = city.TeamId });
        }

        if (!world.Has<AttributeBuffer>(city.Entity))
        {
            world.Add(city.Entity, new AttributeBuffer());
        }

        SetAttributeBase(world, city.Entity, "Health", 1000 + city.Defense);
        SetAttributeBase(world, city.Entity, "Population", city.Population);
        SetAttributeBase(world, city.Entity, "Troops", city.Troops);
        SetAttributeBase(world, city.Entity, "Food", city.Food);
        SetAttributeBase(world, city.Entity, "Gold", city.Gold);
        SetAttributeBase(world, city.Entity, "Production", city.Production);
        SetAttributeBase(world, city.Entity, "Defense", city.Defense);
        SetAttributeBase(world, city.Entity, "Morale", city.Morale);
        SetAttributeBase(world, city.Entity, "Supply", city.Supply);
        SetAttributeBase(world, city.Entity, "Training", city.Training);
        SetAttributeBase(world, city.Entity, "Loyalty", city.Loyalty);
        SetAttributeBase(world, city.Entity, "Command", city.Command);
        SetAttributeBase(world, city.Entity, "TechProgress", city.TechProgress);
    }

    private static void SetAttributeBase(World world, Entity entity, string name, int value)
    {
        int id = AttributeRegistry.GetId(name);
        if (id == AttributeRegistry.InvalidId)
        {
            return;
        }

        AttributeMutationOps.SetBase(world, entity, id, value);
    }

    private static int ReadAttribute(World world, Entity entity, string name)
    {
        int id = AttributeRegistry.GetId(name);
        if (id == AttributeRegistry.InvalidId ||
            !world.IsAlive(entity) ||
            !world.Has<AttributeBuffer>(entity))
        {
            return 0;
        }

        return (int)MathF.Round(world.Get<AttributeBuffer>(entity).GetCurrent(id));
    }

    private static void EnsureCityComponents(World world, Entity entity)
    {
        if (!world.Has<SelectionSelectableTag>(entity))
        {
            world.Add(entity, new SelectionSelectableTag());
        }

        if (!world.Has<SelectionSelectableState>(entity))
        {
            world.Add(entity, SelectionSelectableState.EnabledByDefault);
        }

        if (!world.Has<WorldPositionCm>(entity))
        {
            world.Add(entity, WorldPositionCm.FromCm(0, 0));
        }
    }

    private CityRuntimeState SelectedCity => _cities[Math.Clamp(_selectedCityIndex, 0, Math.Max(0, _cities.Count - 1))];
    private UnitTypeDefinition SelectedUnitType => _unitTypes[Math.Clamp(_selectedUnitTypeIndex, 0, _unitTypes.Length - 1)];

    private CityRuntimeState? FindCity(string id)
    {
        return _cities.FirstOrDefault(city => string.Equals(city.Definition.Id, id, StringComparison.Ordinal));
    }

    private CityRuntimeState? FindNearestHostile(CityRuntimeState source)
    {
        CityRuntimeState? best = null;
        long bestDistance = long.MaxValue;
        for (int i = 0; i < _cities.Count; i++)
        {
            CityRuntimeState candidate = _cities[i];
            if (candidate.TeamId == source.TeamId)
            {
                continue;
            }

            long dx = candidate.Definition.X - source.Definition.X;
            long dy = candidate.Definition.Y - source.Definition.Y;
            long distance = dx * dx + dy * dy;
            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = candidate;
            }
        }

        return best;
    }

    private FactionRuntimeState? FindFaction(int teamId)
    {
        for (int i = 0; i < _factions.Length; i++)
        {
            if (_factions[i].Definition.TeamId == teamId)
            {
                return _factions[i];
            }
        }

        return null;
    }

    private string FactionName(int teamId) => FindFaction(teamId)?.Definition.Name ?? $"Team {teamId}";

    private string BuildPendingBattleLine()
    {
        if (_pendingBattle == null)
        {
            return "No active campaign column.";
        }

        CityRuntimeState? target = FindCity(_pendingBattle.TargetCityId);
        return target == null
            ? "Campaign column has lost its target."
            : $"{_pendingBattle.Troops} {_pendingBattle.UnitType.Name} vs {target.Name}, ETA {_pendingBattle.TurnsRemaining}";
    }

    private string[] BuildOverviewLines()
    {
        int totalPopulation = _factions.Sum(static faction => faction.Population);
        int totalTroops = _factions.Sum(static faction => faction.Troops);
        int playerCities = FindFaction(1)?.Cities ?? 0;
        int playerTroops = FindFaction(1)?.Troops ?? 0;
        return new[]
        {
            $"China map: {_cities.Count}/{_cityDefinitions.Length} cities bound to ECS entities.",
            $"Total population {totalPopulation:N0}; fielded troops {totalTroops:N0}.",
            $"Player Wei controls {playerCities} cities and {playerTroops:N0} troops.",
            $"Graph economy sample: {_graphSummary.CityCount}/{_cityDefinitions.Length} city targets, pop {_graphSummary.Population:N0}, food {_graphSummary.Food:N0}, gold {_graphSummary.Gold:N0}.",
            $"Wei frontier graph: {_graphSummary.WeiFrontierCount} cities, {_graphSummary.WeiTroops:N0} troops, strongest {_graphSummary.WeiStrongestCity}.",
            $"GraphReturnWriter executions {_graphSummary.Executions}; best production city {_graphSummary.BestProductionCity}."
        };
    }

    private string BuildEquipmentLine()
    {
        GameEngine? engine = _engine;
        if (engine == null ||
            !engine.World.IsAlive(_equipmentContainer))
        {
            return "Commander equipment: initializing.";
        }

        return "Commander equipment: " +
               $"weapon {SlotLabel(engine, "weapon")} | " +
               $"armor {SlotLabel(engine, "armor")} | " +
               $"mount {SlotLabel(engine, "mount")} | " +
               $"seal {SlotLabel(engine, "seal")} | " +
               $"manual {SlotLabel(engine, "manual")}";
    }

    private string BuildCommanderLine()
    {
        GameEngine? engine = _engine;
        if (engine == null ||
            !engine.World.IsAlive(_commanderEntity) ||
            !engine.World.Has<AttributeBuffer>(_commanderEntity))
        {
            return "Commander bonuses: waiting for item passive sync.";
        }

        World world = engine.World;
        return $"Commander bonuses: Command {ReadAttribute(world, _commanderEntity, "Command")} | " +
               $"Defense {ReadAttribute(world, _commanderEntity, "Defense")} | " +
               $"Morale {ReadAttribute(world, _commanderEntity, "Morale")} | " +
               $"Supply {ReadAttribute(world, _commanderEntity, "Supply")} | " +
               $"Training {ReadAttribute(world, _commanderEntity, "Training")}";
    }

    private string SlotLabel(GameEngine engine, string slotId)
    {
        if (!engine.World.IsAlive(_equipmentContainer) || !engine.World.Has<ItemContainerCm>(_equipmentContainer))
        {
            return "(missing)";
        }

        ItemLayoutRegistry layouts = engine.GetService(CoreServiceKeys.ItemLayoutRegistry)
            ?? throw new InvalidOperationException("ItemLayoutRegistry service is missing.");
        ItemContainerCm container = engine.World.Get<ItemContainerCm>(_equipmentContainer);
        if (!layouts.TryGet(container.LayoutId, out ItemLayoutDefinition layout) ||
            !layout.TryGetNamedSlotIndex(slotId, out int slotIndex))
        {
            return "(slot missing)";
        }

        InventoryRuntimeService inventory = engine.GetService(CoreServiceKeys.InventoryRuntimeService)
            ?? throw new InvalidOperationException("InventoryRuntimeService service is missing.");
        _itemScratch.Clear();
        inventory.CollectItemsInContainer(_equipmentContainer, _itemScratch);
        for (int i = 0; i < _itemScratch.Count; i++)
        {
            Entity item = _itemScratch[i];
            if (!engine.World.IsAlive(item) ||
                !engine.World.Has<ItemLocationCm>(item))
            {
                continue;
            }

            ItemLocationCm location = engine.World.Get<ItemLocationCm>(item);
            if (location.PlacementKind == ItemPlacementKind.NamedSlot &&
                location.NamedSlotIndex == slotIndex)
            {
                return ItemLabel(engine, item);
            }
        }

        return "(empty)";
    }

    private static string ItemLabel(GameEngine engine, Entity item)
    {
        if (!engine.World.IsAlive(item) || !engine.World.Has<ItemInstanceCm>(item))
        {
            return "(missing)";
        }

        ItemDefinitionRegistry definitions = engine.GetService(CoreServiceKeys.ItemDefinitionRegistry)
            ?? throw new InvalidOperationException("ItemDefinitionRegistry service is missing.");
        ItemInstanceCm instance = engine.World.Get<ItemInstanceCm>(item);
        return definitions.TryGet(instance.DefinitionId, out ItemDefinition definition)
            ? definition.DisplayName
            : $"Item#{item.Id}";
    }

    private string CityName(Entity entity)
    {
        if (entity == Entity.Null)
        {
            return "n/a";
        }

        for (int i = 0; i < _cities.Count; i++)
        {
            if (_cities[i].Entity == entity)
            {
                return _cities[i].Name;
            }
        }

        return $"Entity#{entity.Id}";
    }

    private static string[] BuildCityLines(CityRuntimeState city)
    {
        return new[]
        {
            $"Population {city.Population:N0} | Troops {city.Troops:N0}",
            $"Food {city.Food:N0} | Gold {city.Gold:N0} | Supply {city.Supply}",
            $"Production {city.Production} | Defense {city.Defense} | Command {city.Command}",
            $"Morale {city.Morale} | Loyalty {city.Loyalty} | Training {city.Training}",
            $"Tech progress {city.TechProgress}"
        };
    }

    private string[] BuildFactionLines()
    {
        return _factions
            .OrderByDescending(static faction => faction.Cities)
            .Select(static faction => $"{faction.Definition.Name}: {faction.Cities} cities, {faction.Troops:N0} troops, {faction.Gold:N0} gold, {faction.Stance}")
            .Take(8)
            .ToArray();
    }

    private string[] BuildUnitLines()
    {
        int start = Math.Max(0, _selectedUnitTypeIndex - 2);
        return _unitTypes
            .Skip(start)
            .Take(5)
            .Select((unit, index) =>
            {
                int actual = start + index;
                string marker = actual == _selectedUnitTypeIndex ? "*" : " ";
                return $"{marker} {unit.Name} | {unit.Category} T{unit.Tier} | A{unit.Attack}/D{unit.Defense}/M{unit.Mobility}";
            })
            .ToArray();
    }

    private void AddLog(string message)
    {
        _log.Insert(0, $"T{_turn:000}: {message}");
        if (_log.Count > 10)
        {
            _log.RemoveAt(_log.Count - 1);
        }

        _context.Log($"[SanguoGrandStrategyMod] {message}");
    }

    private void ClearPanel(GameEngine engine)
    {
        if (engine.GetService(CoreServiceKeys.UIRoot) is UIRoot root)
        {
            _panelController.ClearIfOwned(root);
        }
    }

    private static int Clamp(int value, int min, int max) => Math.Min(max, Math.Max(min, value));
}
