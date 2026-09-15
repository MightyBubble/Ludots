using System.Globalization;
using Arch.Core;
using CapabilityStandardGraphBehaviorCommon;
using Ludots.Core.Gameplay.Components;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Orders;
using Ludots.Core.NodeLibraries.GASGraph;
using Ludots.Core.NodeLibraries.GASGraph.Host;
using Ludots.Platform.Abstractions;

namespace CapabilityStandardGraphOpsNodeGalleryMod.Runtime.Drivers;

/// <summary>
/// Driver for the behavior-side order ops (issue #1536): the featured Script graph runs
/// on the gallery caster with the engine's order pipeline already bound, and the caption
/// quotes the observable order-pipeline state — what SubmitAssignedOrder enqueued, or
/// what CompleteActiveOrder settled.
/// </summary>
public sealed class OrderNodeDriver : IGraphOpsNodeDriver
{
    private const string MoveOrderKey = "moveTo";

    public void Seed(GraphOpsNodeDriverContext ctx)
    {
        GraphOpsNodeActorBinding.RequireMapActors(ctx);
        if (ctx.Orders == null || ctx.OrderTypes == null)
        {
            throw new InvalidOperationException(
                $"Gallery '{ctx.Vignette.Op}' requires the engine order pipeline (OrderQueue + OrderTypeRegistry).");
        }

        if (!ctx.SimWorld.Has<PlayerOwner>(ctx.Caster))
        {
            ctx.SimWorld.Add(ctx.Caster, new PlayerOwner { PlayerId = 1 });
        }

        if (!ctx.SimWorld.Has<OrderBuffer>(ctx.Caster))
        {
            ctx.SimWorld.Add(ctx.Caster, new OrderBuffer());
        }

        GraphOpsNodeActorBinding.BindHud(ctx);
    }

    public void Tick(GraphOpsNodeDriverContext ctx)
    {
        if (ctx.Vignette.Op == nameof(GraphNodeOp.CompleteActiveOrder))
        {
            ctx.SimWorld.Set(ctx.Caster, ActiveMoveOrder(ctx.OrderTypes!));
        }

        int terminalBefore = ctx.OrderTypes!.TerminalResults.Count;
        var featured = ctx.ExecuteFeaturedGraph();

        if (ctx.Vignette.Op == nameof(GraphNodeOp.LoadOrderTypeId))
        {
            ctx.CaptionValues["result"] = featured.IntValue.ToString(CultureInfo.InvariantCulture);
            ctx.Metrics.Detail = GraphOpsNodeActorBinding.FormatDetail(ctx.Vignette.DetailTemplate, ctx.CaptionValues);
            GraphOpsNodeActorBinding.SyncHud(ctx);
            return;
        }

        if (ctx.Vignette.Op == nameof(GraphNodeOp.SubmitAssignedOrder))
        {
            int count = 0;
            int x = 0;
            int y = 0;
            while (ctx.Orders!.TryDequeue(out Order order))
            {
                x = (int)order.Args.Spatial.WorldCm.X;
                y = (int)order.Args.Spatial.WorldCm.Z;
                count++;
            }

            if (count == 0)
            {
                throw new InvalidOperationException(
                    "SubmitAssignedOrder vignette expected the featured graph to enqueue at least one order.");
            }

            ctx.CaptionValues["x"] = x.ToString(CultureInfo.InvariantCulture);
            ctx.CaptionValues["y"] = y.ToString(CultureInfo.InvariantCulture);
            ctx.CaptionValues["count"] = count.ToString(CultureInfo.InvariantCulture);
        }
        else
        {
            int completed = ctx.OrderTypes.TerminalResults.Count - terminalBefore;
            if (completed == 0)
            {
                throw new InvalidOperationException(
                    "CompleteActiveOrder vignette expected the featured graph to settle the active order.");
            }

            bool bufferEmpty = ctx.SimWorld.Get<OrderBuffer>(ctx.Caster).ActiveIndex < 0;
            if (!bufferEmpty)
            {
                throw new InvalidOperationException(
                    "CompleteActiveOrder vignette expected the active order slot to be released after completion.");
            }

            ctx.CaptionValues["completed"] = completed.ToString(CultureInfo.InvariantCulture);
        }

        ctx.Metrics.Detail = GraphOpsNodeActorBinding.FormatDetail(ctx.Vignette.DetailTemplate, ctx.CaptionValues);
        GraphOpsNodeActorBinding.SyncHud(ctx);
    }

    public void DrawOverlay(GraphOpsNodeDriverContext ctx, DebugDrawCommandBuffer draw)
    {
        int caster = GraphOpsNodeActorBinding.FindRole(ctx.Vignette, "caster");
        int target = GraphOpsNodeActorBinding.FindRole(ctx.Vignette, "target");
        if (caster < 0 || target < 0)
        {
            return;
        }

        var casterActor = ctx.Vignette.Actors[caster];
        var targetActor = ctx.Vignette.Actors[target];
        GraphShowcaseStagePresenter.DrawAggroLine(
            draw,
            casterActor.X,
            casterActor.Y,
            targetActor.X,
            targetActor.Y);
    }

    private static OrderBuffer ActiveMoveOrder(OrderTypeRegistry orderTypes)
    {
        if (!orderTypes.TryGetId(MoveOrderKey, out int orderTypeId) || orderTypeId <= 0)
        {
            throw new InvalidOperationException(
                $"Order gallery requires the '{MoveOrderKey}' order type to be registered by the engine config.");
        }

        return new OrderBuffer
        {
            ActiveIndex = 0,
            ActiveOrder = new QueuedOrder
            {
                Order = new Order
                {
                    OrderId = 1,
                    OrderTypeId = orderTypeId,
                    PlayerId = 1,
                    Actor = default,
                },
            },
        };
    }
}
