using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Arch.Core;
using DiplomacyTradeGateShowcaseMod.UI;
using Ludots.Core.Components;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.Exchange;
using Ludots.Core.Gameplay.Items;
using Ludots.Core.Gameplay.Relationships;
using Ludots.Core.Input.Runtime;
using Ludots.Core.Map;
using Ludots.Core.Modding;
using Ludots.Core.Scripting;
using Ludots.UI;

namespace DiplomacyTradeGateShowcaseMod.Runtime;

public sealed class DiplomacyTradeGateRuntime
{
    private readonly IModContext _context;
    private readonly DiplomacyTradeGatePanelController _panelController;
    private readonly List<string> _log = new(8);
    private DiplomacyTradeGateConfig? _config;
    private GameEngine? _engine;
    private Entity _source = Entity.Null;
    private Entity _target = Entity.Null;
    private Entity _sourceStash = Entity.Null;
    private Entity _targetStash = Entity.Null;
    private int _sourceLayoutId;
    private int _targetLayoutId;
    private int _creditItemId;
    private int _goodsItemId;
    private int _operationId;
    private int _relationshipTypeId;
    private int _trustMetricId;
    private int _embargoFlagId;
    private int _successfulTrades;
    private bool _scenarioReady;
    private ExchangeExecutionStatus _lastStatus = ExchangeExecutionStatus.MissingOperation;
    private string _status = "Load the diplomacy trade gate map.";

    public DiplomacyTradeGateRuntime(IModContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _panelController = new DiplomacyTradeGatePanelController(this);
    }

    public DiplomacyTradeGateSnapshot Snapshot => BuildSnapshot();

    public Task HandleMapFocusedAsync(ScriptContext context)
    {
        if (context.GetEngine() is not GameEngine engine)
        {
            return Task.CompletedTask;
        }

        if (!DiplomacyTradeGateIds.IsShowcaseMap(engine.CurrentMapSession?.MapId.Value))
        {
            ClearPanel(engine);
            return Task.CompletedTask;
        }

        _engine = engine;
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

        if (DiplomacyTradeGateIds.IsShowcaseMap(context.Get(CoreServiceKeys.MapId).Value))
        {
            DeactivateInputContext(engine.GetService(CoreServiceKeys.InputHandler));
            ClearPanel(engine);
            ResetScenario();
        }

        return Task.CompletedTask;
    }

    public void TryTrade(GameEngine engine)
    {
        EnsureScenario(engine);
        ExchangeRuntime exchange = RequireExchange(engine);
        var context = new ExchangeExecutionContext(_source, _target);
        ExchangeExecutionResult result = exchange.TryExecute(_operationId, in context);
        _lastStatus = result.Status;
        if (result.Succeeded)
        {
            _successfulTrades++;
            _status = "Exchange settled: relationship gate passed.";
        }
        else if (result.Status == ExchangeExecutionStatus.RelationshipDenied)
        {
            _status = "Exchange denied by relationship requirements.";
        }
        else
        {
            _status = $"Exchange failed: {result.Status}.";
        }

        PushLog($"{_status} detail={result.DetailIndex}");
        RefreshPanelInternal(engine);
    }

    public void SignPact(GameEngine engine)
    {
        EnsureScenario(engine);
        RelationshipRuntime relationships = RequireRelationships(engine);
        relationships.SetMetric(_source, _target, _relationshipTypeId, _trustMetricId, _config!.PactTrust);
        relationships.SetFlag(_source, _target, _relationshipTypeId, _embargoFlagId, enabled: false);
        _status = $"Agreement signed: trust {_config.PactTrust}, embargo cleared.";
        PushLog(_status);
        RefreshPanelInternal(engine);
    }

    public void DeclareEmbargo(GameEngine engine)
    {
        EnsureScenario(engine);
        RequireRelationships(engine).SetFlag(_source, _target, _relationshipTypeId, _embargoFlagId, enabled: true);
        _status = "Embargo declared: relationship flag blocks the same exchange.";
        PushLog(_status);
        RefreshPanelInternal(engine);
    }

    public void ClearEmbargo(GameEngine engine)
    {
        EnsureScenario(engine);
        RequireRelationships(engine).SetFlag(_source, _target, _relationshipTypeId, _embargoFlagId, enabled: false);
        _status = "Embargo cleared.";
        PushLog(_status);
        RefreshPanelInternal(engine);
    }

    public void RefreshPanel(GameEngine engine)
    {
        if (DiplomacyTradeGateIds.IsShowcaseMap(engine.CurrentMapSession?.MapId.Value))
        {
            RefreshPanelInternal(engine);
        }
        else
        {
            ClearPanel(engine);
        }
    }

    internal DiplomacyTradeGatePanelState BuildPanelState()
    {
        DiplomacyTradeGateSnapshot snapshot = BuildSnapshot();
        return new DiplomacyTradeGatePanelState(
            Header: _config?.Header ?? "Diplomacy Trade Gate",
            Summary: _config?.Summary ?? string.Empty,
            Controls: _config?.Controls ?? string.Empty,
            Status: snapshot.Status,
            Lines: new[]
            {
                $"source: {snapshot.SourceName}",
                $"target: {snapshot.TargetName}",
                $"trust: {snapshot.Trust}",
                $"embargo: {snapshot.Embargo}",
                $"source credits: {snapshot.SourceCredits}",
                $"target goods: {snapshot.TargetGoods}",
                $"successful trades: {snapshot.SuccessfulTrades}",
                $"last status: {snapshot.LastStatus}",
                $"ids: type {snapshot.RelationshipTypeId}, metric {snapshot.TrustMetricId}, flag {snapshot.EmbargoFlagId}",
            },
            LogLines: _log.Count == 0 ? new[] { snapshot.Status } : _log.ToArray());
    }

    private void EnsureConfig()
    {
        if (_config != null)
        {
            return;
        }

        using Stream stream = _context.GetResource($"{_context.ModId}:assets/Diplomacy/diplomacy_trade_gate_config.json");
        _config = DiplomacyTradeGateConfig.Load(stream);
    }

    private void EnsureScenario(GameEngine engine)
    {
        if (_scenarioReady)
        {
            return;
        }

        if (_config == null)
        {
            throw new InvalidOperationException("Diplomacy trade gate config was not loaded.");
        }

        ResolveIds(engine);
        BuildScenario(engine);
        _scenarioReady = true;
        _status = "No agreement yet. Press T to see RelationshipDenied.";
        PushLog(_status);
    }

    private void ResolveIds(GameEngine engine)
    {
        ItemLayoutRegistry layouts = engine.GetService(CoreServiceKeys.ItemLayoutRegistry)
            ?? throw new InvalidOperationException("ItemLayoutRegistry missing.");
        ItemDefinitionRegistry items = engine.GetService(CoreServiceKeys.ItemDefinitionRegistry)
            ?? throw new InvalidOperationException("ItemDefinitionRegistry missing.");
        ExchangeOperationRegistry operations = engine.GetService(CoreServiceKeys.ExchangeOperationRegistry)
            ?? throw new InvalidOperationException("ExchangeOperationRegistry missing.");
        RelationshipTypeRegistry types = engine.GetService(CoreServiceKeys.RelationshipTypeRegistry)
            ?? throw new InvalidOperationException("RelationshipTypeRegistry missing.");
        RelationshipMetricRegistry metrics = engine.GetService(CoreServiceKeys.RelationshipMetricRegistry)
            ?? throw new InvalidOperationException("RelationshipMetricRegistry missing.");
        RelationshipFlagRegistry flags = engine.GetService(CoreServiceKeys.RelationshipFlagRegistry)
            ?? throw new InvalidOperationException("RelationshipFlagRegistry missing.");

        _sourceLayoutId = layouts.GetId(_config!.SourceLayout);
        _targetLayoutId = layouts.GetId(_config.TargetLayout);
        _creditItemId = items.GetId(_config.CreditItem);
        _goodsItemId = items.GetId(_config.GoodsItem);
        _operationId = operations.GetId(_config.Operation);
        _relationshipTypeId = types.GetId(_config.RelationshipType);
        _trustMetricId = metrics.GetId(_config.TrustMetric);
        _embargoFlagId = flags.GetId(_config.EmbargoFlag);
    }

    private void BuildScenario(GameEngine engine)
    {
        World world = engine.World;
        _source = CreateNamed(world, _config!.SourceName);
        _target = CreateNamed(world, _config.TargetName);

        InventoryRuntimeService inventory = RequireInventory(engine);
        _sourceStash = inventory.CreateContainer(_source, _sourceLayoutId, ItemContainerPurpose.Stash);
        _targetStash = inventory.CreateContainer(_target, _targetLayoutId, ItemContainerPurpose.Stash);
        if (!inventory.TryCreateAndPlaceItem(_sourceStash, _creditItemId, _config.StartingCredits, 0, 0, out _))
        {
            throw new InvalidOperationException("Diplomacy trade gate could not seed source credits.");
        }
    }

    private DiplomacyTradeGateSnapshot BuildSnapshot()
    {
        if (_engine == null || !_scenarioReady || _config == null)
        {
            return new DiplomacyTradeGateSnapshot(
                string.Empty,
                string.Empty,
                0,
                false,
                0,
                0,
                0,
                _lastStatus,
                _status,
                0,
                0,
                0);
        }

        RelationshipRuntime relationships = RequireRelationships(_engine);
        return new DiplomacyTradeGateSnapshot(
            SourceName: NameOf(_engine, _source),
            TargetName: NameOf(_engine, _target),
            Trust: relationships.GetMetric(_source, _target, _relationshipTypeId, _trustMetricId),
            Embargo: relationships.HasFlag(_source, _target, _relationshipTypeId, _embargoFlagId),
            SourceCredits: RequireInventory(_engine).CountStackUnits(_source, _creditItemId),
            TargetGoods: RequireInventory(_engine).CountStackUnits(_target, _goodsItemId),
            SuccessfulTrades: _successfulTrades,
            LastStatus: _lastStatus,
            Status: _status,
            RelationshipTypeId: _relationshipTypeId,
            TrustMetricId: _trustMetricId,
            EmbargoFlagId: _embargoFlagId);
    }

    private static Entity CreateNamed(World world, string name)
    {
        Entity entity = world.Create(
            new Name { Value = name },
            new MapEntity { MapId = new MapId(DiplomacyTradeGateIds.ShowcaseMapId) });
        return entity;
    }

    private static string NameOf(GameEngine engine, Entity entity)
    {
        return engine.World.IsAlive(entity) && engine.World.Has<Name>(entity)
            ? engine.World.Get<Name>(entity).Value
            : "none";
    }

    private static InventoryRuntimeService RequireInventory(GameEngine engine)
    {
        return engine.GetService(CoreServiceKeys.InventoryRuntimeService)
            ?? throw new InvalidOperationException("InventoryRuntimeService missing.");
    }

    private static ExchangeRuntime RequireExchange(GameEngine engine)
    {
        return engine.GetService(CoreServiceKeys.ExchangeRuntime)
            ?? throw new InvalidOperationException("ExchangeRuntime missing.");
    }

    private static RelationshipRuntime RequireRelationships(GameEngine engine)
    {
        return engine.GetService(CoreServiceKeys.RelationshipRuntime)
            ?? throw new InvalidOperationException("RelationshipRuntime missing.");
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
        _engine = null;
        _source = Entity.Null;
        _target = Entity.Null;
        _sourceStash = Entity.Null;
        _targetStash = Entity.Null;
        _successfulTrades = 0;
        _lastStatus = ExchangeExecutionStatus.MissingOperation;
        _log.Clear();
        _status = "Load the diplomacy trade gate map.";
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
        input?.PushContext("DiplomacyTradeGate.Controls");
    }

    private static void DeactivateInputContext(PlayerInputHandler? input)
    {
        input?.PopContext("DiplomacyTradeGate.Controls");
    }
}
