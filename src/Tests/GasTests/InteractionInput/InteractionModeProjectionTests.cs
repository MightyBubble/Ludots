using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using Arch.Core;
using Ludots.Core.Association;
using Ludots.Core.Client;
using Ludots.Core.Config;
using Ludots.Core.EntityCollections;
using Ludots.Core.Gameplay.Components;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.Gameplay.Relationships;
using Ludots.Core.GraphRuntime;
using Ludots.Core.Input.Config;
using Ludots.Core.Input.Interaction;
using Ludots.Core.Input.Runtime;
using Ludots.Core.Input.Systems;
using Ludots.Core.Modding;
using Ludots.Core.NodeLibraries.GASGraph;
using Ludots.Core.NodeLibraries.GASGraph.Host;
using Ludots.Core.Persistence;
using Ludots.Core.Registry;
using Ludots.Core.Scripting;
using Ludots.Platform.Abstractions;
using NUnit.Framework;

namespace Ludots.Tests.GAS
{
    /// <summary>
    /// #1306 routes ①②: the sparse InteractionMode component, the InteractionModeMap plaintext
    /// table (fail-fast on the reserved normal mode, undefined contexts, priority drift), the
    /// per-seat (seatId, contextId, op) projection diff onto the existing PlayerInputHandler
    /// stack, the SetInteractionMode graph op, and the world-save round trip.
    /// </summary>
    [TestFixture]
    [NonParallelizable]
    public sealed class InteractionModeProjectionTests
    {
        private const string ModeNormal = InteractionModeIds.Normal;
        private const string ModeTargeting = "mode.test.targeting";
        private const string ModeSiege = "mode.test.siege";
        private const string ContextA = "imc.test.targeting";
        private const string ContextB = "imc.test.siege_low";
        private const string SeatId = "seat.test.sole";

        private string? _tempRoot;

        [SetUp]
        public void SetUp()
        {
            ConfigKeyRegistry.Clear();
            GraphIdRegistry.Clear();
        }

        [TearDown]
        public void TearDown()
        {
            ConfigKeyRegistry.Clear();
            GraphIdRegistry.Clear();
            if (!string.IsNullOrWhiteSpace(_tempRoot) && Directory.Exists(_tempRoot))
            {
                try
                {
                    Directory.Delete(_tempRoot, recursive: true);
                }
                catch
                {
                    // Best-effort cleanup for temp config roots.
                }
            }

            _tempRoot = null;
        }

        // ── InteractionModeMap install contract ──

        [Test]
        public void ModeMap_Install_RejectsMissingReservedNormalMode()
        {
            InteractionModeMap map = NewMap();

            Assert.That(
                () => map.Install(Config(Mode(ModeTargeting, Ref(ContextA, 1))), InputConfig()),
                Throws.InvalidOperationException.With.Message.Contains(ModeNormal));
        }

        [Test]
        public void ModeMap_Install_RejectsNormalModeWithContexts()
        {
            InteractionModeMap map = NewMap();

            Assert.That(
                () => map.Install(Config(Mode(ModeNormal, Ref(ContextA, 1)), Mode(ModeTargeting, Ref(ContextA, 1))), InputConfig()),
                Throws.InvalidOperationException.With.Message.Contains(ModeNormal));
        }

        [Test]
        public void ModeMap_Install_FailsFastOnUndefinedContextAndPriorityDrift()
        {
            InteractionModeMap map = NewMap();
            Assert.That(
                () => map.Install(Config(Mode(ModeNormal), Mode(ModeTargeting, Ref("imc.test.undefined", 1))), InputConfig()),
                Throws.InvalidOperationException.With.Message.Contains("imc.test.undefined"));

            map = NewMap();
            Assert.That(
                () => map.Install(Config(Mode(ModeNormal), Mode(ModeTargeting, Ref(ContextA, 2))), InputConfig()),
                Throws.InvalidOperationException.With.Message.Contains("priority 2 drifts"));
        }

        [Test]
        public void ModeMap_Install_ResolvesContextSets()
        {
            ModeMapFixture fixture = new();

            Assert.That(fixture.Map.TryGetContexts(fixture.Map.ModeIdRegistry.GetId(ModeTargeting), out var targeting), Is.True);
            Assert.That(targeting.Count, Is.EqualTo(2));
            Assert.That(targeting[0].ContextId, Is.EqualTo(ContextA));
            Assert.That(targeting[1].ContextId, Is.EqualTo(ContextB));

            Assert.That(fixture.Map.TryGetContexts(fixture.Map.ModeIdRegistry.GetId(ModeNormal), out var normal), Is.True);
            Assert.That(normal.Count, Is.EqualTo(0));
            Assert.That(fixture.Map.IsNormalMode(fixture.Map.ModeIdRegistry.GetId(ModeNormal)), Is.True);
            Assert.That(fixture.Map.IsNormalMode(fixture.Map.ModeIdRegistry.GetId(ModeSiege)), Is.False);
        }

        // ── Projection contract ──

        [Test]
        public void Projection_RepWithoutComponent_PushesNoModeContexts()
        {
            using var world = World.Create();
            ProjectionHarness harness = ProjectionHarness.Create(world, withHandler: true);
            Entity rep = world.Create();
            harness.BindSoleSeat(rep);
            harness.System.Update(0.016f);

            Assert.That(harness.System.LastCommands, Is.Empty, "the sparse default projects no mode contexts");
            harness.Backend.Buttons["<Keyboard>/q"] = true;
            harness.Handler.Update();
            Assert.That(harness.Handler.IsDown("CmdA"), Is.False);
        }

        [Test]
        public void Projection_ModeComponent_ActivatesMappedContextsNextTick()
        {
            using var world = World.Create();
            ProjectionHarness harness = ProjectionHarness.Create(world, withHandler: true);
            Entity rep = world.Create();
            harness.BindSoleSeat(rep);

            world.Add(rep, new InteractionMode { ModeId = harness.Map.ModeIdRegistry.GetId(ModeTargeting) });
            harness.System.Update(0.016f);

            Assert.That(harness.System.LastCommands.Count, Is.EqualTo(2));
            Assert.That(harness.System.LastCommands[0], Is.EqualTo(new InputContextProjectionCommand(SeatId, ContextA, InputContextProjectionOp.Push)));
            Assert.That(harness.System.LastCommands[1], Is.EqualTo(new InputContextProjectionCommand(SeatId, ContextB, InputContextProjectionOp.Push)));

            harness.Backend.Buttons["<Keyboard>/q"] = true;
            harness.Handler.Update();
            Assert.That(harness.Handler.IsDown("CmdA"), Is.True, "targeting contexts must be active on the handler stack");

            harness.System.Update(0.016f);
            Assert.That(harness.System.LastCommands, Is.Empty, "a settled mode emits no further commands");
        }

        [Test]
        public void Projection_WritingNormalBack_PopsContextsWithoutResidue()
        {
            using var world = World.Create();
            ProjectionHarness harness = ProjectionHarness.Create(world, withHandler: true);
            Entity rep = world.Create();
            harness.BindSoleSeat(rep);

            world.Add(rep, new InteractionMode { ModeId = harness.Map.ModeIdRegistry.GetId(ModeTargeting) });
            harness.System.Update(0.016f);

            world.Remove<InteractionMode>(rep);
            harness.System.Update(0.016f);

            Assert.That(harness.System.LastCommands.Count, Is.EqualTo(2));
            Assert.That(harness.System.LastCommands[0].Op, Is.EqualTo(InputContextProjectionOp.Pop));
            Assert.That(harness.System.LastCommands[0].ContextId, Is.EqualTo(ContextB), "pops mirror pushes in reverse (LIFO stack discipline)");
            Assert.That(harness.System.LastCommands[1].ContextId, Is.EqualTo(ContextA));

            harness.Backend.Buttons["<Keyboard>/q"] = true;
            harness.Handler.Update();
            Assert.That(harness.Handler.IsDown("CmdA"), Is.False, "mode contexts must be fully popped");
        }

        [Test]
        public void Projection_UnknownModeId_FailsFastNamed()
        {
            using var world = World.Create();
            ProjectionHarness harness = ProjectionHarness.Create(world, withHandler: true);
            Entity rep = world.Create();
            harness.BindSoleSeat(rep);
            world.Add(rep, new InteractionMode { ModeId = 4242 });

            Assert.That(
                () => harness.System.Update(0.016f),
                Throws.InvalidOperationException.With.Message.Contains("4242"));
        }

        [Test]
        public void Projection_HeadlessWithoutHandler_ReemitsCommandsWithoutApplying()
        {
            using var world = World.Create();
            ProjectionHarness harness = ProjectionHarness.Create(world, withHandler: false);
            Entity rep = world.Create();
            harness.BindSoleSeat(rep);
            world.Add(rep, new InteractionMode { ModeId = harness.Map.ModeIdRegistry.GetId(ModeTargeting) });

            harness.System.Update(0.016f);
            Assert.That(harness.System.LastCommands.Count, Is.EqualTo(2));
            harness.System.Update(0.016f);
            Assert.That(harness.System.LastCommands.Count, Is.EqualTo(2), "unconsumed commands re-emit until a handler binds");
        }

        // ── Mounted active-context demand contract (#1306 route ④) ──

        [Test]
        public void Projection_FrameOwnedBySeatRep_PushesItsInputContextNextTick()
        {
            using var world = World.Create();
            ProjectionHarness harness = ProjectionHarness.Create(world, withHandler: true);
            Entity rep = world.Create(new PlayerIdentity { PlayerId = 7 });
            harness.BindSoleSeat(rep);
            Entity carrier = world.Create();
            harness.Ownership.EnsureOwnership(rep, carrier);

            harness.MountContext(rep, ContextA);
            harness.System.Update(0.016f);

            Assert.That(harness.System.LastCommands.Count, Is.EqualTo(1));
            Assert.That(
                harness.System.LastCommands[0],
                Is.EqualTo(new InputContextProjectionCommand(SeatId, ContextA, InputContextProjectionOp.Push)));

            harness.Backend.Buttons["<Keyboard>/q"] = true;
            harness.Handler.Update();
            Assert.That(harness.Handler.IsDown("CmdA"), Is.True, "the frame's input context must be active on the seat handler");

            harness.System.Update(0.016f);
            Assert.That(harness.System.LastCommands, Is.Empty, "a settled frame emits no further commands");
        }

        [Test]
        public void Projection_FrameReclaimed_PopsItsInputContext()
        {
            using var world = World.Create();
            ProjectionHarness harness = ProjectionHarness.Create(world, withHandler: true);
            Entity rep = world.Create(new PlayerIdentity { PlayerId = 7 });
            harness.BindSoleSeat(rep);
            Entity carrier = world.Create();
            harness.Ownership.EnsureOwnership(rep, carrier);

            harness.MountContext(rep, ContextA);
            harness.System.Update(0.016f);
            Assert.That(harness.System.LastCommands.Count, Is.EqualTo(1));

            world.Remove<ActiveInteractionContext>(rep);
            harness.System.Update(0.016f);

            Assert.That(harness.System.LastCommands.Count, Is.EqualTo(1));
            Assert.That(
                harness.System.LastCommands[0],
                Is.EqualTo(new InputContextProjectionCommand(SeatId, ContextA, InputContextProjectionOp.Pop)));

            harness.Backend.Buttons["<Keyboard>/q"] = true;
            harness.Handler.Update();
            Assert.That(harness.Handler.IsDown("CmdA"), Is.False, "the frame's input context must be released after reclamation");
        }

        [Test]
        public void Projection_FrameFromAnotherPlayersDomain_DoesNotTouchTheSeat()
        {
            using var world = World.Create();
            ProjectionHarness harness = ProjectionHarness.Create(world, withHandler: true);
            Entity rep = world.Create(new PlayerIdentity { PlayerId = 7 });
            harness.BindSoleSeat(rep);
            Entity otherRep = world.Create(new PlayerIdentity { PlayerId = 9 });
            Entity carrier = world.Create();
            harness.Ownership.EnsureOwnership(otherRep, carrier);

            harness.MountContext(otherRep, ContextA);
            harness.System.Update(0.016f);

            Assert.That(harness.System.LastCommands, Is.Empty, "contexts mounted on other players' subjects must not project onto this seat");
        }

        [Test]
        public void Projection_FrameAndModeDemandingSameContext_EmitSinglePush()
        {
            using var world = World.Create();
            ProjectionHarness harness = ProjectionHarness.Create(world, withHandler: true);
            Entity rep = world.Create(new PlayerIdentity { PlayerId = 7 });
            harness.BindSoleSeat(rep);
            Entity carrier = world.Create();
            harness.Ownership.EnsureOwnership(rep, carrier);
            world.Add(rep, new InteractionMode { ModeId = harness.Map.ModeIdRegistry.GetId(ModeSiege) });

            harness.MountContext(rep, ContextB);
            harness.System.Update(0.016f);

            Assert.That(harness.System.LastCommands.Count, Is.EqualTo(1), "a context demanded by both the mode and the frame pushes once");
            Assert.That(harness.System.LastCommands[0].ContextId, Is.EqualTo(ContextB));
        }

        [Test]
        public void Projection_FrameDemandingUndefinedContext_FailsFastNamed()
        {
            using var world = World.Create();
            ProjectionHarness harness = ProjectionHarness.Create(world, withHandler: true);
            Entity rep = world.Create(new PlayerIdentity { PlayerId = 7 });
            harness.BindSoleSeat(rep);
            Entity carrier = world.Create();
            harness.Ownership.EnsureOwnership(rep, carrier);

            harness.MountContext(rep, "imc.test.undefined");

            Assert.That(
                () => harness.System.Update(0.016f),
                Throws.InvalidOperationException.With.Message.Contains("imc.test.undefined"));
        }

        // ── SetInteractionMode graph op contract ──

        [Test]
        public void GraphApi_SetInteractionMode_WritesAndClearsSparseComponent()
        {
            using var world = World.Create();
            ModeMapFixture fixture = new();
            var api = new GasGraphRuntimeApi(world);
            api.BindInteractionModeMap(fixture.Map);
            Entity entity = world.Create();

            api.SetInteractionMode(entity, ConfigKeyRegistry.Register(ModeTargeting));
            Assert.That(world.TryGet<InteractionMode>(entity, out var mode), Is.True);
            Assert.That(mode.ModeId, Is.EqualTo(fixture.Map.ModeIdRegistry.GetId(ModeTargeting)));

            api.SetInteractionMode(entity, ConfigKeyRegistry.Register(ModeSiege));
            Assert.That(world.Get<InteractionMode>(entity).ModeId, Is.EqualTo(fixture.Map.ModeIdRegistry.GetId(ModeSiege)));

            api.SetInteractionMode(entity, ConfigKeyRegistry.Register(ModeNormal));
            Assert.That(world.Has<InteractionMode>(entity), Is.False, "the reserved normal mode is the sparse default: no component");
        }

        [Test]
        public void GraphApi_SetInteractionMode_FailsFastOnDeadTargetUnknownModeAndUnboundMap()
        {
            using var world = World.Create();
            ModeMapFixture fixture = new();
            var api = new GasGraphRuntimeApi(world);
            Entity entity = world.Create();

            Assert.That(
                () => api.SetInteractionMode(entity, ConfigKeyRegistry.Register(ModeTargeting)),
                Throws.InvalidOperationException.With.Message.Contains("SetInteractionModeMapUnavailable"));

            api.BindInteractionModeMap(fixture.Map);
            Assert.That(
                () => api.SetInteractionMode(Entity.Null, ConfigKeyRegistry.Register(ModeTargeting)),
                Throws.InvalidOperationException.With.Message.Contains("SetInteractionModeTargetDead"));
            Assert.That(
                () => api.SetInteractionMode(entity, ConfigKeyRegistry.Register("mode.test.unknown")),
                Throws.InvalidOperationException.With.Message.Contains("mode.test.unknown"));
        }

        [Test]
        public void GraphsJson_SetInteractionMode_CompilesExecutesAndWritesTheComponent()
        {
            using var world = World.Create();
            ModeMapFixture fixture = new();
            var api = new GasGraphRuntimeApi(world);
            api.BindInteractionModeMap(fixture.Map);
            Entity caster = world.Create();
            Entity target = world.Create();

            GraphProgramRegistry programs = LoadPrograms(SetInteractionModeGraphJson);
            int graphId = GraphIdRegistry.GetId("tests.graph.set-interaction-mode");
            Assert.That(graphId, Is.GreaterThan(0));
            Assert.That(programs.TryGetProgram(graphId, out ReadOnlySpan<GraphInstruction> program), Is.True);
            GraphExecutor.Execute(world, caster, target, default, program, api);

            Assert.That(world.Has<InteractionMode>(caster), Is.False, "writing mode.normal on the caster clears the sparse component");
            Assert.That(world.TryGet<InteractionMode>(target, out var targetMode), Is.True);
            Assert.That(targetMode.ModeId, Is.EqualTo(fixture.Map.ModeIdRegistry.GetId(ModeTargeting)));
        }

        // ── Save round trip ──

        [Test]
        public void WorldSave_RoundTripsInteractionMode_AndProjectionResultMatchesAfterRestore()
        {
            byte[] payload;
            var serializer = new LudotsBinaryWorldSerializer();
            using (var world = World.Create())
            {
                world.Create(new InteractionMode { ModeId = 4242 });
                payload = serializer.Serialize(world);
            }

            using var restored = serializer.Deserialize(payload);
            Entity restoredEntity = Entity.Null;
            restored.Query(in QueryDescription.Null, entity =>
            {
                if (restored.Has<InteractionMode>(entity))
                {
                    restoredEntity = entity;
                }
            });
            Assert.That(restoredEntity, Is.Not.EqualTo(Entity.Null));
            Assert.That(restored.Get<InteractionMode>(restoredEntity).ModeId, Is.EqualTo(4242), "the raw registry id must round-trip untouched");

            ModeMapFixture fixture = new();
            restored.Set(restoredEntity, new InteractionMode { ModeId = fixture.Map.ModeIdRegistry.GetId(ModeTargeting) });
            var globals = new Dictionary<string, object>();
            ClientLocalSeatBindings.BindSoleSeat(globals, restoredEntity, playerId: 7, SeatId);
            var system = new InputContextProjectionSystem(
                restored,
                globals,
                fixture.Map,
                NewContextProfiles(),
                seatId => null);
            system.Update(0.016f);
            Assert.That(system.LastCommands.Count, Is.EqualTo(2));
        }

        private static ControlDomainQuery NewControlDomainQuery(World world, out OwnershipResolver ownership)
        {
            var types = new RelationshipTypeRegistry();
            var relationships = new RelationshipRuntime(
                world,
                types,
                new RelationshipMetricRegistry(),
                new RelationshipFlagRegistry(),
                new RelationshipBandRegistry(),
                new RelationshipChangeBuffer(capacity: 4),
                new RelationshipReverseIndex(world));
            int ownsTypeId = types.Register("Owns");
            int controlsTypeId = types.Register("Controls");
            ownership = new OwnershipResolver(relationships, ownsTypeId);
            return new ControlDomainQuery(world, relationships, ownership, ownsTypeId, controlsTypeId);
        }

        // ── Harness ──

        private static InteractionModeMap NewMap()
        {
            return new InteractionModeMap(new StringIntRegistry(capacity: 8, startId: 1, invalidId: 0, comparer: StringComparer.Ordinal));
        }

        private sealed class ModeMapFixture
        {
            public ModeMapFixture()
            {
                Map = new InteractionModeMap(new StringIntRegistry(capacity: 8, startId: 1, invalidId: 0, comparer: StringComparer.Ordinal));
                Map.Install(
                    Config(Mode(ModeNormal), Mode(ModeTargeting, Ref(ContextA, 1), Ref(ContextB, 0)), Mode(ModeSiege, Ref(ContextB, 0))),
                    InputConfig());
            }

            public InteractionModeMap Map { get; }
        }

        private static InteractionContextProfileRegistry NewContextProfiles()
        {
            return new InteractionContextProfileRegistry(
                new StringIntRegistry(capacity: 8, startId: 1, invalidId: 0, comparer: StringComparer.Ordinal));
        }

        private sealed class ProjectionHarness
        {
            public PlayerInputHandler Handler = null!;
            public TestInputBackend Backend = null!;
            public InteractionModeMap Map = null!;
            public InputContextProjectionSystem System = null!;
            public InteractionContextProfileRegistry ContextProfiles = null!;
            public OwnershipResolver Ownership = null!;
            private World _world = null!;
            private readonly Dictionary<string, object> _globals = new();

            public static ProjectionHarness Create(World world, bool withHandler)
            {
                var harness = new ProjectionHarness { Map = new ModeMapFixture().Map };
                var domains = NewControlDomainQuery(world, out OwnershipResolver ownership);
                harness.Ownership = ownership;
                harness.ContextProfiles = NewContextProfiles();
                harness._world = world;
                if (withHandler)
                {
                    harness.Backend = new TestInputBackend();
                    harness.Handler = new PlayerInputHandler(harness.Backend, InputConfig());
                }

                harness.System = new InputContextProjectionSystem(
                    world,
                    harness._globals,
                    harness.Map,
                    harness.ContextProfiles,
                    seatId => harness.Handler);
                return harness;
            }

            public void MountContext(Entity subject, string inputContextId)
            {
                _world.Add(subject, new ActiveInteractionContext
                {
                    InputContextId = ContextProfiles.InputContextIdRegistry.Register(inputContextId),
                    Source = ActiveInteractionContextSource.ExecLifecycle,
                });
            }

            public void BindSoleSeat(Entity rep)
            {
                ClientLocalSeatBindings.BindSoleSeat(_globals, rep, playerId: 7, SeatId);
            }
        }

        private static InteractionModesConfig Config(params InteractionModeDefinition[] modes)
        {
            return new InteractionModesConfig { Modes = new List<InteractionModeDefinition>(modes) };
        }

        private static InteractionModeDefinition Mode(string id, params InteractionModeContextRef[] contexts)
        {
            return new InteractionModeDefinition { Id = id, Contexts = new List<InteractionModeContextRef>(contexts) };
        }

        private static InteractionModeContextRef Ref(string contextId, int priority)
        {
            return new InteractionModeContextRef { ContextId = contextId, Priority = priority };
        }

        private static InputConfigRoot InputConfig()
        {
            return new InputConfigRoot
            {
                Actions = new List<InputActionDef>
                {
                    new() { Id = "CmdA", Type = InputActionType.Button },
                    new() { Id = "CmdB", Type = InputActionType.Button },
                },
                Contexts = new List<InputContextDef>
                {
                    new()
                    {
                        Id = ContextA,
                        Priority = 1,
                        Bindings = new List<InputBindingDef> { new() { ActionId = "CmdA", Path = "<Keyboard>/q", Processors = new() } },
                    },
                    new()
                    {
                        Id = ContextB,
                        Priority = 0,
                        Bindings = new List<InputBindingDef> { new() { ActionId = "CmdB", Path = "<Keyboard>/e", Processors = new() } },
                    },
                },
            };
        }

        private GraphProgramRegistry LoadPrograms(string graphJson)
        {
            _tempRoot = Path.Combine(Path.GetTempPath(), "Ludots_InteractionModeProjectionTests", Guid.NewGuid().ToString("N"));
            string graphDir = Path.Combine(_tempRoot, "Core", "GAS");
            Directory.CreateDirectory(graphDir);
            File.WriteAllText(Path.Combine(graphDir, "graphs.json"), graphJson);

            var vfs = new VirtualFileSystem();
            vfs.Mount("Core", Path.Combine(_tempRoot, "Core"));
            var modLoader = new ModLoader(vfs, new FunctionRegistry(), new TriggerManager());
            var pipeline = new ConfigPipeline(vfs, modLoader);
            var catalog = new ConfigCatalog();
            catalog.Add(new ConfigCatalogEntry("GAS/graphs.json", ConfigMergePolicy.ArrayById, "id"));

            var programs = new GraphProgramRegistry();
            var loader = new GraphProgramConfigLoader(pipeline, programs, new TestGraphSymbolResolver());
            var packages = loader.LoadIdsAndCompile(catalog, relativePath: "GAS/graphs.json");
            loader.PatchAndRegister(packages);
            return programs;
        }

        private const string SetInteractionModeGraphJson = """
[
  {
    "id": "tests.graph.set-interaction-mode",
    "kind": "Effect",
    "entry": "clear",
    "nodes": [
      { "id": "clear", "op": "SetInteractionMode", "mode": "mode.normal" },
      { "id": "aim", "op": "SetInteractionMode", "mode": "mode.test.targeting" },
      { "id": "target", "op": "LoadExplicitTarget" }
    ],
    "controlEdges": [
      { "from": "clear", "fromPort": "next", "to": "aim" },
      { "from": "aim", "fromPort": "next", "to": "target" }
    ],
    "valueEdges": [
      { "from": "target", "fromPort": "value", "to": "aim", "toPort": "source" }
    ]
  }
]
""";

        private sealed class TestGraphSymbolResolver : IGraphSymbolResolver
        {
            public int ResolveTag(string name) => TagRegistry.Register(name);
            public int ResolveAttribute(string name) => AttributeRegistry.Register(name);
            public int ResolveEffectTemplate(string name) => EffectTemplateIdRegistry.Register(name);
            public int ResolveRelationshipType(string name) => 1;
            public int ResolveRelationshipMetric(string name) => 1;
            public int ResolveRelationshipFlag(string name) => 1;
            public int ResolveRelationshipReason(string name) => 1;
            public int ResolveTargetDispatchPreset(string name) => 1;
            public int ResolveEntityTemplate(string name) => 1;
        }

        private sealed class TestInputBackend : IInputBackend
        {
            public Dictionary<string, bool> Buttons { get; } = new(StringComparer.Ordinal);

            public float GetAxis(string devicePath) => 0f;
            public bool GetButton(string devicePath) => Buttons.TryGetValue(devicePath, out bool down) && down;
            public Vector2 GetMousePosition() => Vector2.Zero;
            public float GetMouseWheel() => 0f;
            public void EnableIME(bool enable) { }
            public void SetIMECandidatePosition(int x, int y) { }
            public string GetCharBuffer() => string.Empty;
        }
    }
}
