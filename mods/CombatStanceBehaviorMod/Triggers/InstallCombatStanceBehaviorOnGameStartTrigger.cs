using System.Threading.Tasks;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.GAS.Orders;
using Ludots.Core.Gameplay.Relationships;
using Ludots.Core.Gameplay.Teams;
using Ludots.Core.Modding;
using Ludots.Core.Scripting;
using Ludots.Core.Spatial;
using CombatStanceBehaviorMod.Runtime;
using CombatStanceBehaviorMod.Systems;

namespace CombatStanceBehaviorMod.Triggers;

public sealed class InstallCombatStanceBehaviorOnGameStartTrigger : Trigger
{
    private const string InstalledKey = "CombatStanceBehaviorMod.Installed";
    private readonly IModContext _context;

    public InstallCombatStanceBehaviorOnGameStartTrigger(IModContext context)
    {
        _context = context;
        EventKey = GameEvents.GameStart;
    }

    public override Task ExecuteAsync(ScriptContext context)
    {
        var engine = context.GetEngine();
        if (engine == null)
        {
            return Task.CompletedTask;
        }

        if (engine.GlobalContext.TryGetValue(InstalledKey, out var installed) && installed is bool flag && flag)
        {
            return Task.CompletedTask;
        }

        engine.GlobalContext[InstalledKey] = true;

        OrderTypeRegistry orderTypes = engine.GetService(CoreServiceKeys.OrderTypeRegistry)
            ?? throw new System.InvalidOperationException("CombatStanceBehaviorMod requires OrderTypeRegistry.");
        ISpatialQueryService spatial = engine.GetService(CoreServiceKeys.SpatialQueryService)
            ?? throw new System.InvalidOperationException("CombatStanceBehaviorMod requires ISpatialQueryService.");
        OrderQueue orders = engine.GetService(CoreServiceKeys.OrderQueue)
            ?? throw new System.InvalidOperationException("CombatStanceBehaviorMod requires OrderQueue.");
        IClock clock = engine.GetService(CoreServiceKeys.Clock)
            ?? throw new System.InvalidOperationException("CombatStanceBehaviorMod requires IClock.");
        RelationshipRuntime relationships = engine.GetService(CoreServiceKeys.RelationshipRuntime)
            ?? throw new System.InvalidOperationException("CombatStanceBehaviorMod requires RelationshipRuntime.");
        RelationshipTypeRegistry relationshipTypes = engine.GetService(CoreServiceKeys.RelationshipTypeRegistry)
            ?? throw new System.InvalidOperationException("CombatStanceBehaviorMod requires RelationshipTypeRegistry.");
        TeamEntityLookup teams = engine.GetService(CoreServiceKeys.TeamEntityLookup)
            ?? throw new System.InvalidOperationException("CombatStanceBehaviorMod requires TeamEntityLookup.");
        CombatStanceBehaviorSettings settings = new CombatStanceBehaviorSettingsLoader(engine.ConfigPipeline)
            .Load(
                engine.ConfigCatalog ?? throw new System.InvalidOperationException("CombatStanceBehaviorMod requires ConfigCatalog."),
                engine.ConfigConflictReport ?? throw new System.InvalidOperationException("CombatStanceBehaviorMod requires ConfigConflictReport."),
                relationshipTypes);

        engine.RegisterSystem(
            new CombatStanceOrderSystem(engine.World, clock, orders, orderTypes, spatial, settings, relationships, teams, engine.EventBus),
            SystemGroup.PostMovement);
        _context.Log("[CombatStanceBehaviorMod] Combat stance behavior system registered");
        return Task.CompletedTask;
    }
}
