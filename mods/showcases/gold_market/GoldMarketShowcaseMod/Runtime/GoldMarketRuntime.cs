using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Arch.Core;
using GoldMarketShowcaseMod.UI;
using Ludots.Core.Components;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.Exchange;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.Gameplay.Items;
using Ludots.Core.Input.Runtime;
using Ludots.Core.Map;
using Ludots.Core.Modding;
using Ludots.Core.Scripting;
using Ludots.UI;

namespace GoldMarketShowcaseMod.Runtime;

public sealed class GoldMarketRuntime
{
    private readonly IModContext _context;
    private readonly GoldMarketPanelController _panelController;
    private readonly List<string> _log = new(8);
    private GoldMarketConfig? _config;
    private GameEngine? _engine;
    private Entity _buyer = Entity.Null;
    private Entity _market = Entity.Null;
    private Entity _buyerStash = Entity.Null;
    private Entity _marketStash = Entity.Null;
    private int _buyerLayoutId;
    private int _marketLayoutId;
    private int _goldAttributeId;
    private int _buyOperationId;
    private int _expensiveOperationId;
    private int _atomicFailureOperationId;
    private int _relicItemId;
    private int _bonusItemId;
    private int _successfulPurchases;
    private int _failedPurchases;
    private bool _scenarioReady;
    private ExchangeExecutionStatus _lastStatus = ExchangeExecutionStatus.MissingOperation;
    private string _status = "Load the gold market map.";

    public GoldMarketRuntime(IModContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _panelController = new GoldMarketPanelController(this);
    }

    public GoldMarketSnapshot Snapshot => BuildSnapshot();

    public Task HandleMapFocusedAsync(ScriptContext context)
    {
        if (context.GetEngine() is not GameEngine engine)
        {
            return Task.CompletedTask;
        }

        if (!GoldMarketIds.IsShowcaseMap(engine.CurrentMapSession?.MapId.Value))
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

        if (GoldMarketIds.IsShowcaseMap(context.Get(CoreServiceKeys.MapId).Value))
        {
            DeactivateInputContext(engine.GetService(CoreServiceKeys.InputHandler));
            ClearPanel(engine);
            ResetScenario();
        }

        return Task.CompletedTask;
    }

    public void TryBuy(GameEngine engine)
    {
        Execute(engine, _buyOperationId, "Bought the configured market relic.");
    }

    public void TryExpensive(GameEngine engine)
    {
        Execute(engine, _expensiveOperationId, "Bought the expensive bundle.");
    }

    public void TryAtomicFailure(GameEngine engine)
    {
        Execute(engine, _atomicFailureOperationId, "Bought the overloaded bundle.");
    }

    public void RefillGold(GameEngine engine)
    {
        EnsureScenario(engine);
        TagOps tagOps = engine.GetService(CoreServiceKeys.TagOps)
            ?? throw new InvalidOperationException("GoldMarketShowcase requires TagOps.");
        AttributeMutationOps.SetCurrent(engine.World, _buyer, _goldAttributeId, _config!.RefillGold, tagOps);
        _status = $"Gold refilled to {_config.RefillGold}.";
        PushLog(_status);
        RefreshPanelInternal(engine);
    }

    public void RefreshPanel(GameEngine engine)
    {
        if (GoldMarketIds.IsShowcaseMap(engine.CurrentMapSession?.MapId.Value))
        {
            RefreshPanelInternal(engine);
        }
        else
        {
            ClearPanel(engine);
        }
    }

    internal GoldMarketPanelState BuildPanelState()
    {
        GoldMarketSnapshot snapshot = BuildSnapshot();
        return new GoldMarketPanelState(
            Header: _config?.Header ?? "Gold Market Showcase",
            Summary: _config?.Summary ?? string.Empty,
            Controls: _config?.Controls ?? string.Empty,
            Status: snapshot.Status,
            Lines: new[]
            {
                $"buyer: {snapshot.BuyerName}",
                $"market: {snapshot.MarketName}",
                $"gold attribute: {snapshot.Gold}",
                $"relics bought: {snapshot.Relics}",
                $"bonus goods: {snapshot.Bonuses}",
                $"successful purchases: {snapshot.SuccessfulPurchases}",
                $"failed purchases: {snapshot.FailedPurchases}",
                $"last status: {snapshot.LastStatus}",
            },
            LogLines: _log.Count == 0 ? new[] { snapshot.Status } : _log.ToArray());
    }

    private void Execute(GameEngine engine, int operationId, string successText)
    {
        EnsureScenario(engine);
        ExchangeRuntime exchange = engine.GetService(CoreServiceKeys.ExchangeRuntime)
            ?? throw new InvalidOperationException("ExchangeRuntime missing.");
        ExchangeExecutionResult result = exchange.TryExecute(operationId, new ExchangeExecutionContext(_buyer, _market));
        _lastStatus = result.Status;
        if (result.Succeeded)
        {
            _successfulPurchases++;
            _status = successText;
        }
        else
        {
            _failedPurchases++;
            _status = $"Purchase blocked: {result.Status}.";
        }

        PushLog($"{_status} detail={result.DetailIndex}");
        RefreshPanelInternal(engine);
    }

    private void EnsureConfig()
    {
        if (_config != null)
        {
            return;
        }

        using Stream stream = _context.GetResource($"{_context.ModId}:assets/GoldMarket/gold_market_config.json");
        _config = GoldMarketConfig.Load(stream);
    }

    private void EnsureScenario(GameEngine engine)
    {
        if (_scenarioReady)
        {
            return;
        }

        if (_config == null)
        {
            throw new InvalidOperationException("Gold market config was not loaded.");
        }

        ResolveIds(engine);
        BuildScenario(engine);
        _scenarioReady = true;
        _status = "Press B to buy, X for insufficient funds, F for atomic rollback.";
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

        _buyerLayoutId = layouts.GetId(_config!.BuyerLayout);
        _marketLayoutId = layouts.GetId(_config.MarketLayout);
        _goldAttributeId = AttributeRegistry.GetId(_config.GoldAttribute);
        _buyOperationId = operations.GetId(_config.BuyOperation);
        _expensiveOperationId = operations.GetId(_config.ExpensiveOperation);
        _atomicFailureOperationId = operations.GetId(_config.AtomicFailureOperation);
        _relicItemId = items.GetId(_config.RelicItem);
        _bonusItemId = items.GetId(_config.BonusItem);
    }

    private void BuildScenario(GameEngine engine)
    {
        World world = engine.World;
        _buyer = world.Create(
            new Name { Value = _config!.BuyerName },
            new MapEntity { MapId = new MapId(GoldMarketIds.ShowcaseMapId) },
            new AttributeBuffer(),
            new DirtyFlags());
        _market = world.Create(
            new Name { Value = _config.MarketName },
            new MapEntity { MapId = new MapId(GoldMarketIds.ShowcaseMapId) });

        TagOps tagOps = engine.GetService(CoreServiceKeys.TagOps)
            ?? throw new InvalidOperationException("GoldMarketShowcase requires TagOps.");
        AttributeMutationOps.SetCurrent(world, _buyer, _goldAttributeId, _config.StartingGold, tagOps);

        InventoryRuntimeService inventory = engine.GetService(CoreServiceKeys.InventoryRuntimeService)
            ?? throw new InvalidOperationException("InventoryRuntimeService missing.");
        _buyerStash = inventory.CreateContainer(_buyer, _buyerLayoutId, ItemContainerPurpose.Stash);
        _marketStash = inventory.CreateContainer(_market, _marketLayoutId, ItemContainerPurpose.Stash);
    }

    private GoldMarketSnapshot BuildSnapshot()
    {
        if (_engine == null || !_scenarioReady)
        {
            return new GoldMarketSnapshot(
                string.Empty,
                string.Empty,
                0,
                0,
                0,
                _lastStatus,
                _status,
                0,
                0);
        }

        InventoryRuntimeService inventory = _engine.GetService(CoreServiceKeys.InventoryRuntimeService)
            ?? throw new InvalidOperationException("InventoryRuntimeService missing.");
        AttributeBuffer attributes = _engine.World.Get<AttributeBuffer>(_buyer);
        return new GoldMarketSnapshot(
            BuyerName: NameOf(_engine, _buyer),
            MarketName: NameOf(_engine, _market),
            Gold: (int)attributes.GetCurrent(_goldAttributeId),
            Relics: inventory.CountStackUnits(_buyer, _relicItemId),
            Bonuses: inventory.CountStackUnits(_buyer, _bonusItemId),
            LastStatus: _lastStatus,
            Status: _status,
            SuccessfulPurchases: _successfulPurchases,
            FailedPurchases: _failedPurchases);
    }

    private static string NameOf(GameEngine engine, Entity entity)
    {
        return engine.World.IsAlive(entity) && engine.World.Has<Name>(entity)
            ? engine.World.Get<Name>(entity).Value
            : "none";
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
        _buyer = Entity.Null;
        _market = Entity.Null;
        _buyerStash = Entity.Null;
        _marketStash = Entity.Null;
        _successfulPurchases = 0;
        _failedPurchases = 0;
        _lastStatus = ExchangeExecutionStatus.MissingOperation;
        _log.Clear();
        _status = "Load the gold market map.";
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
        input?.PushContext("GoldMarket.Controls");
    }

    private static void DeactivateInputContext(PlayerInputHandler? input)
    {
        input?.PopContext("GoldMarket.Controls");
    }
}
