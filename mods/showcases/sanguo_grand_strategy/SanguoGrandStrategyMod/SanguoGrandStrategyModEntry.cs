using Ludots.Core.Engine;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.Gameplay.Teams;
using Ludots.Core.Modding;
using Ludots.Core.Scripting;
using SanguoGrandStrategyMod.Runtime;
using SanguoGrandStrategyMod.Systems;
using SanguoGrandStrategyMod.Web;

namespace SanguoGrandStrategyMod;

public sealed class SanguoGrandStrategyModEntry : IMod
{
    private SanguoGrandStrategyRuntime? _runtime;
    private SanguoBrowserPanel? _browserPanel;
    private IModContext? _context;

    public void OnLoad(IModContext context)
    {
        _context = context;
        RegisterAttributes();
        RegisterTags();

        _runtime = new SanguoGrandStrategyRuntime(context);
        _browserPanel = new SanguoBrowserPanel();

        context.Log("[SanguoGrandStrategyMod] Loaded.");
        context.OnEvent(GameEvents.GameStart, OnGameStartAsync);
        context.OnEvent(GameEvents.MapLoaded, _runtime.HandleMapFocusedAsync);
        context.OnEvent(GameEvents.MapResumed, _runtime.HandleMapFocusedAsync);
        context.OnEvent(GameEvents.MapUnloaded, _runtime.HandleMapUnloadedAsync);
    }

    public void OnUnload()
    {
        _browserPanel?.Dispose();
        _browserPanel = null;
        _runtime = null;
    }

    private async Task OnGameStartAsync(ScriptContext context)
    {
        GameEngine? engine = context.GetEngine();
        if (engine == null || _runtime == null)
        {
            return;
        }

        if (engine.GlobalContext.TryGetValue(SanguoGrandStrategyIds.InstalledKey, out object? installedObj) &&
            installedObj is bool installed &&
            installed)
        {
            return;
        }

        engine.GlobalContext[SanguoGrandStrategyIds.InstalledKey] = true;
        engine.GlobalContext[SanguoGrandStrategyIds.RuntimeServiceKey] = _runtime;

        SeedTeamRelationships();
        engine.RegisterSystem(new SanguoGrandStrategySimulationSystem(engine, _runtime), SystemGroup.InputCollection);
        engine.RegisterPresentationSystem(new SanguoGrandStrategyPresentationSystem(engine, _runtime));

        if (_browserPanel != null)
        {
            try
            {
                await _browserPanel.TryStartAsync(context, _runtime).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _runtime.SetWebUiStatus($"WebUI DataPlane: startup failed ({ex.Message}).");
                _context?.Log($"[SanguoGrandStrategyMod] WebUI DataPlane startup failed: {ex}");
            }
        }

        _context?.Log("[SanguoGrandStrategyMod] Runtime systems registered.");
    }

    private static void RegisterAttributes()
    {
        AttributeRegistry.Register("Health");
        AttributeRegistry.Register("Population");
        AttributeRegistry.Register("Troops");
        AttributeRegistry.Register("Food");
        AttributeRegistry.Register("Gold");
        AttributeRegistry.Register("Production");
        AttributeRegistry.Register("Defense");
        AttributeRegistry.Register("Morale");
        AttributeRegistry.Register("Supply");
        AttributeRegistry.Register("Training");
        AttributeRegistry.Register("Loyalty");
        AttributeRegistry.Register("Command");
        AttributeRegistry.Register("TechProgress");
    }

    private static void RegisterTags()
    {
        string[] tags =
        {
            "Cooldown.Sanguo.Recruit",
            "Cooldown.Sanguo.Economy",
            "Cooldown.Sanguo.Military",
            "Cooldown.Sanguo.Tech",
            "Cooldown.Sanguo.Diplomacy",
            "State.Sanguo.AtWar",
            "State.Sanguo.TradePact",
            "State.Sanguo.Researching",
            "Sanguo.Slot.Weapon",
            "Sanguo.Slot.Armor",
            "Sanguo.Slot.Mount",
            "Sanguo.Slot.Seal",
            "Sanguo.Slot.Manual",
            "Sanguo.Item",
            "Effect.Sanguo.Recruit",
            "Effect.Sanguo.Economy",
            "Effect.Sanguo.Military",
            "Effect.Sanguo.Tech",
            "Effect.Sanguo.Diplomacy",
            "Effect.Sanguo.Item"
        };

        for (int i = 0; i < tags.Length; i++)
        {
            TagRegistry.Register(tags[i]);
        }
    }

    private static void SeedTeamRelationships()
    {
        TeamManager.SetRelationshipSymmetric(1, 2, TeamRelationship.Hostile);
        TeamManager.SetRelationshipSymmetric(1, 3, TeamRelationship.Hostile);
        TeamManager.SetRelationshipSymmetric(1, 4, TeamRelationship.Neutral);
        TeamManager.SetRelationshipSymmetric(1, 5, TeamRelationship.Hostile);
        TeamManager.SetRelationshipSymmetric(1, 6, TeamRelationship.Neutral);
        TeamManager.SetRelationshipSymmetric(1, 7, TeamRelationship.Hostile);
        TeamManager.SetRelationshipSymmetric(1, 8, TeamRelationship.Neutral);
        TeamManager.SetRelationshipSymmetric(2, 3, TeamRelationship.Neutral);
        TeamManager.SetRelationshipSymmetric(2, 5, TeamRelationship.Hostile);
        TeamManager.SetRelationshipSymmetric(3, 5, TeamRelationship.Hostile);
        TeamManager.SetRelationshipSymmetric(4, 5, TeamRelationship.Neutral);
        TeamManager.SetRelationshipSymmetric(6, 8, TeamRelationship.Neutral);
        TeamManager.SetRelationshipSymmetric(7, 3, TeamRelationship.Hostile);
    }
}
