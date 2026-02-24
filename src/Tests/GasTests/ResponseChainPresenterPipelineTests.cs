using Arch.Core;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.Components;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Input;
using Ludots.Core.Gameplay.GAS.Orders;
using Ludots.Core.Gameplay.GAS.Systems;
using NUnit.Framework;
using static NUnit.Framework.Assert;
 
namespace Ludots.Tests.GAS
{
    [TestFixture]
    public class ResponseChainPresenterPipelineTests
    {
        [Test]
        public void PromptInput_PublishesOrderRequest_AndConsumesOrderTagId()
        {
            var world = World.Create();
            try
            {
                const int tag = 1001;
                const int tplRoot = 10;
 
                var templates = new EffectTemplateRegistry();
                templates.Register(tplRoot, new EffectTemplateData
                {
                    TagId = tag,
                    LifetimeKind = EffectLifetimeKind.Instant,
                    ClockId = GasClockId.Step,
                    DurationTicks = 0,
                    PeriodTicks = 0,
                    ExpireCondition = default,
                    ParticipatesInResponse = true,
                    Modifiers = default
                });
 
                var clock = new DiscreteClock();
                var conditions = new GasConditionRegistry();
                var budget = new GasBudget();
                var requests = new EffectRequestQueue();
                var inputReq = new InputRequestQueue();
                var chainOrders = new OrderQueue();
                var telemetry = new ResponseChainTelemetryBuffer();
                var orderReq = new OrderRequestQueue();
 
                var processing = new EffectProcessingLoopSystem(world, requests, clock, conditions, budget, templates, inputReq, chainOrders, telemetry, orderReq)
                {
                    MaxWorkUnitsPerSlice = int.MaxValue
                };
 
                var target = world.Create(new AttributeBuffer(), new ActiveEffectContainer(), new PlayerOwner { PlayerId = 1 });
                var listener = default(ResponseChainListener);
                listener.Add(tag, ResponseType.PromptInput, priority: 100, effectTemplateId: tplRoot);
                world.Add(target, listener);
 
                requests.Publish(new EffectRequest
                {
                    RootId = 0,
                    Source = target,
                    Target = target,
                    TargetContext = default,
                    TemplateId = tplRoot
                });
 
                processing.Update(0f);
 
                That(orderReq.TryDequeue(out var req), Is.True);
                That(req.PlayerId, Is.EqualTo(1));
                That(req.PromptTagId, Is.EqualTo(tplRoot));
                That(req.AllowedCount, Is.GreaterThanOrEqualTo(2));
 
                var args = default(OrderArgs);
                args.I0 = tplRoot;
                chainOrders.TryEnqueue(new Order { OrderTagId = TestGasOrderTags.ChainActivateEffect, PlayerId = 1, Actor = target, Target = target, Args = args });
                chainOrders.TryEnqueue(new Order { OrderTagId = TestGasOrderTags.ChainPass, PlayerId = 1, Actor = target, Target = target });
                chainOrders.TryEnqueue(new Order { OrderTagId = TestGasOrderTags.ChainPass, PlayerId = 1, Actor = target, Target = target });
 
                processing.Update(0f);
 
                bool sawAdded = false;
                bool sawClosed = false;
                for (int i = 0; i < telemetry.Count; i++)
                {
                    var e = telemetry[i];
                    if (e.Kind == ResponseChainTelemetryKind.ProposalAdded) sawAdded = true;
                    if (e.Kind == ResponseChainTelemetryKind.WindowClosed) sawClosed = true;
                }
 
                That(sawAdded, Is.True);
                That(sawClosed, Is.True);
            }
            finally
            {
                world.Dispose();
            }
        }
    }
}
