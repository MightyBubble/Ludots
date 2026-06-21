using System.Threading.Tasks;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.GAS.Orders;
using Ludots.Core.Gameplay.GAS.Systems;
using Ludots.Core.Modding;
using Ludots.Core.Scripting;
using Ludots.Core.Spatial;
using OpenRaStanceBehaviorMod.Systems;

namespace OpenRaStanceBehaviorMod.Triggers;

public sealed class InstallOpenRaStanceBehaviorOnGameStartTrigger : Trigger
{
    private const string InstalledKey = "OpenRaStanceBehaviorMod.Installed";
    private readonly IModContext _context;

    public InstallOpenRaStanceBehaviorOnGameStartTrigger(IModContext context)
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
            ?? throw new System.InvalidOperationException("OpenRaStanceBehaviorMod requires OrderTypeRegistry.");
        ISpatialQueryService spatial = engine.GetService(CoreServiceKeys.SpatialQueryService)
            ?? throw new System.InvalidOperationException("OpenRaStanceBehaviorMod requires ISpatialQueryService.");
        OrderQueue orders = engine.GetService(CoreServiceKeys.OrderQueue)
            ?? throw new System.InvalidOperationException("OpenRaStanceBehaviorMod requires OrderQueue.");
        IClock clock = engine.GetService(CoreServiceKeys.Clock)
            ?? throw new System.InvalidOperationException("OpenRaStanceBehaviorMod requires IClock.");

        engine.InsertSystemBeforeRequired<OrderBufferSystem>(
            new OpenRaStanceOrderSystem(engine.World, clock, orders, orderTypes, spatial, engine.EventBus),
            SystemGroup.PostMovement);
        _context.Log("[OpenRaStanceBehaviorMod] OpenRA stance behavior system registered");
        return Task.CompletedTask;
    }
}
