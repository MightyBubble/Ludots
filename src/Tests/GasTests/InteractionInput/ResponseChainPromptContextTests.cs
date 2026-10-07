using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Arch.Core;
using Ludots.Core.Components;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.Components;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Input;
using Ludots.Core.Gameplay.GAS.Orders;
using Ludots.Core.Gameplay.GAS.Systems;
using Ludots.Core.Gameplay.MapTriggers;
using Ludots.Core.Gameplay.Teams;
using Ludots.Core.GraphRuntime;
using Ludots.Core.Map;
using Ludots.Core.NodeLibraries.GASGraph;
using Ludots.Core.NodeLibraries.GASGraph.Host;
using Ludots.Core.Scripting;
using NUnit.Framework;
using static NUnit.Framework.Assert;

namespace Ludots.Tests.GAS
{
    /// <summary>
    /// Response-chain prompts reach players through interaction contexts: the engine asks the
    /// owner of the listener that wants to respond, announces the prompt to that player's rep,
    /// and the mod's context graph answers with SubmitResponseChainOrder. Keys never live in the engine.
    /// </summary>
    [TestFixture]
    [NonParallelizable]
    public sealed class ResponseChainPromptContextTests
    {
        private const int AttrHealth = 0;
        private const int TagSpell = 500;
        private const int TagCounter = 501;
        private const int TplSpell = 3101;
        private const int TplCounter = 3102;
        private const int DefenderPlayer = 1;
        private const int CasterPlayer = 2;
        private const float SpellDamage = 10f;
        private const float CounterDamage = 3f;
        private static readonly MapId Map = new("map_response_chain_prompt_probe");

        [Test]
        public void SpellOpensWindow_PromptGoesToListenerOwner_AndIsAnnouncedOnItsRep()
        {
            using var h = new Harness();

            h.CastSpell();

            That(h.Prompt.IsOpen, Is.True);
            That(h.Prompt.PlayerId, Is.EqualTo(DefenderPlayer), "the defender's trap asked to respond, so the defender answers");
            That(h.Prompt.Responder, Is.EqualTo(h.Defender));
            That(h.Prompt.WindowSource, Is.EqualTo(h.Caster));
            That(h.Prompt.WindowTarget, Is.EqualTo(h.Defender));
            That(h.Prompt.OfferedEffectTemplateId, Is.EqualTo(TplCounter));

            h.Events.Update(0f);
            That(h.Announced, Has.Count.EqualTo(1));
            That(h.Announced[0].Event, Is.EqualTo(GameEvents.ResponseChainPromptOpened.Value));
            That(h.Announced[0].Source, Is.EqualTo(h.Caster), "sourceEntity = the unit whose spell opened the window");
            That(h.Announced[0].Target, Is.EqualTo(h.DefenderRep), "targetEntity = the prompted player's rep, where the mod mounts its context");
            That(h.Announced[0].Map, Is.EqualTo(Map));
        }

        [Test]
        public void Activate_CounterHitsCaster_SpellStillLands_PromptClosesAndIsAnnounced()
        {
            using var h = new Harness();
            h.CastSpell();
            h.Events.Update(0f);

            h.Answer(h.DefenderRep, TestResponseChainOrderTypeIds.ChainActivateEffect);
            That(h.Prompt.LastSubmissionResult, Is.EqualTo(OrderSubmitResult.Queued));
            h.Step();

            That(h.Health(h.Caster), Is.EqualTo(1000f - CounterDamage), "the counter strikes back at the unit that cast the spell");
            That(h.Health(h.Defender), Is.EqualTo(1000f - SpellDamage), "activating does not stop the spell");
            That(h.Prompt.IsOpen, Is.False, "nobody else wants to respond, so the chain resolves");

            h.Events.Update(0f);
            That(h.Announced, Has.Count.EqualTo(2));
            That(h.Announced[1].Event, Is.EqualTo(GameEvents.ResponseChainPromptClosed.Value));
            That(h.Announced[1].Target, Is.EqualTo(h.DefenderRep), "the same rep hears the close so its context can step down");

            h.Events.Update(0f);
            That(h.Announced, Has.Count.EqualTo(2), "no repeat announcements while nothing changes");
        }

        [Test]
        public void Pass_ResolvesImmediately_OnlyTheSpellLands()
        {
            using var h = new Harness();
            h.CastSpell();

            h.Answer(h.DefenderRep, TestResponseChainOrderTypeIds.ChainPass);
            h.Step();

            That(h.Prompt.IsOpen, Is.False, "one pass from the prompted player is enough to resolve");
            That(h.Health(h.Defender), Is.EqualTo(1000f - SpellDamage));
            That(h.Health(h.Caster), Is.EqualTo(1000f));
        }

        [Test]
        public void Negate_CancelsTheSpellThatOpenedTheWindow()
        {
            using var h = new Harness();
            h.CastSpell();

            h.Answer(h.DefenderRep, TestResponseChainOrderTypeIds.ChainNegate);
            h.Step();

            That(h.Prompt.IsOpen, Is.False);
            That(h.Health(h.Defender), Is.EqualTo(1000f), "negate cancels the most recent link, which here is the spell itself");
            That(h.Health(h.Caster), Is.EqualTo(1000f));
        }

        [Test]
        public void SecondAnswerToTheSamePrompt_IsCountedAndNotQueued()
        {
            using var h = new Harness();
            h.CastSpell();

            h.Answer(h.DefenderRep, TestResponseChainOrderTypeIds.ChainPass);
            h.Answer(h.DefenderRep, TestResponseChainOrderTypeIds.ChainNegate);

            That(h.Prompt.Answered, Is.True);
            That(h.Prompt.RejectedWithoutPromptCount, Is.EqualTo(1));
            That(h.ChainOrders.Count, Is.EqualTo(1), "only the first answer counts");

            h.Step();
            That(h.Health(h.Defender), Is.EqualTo(1000f - SpellDamage), "the pass wins; the late negate never reached the chain");
        }

        [Test]
        public void AnswerFromAnotherPlayersRep_IsCountedAndNotQueued()
        {
            using var h = new Harness();
            h.CastSpell();

            h.Answer(h.CasterRep, TestResponseChainOrderTypeIds.ChainNegate);

            That(h.Prompt.RejectedWithoutPromptCount, Is.EqualTo(1));
            That(h.ChainOrders.Count, Is.EqualTo(0));
            That(h.Prompt.IsOpen, Is.True);
            That(h.Prompt.Answered, Is.False);
        }

        [Test]
        public void ChainOrderFromAUnitOtherThanTheResponder_IsRejectedAndThePromptKeepsWaiting()
        {
            using var h = new Harness();
            h.CastSpell();
            var foreign = new Order { Actor = h.Caster, OrderTypeId = TestResponseChainOrderTypeIds.ChainNegate };
            That(h.ChainOrders.TryEnqueueAssigned(ref foreign), Is.True);

            h.Step();

            That(h.AdmissionResults.TryGet(foreign.OrderId, OrderAdmissionStage.EntityIntake, out var outcome), Is.True);
            That(outcome.Result, Is.EqualTo(OrderSubmitResult.RejectedValidation));
            That(h.Prompt.IsOpen, Is.True, "the defender still owes an answer");
            That(h.Health(h.Defender), Is.EqualTo(1000f));

            h.Answer(h.DefenderRep, TestResponseChainOrderTypeIds.ChainPass);
            h.Step();
            That(h.Health(h.Defender), Is.EqualTo(1000f - SpellDamage), "the foreign negate never counted");
        }

        [Test]
        public void ActivatedCounterThatAlsoOpensAWindow_RepromptsTheOtherSide_WhoCanNegateTheCounter()
        {
            using var h = new Harness(counterParticipatesInResponse: true, casterAnswersCounter: true);
            h.CastSpell();
            h.Events.Update(0f);

            h.Answer(h.DefenderRep, TestResponseChainOrderTypeIds.ChainActivateEffect);
            h.Step();

            That(h.Prompt.IsOpen, Is.True, "the counter opens a new round of responses");
            That(h.Prompt.PlayerId, Is.EqualTo(CasterPlayer), "now the caster's own trap asks to respond");
            That(h.Prompt.Responder, Is.EqualTo(h.Caster));
            That(h.Prompt.Answered, Is.False);

            h.Events.Update(0f);
            That(h.Announced.Select(a => (a.Event, a.Target)), Is.EqualTo(new[]
            {
                (GameEvents.ResponseChainPromptOpened.Value, h.DefenderRep),
                (GameEvents.ResponseChainPromptClosed.Value, h.DefenderRep),
                (GameEvents.ResponseChainPromptOpened.Value, h.CasterRep),
            }), "the defender's context steps down and the caster's context steps up");

            h.Answer(h.DefenderRep, TestResponseChainOrderTypeIds.ChainPass);
            That(h.Prompt.RejectedWithoutPromptCount, Is.EqualTo(1), "it is no longer the defender's turn");

            h.Answer(h.CasterRep, TestResponseChainOrderTypeIds.ChainNegate);
            h.Step();

            That(h.Prompt.IsOpen, Is.False);
            That(h.Health(h.Caster), Is.EqualTo(1000f), "the caster negated the counter");
            That(h.Health(h.Defender), Is.EqualTo(1000f - SpellDamage), "the spell underneath still lands");
        }

        [Test]
        public void ListenerWithoutPlayerOwner_FailsClosed()
        {
            using var h = new Harness(defenderOwned: false);

            var error = Throws<InvalidOperationException>(() => h.CastSpell());

            That(error!.Message, Does.Contain(EffectProposalProcessingSystem.PromptResponderWithoutOwnerError));
        }

        [Test]
        public void ResponderDestroyed_WindowClosesUnansweredAndIsCounted()
        {
            using var h = new Harness();
            h.CastSpell();
            That(h.Prompt.IsOpen, Is.True);

            h.World.Destroy(h.Defender);
            h.Step();

            That(h.Prompt.IsOpen, Is.False, "nobody is left to answer, so the window cannot keep waiting");
            That(h.Prompt.AbandonedForMissingActorCount, Is.EqualTo(1));
        }

        [Test]
        public void AnswerWithoutWaitingPrompt_IsCountedAndNotQueued()
        {
            using var h = new Harness();

            h.Answer(h.DefenderRep, TestResponseChainOrderTypeIds.ChainPass);

            That(h.Prompt.RejectedWithoutPromptCount, Is.EqualTo(1));
            That(h.ChainOrders.Count, Is.EqualTo(0));
        }

        [Test]
        public void AnswerWithNonResponseChainOrderType_FailsClosed()
        {
            using var h = new Harness();
            h.CastSpell();

            var error = Throws<InvalidOperationException>(() => h.Answer(h.DefenderRep, 101));

            That(error!.Message, Does.Contain("NotResponseChainOrderType"));
        }

        [Test]
        public void AnswerWithFullChainQueue_PublishesRejectedResult_AndThePlayerMayAnswerAgain()
        {
            using var h = new Harness(chainQueueCapacity: 1);
            h.CastSpell();
            var seed = new Order { Actor = h.Caster, OrderTypeId = TestResponseChainOrderTypeIds.ChainPass };
            That(h.ChainOrders.TryEnqueue(in seed), Is.True);

            h.Answer(h.DefenderRep, TestResponseChainOrderTypeIds.ChainNegate);

            That(h.Prompt.LastSubmissionResult, Is.EqualTo(OrderSubmitResult.RejectedQueueFull));
            That(h.AdmissionResults.TryGet(h.Prompt.LastSubmittedOrderId, OrderAdmissionStage.GlobalIntake, out var outcome), Is.True);
            That(outcome.Result, Is.EqualTo(OrderSubmitResult.RejectedQueueFull));
            That(h.Prompt.Answered, Is.False, "a rejected answer does not use up the player's turn");
        }

        [Test]
        public void PromptForPlayerWithoutRep_IsCountedAndNotAnnounced()
        {
            using var h = new Harness(registerDefenderRep: false);
            h.CastSpell();

            h.Events.Update(0f);

            That(h.Events.PromptsWithoutRepresentative, Is.EqualTo(1));
            That(h.Announced, Is.Empty);
        }

        [Test]
        public void AnswerWithoutResponseChainBinding_FailsClosed()
        {
            using var world = World.Create();
            Entity rep = world.Create(new PlayerOwner { PlayerId = DefenderPlayer });
            var api = new GasGraphRuntimeApi(world, null, null, null);

            var error = Throws<InvalidOperationException>(() => RunAnswer(world, api, rep, TestResponseChainOrderTypeIds.ChainPass));

            That(error!.Message, Does.Contain("ResponseChainUnavailable"));
        }

        [Test]
        public void SubmitResponseChainOrder_CompilesFromTriggerGraphWithSemanticOrderType()
        {
            var doc = AnswerDocument(orderType: "chainPass");

            GraphControlFlowCompileResult result = GraphControlFlowCompiler.Compile(doc);

            That(result.Diagnostics.Where(d => d.Severity == GraphDiagnosticSeverity.Error), Is.Empty,
                () => string.Join("\n", result.Diagnostics.Select(d => d.Message)));
            GraphProgramPackage package = result.Package!.Value;
            GraphInstruction submit = package.Program.Single(i => i.Op == (ushort)GraphNodeOp.SubmitResponseChainOrder);
            That(package.Symbols[submit.Imm], Is.EqualTo("chainPass"));
        }

        [Test]
        public void SubmitResponseChainOrder_WithoutOrderType_FailsValidation()
        {
            GraphControlFlowCompileResult result = GraphControlFlowCompiler.Compile(AnswerDocument(orderType: null));

            That(result.Diagnostics.Any(d => d.Severity == GraphDiagnosticSeverity.Error && d.Message.Contains("orderType")), Is.True);
        }

        private static GraphControlFlowDocument AnswerDocument(string? orderType)
        {
            return new GraphControlFlowDocument
            {
                Id = "Graph.Probe.ResponseChain.Answer",
                Kind = "TriggerGraph",
                Entries = new List<TriggerGraphEntryConfig>
                {
                    new() { Label = "on_pass", Event = "MapLoaded", Start = "answer" },
                },
                Nodes = new List<GraphControlFlowNode>
                {
                    new() { Id = "answer", Op = "SubmitResponseChainOrder", OrderType = orderType },
                    new() { Id = "done", Op = "HaltReturnInt" },
                },
                ControlEdges = new List<GraphControlFlowEdge> { new("answer", "next", "done") },
            };
        }

        private static void RunAnswer(World world, IGraphRuntimeApi api, Entity rep, int orderTypeId)
        {
            var state = new GraphExecutionState
            {
                World = world,
                Caster = rep,
                ExplicitTarget = rep,
                Api = api,
                F = new float[GraphVmLimits.MaxFloatRegisters],
                I = new int[GraphVmLimits.MaxIntRegisters],
                B = new byte[GraphVmLimits.MaxBoolRegisters],
                E = new Entity[GraphVmLimits.MaxEntityRegisters],
                Targets = new Entity[GraphVmLimits.MaxTargets],
                TargetList = new GraphTargetList(new Entity[GraphVmLimits.MaxTargets]),
                CallStack = new int[GraphVmLimits.MaxCallStackDepth],
            };
            state.E[0] = rep;
            GasGraphOpHandlerTable.Execute(
                ref state,
                new GraphInstruction[]
                {
                    new() { Op = (ushort)GraphNodeOp.SubmitResponseChainOrder, Imm = orderTypeId },
                    new() { Op = (ushort)GraphNodeOp.HaltReturnInt, A = 0 },
                },
                GasGraphOpHandlerTable.Instance);
        }

        private readonly record struct Announcement(string Event, Entity Source, Entity Target, MapId Map);

        private sealed class Harness : IDisposable
        {
            private readonly EffectRequestQueue _requests = new();
            private readonly EffectProcessingLoopSystem _processing;
            private readonly GasGraphRuntimeApi _api;

            private readonly World _world = World.Create();

            public World World => _world;
            public ResponseChainPromptState Prompt { get; } = new();
            public OrderAdmissionResultBuffer AdmissionResults { get; } = new(16, 16);
            public OrderQueue ChainOrders { get; }
            public ResponseChainPromptEventSystem Events { get; }
            public List<Announcement> Announced { get; } = new();
            public Entity Caster { get; }
            public Entity Defender { get; }
            public Entity DefenderRep { get; }
            public Entity CasterRep { get; }

            public Harness(
                int chainQueueCapacity = 64,
                bool registerDefenderRep = true,
                bool defenderOwned = true,
                bool counterParticipatesInResponse = false,
                bool casterAnswersCounter = false)
            {
                var templates = new EffectTemplateRegistry();
                templates.Register(TplSpell, Damage(TagSpell, SpellDamage, participatesInResponse: true));
                templates.Register(TplCounter, Damage(TagCounter, CounterDamage, counterParticipatesInResponse));
                var builtinHandlers = new BuiltinHandlerRegistry();
                BuiltinHandlers.RegisterAll(builtinHandlers);
                GasTestEffectExecutionPlanFinalizer.FinalizeAll(
                    templates,
                    new PresetTypeRegistry(),
                    builtinHandlers,
                    new GraphProgramRegistry(),
                    "Test/ResponseChainPromptContextTests.json");

                Caster = _world.Create(new PlayerOwner { PlayerId = CasterPlayer }, new AttributeBuffer(), new DirtyFlags());
                Defender = _world.Create(new AttributeBuffer(), new DirtyFlags());
                if (defenderOwned)
                {
                    _world.Add(Defender, new PlayerOwner { PlayerId = DefenderPlayer });
                }
                SetHealth(Caster, 1000f);
                SetHealth(Defender, 1000f);
                AddPromptListener(Defender, TagSpell);
                if (casterAnswersCounter)
                {
                    AddPromptListener(Caster, TagCounter);
                }

                ChainOrders = new OrderQueue(chainQueueCapacity, AdmissionResults);
                _processing = new EffectProcessingLoopSystem(
                    _world,
                    _requests,
                    new DiscreteClock(),
                    new GasConditionRegistry(),
                    1024,
                    GasConstants.MAX_EFFECT_REQUESTS_PER_FRAME,
                    new GasBudget(),
                    templates,
                    Prompt,
                    ChainOrders,
                    new ResponseChainTelemetryBuffer(),
                    new OrderRequestQueue(capacity: 16),
                    responseChainOrderTypes: TestResponseChainOrderTypeIds.Types,
                    tagOps: new TagOps(new DirtyEntityQueue(GasConstants.MAX_EFFECT_REQUESTS_PER_FRAME), new TagRuleRegistry()))
                {
                    MaxWorkUnitsPerSlice = int.MaxValue
                };

                _api = new GasGraphRuntimeApi(_world, null, null, null);
                _api.BindResponseChain(ChainOrders, Prompt, TestResponseChainOrderTypeIds.Types);

                DefenderRep = _world.Create(new PlayerOwner { PlayerId = DefenderPlayer }, new MapEntity { MapId = Map });
                CasterRep = _world.Create(new PlayerOwner { PlayerId = CasterPlayer }, new MapEntity { MapId = Map });

                var players = new PlayerEntityLookup();
                if (registerDefenderRep)
                {
                    players.Register(DefenderPlayer, DefenderRep);
                }
                players.Register(CasterPlayer, CasterRep);

                var triggers = new TriggerManager { EventSchemas = new EventSchemaRegistry() };
                triggers.RegisterEventHandler(GameEvents.ResponseChainPromptOpened, ctx => Record(GameEvents.ResponseChainPromptOpened, ctx));
                triggers.RegisterEventHandler(GameEvents.ResponseChainPromptClosed, ctx => Record(GameEvents.ResponseChainPromptClosed, ctx));
                Events = new ResponseChainPromptEventSystem(Prompt, players, triggers, _world, () => new ScriptContext());
            }

            public void CastSpell()
            {
                _requests.Publish(new EffectRequest { Source = Caster, Target = Defender, TemplateId = TplSpell });
                Step();
            }

            public void Step()
            {
                AdmissionResults.BeginLogicStep();
                _processing.Update(1f);
                AdmissionResults.EndEntityIntake();
                AdmissionResults.EndLogicStep();
            }

            public void Answer(Entity rep, int orderTypeId) => RunAnswer(_world, _api, rep, orderTypeId);

            private void SetHealth(Entity entity, float value)
            {
                ref var attributes = ref _world.Get<AttributeBuffer>(entity);
                attributes.SetBase(AttrHealth, value);
                attributes.SetCurrent(AttrHealth, value);
            }

            public float Health(Entity entity) => _world.Get<AttributeBuffer>(entity).GetCurrent(AttrHealth);

            public void Dispose() => _world.Dispose();

            private static EffectTemplateData Damage(int categoryId, float amount, bool participatesInResponse)
            {
                var modifiers = default(EffectModifiers);
                modifiers.Add(AttrHealth, ModifierOp.Add, -amount);
                return new EffectTemplateData
                {
                    CategoryId = categoryId,
                    LifetimeKind = EffectLifetimeKind.Instant,
                    ClockId = GasClockId.Step,
                    ParticipatesInResponse = participatesInResponse,
                    Modifiers = modifiers,
                };
            }

            private unsafe void AddPromptListener(Entity owner, int firingCategoryId)
            {
                var listener = new ResponseChainListener();
                listener.Add(firingCategoryId, ResponseType.PromptInput, priority: 100, effectTemplateId: TplCounter);
                _world.Add(owner, listener);
            }

            private Task Record(EventKey eventKey, ScriptContext context)
            {
                Announced.Add(new Announcement(
                    eventKey.Value,
                    context.Get<Entity>(MapTriggerEventPayloadKeys.SourceEntity),
                    context.Get<Entity>(MapTriggerEventPayloadKeys.TargetEntity),
                    context.Get<MapId>(ContextKeys.MapId)));
                return Task.CompletedTask;
            }
        }
    }
}
