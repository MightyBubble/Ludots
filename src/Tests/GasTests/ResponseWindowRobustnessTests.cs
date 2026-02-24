using Arch.Core;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Systems;
using NUnit.Framework;
using static NUnit.Framework.Assert;

namespace Ludots.Tests.GAS
{
    [TestFixture]
    public class ResponseWindowRobustnessTests
    {
        [Test]
        public void ProposalProcessing_ResetSlice_DoesNotDoubleApplyInstantEffects()
        {
            var world = World.Create();
            try
            {
                int attrHealth = 0;
                int tplInstant = 1001;

                var templates = new EffectTemplateRegistry();
                var mods = default(EffectModifiers);
                mods.Add(attrId: attrHealth, ModifierOp.Add, -10f);
                templates.Register(tplInstant, new EffectTemplateData
                {
                    TagId = 1,
                    LifetimeKind = EffectLifetimeKind.Instant,
                    ClockId = GasClockId.Step,
                    DurationTicks = 0,
                    PeriodTicks = 0,
                    ExpireCondition = default,
                    ParticipatesInResponse = false,
                    Modifiers = mods
                });

                var budget = new GasBudget();
                var queue = new EffectRequestQueue();

                var target = world.Create(new AttributeBuffer());
                world.Get<AttributeBuffer>(target).SetCurrent(attrHealth, 100f);

                queue.Publish(new EffectRequest
                {
                    RootId = 1,
                    Source = default,
                    Target = target,
                    TargetContext = default,
                    TemplateId = tplInstant
                });

                var sys = new EffectProposalProcessingSystem(world, queue, budget, templates, inputRequests: null, chainOrders: null)
                {
                    MaxWorkUnitsPerSlice = 2
                };

                bool completed = sys.UpdateSlice(dt: 1f, timeBudgetMs: int.MaxValue);
                That(completed, Is.False);

                float hpAfterFirstSlice = world.Get<AttributeBuffer>(target).GetCurrent(attrHealth);
                That(hpAfterFirstSlice, Is.EqualTo(90f));
                That(queue.Count, Is.EqualTo(1));

                sys.ResetSlice();
                sys.MaxWorkUnitsPerSlice = int.MaxValue;

                while (!sys.UpdateSlice(dt: 1f, timeBudgetMs: int.MaxValue)) { }

                That(queue.Count, Is.EqualTo(0));
                That(world.Get<AttributeBuffer>(target).GetCurrent(attrHealth), Is.EqualTo(hpAfterFirstSlice));
            }
            finally
            {
                world.Dispose();
            }
        }

        [Test]
        public void ProposalProcessing_ChainDepthOverflow_IsDroppedAndCounted()
        {
            var world = World.Create();
            try
            {
                int tplRoot = 2000;
                int rootTag = 100;

                var templates = new EffectTemplateRegistry();
                templates.Register(tplRoot, new EffectTemplateData
                {
                    TagId = rootTag,
                    LifetimeKind = EffectLifetimeKind.Instant,
                    ClockId = GasClockId.Step,
                    DurationTicks = 0,
                    PeriodTicks = 0,
                    ExpireCondition = default,
                    ParticipatesInResponse = true,
                    Modifiers = default
                });

                int chainResponses = ResponseChainListener.CAPACITY;
                for (int i = 0; i < chainResponses; i++)
                {
                    int tplId = 3000 + i;
                    templates.Register(tplId, new EffectTemplateData
                    {
                        TagId = 200 + i,
                        LifetimeKind = EffectLifetimeKind.Instant,
                        ClockId = GasClockId.Step,
                        DurationTicks = 0,
                        PeriodTicks = 0,
                        ExpireCondition = default,
                        ParticipatesInResponse = false,
                        Modifiers = default
                    });
                }

                var listenerEntity = world.Create();
                unsafe
                {
                    var listener = new ResponseChainListener();
                    for (int i = 0; i < chainResponses; i++)
                    {
                        listener.Add(rootTag, ResponseType.Chain, priority: 10, effectTemplateId: 3000 + i);
                    }
                    world.Add(listenerEntity, listener);
                }

                var budget = new GasBudget();
                var queue = new EffectRequestQueue();
                var target = world.Create();

                queue.Publish(new EffectRequest
                {
                    RootId = 1,
                    Source = default,
                    Target = target,
                    TargetContext = default,
                    TemplateId = tplRoot
                });

                var sys = new EffectProposalProcessingSystem(world, queue, budget, templates, inputRequests: null, chainOrders: null)
                {
                    MaxWorkUnitsPerSlice = int.MaxValue
                };

                while (!sys.UpdateSlice(dt: 1f, timeBudgetMs: int.MaxValue)) { }

                int allowedChains = GasConstants.MAX_DEPTH - 1;
                int expectedDropped = chainResponses - allowedChains;
                That(budget.ResponseDepthDropped, Is.EqualTo(expectedDropped));
                That(queue.Count, Is.EqualTo(0));
            }
            finally
            {
                world.Dispose();
            }
        }
    }
}
