using System;
using System.Collections.Generic;
using System.Linq;
using Arch.Core;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.GraphRuntime;
using Ludots.Core.Input.Interaction;
using Ludots.Core.NodeLibraries.GASGraph;
using Ludots.Core.NodeLibraries.GASGraph.Host;
using Ludots.Core.Presentation.Events;
using Ludots.Core.Presentation.Presenters;
using Ludots.Core.Registry;
using Ludots.Platform.Abstractions;
using NUnit.Framework;

namespace Ludots.Tests.GAS
{
    /// <summary>
    /// Derived interaction contexts (constitution §8.2/§8.3) — the entity-mounted
    /// coexisting set, the activation/deactivation kernel (parent validation, transitive
    /// descendant removal, ContextActivated/Deactivated presentation events as plain
    /// lifecycle notifications), and the ActivateContext/DeactivateContext op dispatch.
    /// Presentation appearance driven by context is presenter-side binding/rules only —
    /// interaction-context lifecycle never touches the presentation domain.
    /// </summary>
    [TestFixture]
    [NonParallelizable]
    public sealed class InteractionContextInstanceTests
    {
        private const string BattleProfile = "interaction.context.test.battle";
        private const string AimProfile = "interaction.context.test.aim";
        private const string BoxingProfile = "interaction.context.test.boxing";

        [SetUp]
        public void SetUp()
        {
            ConfigKeyRegistry.Clear();
        }

        [TearDown]
        public void TearDown()
        {
            ConfigKeyRegistry.Clear();
        }

        [Test]
        public void ContextSet_CoexistsInstancesAndFailsOnCapacity()
        {
            var set = new InteractionContextInstances();
            set.Add(new InteractionContextInstance { ContextId = 1, ParentContextId = 0 });
            set.Add(new InteractionContextInstance { ContextId = 2, ParentContextId = 1 });

            Assert.That(set.Count, Is.EqualTo(2));
            Assert.That(set.IndexOf(2), Is.EqualTo(1));
            Assert.That(set[1].ParentContextId, Is.EqualTo(1));

            set.RemoveAt(0);
            Assert.That(set.Count, Is.EqualTo(1));
            Assert.That(set[0].ContextId, Is.EqualTo(2), "removal compacts the set in order");

            set.Add(new InteractionContextInstance { ContextId = 3 });
            set.Add(new InteractionContextInstance { ContextId = 4 });
            set.Add(new InteractionContextInstance { ContextId = 5 });
            Assert.That(
                () => set.Add(new InteractionContextInstance { ContextId = 6 }),
                Throws.InvalidOperationException.With.Message.Contains("capacity"));
        }

        [Test]
        public void Activate_MountsInstancePublishesEventAndCarriesParentScope()
        {
            using World world = NewWorld(out var runtime, out var profiles, out var events, out var commands);
            Entity subject = world.Create();

            runtime.Activate(subject, Key(AimProfile), 0);

            Assert.That(world.TryGet<InteractionContextInstances>(subject, out var derived), Is.True);
            Assert.That(derived.Count, Is.EqualTo(1));
            Assert.That(derived[0].ContextId, Is.EqualTo(profiles.ProfileIdRegistry.GetId(AimProfile)));
            Assert.That(derived[0].ParentContextId, Is.EqualTo(0));
            Assert.That(derived[0].Source, Is.EqualTo(InteractionContextInstanceSource.ContextInstanceOp));

            Assert.That(events.GetSpan().Length, Is.EqualTo(1));
            PresentationEvent evt = events.GetSpan()[0];
            Assert.That(evt.Kind, Is.EqualTo(PresentationEventKind.ContextActivated));
            Assert.That(evt.KeyId, Is.EqualTo(profiles.ProfileIdRegistry.GetId(AimProfile)));
            Assert.That(evt.Source, Is.EqualTo(subject));
            Assert.That(evt.PayloadB, Is.EqualTo(0), "parent profile id rides PayloadB");
        }

        [Test]
        public void Activate_ChildUnderDerivedParent_CoexistsWithSibling()
        {
            using World world = NewWorld(out var runtime, out var profiles, out _, out _);
            Entity subject = world.Create();
            runtime.Activate(subject, Key(AimProfile), 0);
            runtime.Activate(subject, Key(BoxingProfile), Key(AimProfile));

            InteractionContextInstances derived = world.Get<InteractionContextInstances>(subject);
            Assert.That(derived.Count, Is.EqualTo(2), "aim and boxing coexist (瞄准中可移动)");
            Assert.That(derived[1].ParentContextId, Is.EqualTo(profiles.ProfileIdRegistry.GetId(AimProfile)));
        }

        [Test]
        public void Activate_AlreadyActiveOrInactiveParent_FailsFastNamed()
        {
            using World world = NewWorld(out var runtime, out _, out _, out _);
            Entity subject = world.Create();
            runtime.Activate(subject, Key(AimProfile), 0);

            Assert.That(
                () => runtime.Activate(subject, Key(AimProfile), 0),
                Throws.InvalidOperationException.With.Message.Contains("AlreadyActive"));

            Assert.That(
                () => runtime.Activate(subject, Key(BoxingProfile), Key(BattleProfile)),
                Throws.InvalidOperationException.With.Message.Contains("ParentInactive"));
        }

        [Test]
        public void Activate_BaseMountedProfileCountsAsActive()
        {
            using World world = NewWorld(out var runtime, out var profiles, out _, out _);
            Entity subject = world.Create();
            int battleId = profiles.ProfileIdRegistry.GetId(BattleProfile);
            world.Add(subject, new InteractionContextInstance { ContextId = battleId, Source = InteractionContextInstanceSource.TemplateSpawn });

            runtime.Activate(subject, Key(BoxingProfile), Key(BattleProfile));

            Assert.That(
                () => runtime.Activate(subject, Key(BattleProfile), 0),
                Throws.InvalidOperationException.With.Message.Contains("AlreadyActive"),
                "the base-mounted profile is an active context for idempotency checks");
        }

        [Test]
        public void Deactivate_RemovesInstancesAndPublishesEvent_NoPresenterCoupling()
        {
            using World world = NewWorld(out var runtime, out var profiles, out var events, out var commands);
            Entity subject = world.Create();
            runtime.Activate(subject, Key(AimProfile), 0);
            events.Clear();

            runtime.Deactivate(subject, Key(AimProfile));

            Assert.That(world.Has<InteractionContextInstances>(subject), Is.True);
            Assert.That(world.Get<InteractionContextInstances>(subject).Count, Is.EqualTo(0));

            // No presentation-domain command: context lifecycle is pure state + event.
            Assert.That(commands.Count, Is.EqualTo(0));

            Assert.That(events.GetSpan().Length, Is.EqualTo(1));
            Assert.That(events.GetSpan()[0].Kind, Is.EqualTo(PresentationEventKind.ContextDeactivated));
            Assert.That(events.GetSpan()[0].PayloadB, Is.EqualTo(0), "parent rides PayloadB");
        }

        [Test]
        public void Deactivate_ParentRemovesDescendantsTransitively()
        {
            using World world = NewWorld(out var runtime, out var profiles, out _, out var commands);
            Entity subject = world.Create();
            runtime.Activate(subject, Key(AimProfile), 0);
            runtime.Activate(subject, Key(BoxingProfile), Key(AimProfile));

            runtime.Deactivate(subject, Key(AimProfile));

            InteractionContextInstances derived = world.Get<InteractionContextInstances>(subject);
            Assert.That(derived.Count, Is.EqualTo(0), "父停用自动清子");
            Assert.That(commands.Count, Is.EqualTo(0), "pure state/event lifecycle — no presenter commands");
        }

        [Test]
        public void Deactivate_NotMountedOrBaseMounted_FailsFastNamed()
        {
            using World world = NewWorld(out var runtime, out var profiles, out _, out _);
            Entity subject = world.Create();

            Assert.That(
                () => runtime.Deactivate(subject, Key(AimProfile)),
                Throws.InvalidOperationException.With.Message.Contains("NotActive"));

            int battleId = profiles.ProfileIdRegistry.GetId(BattleProfile);
            world.Add(subject, new InteractionContextInstance { ContextId = battleId, Source = InteractionContextInstanceSource.ExecLifecycle });
            Assert.That(
                () => runtime.Deactivate(subject, Key(BattleProfile)),
                Throws.InvalidOperationException.With.Message.Contains("NotActive"),
                "base mounts belong to their own lifecycles and are not op-poppable");
        }


        [Test]
        public void ActivateContext_OmittedParentFollowsTheRunningMount()
        {
            using World world = NewWorld(out var runtime, out var profiles, out _, out _);
            var api = new GasGraphRuntimeApi(world);
            api.BindContextInstances(runtime);
            Entity subject = world.Create();
            int battleId = profiles.ProfileIdRegistry.GetId(BattleProfile);
            world.Add(subject, new InteractionContextInstance { ContextId = battleId });

            Assert.That(
                () => api.ActivateContext(subject, Key(AimProfile), ContextOpEncoding.OwnMountParent),
                Throws.InvalidOperationException.With.Message.Contains("ActivateContextOutsideMount"));

            api.PushContextMount(subject, battleId);
            api.ActivateContext(subject, Key(AimProfile), ContextOpEncoding.OwnMountParent);

            InteractionContextInstances derived = world.Get<InteractionContextInstances>(subject);
            Assert.That(derived.Count, Is.EqualTo(1));
            Assert.That(derived[0].ParentContextId, Is.EqualTo(battleId));
        }

        [Test]
        public void ActivateContext_ExplicitZeroParentStaysUnconstrained()
        {
            using World world = NewWorld(out var runtime, out _, out _, out _);
            var api = new GasGraphRuntimeApi(world);
            api.BindContextInstances(runtime);
            Entity subject = world.Create();

            api.ActivateContext(subject, Key(AimProfile), 0);

            Assert.That(world.Get<InteractionContextInstances>(subject)[0].ParentContextId, Is.EqualTo(0));
        }

        [Test]
        public void Publish_ForwardsLifecycleToTheMapDispatcher()
        {
            using World world = NewWorld(out var runtime, out var profiles, out _, out _);
            Entity subject = world.Create();
            var seen = new List<(PresentationEventKind Kind, int ProfileId)>();
            runtime.ContextLifecycleDispatcher = (kind, entity, profileId) =>
            {
                Assert.That(entity, Is.EqualTo(subject));
                seen.Add((kind, profileId));
            };

            runtime.Activate(subject, Key(AimProfile), 0);
            runtime.Deactivate(subject, Key(AimProfile));

            int aimId = profiles.ProfileIdRegistry.GetId(AimProfile);
            Assert.That(seen, Is.EqualTo(new[]
            {
                (PresentationEventKind.ContextActivated, aimId),
                (PresentationEventKind.ContextDeactivated, aimId),
            }));
        }

        [Test]
        public void GraphApi_ActivateAndDeactivateContext_DispatchToKernel()
        {
            using World world = NewWorld(out var runtime, out var profiles, out _, out _);
            var api = new GasGraphRuntimeApi(world);
            Entity subject = world.Create();

            Assert.That(
                () => api.ActivateContext(subject, Key(AimProfile), 0),
                Throws.InvalidOperationException.With.Message.Contains("ContextInstanceRuntimeUnavailable"));

            api.BindContextInstances(runtime);
            api.ActivateContext(subject, Key(AimProfile), 0);
            api.DeactivateContext(subject, Key(AimProfile));

            Assert.That(world.Has<InteractionContextInstances>(subject), Is.True);
            Assert.That(world.Get<InteractionContextInstances>(subject).Count, Is.EqualTo(0));
        }

        [Test]
        public void GraphOpHandlers_ExecuteActivateAndDeactivateOpcodes()
        {
            using World world = NewWorld(out var runtime, out var profiles, out _, out _);
            var api = new GasGraphRuntimeApi(world);
            api.BindContextInstances(runtime);
            Entity subject = world.Create();

            int packedActivate = ContextOpEncodingForTest.Pack(Key(AimProfile), 0);
            GraphInstruction[] activateProgram =
            {
                new GraphInstruction { Op = (ushort)GraphNodeOp.ActivateContext, A = byte.MaxValue, Imm = packedActivate },
                new GraphInstruction { Op = (ushort)GraphNodeOp.HaltReturnInt, A = 0 },
            };
            GraphExecutor.Execute(world, subject, subject, default, activateProgram, api);

            Assert.That(world.TryGet<InteractionContextInstances>(subject, out var derived), Is.True);
            Assert.That(derived[0].ContextId, Is.EqualTo(profiles.ProfileIdRegistry.GetId(AimProfile)));

            GraphInstruction[] deactivateProgram =
            {
                new GraphInstruction { Op = (ushort)GraphNodeOp.DeactivateContext, A = byte.MaxValue, Imm = ContextOpEncodingForTest.Pack(Key(AimProfile), 0) },
                new GraphInstruction { Op = (ushort)GraphNodeOp.HaltReturnInt, A = 0 },
            };
            GraphExecutor.Execute(world, subject, subject, default, deactivateProgram, api);
            Assert.That(world.Get<InteractionContextInstances>(subject).Count, Is.EqualTo(0));
        }

        [Test]
        public void IsContextActive_FailsClosedWhenDeadOrUnknownAndReadsTheMountedSet()
        {
            using World world = NewWorld(out var runtime, out var profiles, out _, out _);
            Entity subject = world.Create();
            int aimKey = Key(AimProfile);

            Assert.That(runtime.IsContextActive(subject, aimKey), Is.False);

            runtime.Activate(subject, aimKey, 0);
            Assert.That(runtime.IsContextActive(subject, aimKey), Is.True);

            int battleId = profiles.ProfileIdRegistry.GetId(BattleProfile);
            world.Add(subject, new InteractionContextInstance { ContextId = battleId });
            Assert.That(runtime.IsContextActive(subject, Key(BattleProfile)), Is.True, "a base mount counts as active");

            Entity dead = world.Create();
            world.Destroy(dead);
            Assert.That(
                () => runtime.IsContextActive(dead, aimKey),
                Throws.InvalidOperationException.With.Message.Contains("SubjectDead"));
            Assert.That(
                () => runtime.IsContextActive(subject, Key("interaction.context.test.missing")),
                Throws.InvalidOperationException.With.Message.Contains("Unknown"));

            var api = new GasGraphRuntimeApi(world);
            api.BindContextInstances(runtime);
            GraphInstruction[] program =
            {
                new GraphInstruction { Op = (ushort)GraphNodeOp.IsContextActive, A = byte.MaxValue, Imm = aimKey, Dst = 0 },
                new GraphInstruction { Op = (ushort)GraphNodeOp.HaltReturnInt, A = 0 },
            };
            var frame = GraphFrame.Bind(
                GraphKind.Effect,
                GraphEntityPreset.None,
                world,
                subject,
                subject,
                default,
                api,
                programs: null,
                new float[GraphVmLimits.MaxFloatRegisters],
                new int[GraphVmLimits.MaxIntRegisters],
                new byte[GraphVmLimits.MaxBoolRegisters],
                new Entity[GraphVmLimits.MaxEntityRegisters],
                new Entity[GraphVmLimits.MaxTargets],
                new int[GraphVmLimits.MaxIntIds],
                new int[GraphVmLimits.MaxCallStackDepth]);
            GraphExecutor.Execute(ref frame, program, programAlreadyValidated: false);
            Assert.That(frame.B[0], Is.EqualTo(1));
        }

        [Test]
        public void Compiler_EmitsIsContextActiveWithOptionalSource()
        {
            foreach (string kind in new[] { "Effect", "Query" })
            {
                var doc = new GraphControlFlowDocument
                {
                    Id = "ctx.active." + kind,
                    Kind = kind,
                    Entry = "check",
                    Nodes =
                    {
                        new GraphControlFlowNode { Id = "check", Op = "IsContextActive", Context = AimProfile },
                    },
                };
                GraphControlFlowCompileResult result = GraphControlFlowCompiler.Compile(doc);
                Assert.That(result.Diagnostics, Is.Empty, string.Join("; ", result.Diagnostics));
                GraphInstruction check = result.Package!.Value.Program.First(instruction => instruction.Op == (ushort)GraphNodeOp.IsContextActive);
                Assert.That(check.A, Is.EqualTo(byte.MaxValue), "an omitted source reads the caster");
                Assert.That(result.Package.Value.Symbols[check.Imm], Is.EqualTo(AimProfile));
            }
        }

        [Test]
        public void Compiler_EmitsActivateContextWithParentSymbolAndOptionalSource()
        {
            var doc = new GraphControlFlowDocument
            {
                Id = "ctx.activate.probe",
                Kind = "Effect",
                Entry = "act",
                Nodes =
                {
                    new GraphControlFlowNode { Id = "act", Op = "ActivateContext", Context = AimProfile, ParentContext = BattleProfile },
                },
            };

            GraphControlFlowCompileResult result = GraphControlFlowCompiler.Compile(doc);
            Assert.That(result.Diagnostics, Is.Empty, string.Join("; ", result.Diagnostics));
            GraphProgramPackage package = result.Package!.Value;
            int activatePc = Array.FindIndex(package.Program, instruction => instruction.Op == (ushort)GraphNodeOp.ActivateContext);
            Assert.That(activatePc, Is.GreaterThanOrEqualTo(0));
            Assert.That(package.Program[activatePc].Dst, Is.Not.EqualTo(byte.MaxValue), "parent context encodes as the byte symbol");

            doc = new GraphControlFlowDocument
            {
                Id = "ctx.deactivate.probe",
                Kind = "Effect",
                Entry = "stop",
                Nodes =
                {
                    new GraphControlFlowNode { Id = "stop", Op = "DeactivateContext", Context = AimProfile },
                },
            };

            result = GraphControlFlowCompiler.Compile(doc);
            Assert.That(result.Diagnostics, Is.Empty, string.Join("; ", result.Diagnostics));
            Assert.That(
                Array.Exists(result.Package!.Value.Program, instruction => instruction.Op == (ushort)GraphNodeOp.DeactivateContext),
                Is.True);
        }

        private static int Key(string profileId)
        {
            return ConfigKeyRegistry.Register(profileId);
        }

        private static World NewWorld(
            out InteractionContextInstanceRuntime runtime,
            out InteractionContextProfileRegistry profiles,
            out PresentationEventStream events,
            out PresenterCommandBuffer commands)
        {
            World world = World.Create();
            var profileIds = new StringIntRegistry(capacity: 8, startId: 1, invalidId: 0, comparer: StringComparer.Ordinal);
            profiles = new InteractionContextProfileRegistry(profileIds);
            profiles.Install(
                new InteractionContextProfilesConfig
                {
                    Profiles = new List<InteractionContextProfileDefinition>
                    {
                        NewProfile(BattleProfile),
                        NewProfile(AimProfile),
                        NewProfile(BoxingProfile),
                    },
                },
                NewRegistry(),
                NewRegistry(),
                NewRegistry());
            events = new PresentationEventStream(capacity: 16);
            commands = new PresenterCommandBuffer(capacity: 16);
            runtime = new InteractionContextInstanceRuntime(world, profiles, events);
            return world;
        }

        private static InteractionContextProfileDefinition NewProfile(string id)
            => new()
            {
                Id = id,
            };

        private static StringIntRegistry NewRegistry()
            => new(capacity: 8, startId: 1, invalidId: 0, comparer: StringComparer.Ordinal);

        /// <summary>Test-visible alias of the internal op encoding.</summary>
        private static class ContextOpEncodingForTest
        {
            public static int Pack(int contextKeyId, int parentContextKeyId)
            {
                return contextKeyId | (parentContextKeyId << 16);
            }
        }
    }
}
