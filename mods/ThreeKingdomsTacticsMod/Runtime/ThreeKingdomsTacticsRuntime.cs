using Arch.Core;
using Ludots.Core.Components;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.Components;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.Gameplay.GAS.Systems;
using Ludots.Core.Gameplay.Items;
using Ludots.Core.Gameplay.Teams;
using Ludots.Core.GraphRuntime;
using Ludots.Core.Input.Selection;
using Ludots.Core.Map;
using Ludots.Core.Mathematics;
using Ludots.Core.NodeLibraries.GASGraph.Host;
using Ludots.Core.Scripting;
using Ludots.UI;
using ThreeKingdomsTacticsMod.UI;
using GasGraphExecutor = Ludots.Core.NodeLibraries.GASGraph.GraphExecutor;

namespace ThreeKingdomsTacticsMod.Runtime;

public sealed class ThreeKingdomsTacticsRuntime
{
    private readonly ThreeKingdomsTacticsPanelController _panel = new();
    private readonly List<ThreeKingdomsUnitState> _units = new(100);
    private readonly List<string> _log = new(32);
    private readonly List<Entity> _arsenalItems = new(128);

    private Entity _localPlayer = Entity.Null;
    private Entity _arsenal = Entity.Null;
    private int _selectedIndex;
    private int _turn = 1;
    private int _round = 1;
    private string _phase = "Player";
    private string _lastAction = "Scenario loaded.";
    private int _healthAttrId;
    private int _moraleAttrId;
    private int _suppliesAttrId;
    private int _leadershipAttrId;
    private int _warPowerAttrId;
    private int _strategyAttrId;
    private string _lastGraphProgram = "None";
    private int _lastGraphScore;
    private string _lastItemTransaction = "None";
    private int _developmentLevel;
    private int _campaignMomentum;
    private string _outcome = "InProgress";

    public ThreeKingdomsTacticsSnapshot Snapshot { get; private set; } = EmptySnapshot();

    public IReadOnlyList<ThreeKingdomsUnitState> Units => _units;

    public Task HandleMapFocusedAsync(ScriptContext context)
    {
        if (context.GetEngine() is not GameEngine engine)
        {
            return Task.CompletedTask;
        }

        if (IsTacticsMap(engine))
        {
            EnsureScenario(engine);
            Refresh(engine);
        }
        else
        {
            ClearPanel(engine);
        }

        return Task.CompletedTask;
    }

    public Task HandleMapUnloadedAsync(ScriptContext context)
    {
        if (context.GetEngine() is GameEngine engine)
        {
            ClearPanel(engine);
            _units.Clear();
            _arsenalItems.Clear();
            _localPlayer = Entity.Null;
            _arsenal = Entity.Null;
            _developmentLevel = 0;
            _campaignMomentum = 0;
            _outcome = "InProgress";
            Snapshot = EmptySnapshot();
        }

        return Task.CompletedTask;
    }

    public void Update(GameEngine engine, float dt)
    {
        if (!IsTacticsMap(engine))
        {
            return;
        }

        EnsureScenario(engine);
        Refresh(engine);
    }

    public void SelectNext(GameEngine engine)
    {
        EnsureScenario(engine);
        for (int i = 1; i <= _units.Count; i++)
        {
            int next = (_selectedIndex + i) % _units.Count;
            if (_units[next].TeamId == 1 && _units[next].IsAlive)
            {
                Select(engine, next);
                Log($"Selected {_units[next].General.Name}.");
                break;
            }
        }
        Refresh(engine);
    }

    public void Select(GameEngine engine, int index)
    {
        EnsureScenario(engine);
        if ((uint)index >= (uint)_units.Count || !_units[index].IsAlive)
        {
            return;
        }

        _selectedIndex = index;
        BindSelection(engine, _units[index].Entity);
        Refresh(engine);
    }

    public void MoveSelected(GameEngine engine, int dx, int dy)
    {
        EnsureScenario(engine);
        ThreeKingdomsUnitState unit = SelectedPlayerUnit();
        if (unit.Acted)
        {
            Log($"{unit.General.Name} already acted this turn.");
            Refresh(engine);
            return;
        }

        int distance = Math.Abs(dx) + Math.Abs(dy);
        int move = Math.Max(1, unit.TroopType.Move);
        if (distance > move)
        {
            Log($"{unit.General.Name} cannot move {distance}; {unit.TroopType.Name} move is {move}.");
            Refresh(engine);
            return;
        }

        int nextX = Math.Clamp(unit.X + dx, 0, ThreeKingdomsContent.MapWidth - 1);
        int nextY = Math.Clamp(unit.Y + dy, 0, ThreeKingdomsContent.MapHeight - 1);
        if (FindUnitAt(nextX, nextY, aliveOnly: true) != null)
        {
            Log($"Tile {nextX},{nextY} is occupied.");
            Refresh(engine);
            return;
        }

        unit.X = nextX;
        unit.Y = nextY;
        unit.Supplies = Math.Max(0, unit.Supplies - unit.TroopType.SupplyCost);
        unit.Acted = true;
        unit.Status = TerrainAt(nextX, nextY);
        SyncEntityPosition(engine.World, unit);
        Log($"{unit.General.Name} marched to {nextX},{nextY} through {unit.Status}.");
        Refresh(engine);
    }

    public void AttackNearest(GameEngine engine)
    {
        EnsureScenario(engine);
        ThreeKingdomsUnitState unit = SelectedPlayerUnit();
        ThreeKingdomsUnitState? target = FindNearestEnemy(unit);
        if (target == null)
        {
            Log("No enemy remains.");
            Refresh(engine);
            return;
        }

        int distance = Manhattan(unit, target);
        if (distance > Math.Max(1, unit.TroopType.Range))
        {
            Log($"{target.General.Name} is range {distance}; {unit.TroopType.Name} range is {unit.TroopType.Range}.");
            Refresh(engine);
            return;
        }

        int damage = Math.Max(8, unit.TroopType.Attack + unit.General.WarPower / 5 - target.TroopType.Defense);
        ApplyDamage(engine, unit, target, damage, "attack");
        unit.Acted = true;
        Refresh(engine);
    }

    public void CastSelectedSkill(GameEngine engine)
    {
        EnsureScenario(engine);
        ThreeKingdomsUnitState unit = SelectedPlayerUnit();
        if (unit.Cooldown > 0)
        {
            Log($"{unit.General.SkillName} cooldown {unit.Cooldown}.");
            Refresh(engine);
            return;
        }

        ThreeKingdomsUnitState? target = FindNearestEnemy(unit);
        ThreeKingdomsSkillGraphScore score = ExecuteSkillScoreGraph(engine, unit, target);
        UseGasAbility(engine, unit, target);
        int power = unit.General.SkillPower + score.Value;
        string skillLabel = $"{unit.General.SkillName} [{score.GraphId}+{score.Value}]";
        switch (unit.General.SkillPattern)
        {
            case ThreeKingdomsSkillPattern.Fire:
                if (target != null)
                {
                    ApplyDamage(engine, unit, target, power + 18, skillLabel);
                    target.Status = "Burning";
                }
                break;
            case ThreeKingdomsSkillPattern.Flood:
                if (target != null)
                {
                    ApplyDamage(engine, unit, target, power + 10, skillLabel);
                    target.Morale = Math.Max(0, target.Morale - 12 - score.Value / 8);
                    target.Status = "Flooded";
                }
                break;
            case ThreeKingdomsSkillPattern.Duel:
                if (target != null)
                {
                    ApplyDamage(engine, unit, target, power + unit.General.WarPower / 3, skillLabel);
                    unit.Morale = Math.Min(120, unit.Morale + 6 + score.Value / 12);
                }
                break;
            case ThreeKingdomsSkillPattern.Rally:
                foreach (ThreeKingdomsUnitState ally in _units)
                {
                    if (ally.TeamId == unit.TeamId && ally.IsAlive && Manhattan(unit, ally) <= 4)
                    {
                        ally.Morale = Math.Min(120, ally.Morale + 8 + score.Value / 8);
                        ally.Status = "Rallied";
                    }
                }
                Log($"{unit.General.Name} rallied nearby allies with {skillLabel}.");
                break;
            case ThreeKingdomsSkillPattern.Ambush:
                if (target != null)
                {
                    ApplyDamage(engine, unit, target, power + 22, skillLabel);
                    target.Acted = true;
                    target.Status = "Ambushed";
                }
                break;
            case ThreeKingdomsSkillPattern.Heal:
                unit.Health = Math.Min(MaxHealth(unit), unit.Health + power + 28);
                unit.Morale = Math.Min(120, unit.Morale + 6 + score.Value / 16);
                unit.Status = "Recovered";
                SyncEntityAttributes(engine.World, unit);
                Log($"{unit.General.Name} restored troops with {skillLabel}.");
                break;
            case ThreeKingdomsSkillPattern.Siege:
                if (target != null)
                {
                    ApplyDamage(engine, unit, target, power + unit.TroopType.Attack, skillLabel);
                    target.Supplies = Math.Max(0, target.Supplies - 12 - score.Value / 4);
                }
                break;
            case ThreeKingdomsSkillPattern.Supply:
                unit.Supplies = Math.Min(160, unit.Supplies + power + 30);
                unit.Morale = Math.Min(120, unit.Morale + 4 + score.Value / 16);
                unit.Status = "Supplied";
                SyncEntityAttributes(engine.World, unit);
                Log($"{unit.General.Name} restored supplies with {skillLabel}.");
                break;
            case ThreeKingdomsSkillPattern.Mobility:
                unit.Acted = false;
                unit.Morale = Math.Min(120, unit.Morale + 4 + score.Value / 16);
                unit.Status = "Swift";
                Log($"{unit.General.Name} opened a second maneuver window with {skillLabel}.");
                break;
            case ThreeKingdomsSkillPattern.Stratagem:
                if (target != null)
                {
                    target.Morale = Math.Max(0, target.Morale - (power + unit.General.Strategy / 4));
                    target.Status = target.Morale <= 20 ? "Routed" : "Shaken";
                    SyncEntityAttributes(engine.World, target);
                    Log($"{unit.General.Name} broke {target.General.Name}'s morale with {skillLabel}.");
                }
                break;
        }

        unit.Cooldown = 2;
        unit.Acted = true;
        Refresh(engine);
    }

    public void CycleTroopType(GameEngine engine)
    {
        EnsureScenario(engine);
        ThreeKingdomsUnitState unit = SelectedPlayerUnit();
        int nextIndex = (unit.TroopType.Index % ThreeKingdomsContent.TroopTypes.Count) + 1;
        ThreeKingdomsTroopTypeDefinition nextTroop = ThreeKingdomsContent.TroopTypes[nextIndex - 1];
        InventoryRuntimeService inventory = RequireInventory(engine);
        ItemDefinitionRegistry definitions = RequireItemDefinitions(engine);
        int definitionId = RequireItemDefinitionId(definitions, nextTroop);
        if (!inventory.ConsumeStackUnits(_localPlayer, definitionId, 1))
        {
            Log($"{nextTroop.Name} troop charter is not available in the arsenal.");
            Refresh(engine);
            return;
        }

        _arsenalItems.RemoveAll(item => !engine.World.IsAlive(item));
        _lastItemTransaction = $"consumed {nextTroop.ItemDefinitionId}";
        unit.TroopType = nextTroop;
        unit.Status = $"Reformed as {unit.TroopType.Name}";
        unit.Supplies = Math.Min(160, unit.Supplies + 10);
        Log($"{unit.General.Name} changed troop charter to {unit.TroopType.Name} via item {_lastItemTransaction}.");
        Refresh(engine);
    }

    public void DevelopSelected(GameEngine engine)
    {
        EnsureScenario(engine);
        if (IsTerminalOutcome())
        {
            Log($"{_outcome} has already been reached.");
            Refresh(engine);
            return;
        }

        ThreeKingdomsUnitState unit = SelectedPlayerUnit();
        string terrain = TerrainAt(unit.X, unit.Y);
        int supplyGain = terrain switch
        {
            "City" => 34,
            "Farmland" => 28,
            "Pass" => 22,
            "Forest" => 18,
            "River" => 14,
            _ => 20
        };
        int moraleGain = terrain is "City" or "Pass" ? 14 : 10;

        _developmentLevel++;
        _campaignMomentum = Math.Min(24, _campaignMomentum + 12);
        unit.Supplies = Math.Min(180, unit.Supplies + supplyGain);
        unit.Morale = Math.Min(130, unit.Morale + moraleGain);
        unit.Cooldown = 0;
        unit.Acted = false;
        unit.Status = $"Developed {terrain}";
        SyncEntityAttributes(engine.World, unit);
        Log($"{unit.General.Name} developed {terrain}: supplies +{supplyGain}, morale +{moraleGain}, campaign momentum {_campaignMomentum}.");
        Refresh(engine);
    }

    public void AdvanceSelectedTowardEnemy(GameEngine engine)
    {
        EnsureScenario(engine);
        if (IsTerminalOutcome())
        {
            Log($"{_outcome} has already been reached.");
            Refresh(engine);
            return;
        }

        ThreeKingdomsUnitState unit = SelectedPlayerUnit();
        ThreeKingdomsUnitState? target = FindNearestEnemy(unit);
        if (target == null)
        {
            Log("No enemy remains.");
            Refresh(engine);
            return;
        }

        int beforeX = unit.X;
        int beforeY = unit.Y;
        int budget = Math.Max(1, unit.TroopType.Move + Math.Min(3, _campaignMomentum / 4));
        int moved = MoveTowardTarget(engine, unit, target, budget);
        unit.Supplies = Math.Max(0, unit.Supplies - Math.Max(1, moved));
        unit.Status = moved > 0 ? $"Advanced toward {target.General.Name}" : $"Holding before {target.General.Name}";
        SyncEntityAttributes(engine.World, unit);
        Log($"{unit.General.Name} advanced from {beforeX},{beforeY} to {unit.X},{unit.Y}; nearest enemy {target.General.Name} range {Manhattan(unit, target)}.");
        Refresh(engine);
    }

    public void ResolveCampaignBattle(GameEngine engine)
    {
        EnsureScenario(engine);
        if (IsTerminalOutcome())
        {
            Log($"{_outcome} has already been reached.");
            Refresh(engine);
            return;
        }

        ThreeKingdomsUnitState unit = SelectedPlayerUnit();
        ThreeKingdomsUnitState? target = FindNearestEnemy(unit);
        if (target == null)
        {
            Log("No enemy remains.");
            Refresh(engine);
            return;
        }

        int moved = 0;
        int range = Math.Max(1, unit.TroopType.Range);
        if (Manhattan(unit, target) > range)
        {
            moved = MoveTowardTarget(engine, unit, target, unit.TroopType.Move + Math.Min(4, _campaignMomentum / 3));
        }

        ThreeKingdomsSkillGraphScore score = ExecuteSkillScoreGraph(engine, unit, target);
        UseGasAbility(engine, unit, target);
        int formationBonus = _campaignMomentum * 18 + _developmentLevel * 6;
        int damage = unit.TroopType.Attack + unit.General.SkillPower + unit.General.WarPower / 2 + score.Value + formationBonus;
        if (_campaignMomentum > 0)
        {
            damage = Math.Max(damage, target.Health + target.TroopType.Defense);
        }

        string label = $"{unit.General.SkillName} campaign battle [{score.GraphId}+{score.Value}, momentum {_campaignMomentum}]";
        ApplyDamage(engine, unit, target, damage, label);
        unit.Acted = true;
        unit.Cooldown = Math.Max(unit.Cooldown, 1);
        unit.Supplies = Math.Max(0, unit.Supplies - Math.Max(3, moved));
        unit.Morale = Math.Min(130, unit.Morale + 4);
        SyncEntityAttributes(engine.World, unit);

        if (!target.IsAlive)
        {
            _campaignMomentum = Math.Max(0, _campaignMomentum - 1);
            Log($"{unit.General.Name} eliminated {target.General.Name}; enemy formations remaining {_units.Count(static candidate => candidate.TeamId == 2 && candidate.IsAlive)}.");
            SelectNextLivingPlayerAfter(engine, _selectedIndex);
        }

        Refresh(engine);
    }

    public void EndTurn(GameEngine engine)
    {
        EnsureScenario(engine);
        _turn++;
        if (_turn % 2 == 1)
        {
            _round++;
        }

        _phase = _phase == "Player" ? "Enemy" : "Player";
        for (int i = 0; i < _units.Count; i++)
        {
            ThreeKingdomsUnitState unit = _units[i];
            if (!unit.IsAlive)
            {
                continue;
            }

            unit.Acted = false;
            if (unit.Cooldown > 0)
            {
                unit.Cooldown--;
            }
            unit.Supplies = Math.Max(0, unit.Supplies - 1);
            if (unit.Supplies == 0)
            {
                unit.Morale = Math.Max(0, unit.Morale - 4);
            }
            SyncEntityAttributes(engine.World, unit);
        }

        if (_phase == "Enemy")
        {
            RunEnemyPulse(engine);
            _phase = "Player";
            _turn++;
        }

        Log($"Round {_round} begins. Player command phase.");
        Refresh(engine);
    }

    public bool TryBuildSnapshot(GameEngine engine, out ThreeKingdomsTacticsSnapshot snapshot)
    {
        if (!IsTacticsMap(engine))
        {
            snapshot = Snapshot;
            return false;
        }

        snapshot = BuildSnapshot(engine);
        return true;
    }

    public ThreeKingdomsTacticsSnapshot BuildSnapshot(GameEngine engine)
    {
        EnsureScenario(engine);
        UpdateOutcome();
        IReadOnlyList<ThreeKingdomsUnitView> unitViews = _units
            .Select((unit, index) => new ThreeKingdomsUnitView(
                index,
                unit.General.Name,
                unit.General.Faction,
                unit.TeamId,
                unit.X,
                unit.Y,
                unit.Health,
                unit.Morale,
                unit.Supplies,
                unit.TroopType.Name,
                unit.General.SkillName,
                unit.Status,
                index == _selectedIndex,
                unit.IsAlive))
            .ToArray();

        ThreeKingdomsUnitState? selected = TrySelectedPlayerUnit();
        int graphPrograms = CountLoadedGraphs(engine);
        int gasAbilityCount = CountRegisteredAbilities(engine);
        Snapshot = new ThreeKingdomsTacticsSnapshot(
            _turn,
            _round,
            _phase,
            _outcome,
            _developmentLevel,
            _campaignMomentum,
            ThreeKingdomsContent.MapWidth,
            ThreeKingdomsContent.MapHeight,
            _selectedIndex,
            selected?.General.Name ?? "None",
            selected?.General.SkillName ?? "None",
            selected?.TroopType.Name ?? "None",
            _units.Count(static unit => unit.TeamId == 1 && unit.IsAlive),
            _units.Count(static unit => unit.TeamId != 1 && unit.IsAlive),
            ThreeKingdomsContent.Generals.Count,
            ThreeKingdomsContent.Generals.Select(static general => general.SkillId).Distinct(StringComparer.Ordinal).Count(),
            ThreeKingdomsContent.TroopTypes.Count,
            CountLiveArsenalItems(engine),
            gasAbilityCount,
            graphPrograms,
            _lastGraphProgram,
            _lastGraphScore,
            _lastItemTransaction,
            _lastAction,
            unitViews,
            _log.ToArray());
        return Snapshot;
    }

    public string[] BuildMapRows()
    {
        int minX = 0;
        int maxX = ThreeKingdomsContent.MapWidth - 1;
        int minY = 0;
        int maxY = ThreeKingdomsContent.MapHeight - 1;
        var rows = new string[maxY - minY + 1];
        for (int y = minY; y <= maxY; y++)
        {
            char[] cells = new char[maxX - minX + 1];
            for (int x = minX; x <= maxX; x++)
            {
                cells[x - minX] = TerrainGlyph(x, y);
            }

            foreach (ThreeKingdomsUnitState unit in _units)
            {
                if (!unit.IsAlive || unit.Y != y || unit.X < minX || unit.X > maxX)
                {
                    continue;
                }

                cells[unit.X - minX] = unit.TeamId == 1 ? 'P' : unit.TeamId == 2 ? 'E' : 'N';
            }

            rows[y - minY] = new string(cells);
        }

        return rows;
    }

    private void EnsureScenario(GameEngine engine)
    {
        ResolveAttributeIds();
        EnsureTeams();
        EnsureLocalPlayer(engine);
        if (_units.Count == 0)
        {
            SeedUnits(engine);
            SeedArsenal(engine);
            Select(engine, 0);
            Log("三国全图战棋开局：100 名武将、100 兵种、兵符军械库、GAS 技能与 Graph 评分已加载。");
        }
    }

    private static bool IsTacticsMap(GameEngine engine)
    {
        string? mapId = engine.CurrentMapSession?.MapId.Value;
        if (string.Equals(mapId, ThreeKingdomsTacticsIds.MapId, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        List<string>? tags = engine.CurrentMapSession?.MapConfig?.Tags;
        return tags?.Any(static tag => string.Equals(tag, ThreeKingdomsTacticsIds.MapTag, StringComparison.OrdinalIgnoreCase)) == true;
    }

    private void Refresh(GameEngine engine)
    {
        BuildSnapshot(engine);
        if (engine.GetService(CoreServiceKeys.UIRoot) is UIRoot root)
        {
            _panel.MountOrRefresh(root, engine, this);
        }
    }

    private void ClearPanel(GameEngine engine)
    {
        if (engine.GetService(CoreServiceKeys.UIRoot) is UIRoot root)
        {
            _panel.ClearIfOwned(root);
        }
    }

    private void ResolveAttributeIds()
    {
        _healthAttrId = AttributeRegistry.Register("Health");
        _moraleAttrId = AttributeRegistry.Register("Morale");
        _suppliesAttrId = AttributeRegistry.Register("Supplies");
        _leadershipAttrId = AttributeRegistry.Register("Leadership");
        _warPowerAttrId = AttributeRegistry.Register("WarPower");
        _strategyAttrId = AttributeRegistry.Register("Strategy");
    }

    private static void EnsureTeams()
    {
        TeamManager.SetRelationshipSymmetric(1, 2, TeamRelationship.Hostile);
        TeamManager.SetRelationshipSymmetric(1, 3, TeamRelationship.Neutral);
        TeamManager.SetRelationshipSymmetric(2, 3, TeamRelationship.Hostile);
    }

    private void EnsureLocalPlayer(GameEngine engine)
    {
        if (engine.World.IsAlive(_localPlayer))
        {
            return;
        }

        _localPlayer = engine.GetService(CoreServiceKeys.LocalPlayerEntity);
        if (!engine.World.IsAlive(_localPlayer))
        {
            _localPlayer = engine.World.Create(new PlayerOwner { PlayerId = 1 }, new PlayerIdentity { PlayerId = 1 });
            engine.SetService(CoreServiceKeys.LocalPlayerEntity, _localPlayer);
            engine.SetService(CoreServiceKeys.LocalPlayerId, 1);
        }
    }

    private void SeedUnits(GameEngine engine)
    {
        int playerCount = 12;
        int enemyCount = 12;
        for (int i = 0; i < playerCount + enemyCount; i++)
        {
            ThreeKingdomsGeneralDefinition general = ThreeKingdomsContent.Generals[i];
            ThreeKingdomsTroopTypeDefinition troop = ThreeKingdomsContent.TroopTypes[general.TroopTypeIndex - 1];
            int team = i < playerCount ? 1 : 2;
            int col = i % 6;
            int row = i / 6;
            int x = team == 1 ? 6 + col * 3 : 40 + col * 3;
            int y = 6 + row * 4;
            var unit = new ThreeKingdomsUnitState
            {
                General = general,
                TroopType = troop,
                TeamId = team,
                X = x,
                Y = y,
                Health = 120 + general.Leadership + troop.Defense,
                Morale = 80 + general.Strategy / 5,
                Supplies = 120,
                Status = TerrainAt(x, y)
            };
            unit.Entity = CreateUnitEntity(engine, unit);
            _units.Add(unit);
        }
    }

    private Entity CreateUnitEntity(GameEngine engine, ThreeKingdomsUnitState unit)
    {
        var attributes = new AttributeBuffer();
        attributes.SetBase(_healthAttrId, MaxHealth(unit));
        attributes.SetCurrent(_healthAttrId, unit.Health);
        attributes.SetBase(_moraleAttrId, unit.Morale);
        attributes.SetBase(_suppliesAttrId, unit.Supplies);
        attributes.SetBase(_leadershipAttrId, unit.General.Leadership);
        attributes.SetBase(_warPowerAttrId, unit.General.WarPower);
        attributes.SetBase(_strategyAttrId, unit.General.Strategy);

        var abilities = new AbilityStateBuffer();
        int abilityId = AbilityIdRegistry.GetId(unit.General.SkillId);
        if (abilityId <= 0)
        {
            abilityId = AbilityIdRegistry.Register(unit.General.SkillId);
        }
        abilities.AddAbility(abilityId);

        Entity entity = engine.World.Create(
            new Name { Value = unit.General.Name },
            new Team { Id = unit.TeamId },
            new PlayerOwner { PlayerId = unit.TeamId == 1 ? 1 : 2 },
            WorldPositionCm.FromCm(unit.X * 100, unit.Y * 100),
            new PreviousWorldPositionCm { Value = WorldPositionCm.FromCm(unit.X * 100, unit.Y * 100).Value },
            attributes,
            abilities,
            new GameplayTagContainer(),
            new TagCountContainer(),
            new TimedTagBuffer(),
            new OrderBuffer(),
            new BlackboardSpatialBuffer(),
            new BlackboardEntityBuffer(),
            new BlackboardIntBuffer(),
            new SelectionSelectableTag(),
            SelectionSelectableState.EnabledByDefault,
            new MapEntity { MapId = new MapId(ThreeKingdomsTacticsIds.MapId) });
        return entity;
    }

    private void SeedArsenal(GameEngine engine)
    {
        InventoryRuntimeService inventory = RequireInventory(engine);
        ItemLayoutRegistry layouts = engine.GetService(CoreServiceKeys.ItemLayoutRegistry)
            ?? throw new InvalidOperationException("ThreeKingdomsTacticsMod requires ItemLayoutRegistry.");
        ItemDefinitionRegistry definitions = RequireItemDefinitions(engine);

        int layoutId = layouts.GetId("tk_layout_arsenal_grid");
        if (layoutId <= 0)
        {
            throw new InvalidOperationException("ThreeKingdomsTacticsMod requires item layout tk_layout_arsenal_grid.");
        }

        _arsenal = inventory.CreateContainer(_localPlayer, layoutId, ItemContainerPurpose.Stash);
        TrackMapEntity(engine, _arsenal);
        for (int i = 0; i < ThreeKingdomsContent.TroopTypes.Count; i++)
        {
            ThreeKingdomsTroopTypeDefinition troop = ThreeKingdomsContent.TroopTypes[i];
            int definitionId = RequireItemDefinitionId(definitions, troop);

            Entity item = inventory.CreateItem(definitionId, stackCount: 1, charges: troop.Move, durability: troop.Defense);
            TrackMapEntity(engine, item);
            if (inventory.TryAutoPlaceItem(item, _arsenal))
            {
                _arsenalItems.Add(item);
            }
            else
            {
                if (engine.World.IsAlive(item))
                {
                    engine.World.Destroy(item);
                }

                throw new InvalidOperationException($"ThreeKingdomsTacticsMod could not place troop charter item {troop.ItemDefinitionId}.");
            }
        }
    }

    private void TrackMapEntity(GameEngine engine, Entity entity)
    {
        if (!engine.World.IsAlive(entity))
        {
            return;
        }

        if (engine.World.Has<MapEntity>(entity))
        {
            engine.World.Set(entity, new MapEntity { MapId = new MapId(ThreeKingdomsTacticsIds.MapId) });
        }
        else
        {
            engine.World.Add(entity, new MapEntity { MapId = new MapId(ThreeKingdomsTacticsIds.MapId) });
        }
    }

    private void BindSelection(GameEngine engine, Entity target)
    {
        var selection = engine.GetService(CoreServiceKeys.SelectionRuntime);
        if (selection == null || !engine.World.IsAlive(_localPlayer) || !engine.World.IsAlive(target))
        {
            return;
        }

        Span<Entity> next = stackalloc Entity[1];
        next[0] = target;
        selection.ReplaceSelection(_localPlayer, SelectionSetKeys.LivePrimary, next);
        selection.TryBindView(_localPlayer, SelectionViewKeys.Primary, _localPlayer, SelectionSetKeys.LivePrimary);
        engine.GlobalContext[CoreServiceKeys.SelectionViewViewerEntity.Name] = _localPlayer;
        engine.GlobalContext[CoreServiceKeys.SelectionViewKey.Name] = SelectionViewKeys.Primary;
    }

    private void UseGasAbility(GameEngine engine, ThreeKingdomsUnitState unit, ThreeKingdomsUnitState? target)
    {
        GasGraphRuntimeApi graphApi = CreateGraphRuntimeApi(engine);
        var abilitySystem = new AbilitySystem(
            engine.World,
            engine.GetService(CoreServiceKeys.EffectRequestQueue),
            engine.GetService(CoreServiceKeys.AbilityDefinitionRegistry),
            engine.GetService(CoreServiceKeys.TagOps),
            engine.GetService(CoreServiceKeys.GraphProgramRegistry),
            graphApi,
            engine.GetService(CoreServiceKeys.ProgressionRequirementEvaluator));
        abilitySystem.TryActivateAbility(unit.Entity, 0, target?.Entity ?? unit.Entity);
    }

    private ThreeKingdomsSkillGraphScore ExecuteSkillScoreGraph(GameEngine engine, ThreeKingdomsUnitState unit, ThreeKingdomsUnitState? target)
    {
        const string graphKey = "tk.graph.skillScore";
        GraphProgramRegistry registry = engine.GetService(CoreServiceKeys.GraphProgramRegistry)
            ?? throw new InvalidOperationException("ThreeKingdomsTacticsMod requires GraphProgramRegistry.");
        int graphId = GraphIdRegistry.GetId(graphKey);
        if (graphId <= 0 || !registry.TryGetProgram(graphId, out ReadOnlySpan<GraphInstruction> program))
        {
            throw new InvalidOperationException($"ThreeKingdomsTacticsMod requires graph program {graphKey}.");
        }

        Entity explicitTarget = target?.Entity ?? unit.Entity;
        IntVector2 targetPos = target == null ? new IntVector2(unit.X, unit.Y) : new IntVector2(target.X, target.Y);
        float score = GasGraphExecutor.ExecuteScore(
            engine.World,
            unit.Entity,
            explicitTarget,
            targetPos,
            program,
            CreateGraphRuntimeApi(engine));
        int rounded = Math.Max(0, (int)MathF.Round(score));
        _lastGraphProgram = graphKey;
        _lastGraphScore = rounded;
        return new ThreeKingdomsSkillGraphScore(graphKey, rounded);
    }

    private static GasGraphRuntimeApi CreateGraphRuntimeApi(GameEngine engine)
    {
        return GasGraphRuntimeApi.CreateProduction(
            engine.World,
            engine.SpatialQueries,
            engine.SpatialCoords,
            engine.EventBus,
            engine.GetService(CoreServiceKeys.EffectRequestQueue),
            engine.GlobalContext);
    }

    private void ApplyDamage(GameEngine engine, ThreeKingdomsUnitState source, ThreeKingdomsUnitState target, int damage, string label)
    {
        int mitigated = Math.Max(1, damage - target.TroopType.Defense / 3);
        target.Health = Math.Max(0, target.Health - mitigated);
        target.Morale = Math.Max(0, target.Morale - Math.Max(4, mitigated / 4));
        target.Status = target.Health <= 0 ? "Defeated" : $"Hit by {label}";
        SyncEntityAttributes(engine.World, target);
        Log($"{source.General.Name} used {label} on {target.General.Name}: -{mitigated} HP.");
    }

    private void SyncEntityPosition(World world, ThreeKingdomsUnitState unit)
    {
        if (!world.IsAlive(unit.Entity))
        {
            return;
        }

        var position = WorldPositionCm.FromCm(unit.X * 100, unit.Y * 100);
        if (world.Has<PreviousWorldPositionCm>(unit.Entity))
        {
            world.Get<PreviousWorldPositionCm>(unit.Entity) = new PreviousWorldPositionCm { Value = position.Value };
        }
        if (world.Has<WorldPositionCm>(unit.Entity))
        {
            world.Get<WorldPositionCm>(unit.Entity) = position;
        }
    }

    private void SyncEntityAttributes(World world, ThreeKingdomsUnitState unit)
    {
        if (!world.IsAlive(unit.Entity) || !world.Has<AttributeBuffer>(unit.Entity))
        {
            return;
        }

        ref AttributeBuffer attrs = ref world.Get<AttributeBuffer>(unit.Entity);
        attrs.SetCurrent(_healthAttrId, unit.Health);
        attrs.SetCurrent(_moraleAttrId, unit.Morale);
        attrs.SetCurrent(_suppliesAttrId, unit.Supplies);
    }

    private void RunEnemyPulse(GameEngine engine)
    {
        foreach (ThreeKingdomsUnitState enemy in _units)
        {
            if (enemy.TeamId == 1 || !enemy.IsAlive)
            {
                continue;
            }

            ThreeKingdomsUnitState? target = _units
                .Where(static unit => unit.TeamId == 1 && unit.IsAlive)
                .OrderBy(unit => Manhattan(enemy, unit))
                .FirstOrDefault();
            if (target == null)
            {
                break;
            }

            int distance = Manhattan(enemy, target);
            if (distance <= enemy.TroopType.Range)
            {
                ApplyDamage(engine, enemy, target, Math.Max(6, enemy.TroopType.Attack / 2), "enemy counter");
            }
            else
            {
                int stepX = Math.Sign(target.X - enemy.X);
                int stepY = Math.Sign(target.Y - enemy.Y);
                enemy.X = Math.Clamp(enemy.X + stepX, 0, ThreeKingdomsContent.MapWidth - 1);
                enemy.Y = Math.Clamp(enemy.Y + stepY, 0, ThreeKingdomsContent.MapHeight - 1);
                enemy.Status = "Advancing";
                SyncEntityPosition(engine.World, enemy);
            }
        }
    }

    private int MoveTowardTarget(GameEngine engine, ThreeKingdomsUnitState unit, ThreeKingdomsUnitState target, int budget)
    {
        int moved = 0;
        int range = Math.Max(1, unit.TroopType.Range);
        while (budget > 0 && Manhattan(unit, target) > range)
        {
            int stepX = Math.Sign(target.X - unit.X);
            int stepY = Math.Sign(target.Y - unit.Y);
            bool movedThisStep =
                TryStep(engine, unit, stepX, 0) ||
                TryStep(engine, unit, 0, stepY) ||
                TryStep(engine, unit, stepX, stepY);
            if (!movedThisStep)
            {
                break;
            }

            budget--;
            moved++;
        }

        return moved;
    }

    private bool TryStep(GameEngine engine, ThreeKingdomsUnitState unit, int dx, int dy)
    {
        if (dx == 0 && dy == 0)
        {
            return false;
        }

        int nextX = Math.Clamp(unit.X + dx, 0, ThreeKingdomsContent.MapWidth - 1);
        int nextY = Math.Clamp(unit.Y + dy, 0, ThreeKingdomsContent.MapHeight - 1);
        if (nextX == unit.X && nextY == unit.Y)
        {
            return false;
        }

        if (FindUnitAt(nextX, nextY, aliveOnly: true) != null)
        {
            return false;
        }

        unit.X = nextX;
        unit.Y = nextY;
        unit.Status = TerrainAt(nextX, nextY);
        SyncEntityPosition(engine.World, unit);
        return true;
    }

    private void SelectNextLivingPlayerAfter(GameEngine engine, int previousIndex)
    {
        for (int i = 1; i <= _units.Count; i++)
        {
            int next = (previousIndex + i) % _units.Count;
            if (_units[next].TeamId == 1 && _units[next].IsAlive)
            {
                _selectedIndex = next;
                BindSelection(engine, _units[next].Entity);
                return;
            }
        }
    }

    private void UpdateOutcome()
    {
        if (_units.Count == 0)
        {
            _outcome = "NotLoaded";
            return;
        }

        int playerAlive = _units.Count(static unit => unit.TeamId == 1 && unit.IsAlive);
        int enemyAlive = _units.Count(static unit => unit.TeamId == 2 && unit.IsAlive);
        if (enemyAlive == 0)
        {
            if (!string.Equals(_outcome, "Victory", StringComparison.Ordinal))
            {
                _outcome = "Victory";
                _phase = "Victory";
                Log("Victory achieved: all opposing formations have been eliminated.");
            }
            return;
        }

        if (playerAlive == 0)
        {
            if (!string.Equals(_outcome, "Defeat", StringComparison.Ordinal))
            {
                _outcome = "Defeat";
                _phase = "Defeat";
                Log("Defeat: all player formations have been eliminated.");
            }
            return;
        }

        _outcome = "InProgress";
    }

    private bool IsTerminalOutcome()
    {
        UpdateOutcome();
        return string.Equals(_outcome, "Victory", StringComparison.Ordinal) ||
               string.Equals(_outcome, "Defeat", StringComparison.Ordinal);
    }

    private ThreeKingdomsUnitState SelectedPlayerUnit()
    {
        ThreeKingdomsUnitState? selected = TrySelectedPlayerUnit();
        if (selected == null)
        {
            throw new InvalidOperationException("No living player unit remains.");
        }

        return selected;
    }

    private ThreeKingdomsUnitState? TrySelectedPlayerUnit()
    {
        if (_units.Count == 0)
        {
            return null;
        }

        if ((uint)_selectedIndex >= (uint)_units.Count || !_units[_selectedIndex].IsAlive || _units[_selectedIndex].TeamId != 1)
        {
            _selectedIndex = _units.FindIndex(static unit => unit.TeamId == 1 && unit.IsAlive);
        }

        return _selectedIndex < 0 ? null : _units[_selectedIndex];
    }

    private ThreeKingdomsUnitState? FindNearestEnemy(ThreeKingdomsUnitState source)
    {
        return _units
            .Where(unit => unit.TeamId != source.TeamId && unit.IsAlive)
            .OrderBy(unit => Manhattan(source, unit))
            .FirstOrDefault();
    }

    private ThreeKingdomsUnitState? FindUnitAt(int x, int y, bool aliveOnly)
    {
        return _units.FirstOrDefault(unit => unit.X == x && unit.Y == y && (!aliveOnly || unit.IsAlive));
    }

    private static int Manhattan(ThreeKingdomsUnitState a, ThreeKingdomsUnitState b)
    {
        return Math.Abs(a.X - b.X) + Math.Abs(a.Y - b.Y);
    }

    private static int MaxHealth(ThreeKingdomsUnitState unit)
    {
        return 120 + unit.General.Leadership + unit.TroopType.Defense;
    }

    private static string TerrainAt(int x, int y)
    {
        int value = Math.Abs((x * 31 + y * 17 + (x / 7) * 9) % 23);
        return value switch
        {
            0 or 1 => "River",
            2 or 3 or 4 => "Forest",
            5 => "Pass",
            6 or 7 => "Farmland",
            8 => "City",
            _ => "Plain"
        };
    }

    private static char TerrainGlyph(int x, int y)
    {
        return TerrainAt(x, y) switch
        {
            "River" => '~',
            "Forest" => '^',
            "Pass" => '#',
            "Farmland" => ',',
            "City" => '@',
            _ => '.'
        };
    }

    private int CountRegisteredAbilities(GameEngine engine)
    {
        int count = 0;
        var registry = engine.GetService(CoreServiceKeys.AbilityDefinitionRegistry);
        if (registry == null)
        {
            return count;
        }

        for (int i = 0; i < ThreeKingdomsContent.Generals.Count; i++)
        {
            int abilityId = AbilityIdRegistry.GetId(ThreeKingdomsContent.Generals[i].SkillId);
            if (abilityId > 0 && registry.TryGet(abilityId, out _))
            {
                count++;
            }
        }
        return count;
    }

    private static int CountLoadedGraphs(GameEngine engine)
    {
        GraphProgramRegistry? registry = engine.GetService(CoreServiceKeys.GraphProgramRegistry);
        if (registry == null)
        {
            return 0;
        }

        int count = 0;
        string[] graphIds =
        [
            "tk.graph.skillScore",
            "tk.graph.supplyPressure",
            "tk.graph.moraleBreak"
        ];
        for (int i = 0; i < graphIds.Length; i++)
        {
            int graphId = GraphIdRegistry.GetId(graphIds[i]);
            if (graphId > 0 && registry.TryGetProgram(graphId, out _))
            {
                count++;
            }
        }

        return count;
    }

    private int CountLiveArsenalItems(GameEngine engine)
    {
        _arsenalItems.RemoveAll(item => !engine.World.IsAlive(item));
        return _arsenalItems.Count;
    }

    private static InventoryRuntimeService RequireInventory(GameEngine engine)
    {
        return engine.GetService(CoreServiceKeys.InventoryRuntimeService)
            ?? throw new InvalidOperationException("ThreeKingdomsTacticsMod requires InventoryRuntimeService.");
    }

    private static ItemDefinitionRegistry RequireItemDefinitions(GameEngine engine)
    {
        return engine.GetService(CoreServiceKeys.ItemDefinitionRegistry)
            ?? throw new InvalidOperationException("ThreeKingdomsTacticsMod requires ItemDefinitionRegistry.");
    }

    private static int RequireItemDefinitionId(ItemDefinitionRegistry definitions, ThreeKingdomsTroopTypeDefinition troop)
    {
        int definitionId = definitions.GetId(troop.ItemDefinitionId);
        if (definitionId <= 0)
        {
            throw new InvalidOperationException($"ThreeKingdomsTacticsMod requires item definition {troop.ItemDefinitionId}.");
        }

        return definitionId;
    }

    private void Log(string line)
    {
        _lastAction = line;
        _log.Insert(0, $"T{_turn:000} {line}");
        if (_log.Count > 12)
        {
            _log.RemoveRange(12, _log.Count - 12);
        }
    }

    private static ThreeKingdomsTacticsSnapshot EmptySnapshot()
    {
        return new ThreeKingdomsTacticsSnapshot(
            0,
            0,
            "None",
            "NotLoaded",
            0,
            0,
            ThreeKingdomsContent.MapWidth,
            ThreeKingdomsContent.MapHeight,
            0,
            string.Empty,
            string.Empty,
            string.Empty,
            0,
            0,
            0,
            0,
            0,
            0,
            0,
            0,
            "None",
            0,
            "None",
            "Not loaded.",
            Array.Empty<ThreeKingdomsUnitView>(),
            Array.Empty<string>());
    }

    private readonly record struct ThreeKingdomsSkillGraphScore(string GraphId, int Value);
}
