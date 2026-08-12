using System;
using Arch.Core;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Input;
using Ludots.Core.Gameplay.GAS.Orders;
using Ludots.Core.Gameplay.GAS.Systems;
using Ludots.Core.GraphRuntime;
using NUnit.Framework;
using static NUnit.Framework.Assert;

namespace Ludots.Tests.GAS
{
    [TestFixture]
    public class InteractiveWindowStressTests
    {
        [Test]
        public void InteractiveWindow_Stress_ReportsPhaseTimeAndGc()
        {
            var world = World.Create();
            try
            {
                int attrHealth = 0;
                int tagOpen = 500;
                int tagDamage = 501;
                int inputRequestTag = 800;

                int tplOpen = 3001;
                int tplDamage = 3002;

                var templates = new EffectTemplateRegistry();
                templates.Register(tplOpen, new EffectTemplateData
                {
                    TagId = tagOpen,
                    LifetimeKind = EffectLifetimeKind.Instant,
                    ClockId = GasClockId.Step,
                    DurationTicks = 0,
                    PeriodTicks = 0,
                    ExpireCondition = default,
                    ParticipatesInResponse = true,
                    Modifiers = default
                });

                var dmgMods = default(EffectModifiers);
                dmgMods.Add(attrHealth, ModifierOp.Add, -1f);
                templates.Register(tplDamage, new EffectTemplateData
                {
                    TagId = tagDamage,
                    LifetimeKind = EffectLifetimeKind.Instant,
                    ClockId = GasClockId.Step,
                    DurationTicks = 0,
                    PeriodTicks = 0,
                    ExpireCondition = default,
                    ParticipatesInResponse = false,
                    Modifiers = dmgMods
                });

                var presetTypes = new PresetTypeRegistry();
                var builtinHandlers = new BuiltinHandlerRegistry();
                BuiltinHandlers.RegisterAll(builtinHandlers);
                GasTestEffectExecutionPlanFinalizer.FinalizeAll(
                    templates,
                    presetTypes,
                    builtinHandlers,
                    new GraphProgramRegistry(),
                    "Test/InteractiveWindowStressTests.Stress.json");

                var listenerEntity = world.Create();
                unsafe
                {
                    var listener = new ResponseChainListener();
                    listener.Add(tagOpen, ResponseType.PromptInput, priority: 100, effectTemplateId: inputRequestTag);
                    world.Add(listenerEntity, listener);
                }

                var clock = new DiscreteClock();
                var conditions = new GasConditionRegistry();
                var budget = new GasBudget();
                var requests = new EffectRequestQueue();
                var inputReq = new InputRequestQueue(capacity: 4096);
                var orderReq = new OrderRequestQueue(capacity: 4096);
                var admissionResults = new OrderAdmissionResultBuffer(6, 6);
                var chainOrders = new OrderQueue(64, admissionResults);

                var processing = new EffectProcessingLoopSystem(
                    world,
                    requests,
                    clock,
                    conditions,
                    16384,
                    GasConstants.MAX_EFFECT_REQUESTS_PER_FRAME,
                    budget,
                    templates,
                    inputReq,
                    chainOrders,
                    new ResponseChainTelemetryBuffer(),
                    orderReq,
                    responseChainOrderTypes: TestResponseChainOrderTypeIds.Types,
                    tagOps: new TagOps(new DirtyEntityQueue(GasConstants.MAX_EFFECT_REQUESTS_PER_FRAME), new TagRuleRegistry()))
                {
                    MaxWorkUnitsPerSlice = int.MaxValue
                };

                var source = world.Create();
                var target = world.Create(AttributeBuffer.CreateAttached(), new DirtyFlags());
                ref var attr = ref world.Get<AttributeBuffer>(target);
                attr.SetCurrent(attrHealth, 1000f);

                for (int i = 0; i < 10; i++)
                {
                    admissionResults.BeginLogicStep();
                    requests.Publish(new EffectRequest { Source = source, Target = target, TemplateId = tplOpen });
                    processing.Update(1f);
                    That(inputReq.TryDequeue(out _), Is.True);
                    That(orderReq.TryDequeue(out _), Is.True);
                    var activate = new Order { OrderTypeId = TestResponseChainOrderTypeIds.ChainActivateEffect, Actor = source, Args = new OrderArgs { I0 = tplDamage } };
                    var pass1 = new Order { OrderTypeId = TestResponseChainOrderTypeIds.ChainPass, Actor = source };
                    var pass2 = new Order { OrderTypeId = TestResponseChainOrderTypeIds.ChainPass, Actor = source };
                    That(chainOrders.SubmitAssigned(ref activate), Is.EqualTo(OrderSubmitResult.Queued));
                    That(chainOrders.SubmitAssigned(ref pass1), Is.EqualTo(OrderSubmitResult.Queued));
                    That(chainOrders.SubmitAssigned(ref pass2), Is.EqualTo(OrderSubmitResult.Queued));
                    processing.Update(1f);
                    admissionResults.EndEntityIntake();
                    admissionResults.EndLogicStep();
                }

                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();

                int windows = 10_000;
                long alloc0 = GC.GetAllocatedBytesForCurrentThread();
                int gen0_0 = GC.CollectionCount(0);
                int gen1_0 = GC.CollectionCount(1);
                int gen2_0 = GC.CollectionCount(2);

                long ticksCollect = 0;
                long ticksWait = 0;
                long ticksResolve = 0;
                long ticksOther = 0;
                int failedInputDequeues = 0;
                int failedOrderDequeues = 0;
                int failedSubmissions = 0;
                int maxAdmissionCount = 0;

                long elapsedStart = System.Diagnostics.Stopwatch.GetTimestamp();
                for (int i = 0; i < windows; i++)
                {
                    admissionResults.BeginLogicStep();
                    requests.Publish(new EffectRequest { Source = source, Target = target, TemplateId = tplOpen });
                    long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
                    byte phase0 = processing.DebugProposalWindowPhase;
                    processing.Update(1f);
                    long dt = System.Diagnostics.Stopwatch.GetTimestamp() - t0;
                    switch (phase0)
                    {
                        case 1: ticksCollect += dt; break;
                        case 2: ticksWait += dt; break;
                        case 3: ticksResolve += dt; break;
                        default: ticksOther += dt; break;
                    }

                    if (!inputReq.TryDequeue(out _)) failedInputDequeues++;
                    if (!orderReq.TryDequeue(out _)) failedOrderDequeues++;
                    var activate = new Order { OrderTypeId = TestResponseChainOrderTypeIds.ChainActivateEffect, Actor = source, Args = new OrderArgs { I0 = tplDamage } };
                    var pass1 = new Order { OrderTypeId = TestResponseChainOrderTypeIds.ChainPass, Actor = source };
                    var pass2 = new Order { OrderTypeId = TestResponseChainOrderTypeIds.ChainPass, Actor = source };
                    if (chainOrders.SubmitAssigned(ref activate) != OrderSubmitResult.Queued) failedSubmissions++;
                    if (chainOrders.SubmitAssigned(ref pass1) != OrderSubmitResult.Queued) failedSubmissions++;
                    if (chainOrders.SubmitAssigned(ref pass2) != OrderSubmitResult.Queued) failedSubmissions++;

                    t0 = System.Diagnostics.Stopwatch.GetTimestamp();
                    phase0 = processing.DebugProposalWindowPhase;
                    processing.Update(1f);
                    dt = System.Diagnostics.Stopwatch.GetTimestamp() - t0;
                    switch (phase0)
                    {
                        case 1: ticksCollect += dt; break;
                        case 2: ticksWait += dt; break;
                        case 3: ticksResolve += dt; break;
                        default: ticksOther += dt; break;
                    }

                    maxAdmissionCount = Math.Max(maxAdmissionCount, admissionResults.Count);
                    admissionResults.EndEntityIntake();
                    admissionResults.EndLogicStep();
                }
                long elapsedTicks = System.Diagnostics.Stopwatch.GetTimestamp() - elapsedStart;

                long alloc1 = GC.GetAllocatedBytesForCurrentThread();
                int gen0_1 = GC.CollectionCount(0);
                int gen1_1 = GC.CollectionCount(1);
                int gen2_1 = GC.CollectionCount(2);

                double msCollect = ticksCollect * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
                double msWait = ticksWait * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
                double msResolve = ticksResolve * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
                double msOther = ticksOther * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
                double msSum = msCollect + msWait + msResolve + msOther;
                double elapsedMs = elapsedTicks * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
                double perWindowUs = (elapsedMs * 1000.0) / windows;

                Console.WriteLine($"[MUD][WINDOW][STRESS] Windows={windows} ElapsedMs={elapsedMs:F1} PerWindowUs={perWindowUs:F3}");
                Console.WriteLine($"[MUD][WINDOW][STRESS] TimeMs: Collect={msCollect:F2} Wait={msWait:F2} Resolve={msResolve:F2} Other={msOther:F2} Sum={msSum:F2}");
                Console.WriteLine($"[MUD][WINDOW][STRESS] AllocBytes(CurrentThread)={alloc1 - alloc0}");
                Console.WriteLine($"[MUD][WINDOW][STRESS] GC Collections Δ: Gen0={gen0_1 - gen0_0} Gen1={gen1_1 - gen1_0} Gen2={gen2_1 - gen2_0}");

                That(alloc1 - alloc0, Is.Zero);
                That(failedInputDequeues, Is.Zero);
                That(failedOrderDequeues, Is.Zero);
                That(failedSubmissions, Is.Zero);
                That(maxAdmissionCount, Is.LessThanOrEqualTo(admissionResults.GenerationCapacity));
                That(chainOrders.Count, Is.Zero);
                That(admissionResults.Count, Is.LessThanOrEqualTo(admissionResults.GenerationCapacity));
                That(admissionResults.OverflowCount, Is.Zero);
                That(world.Get<AttributeBuffer>(target).GetCurrent(attrHealth), Is.LessThan(1000f));
                Pass("Interactive window stress complete");
            }
            finally
            {
                world.Dispose();
            }
        }
    }
}
