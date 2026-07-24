using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using Ludots.Core.Input.Interaction;
using Ludots.Core.Registry;
using NUnit.Framework;

namespace Ludots.Tests.GAS
{
    /// <summary>
    /// RFC-0065 CTX-7 (§5.5, §6.1 M7, DEC-11/DEC-13): CastCommitProfile kernel — activation and
    /// frame-action op sequences over the interaction op registry (pushFrame/popFrame/submitOrder),
    /// real frame pushes onto the InteractionContextStack, payload value sources as registry items,
    /// load-time fail-fast (unknown ops, FSM-shaped schema keys), and steady-state zero allocation.
    /// Profile ids and action names ("Confirm"/"Back") are test data, never Core concepts.
    /// </summary>
    [TestFixture]
    [NonParallelizable]
    public sealed class CastCommitProfileTests
    {
        private const string QuickProfileId = "cast.commit.quick";
        private const string AimConfirmProfileId = "cast.commit.aim_confirm";
        private const string TargetingContextProfileId = "ctx.targeting.ground";
        private const string ConfirmActionName = "Confirm";
        private const string BackActionName = "Back";
        private const string SpatialPayloadKey = "spatial";

        private const string ExemplarJson = """
        {
          "profiles": [
            { "id": "cast.commit.quick", "onActivate": [ { "op": "submitOrder", "payload": { "spatial": "cursorWorld" } } ] },
            { "id": "cast.commit.aim_confirm",
              "onActivate": [ { "op": "pushFrame", "contextProfileId": "ctx.targeting.ground" } ],
              "frameActions": { "Confirm": [ { "op": "submitOrder", "payload": { "spatial": "framePointer" } }, { "op": "popFrame" } ],
                                "Back": [ { "op": "popFrame" } ] } }
          ]
        }
        """;

        [Test]
        public void ExecuteActivation_QuickProfile_SubmitsWithCursorWorldValueSource()
        {
            Harness harness = Harness.Create();
            harness.InstallExemplarProfiles();
            var ctx = harness.CreateContext();

            harness.Commit.ExecuteActivation(harness.ProfileId(QuickProfileId), in ctx);

            Assert.That(harness.Submits.CallCount, Is.EqualTo(1), "quick activation submits exactly once.");
            Assert.That(harness.Submits.LastPayloadCount, Is.EqualTo(1));
            Assert.That(harness.Submits.LastKeyId, Is.EqualTo(harness.Commit.PayloadKeyRegistry.GetId(SpatialPayloadKey)));
            Assert.That(
                harness.Submits.LastValueSourceId,
                Is.EqualTo(harness.Commit.PayloadValueSourceRegistry.GetId(CastCommitPayloadValueSources.CursorWorld)));
            Assert.That(harness.Stack.Count, Is.EqualTo(1), "quick activation never touches the frame stack.");
        }

        [Test]
        public void ExecuteActivation_AimConfirm_PushesTargetingFrameOntoTheStack()
        {
            Harness harness = Harness.Create();
            harness.InstallExemplarProfiles();
            var ctx = harness.CreateContext();

            harness.Commit.ExecuteActivation(harness.ProfileId(AimConfirmProfileId), in ctx);

            Assert.That(harness.Submits.CallCount, Is.EqualTo(0), "no order until the frame action fires.");
            Assert.That(harness.Stack.Count, Is.EqualTo(2));
            Assert.That(harness.Stack.TryPeek(out InteractionContextFrame frame), Is.True);
            Assert.That(
                frame.ActiveCollectionKeyId,
                Is.EqualTo(harness.Stack.CollectionKeyRegistry.GetId(Harness.TargetingCollectionKey)),
                "the pushed frame is the real targeting context profile.");
        }

        [Test]
        public void FrameAction_Confirm_SubmitsWithFramePointer_ThenPopsTheFrame()
        {
            Harness harness = Harness.Create();
            harness.InstallExemplarProfiles();
            var ctx = harness.CreateContext();
            int profileId = harness.ProfileId(AimConfirmProfileId);
            harness.Commit.ExecuteActivation(profileId, in ctx);

            bool handled = harness.Commit.TryExecuteFrameAction(
                profileId, harness.Commit.ActionIdRegistry.GetId(ConfirmActionName), in ctx);

            Assert.That(handled, Is.True);
            Assert.That(harness.Submits.CallCount, Is.EqualTo(1));
            Assert.That(
                harness.Submits.LastValueSourceId,
                Is.EqualTo(harness.Commit.PayloadValueSourceRegistry.GetId(CastCommitPayloadValueSources.FramePointer)));
            Assert.That(harness.Submits.LastStackCountAtSubmit, Is.EqualTo(2), "submit runs before popFrame in the declared sequence.");
            Assert.That(harness.Stack.Count, Is.EqualTo(1), "popFrame restores the stack to the default frame.");
        }

        [Test]
        public void FrameAction_Back_OnlyPops_NoSubmit()
        {
            Harness harness = Harness.Create();
            harness.InstallExemplarProfiles();
            var ctx = harness.CreateContext();
            int profileId = harness.ProfileId(AimConfirmProfileId);
            harness.Commit.ExecuteActivation(profileId, in ctx);

            bool handled = harness.Commit.TryExecuteFrameAction(
                profileId, harness.Commit.ActionIdRegistry.GetId(BackActionName), in ctx);

            Assert.That(handled, Is.True);
            Assert.That(harness.Submits.CallCount, Is.EqualTo(0), "Back never produces an order.");
            Assert.That(harness.Stack.Count, Is.EqualTo(1));
        }

        [Test]
        public void FrameAction_Undeclared_IsNotIntercepted()
        {
            Harness harness = Harness.Create();
            harness.InstallExemplarProfiles();
            var ctx = harness.CreateContext();
            int unrelatedActionId = harness.Commit.ActionIdRegistry.Register("someOtherAction");

            Assert.That(harness.Commit.TryExecuteFrameAction(harness.ProfileId(AimConfirmProfileId), unrelatedActionId, in ctx), Is.False);
            Assert.That(harness.Commit.TryExecuteFrameAction(harness.ProfileId(QuickProfileId), unrelatedActionId, in ctx), Is.False);
            Assert.That(harness.Submits.CallCount, Is.EqualTo(0));
        }

        [Test]
        public void ExemplarJson_ParsesValidatesInstallsAndExecutes()
        {
            var root = JsonNode.Parse(ExemplarJson)!.AsObject();
            CastCommitProfileConfigLoader.ValidateSchemaKeys(root, "test");
            var config = root.Deserialize<CastCommitProfilesConfig>(
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            Assert.That(config, Is.Not.Null);
            CastCommitProfileConfigLoader.Validate(config, "test");

            Harness harness = Harness.Create();
            harness.Commit.Install(config);
            var ctx = harness.CreateContext();
            harness.Commit.ExecuteActivation(harness.ProfileId(AimConfirmProfileId), in ctx);
            Assert.That(harness.Stack.Count, Is.EqualTo(2), "JSON-declared pushFrame must execute for real.");
        }

        [Test]
        public void SchemaGuard_StateTableKeys_FailFast()
        {
            // DEC-13: FSM-shaped schemas must be rejected before deserialization would drop them.
            string stateTableJson = """
            { "profiles": [ { "id": "cast.commit.bad", "states": [ { "id": "idle" } ] } ] }
            """;
            string transitionTableJson = """
            { "profiles": [ { "id": "cast.commit.bad", "onActivate": [ { "op": "popFrame" } ], "transitions": [] } ] }
            """;

            Assert.Throws<InvalidOperationException>(() =>
                CastCommitProfileConfigLoader.ValidateSchemaKeys(JsonNode.Parse(stateTableJson)!.AsObject(), "test"));
            Assert.Throws<InvalidOperationException>(() =>
                CastCommitProfileConfigLoader.ValidateSchemaKeys(JsonNode.Parse(transitionTableJson)!.AsObject(), "test"));
        }

        [Test]
        public void Install_UnknownOpKind_Throws()
        {
            Harness harness = Harness.Create();
            Assert.Throws<InvalidOperationException>(() => harness.Commit.Install(Harness.Config(new CastCommitProfileDefinition
            {
                Id = "cast.commit.bad.op",
                OnActivate = Harness.Ops(new CastCommitOpDefinition { Op = "teleportCamera" }),
            })));
        }

        [Test]
        public void Install_PushFrameWithoutContextProfile_OrDanglingContextProfile_Throws()
        {
            Harness harness = Harness.Create();
            Assert.Throws<InvalidOperationException>(() => harness.Commit.Install(Harness.Config(new CastCommitProfileDefinition
            {
                Id = "cast.commit.bad.pushframe",
                OnActivate = Harness.Ops(new CastCommitOpDefinition { Op = InteractionOpKinds.PushFrame }),
            })));

            Assert.Throws<InvalidOperationException>(() => harness.Commit.Install(Harness.Config(new CastCommitProfileDefinition
            {
                Id = "cast.commit.bad.dangling",
                OnActivate = Harness.Ops(new CastCommitOpDefinition
                {
                    Op = InteractionOpKinds.PushFrame,
                    ContextProfileId = "ctx.not.installed",
                }),
            })));
        }

        [Test]
        public void Install_UnknownPayloadValueSource_Throws()
        {
            Harness harness = Harness.Create();
            Assert.Throws<InvalidOperationException>(() => harness.Commit.Install(Harness.Config(new CastCommitProfileDefinition
            {
                Id = "cast.commit.bad.valuesource",
                OnActivate = Harness.Ops(new CastCommitOpDefinition
                {
                    Op = InteractionOpKinds.SubmitOrder,
                    Payload = new Dictionary<string, string> { [SpatialPayloadKey] = "somewhereElse" },
                }),
            })));
        }

        [Test]
        public void RegisterOp_DuplicateKind_Throws_AndCustomOpsExecute()
        {
            Harness harness = Harness.Create();
            Assert.Throws<InvalidOperationException>(() =>
                harness.Commit.RegisterOp(InteractionOpKinds.SubmitOrder, static (in InteractionOpContext _, in InteractionOpArgs _) => { }));

            int customCalls = 0;
            harness.Commit.RegisterOp("recordOnly", (in InteractionOpContext _, in InteractionOpArgs _) => customCalls++);
            harness.Commit.Install(Harness.Config(new CastCommitProfileDefinition
            {
                Id = "cast.commit.custom",
                OnActivate = Harness.Ops(new CastCommitOpDefinition { Op = "recordOnly" }),
            }));

            var ctx = harness.CreateContext();
            harness.Commit.ExecuteActivation(harness.ProfileId("cast.commit.custom"), in ctx);
            Assert.That(customCalls, Is.EqualTo(1), "mod-registered ops execute without Core changes (DEC-11).");
        }

        [Test]
        public void DefaultConfigFile_DeserializesValidatesAndInstalls()
        {
            string configPath = Path.Combine(FindRepoRoot(), "assets", "Configs", "Input", "cast_commit_profiles.json");
            Assert.That(File.Exists(configPath), Is.True, $"Missing {configPath}");

            var root = JsonNode.Parse(File.ReadAllText(configPath))!.AsObject();
            CastCommitProfileConfigLoader.ValidateSchemaKeys(root, "assets");
            var config = root.Deserialize<CastCommitProfilesConfig>(
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            Assert.That(config, Is.Not.Null);
            CastCommitProfileConfigLoader.Validate(config, "assets");

            Harness harness = Harness.Create();
            harness.Commit.Install(config);
        }

        [Test]
        public void ExecuteOpSequences_SteadyState_AreAllocationFree()
        {
            Harness harness = Harness.Create();
            harness.InstallExemplarProfiles();
            var ctx = harness.CreateContext();
            int quickId = harness.ProfileId(QuickProfileId);
            int aimId = harness.ProfileId(AimConfirmProfileId);
            int confirmActionId = harness.Commit.ActionIdRegistry.GetId(ConfirmActionName);

            // Warmup registers every lazily-registered id (stack registries) once.
            harness.Commit.ExecuteActivation(quickId, in ctx);
            harness.Commit.ExecuteActivation(aimId, in ctx);
            harness.Commit.TryExecuteFrameAction(aimId, confirmActionId, in ctx);

            long allocated = MeasureExecutionAllocations(harness, quickId, aimId, confirmActionId, in ctx);
            allocated = Math.Min(allocated, MeasureExecutionAllocations(harness, quickId, aimId, confirmActionId, in ctx));
            Assert.That(allocated, Is.EqualTo(0), "Steady-state op sequence execution must be allocation free.");
        }

        private static long MeasureExecutionAllocations(
            Harness harness,
            int quickId,
            int aimId,
            int confirmActionId,
            in InteractionOpContext ctx)
        {
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 10_000; i++)
            {
                harness.Commit.ExecuteActivation(quickId, in ctx);
                harness.Commit.ExecuteActivation(aimId, in ctx);
                harness.Commit.TryExecuteFrameAction(aimId, confirmActionId, in ctx);
            }

            return GC.GetAllocatedBytesForCurrentThread() - before;
        }

        private static string FindRepoRoot()
        {
            var current = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);
            while (current != null)
            {
                if (File.Exists(Path.Combine(current.FullName, "src", "Core", "Ludots.Core.csproj")))
                {
                    return current.FullName;
                }

                current = current.Parent;
            }

            throw new DirectoryNotFoundException("Could not locate repo root containing src/Core/Ludots.Core.csproj");
        }

        /// <summary>Records submitOrder invocations without allocating (steady-state safe).</summary>
        internal sealed class SubmitRecorder
        {
            public int CallCount;
            public int LastPayloadCount;
            public int LastKeyId;
            public int LastValueSourceId;
            public int LastStackCountAtSubmit;

            public void Submit(in InteractionOpContext ctx, in CastCommitOrderPayload payload)
            {
                CallCount++;
                LastPayloadCount = payload.Count;
                LastStackCountAtSubmit = ctx.Stack.Count;
                if (payload.Count > 0)
                {
                    LastKeyId = payload[0].KeyId;
                    LastValueSourceId = payload[0].ValueSourceId;
                }
            }
        }

        internal sealed class Harness
        {
            public const string TargetingCollectionKey = "collection.test.targeting";

            public CastCommitProfileRegistry Commit = null!;
            public InteractionContextStack Stack = null!;
            public StringIntRegistry ProfileIds = null!;
            private SubmitRecorder _submits = null!;
            private CastCommitOrderSubmit _submitDelegate = null!;

            public SubmitRecorder Submits => _submits;

            public static Harness Create()
            {
                var collectionKeys = new StringIntRegistry(capacity: 16, startId: 1, invalidId: 0, comparer: StringComparer.Ordinal);
                var stack = new InteractionContextStack(collectionKeys);
                stack.Push(InteractionContextFrameDescriptor.Create(
                    InteractionContextIds.Default,
                    "collection.test.command_source",
                    "view.test.default"));

                var contextProfiles = new InteractionContextProfileRegistry(stack.ContextIdRegistry);
                contextProfiles.Install(new InteractionContextProfilesConfig
                {
                    Profiles = new List<InteractionContextProfileDefinition>
                    {
                        new()
                        {
                            Id = TargetingContextProfileId,
                            ActiveCollectionKey = TargetingCollectionKey,
                            ActiveEntityViewKey = "view.test.targeting",
                        },
                    },
                });

                var profileIds = new StringIntRegistry(capacity: 16, startId: 1, invalidId: 0, comparer: StringComparer.Ordinal);
                var actionIds = new StringIntRegistry(capacity: 16, startId: 1, invalidId: 0, comparer: StringComparer.Ordinal);
                var recorder = new SubmitRecorder();
                return new Harness
                {
                    Commit = new CastCommitProfileRegistry(profileIds, actionIds, contextProfiles),
                    Stack = stack,
                    ProfileIds = profileIds,
                    _submits = recorder,
                    _submitDelegate = recorder.Submit,
                };
            }

            public int ProfileId(string name) => ProfileIds.GetId(name);

            public InteractionOpContext CreateContext()
            {
                return new InteractionOpContext(Stack, _submitDelegate);
            }

            /// <summary>The §5.5 exemplars, mirroring the JSON schema as typed definitions.</summary>
            public void InstallExemplarProfiles()
            {
                Commit.Install(Config(
                    new CastCommitProfileDefinition
                    {
                        Id = QuickProfileId,
                        OnActivate = Ops(new CastCommitOpDefinition
                        {
                            Op = InteractionOpKinds.SubmitOrder,
                            Payload = new Dictionary<string, string>
                            {
                                [SpatialPayloadKey] = CastCommitPayloadValueSources.CursorWorld,
                            },
                        }),
                    },
                    new CastCommitProfileDefinition
                    {
                        Id = AimConfirmProfileId,
                        OnActivate = Ops(new CastCommitOpDefinition
                        {
                            Op = InteractionOpKinds.PushFrame,
                            ContextProfileId = TargetingContextProfileId,
                        }),
                        FrameActions = new Dictionary<string, List<CastCommitOpDefinition>>
                        {
                            [ConfirmActionName] = Ops(
                                new CastCommitOpDefinition
                                {
                                    Op = InteractionOpKinds.SubmitOrder,
                                    Payload = new Dictionary<string, string>
                                    {
                                        [SpatialPayloadKey] = CastCommitPayloadValueSources.FramePointer,
                                    },
                                },
                                new CastCommitOpDefinition { Op = InteractionOpKinds.PopFrame }),
                            [BackActionName] = Ops(new CastCommitOpDefinition { Op = InteractionOpKinds.PopFrame }),
                        },
                    }));
            }

            public static CastCommitProfilesConfig Config(params CastCommitProfileDefinition[] profiles)
            {
                return new CastCommitProfilesConfig { Profiles = new List<CastCommitProfileDefinition>(profiles) };
            }

            public static List<CastCommitOpDefinition> Ops(params CastCommitOpDefinition[] ops)
            {
                return new List<CastCommitOpDefinition>(ops);
            }
        }
    }
}
