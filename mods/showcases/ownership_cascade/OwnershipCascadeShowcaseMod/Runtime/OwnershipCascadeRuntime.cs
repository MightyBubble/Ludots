using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Arch.Core;
using Ludots.Core.Association;
using Ludots.Core.Components;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.Relationships;
using Ludots.Core.Input.Runtime;
using Ludots.Core.Map;
using Ludots.Core.Modding;
using Ludots.Core.Scripting;
using Ludots.UI;
using OwnershipCascadeShowcaseMod.UI;

namespace OwnershipCascadeShowcaseMod.Runtime;

public sealed class OwnershipCascadeRuntime
{
    private readonly IModContext _context;
    private readonly OwnershipCascadePanelController _panelController;
    private readonly List<string> _log = new(8);
    private readonly Entity[] _incomingScratch = new Entity[4];
    private GameEngine? _activeEngine;
    private OwnershipCascadeConfig? _config;
    private Entity _solePossessedRep;
    private Entity _enemyPlayer;
    private Entity _city;
    private Entity _garrison;
    private Entity _warehouse;
    private Entity _production;
    private bool _scenarioReady;
    private string _status = "Load the ownership cascade map.";

    public OwnershipCascadeRuntime(IModContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _panelController = new OwnershipCascadePanelController(this);
    }

    public OwnershipCascadeSnapshot Snapshot => BuildSnapshot();

    public Task HandleMapFocusedAsync(ScriptContext context)
    {
        if (context.GetEngine() is not GameEngine engine)
        {
            return Task.CompletedTask;
        }

        if (!OwnershipCascadeIds.IsShowcaseMap(engine.CurrentMapSession?.MapId.Value))
        {
            ClearPanel(engine);
            return Task.CompletedTask;
        }

        _activeEngine = engine;
        EnsureConfig();
        ActivateInputContext(engine.GetService(CoreServiceKeys.InputHandler));
        EnsureScenario(engine);
        RefreshPanelInternal(engine);
        return Task.CompletedTask;
    }

    public Task HandleMapUnloadedAsync(ScriptContext context)
    {
        if (context.GetEngine() is not GameEngine engine)
        {
            return Task.CompletedTask;
        }

        if (OwnershipCascadeIds.IsShowcaseMap(context.Get(CoreServiceKeys.MapId).Value))
        {
            DeactivateInputContext(engine.GetService(CoreServiceKeys.InputHandler));
            ClearPanel(engine);
            ResetScenario();
        }

        return Task.CompletedTask;
    }

    public void CaptureForSolePossessedRep(GameEngine engine)
    {
        EnsureScenario(engine);
        ReassignCity(engine, _solePossessedRep, "Captured by sole possessed player representative.");
    }

    public void ReclaimForEnemy(GameEngine engine)
    {
        EnsureScenario(engine);
        ReassignCity(engine, _enemyPlayer, "Reclaimed by enemy player.");
    }

    public void RefreshPanel(GameEngine engine)
    {
        if (OwnershipCascadeIds.IsShowcaseMap(engine.CurrentMapSession?.MapId.Value))
        {
            RefreshPanelInternal(engine);
        }
        else
        {
            ClearPanel(engine);
        }
    }

    internal OwnershipCascadePanelState BuildPanelState()
    {
        OwnershipCascadeSnapshot snapshot = BuildSnapshot();
        return new OwnershipCascadePanelState(
            Header: _config?.Header ?? "Ownership Cascade",
            Summary: _config?.Summary ?? string.Empty,
            Controls: _config?.Controls ?? string.Empty,
            Status: snapshot.Status,
            Lines: new[]
            {
                $"{_config?.CityOwnerLabel ?? "city owner"}: {snapshot.CityOwner}",
                $"{LabelAt(0, "garrison")}: {snapshot.GarrisonOwner}",
                $"{LabelAt(1, "warehouse")}: {snapshot.WarehouseOwner}",
                $"{LabelAt(2, "production")}: {snapshot.ProductionOwner}",
                $"Owns type id: {snapshot.OwnsTypeId}",
                $"incoming counts: city {snapshot.CityIncomingCount}, garrison {snapshot.GarrisonIncomingCount}, warehouse {snapshot.WarehouseIncomingCount}, production {snapshot.ProductionIncomingCount}",
            },
            LogLines: _log.Count == 0 ? new[] { snapshot.Status } : _log.ToArray());
    }

    private void EnsureConfig()
    {
        if (_config != null)
        {
            return;
        }

        using Stream stream = _context.GetResource($"{_context.ModId}:assets/Ownership/ownership_cascade_config.json");
        _config = OwnershipCascadeConfig.Load(stream);
    }

    private void EnsureScenario(GameEngine engine)
    {
        if (_scenarioReady)
        {
            return;
        }

        if (_config == null)
        {
            throw new InvalidOperationException("Ownership cascade config was not loaded.");
        }

        World world = engine.World;
        _solePossessedRep = CreateNamed(world, _config.PlayerRepName);
        _enemyPlayer = CreateNamed(world, _config.EnemyPlayerName);
        _city = CreateNamed(world, _config.CityName);
        _garrison = CreateNamed(world, _config.GarrisonName);
        _warehouse = CreateNamed(world, _config.WarehouseName);
        _production = CreateNamed(world, _config.ProductionName);

        OwnershipResolver ownership = RequireOwnership(engine);
        ownership.EnsureOwnership(_enemyPlayer, _city);
        ownership.EnsureOwnership(_city, _garrison);
        ownership.EnsureOwnership(_city, _warehouse);
        ownership.EnsureOwnership(_city, _production);

        _scenarioReady = true;
        _status = "Neutral city starts under enemy ownership. Press C to capture.";
        PushLog(_status);
    }

    private void ReassignCity(GameEngine engine, Entity player, string status)
    {
        OwnershipResolver ownership = RequireOwnership(engine);
        ownership.EnsureOwnership(player, _city);
        ownership.EnsureOwnership(_city, _garrison);
        ownership.EnsureOwnership(_city, _warehouse);
        ownership.EnsureOwnership(_city, _production);
        _status = status;
        PushLog(status);
        RefreshPanelInternal(engine);
    }

    private OwnershipCascadeSnapshot BuildSnapshot()
    {
        GameEngine? engine = TryGetEngine();
        if (engine == null || !_scenarioReady)
        {
            return new OwnershipCascadeSnapshot(
                "none",
                "none",
                "none",
                "none",
                _status,
                0,
                0,
                0,
                0,
                0);
        }

        OwnershipResolver ownership = RequireOwnership(engine);
        RelationshipRuntime relationships = engine.GetService(CoreServiceKeys.RelationshipRuntime)
            ?? throw new InvalidOperationException("RelationshipRuntime missing.");
        int ownsTypeId = ownership.OwnsTypeId;
        return new OwnershipCascadeSnapshot(
            CityOwner: ResolveOwnerName(engine, _city),
            GarrisonOwner: ResolveOwnerName(engine, _garrison),
            WarehouseOwner: ResolveOwnerName(engine, _warehouse),
            ProductionOwner: ResolveOwnerName(engine, _production),
            Status: _status,
            OwnsTypeId: ownsTypeId,
            CityIncomingCount: relationships.CollectIncoming(_city, ownsTypeId, _incomingScratch),
            GarrisonIncomingCount: relationships.CollectIncoming(_garrison, ownsTypeId, _incomingScratch),
            WarehouseIncomingCount: relationships.CollectIncoming(_warehouse, ownsTypeId, _incomingScratch),
            ProductionIncomingCount: relationships.CollectIncoming(_production, ownsTypeId, _incomingScratch));
    }

    private string ResolveOwnerName(GameEngine engine, Entity entity)
    {
        OwnershipResolver ownership = RequireOwnership(engine);
        if (!ownership.TryResolveRootOwner(entity, out Entity owner))
        {
            return "none";
        }

        return engine.World.Has<Name>(owner) ? engine.World.Get<Name>(owner).Value : $"Entity {owner.Id}";
    }

    private static Entity CreateNamed(World world, string name)
    {
        Entity entity = world.Create(new Name { Value = name });
        world.Add(entity, new MapEntity { MapId = new MapId(OwnershipCascadeIds.ShowcaseMapId) });
        return entity;
    }

    private OwnershipResolver RequireOwnership(GameEngine engine)
    {
        return engine.GetService(CoreServiceKeys.OwnershipResolver)
            ?? throw new InvalidOperationException("OwnershipResolver missing.");
    }

    private GameEngine? TryGetEngine()
    {
        return _activeEngine;
    }

    private void RefreshPanelInternal(GameEngine engine)
    {
        if (engine.GetService(CoreServiceKeys.UIRoot) is UIRoot root)
        {
            _panelController.MountOrRefresh(root, engine);
        }
    }

    private void ClearPanel(GameEngine engine)
    {
        if (engine.GetService(CoreServiceKeys.UIRoot) is UIRoot root)
        {
            _panelController.ClearIfOwned(root);
        }
    }

    private void ResetScenario()
    {
        _scenarioReady = false;
        _solePossessedRep = Entity.Null;
        _enemyPlayer = Entity.Null;
        _city = Entity.Null;
        _garrison = Entity.Null;
        _warehouse = Entity.Null;
        _production = Entity.Null;
        _log.Clear();
        _activeEngine = null;
        _status = "Load the ownership cascade map.";
    }

    private string LabelAt(int index, string fallback)
    {
        string[]? labels = _config?.OwnedChildLabels;
        return labels != null && (uint)index < (uint)labels.Length && !string.IsNullOrWhiteSpace(labels[index])
            ? labels[index]
            : fallback;
    }

    private void PushLog(string line)
    {
        _log.Insert(0, line);
        if (_log.Count > 5)
        {
            _log.RemoveAt(_log.Count - 1);
        }
    }

    private static void ActivateInputContext(PlayerInputHandler? input)
    {
        input?.PushContext("OwnershipCascade.Controls");
    }

    private static void DeactivateInputContext(PlayerInputHandler? input)
    {
        input?.PopContext("OwnershipCascade.Controls");
    }
}
