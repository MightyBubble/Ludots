using System;
using System.Numerics;
using System.Threading.Tasks;
using Arch.Core;
using CombatStanceBehaviorMod.Components;
using CombatStanceShowcaseMod.Runtime;
using Ludots.Core.Components;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Orders;
using Ludots.Core.Modding;
using Ludots.Core.Scripting;

namespace CombatStanceShowcaseMod.Triggers;

public sealed class InstallCombatStanceShowcaseOrdersTrigger : Trigger
{
    private readonly IModContext _context;

    public InstallCombatStanceShowcaseOrdersTrigger(IModContext context)
    {
        _context = context;
        EventKey = GameEvents.MapLoaded;
    }

    public override Task ExecuteAsync(ScriptContext context)
    {
        GameEngine? engine = context.GetEngine();
        if (engine == null)
        {
            return Task.CompletedTask;
        }

        CombatStanceShowcaseConfig config = new CombatStanceShowcaseConfigLoader(engine.ConfigPipeline)
            .Load(
                engine.ConfigCatalog ?? throw new InvalidOperationException("CombatStanceShowcaseMod requires ConfigCatalog."),
                engine.ConfigConflictReport ?? throw new InvalidOperationException("CombatStanceShowcaseMod requires ConfigConflictReport."));

        if (!string.Equals(engine.CurrentMapSession?.MapConfig?.Id, config.MapId, StringComparison.Ordinal))
        {
            return Task.CompletedTask;
        }

        OrderTypeRegistry orderTypes = engine.GetService(CoreServiceKeys.OrderTypeRegistry)
            ?? throw new InvalidOperationException("CombatStanceShowcaseMod requires OrderTypeRegistry.");
        OrderQueue orders = engine.GetService(CoreServiceKeys.OrderQueue)
            ?? throw new InvalidOperationException("CombatStanceShowcaseMod requires OrderQueue.");

        for (int i = 0; i < config.InitialOrders.Length; i++)
        {
            SubmitInitialOrder(engine.World, orderTypes, orders, config.InitialOrders[i], i);
        }

        _context.Log("[CombatStanceShowcaseMod] Initial combat stance orders submitted");
        return Task.CompletedTask;
    }

    private static void SubmitInitialOrder(
        World world,
        OrderTypeRegistry orderTypes,
        OrderQueue orders,
        CombatStanceShowcaseOrderConfig config,
        int index)
    {
        Entity actor = FindEntity(world, config.Actor, $"initialOrders[{index}].actor");
        if (!orderTypes.TryGetId(config.OrderTypeKey, out int orderTypeId) || orderTypeId <= 0)
        {
            throw new InvalidOperationException($"Combat stance showcase initialOrders[{index}].orderTypeKey references unknown order '{config.OrderTypeKey}'.");
        }

        var order = new Order
        {
            Actor = actor,
            OrderTypeId = orderTypeId,
            SubmitMode = OrderSubmitMode.Immediate
        };

        if (config.OrderTypeKey == StanceOrderKeys.AttackMove ||
            config.OrderTypeKey == StanceOrderKeys.AssaultMove)
        {
            order.Args.I0 = config.LeashRadiusCm!.Value;
            order.Args.Spatial.Kind = OrderSpatialKind.WorldCm;
            order.Args.Spatial.Mode = OrderCollectionMode.Single;
            order.Args.Spatial.WorldCm = new Vector3(config.Destination!.XCm!.Value, 0f, config.Destination.YCm!.Value);
        }
        else if (config.OrderTypeKey == StanceOrderKeys.Guard)
        {
            order.Target = FindEntity(world, config.Target, $"initialOrders[{index}].target");
            order.Args.I0 = config.RadiusCm!.Value;
            order.Args.I1 = config.LeashRadiusCm!.Value;
        }
        else if (config.OrderTypeKey == StanceOrderKeys.SetCombatStance)
        {
            order.Args.I0 = ParseStance(config.Stance, $"initialOrders[{index}].stance");
            order.Args.I1 = config.LeashRadiusCm.GetValueOrDefault();
            order.Args.I2 = config.RetaliationTtlSteps.GetValueOrDefault();
        }
        else
        {
            throw new InvalidOperationException($"Combat stance showcase initialOrders[{index}] has unsupported order '{config.OrderTypeKey}'.");
        }

        if (!orders.TryEnqueue(in order))
        {
            throw new InvalidOperationException($"Combat stance showcase could not enqueue initial order '{config.OrderTypeKey}' for '{config.Actor}'.");
        }
    }

    private static int ParseStance(string stance, string context)
    {
        return stance switch
        {
            "HoldFire" => CombatStances.HoldFire,
            "ReturnFire" => CombatStances.ReturnFire,
            "Defend" => CombatStances.Defend,
            "AttackAnything" => CombatStances.AttackAnything,
            _ => throw new InvalidOperationException($"Combat stance showcase {context} references unknown stance '{stance}'.")
        };
    }

    private static Entity FindEntity(World world, string name, string context)
    {
        Entity result = Entity.Null;
        var query = new QueryDescription().WithAll<Name>();
        world.Query(in query, (Entity entity, ref Name entityName) =>
        {
            if (result == Entity.Null && string.Equals(entityName.Value, name, StringComparison.Ordinal))
            {
                result = entity;
            }
        });

        if (result == Entity.Null)
        {
            throw new InvalidOperationException($"Combat stance showcase {context} could not find entity '{name}'.");
        }

        return result;
    }
}
