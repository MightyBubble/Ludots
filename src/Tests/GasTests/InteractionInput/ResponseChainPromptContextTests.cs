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
    /// Response-chain prompts reach players through interaction contexts: the engine announces
    /// "a prompt is waiting on you" to the player's representative entity, and the mod's
    /// context graph answers with SubmitResponseChainOrder. Keys never live in the engine.
    /// </summary>
    [TestFixture]
    [NonParallelizable]
    public sealed class ResponseChainPromptContextTests
    {
        private const int AttrHealth = 0;
        private const int TagOpen = 500;
        private const int TagDamage = 501;
        private const int TplOpen = 3101;
        private const int TplDamage = 3102;
        private const int PromptedPlayer = 1;
        private static readonly MapId Map = new("map_response_chain_prompt_probe");

        [Test]
        public void PromptWaitsOnPlayer_ContextAnswers_OfferedEffectLandsAndPromptCloses()
        {
            using var h = new Harness();

            h.PublishOpeningSpell();

            That(h.Prompt.IsOpen, Is.True, "the window waits for the owning player");
            That(h.Prompt.PlayerId, Is.EqualTo(PromptedPlayer));
            That(h.Prompt.Actor, Is.EqualTo(h.Caster));
            That(h.Prompt.OfferedEffectTemplateId, Is.EqualTo(TplDamage));

            h.Events.Update(0f);
            That(h.Announced, Has.Count.EqualTo(1));
            That(h.Announced[0].Event, Is.EqualTo(GameEvents.ResponseChainPromptOpened.Value));
            That(h.Announced[0].Source, Is.EqualTo(h.Caster), "sourceEntity = the unit whose spell opened the window");
            That(h.Announced[0].Target, Is.EqualTo(h.Rep), "targetEntity = the prompted player's rep, where the mod mounts its context");
            That(h.Announced[0].Map, Is.EqualTo(Map));

            h.Answer(h.Rep, TestResponseChainOrderTypeIds.ChainActivateEffect);
            h.Answer(h.Rep, TestResponseChainOrderTypeIds.ChainPass);
            h.Answer(h.Rep, TestResponseChainOrderTypeIds.ChainPass);
            That(h.Prompt.LastSubmissionResult, Is.EqualTo(OrderSubmitResult.Queued));

            h.Step();

            That(h.TargetHealth(), Is.EqualTo(999f), "activating chains the offered damage onto the spell's target");
            That(h.Prompt.IsOpen, Is.False, "two passes close the window");

            h.Events.Update(0f);
            That(h.Announced, Has.Count.EqualTo(2));
            That(h.Announced[1].Event, Is.EqualTo(GameEvents.ResponseChainPromptClosed.Value));
            That(h.Announced[1].Target, Is.EqualTo(h.Rep), "the same rep hears the close so its context can step down");

            h.Events.Update(0f);
            That(h.Announced, Has.Count.EqualTo(2), "no repeat announcements while nothing changes");
        }

        [Test]
        public void PromptActorDestroyed_WindowClosesUnansweredAndIsCounted()
        {
            using var h = new Harness();
            h.PublishOpeningSpell();
            That(h.Prompt.IsOpen, Is.True);

            h.World.Destroy(h.Caster);
            h.Step();

            That(h.Prompt.IsOpen, Is.False, "nobody is left to answer, so the window cannot keep waiting");
            That(h.Prompt.AbandonedForMissingActorCount, Is.EqualTo(1));
        }

        [Test]
        public void AnswerWithoutWaitingPrompt_IsCountedAndNotQueued()
        {
            using var h = new Harness();

            h.Answer(h.Rep, TestResponseChainOrderTypeIds.ChainPass);

            That(h.Prompt.RejectedWithoutPromptCount, Is.EqualTo(1));
            That(h.ChainOrders.Count, Is.EqualTo(0));
        }

        [Test]
        public void AnswerFromAnotherPlayersRep_IsCountedAndNotQueued()
        {
            using var h = new Harness();
            Entity otherRep = h.World.Create(new PlayerOwner { PlayerId = 2 }, new MapEntity { MapId = Map });
            h.PublishOpeningSpell();

            h.Answer(otherRep, TestResponseChainOrderTypeIds.ChainPass);

            That(h.Prompt.RejectedWithoutPromptCount, Is.EqualTo(1));
            That(h.ChainOrders.Count, Is.EqualTo(0));
            That(h.Prompt.IsOpen, Is.True);
        }

        [Test]
        public void AnswerWithNonResponseChainOrderType_FailsClosed()
        {
            using var h = new Harness();
            h.PublishOpeningSpell();

            var error = Throws<InvalidOperationException>(() => h.Answer(h.Rep, 101));

            That(error!.Message, Does.Contain("NotResponseChainOrderType"));
        }

        [Test]
        public void AnswerWithFullChainQueue_PublishesRejectedResult()
        {
            using var h = new Harness(chainQueueCapacity: 1);
            h.PublishOpeningSpell();
            var seed = new Order { Actor = h.Caster, OrderTypeId = TestResponseChainOrderTypeIds.ChainPass };
            That(h.ChainOrders.TryEnqueue(in seed), Is.True);

            h.Answer(h.Rep, TestResponseChainOrderTypeIds.ChainNegate);

            That(h.Prompt.LastSubmissionResult, Is.EqualTo(OrderSubmitResult.RejectedQueueFull));
            That(h.AdmissionResults.TryGet(h.Prompt.LastSubmittedOrderId, OrderAdmissionStage.GlobalIntake, out var outcome), Is.True);
            That(outcome.Result, Is.EqualTo(OrderSubmitResult.RejectedQueueFull));
        }

        [Test]
        public void PromptForPlayerWithoutRep_IsCountedAndNotAnnounced()
        {
            using var h = new Harness(registerRep: false);
            h.PublishOpeningSpell();

            h.Events.Update(0f);

            That(h.Events.PromptsWithoutRepresentative, Is.EqualTo(1));
            That(h.Announced, Is.Empty);
        }

        [Test]
        public void AnswerWithoutResponseChainBinding_FailsClosed()
        {
            using var world = World.Create();
            Entity rep = world.Create(new PlayerOwner { PlayerId = PromptedPlayer });
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
            public Entity Target { get; }
            public Entity Rep { get; }

            public Harness(int chainQueueCapacity = 64, bool registerRep = true)
            {
                var templates = new EffectTemplateRegistry();
                templates.Register(TplOpen, new EffectTemplateData
                {
                    CategoryId = TagOpen,
                    LifetimeKind = EffectLifetimeKind.Instant,
                    ClockId = GasClockId.Step,
                    ParticipatesInResponse = true,
                });
                var damage = default(EffectModifiers);
                damage.Add(AttrHealth, ModifierOp.Add, -1f);
                templates.Register(TplDamage, new EffectTemplateData
                {
                    CategoryId = TagDamage,
                    LifetimeKind = EffectLifetimeKind.Instant,
                    ClockId = GasClockId.Step,
                    ParticipatesInResponse = false,
                    Modifiers = damage,
                });
                var builtinHandlers = new BuiltinHandlerRegistry();
                BuiltinHandlers.RegisterAll(builtinHandlers);
                GasTestEffectExecutionPlanFinalizer.FinalizeAll(
                    templates,
                    new PresetTypeRegistry(),
                    builtinHandlers,
                    new GraphProgramRegistry(),
                    "Test/ResponseChainPromptContextTests.json");

                Entity listenerEntity = _world.Create();
                unsafe
                {
                    var listener = new ResponseChainListener();
                    listener.Add(TagOpen, ResponseType.PromptInput, priority: 100, effectTemplateId: TplDamage);
                    _world.Add(listenerEntity, listener);
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

                Caster = _world.Create(new PlayerOwner { PlayerId = PromptedPlayer });
                Target = _world.Create(new AttributeBuffer(), new DirtyFlags());
                _world.Get<AttributeBuffer>(Target).SetCurrent(AttrHealth, 1000f);
                Rep = _world.Create(new PlayerOwner { PlayerId = PromptedPlayer }, new MapEntity { MapId = Map });

                var players = new PlayerEntityLookup();
                if (registerRep)
                {
                    players.Register(PromptedPlayer, Rep);
                }

                var triggers = new TriggerManager { EventSchemas = new EventSchemaRegistry() };
                triggers.RegisterEventHandler(GameEvents.ResponseChainPromptOpened, ctx => Record(GameEvents.ResponseChainPromptOpened, ctx));
                triggers.RegisterEventHandler(GameEvents.ResponseChainPromptClosed, ctx => Record(GameEvents.ResponseChainPromptClosed, ctx));
                Events = new ResponseChainPromptEventSystem(Prompt, players, triggers, _world, () => new ScriptContext());
            }

            public void PublishOpeningSpell()
            {
                _requests.Publish(new EffectRequest { Source = Caster, Target = Target, TemplateId = TplOpen });
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

            public float TargetHealth() => _world.Get<AttributeBuffer>(Target).GetCurrent(AttrHealth);

            public void Dispose() => _world.Dispose();

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
