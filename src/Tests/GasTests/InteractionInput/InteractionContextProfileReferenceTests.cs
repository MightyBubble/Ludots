using System;
using System.Collections.Generic;
using Ludots.Core.EntityCollections;
using Ludots.Core.Gameplay.MapTriggers;
using Ludots.Core.GraphRuntime;
using Ludots.Core.Input.Interaction;
using Ludots.Core.NodeLibraries.GASGraph;
using Ludots.Core.NodeLibraries.GASGraph.Host;
using Ludots.Core.Registry;
using Ludots.Core.Scripting;
using NUnit.Framework;

namespace Ludots.Tests.GAS
{
    /// <summary>
    /// #1398 S2b: the profile <c>bindings[]</c> / <c>triggers[]</c> load chain — structural
    /// validation in the config loader and reference resolution at registry install
    /// (unknown semantic action ids, trigger graph ids, and entry event names fail fast).
    /// </summary>
    [TestFixture]
    [NonParallelizable]
    public sealed class InteractionContextProfileReferenceTests
    {
        private const string ProfileId = "interaction.context.test.battle";
        private const string GraphName = "Graph.TriggerGraph.ContextProbe";
        private const string OtherGraphName = "Graph.TriggerGraph.ContextOther";

        [SetUp]
        public void SetUp()
        {
            GraphIdRegistry.Clear();
        }

        [TearDown]
        public void TearDown()
        {
            GraphIdRegistry.Clear();
        }

        [Test]
        public void Loader_RejectsDuplicateAndBlankBindings()
        {
            var config = Config(Profile(Bindings: new List<string> { "BoxSelect", "BoxSelect" }));

            Assert.That(
                () => InteractionContextProfileConfigLoader.Validate(config, "test"),
                Throws.InvalidOperationException.With.Message.Contains("duplicates semantic action"));

            config = Config(Profile(Bindings: new List<string> { " BoxSelect" }));
            Assert.That(
                () => InteractionContextProfileConfigLoader.Validate(config, "test"),
                Throws.InvalidOperationException.With.Message.Contains("bindings[0]"));
        }

        [Test]
        public void Loader_RejectsBlankTriggerMountFields()
        {
            var config = Config(Profile(Triggers: new List<InteractionContextTriggerMount>
            {
                new() { Trigger = " " },
            }));

            Assert.That(
                () => InteractionContextProfileConfigLoader.Validate(config, "test"),
                Throws.InvalidOperationException.With.Message.Contains("triggers[0].trigger"));
        }

        [Test]
        public void Install_UnknownSemanticAction_FailsFast()
        {
            InteractionContextProfileRegistry registry = NewRegistry(out var collectionKeys, out var filters, out var intents);
            var config = Config(Profile(Bindings: new List<string> { "NoSuchAction" }));

            Assert.That(
                () => registry.Install(config, collectionKeys, filters, intents, Catalog(inputActionIds: new[] { "BoxSelect" })),
                Throws.InvalidOperationException.With.Message.Contains("NoSuchAction"));
        }

        [Test]
        public void Install_BindingsWithoutCatalog_FailsFast()
        {
            InteractionContextProfileRegistry registry = NewRegistry(out var collectionKeys, out var filters, out var intents);
            var config = Config(Profile(Bindings: new List<string> { "BoxSelect" }));

            Assert.That(
                () => registry.Install(config, collectionKeys, filters, intents),
                Throws.InvalidOperationException.With.Message.Contains("no reference catalog"));
        }

        [Test]
        public void Install_UnknownTriggerGraph_FailsFast()
        {
            InteractionContextProfileRegistry registry = NewRegistry(out var collectionKeys, out var filters, out var intents);
            var config = Config(Profile(Triggers: new List<InteractionContextTriggerMount>
            {
                new() { Trigger = "Graph.TriggerGraph.Ghost" },
            }));

            Assert.That(
                () => registry.Install(config, collectionKeys, filters, intents, Catalog()),
                Throws.InvalidOperationException.With.Message.Contains("Graph.TriggerGraph.Ghost"));
        }

        [Test]
        public void Install_NonTriggerGraphKind_FailsFast()
        {
            InteractionContextProfileRegistry registry = NewRegistry(out var collectionKeys, out var filters, out var intents);
            var programs = new GraphProgramRegistry();
            RegisterProgram(programs, OtherGraphName, GraphKind.Script, HaltProgram(), entries: null);
            var config = Config(Profile(Triggers: new List<InteractionContextTriggerMount>
            {
                new() { Trigger = OtherGraphName },
            }));

            Assert.That(
                () => registry.Install(config, collectionKeys, filters, intents, Catalog(programs)),
                Throws.InvalidOperationException.With.Message.Contains("Script"));
        }

        [Test]
        public void Install_UnknownEntryEvent_FailsFast()
        {
            InteractionContextProfileRegistry registry = NewRegistry(out var collectionKeys, out var filters, out var intents);
            var programs = new GraphProgramRegistry();
            RegisterProgram(programs, GraphName, GraphKind.TriggerGraph, HaltProgram(), ProbeEntries());
            var config = Config(Profile(Triggers: new List<InteractionContextTriggerMount>
            {
                new() { Trigger = GraphName, Event = "NoSuchEvent" },
            }));

            Assert.That(
                () => registry.Install(config, collectionKeys, filters, intents, Catalog(programs)),
                Throws.InvalidOperationException.With.Message.Contains("NoSuchEvent"));
        }

        [Test]
        public void Install_ValidBindingsAndTriggers_InstallAndResolve()
        {
            InteractionContextProfileRegistry registry = NewRegistry(out var collectionKeys, out var filters, out var intents);
            var programs = new GraphProgramRegistry();
            RegisterProgram(programs, GraphName, GraphKind.TriggerGraph, HaltProgram(), ProbeEntries());
            var config = Config(Profile(
                Bindings: new List<string> { "BoxSelect", "ModifierAdd" },
                Triggers: new List<InteractionContextTriggerMount>
                {
                    new() { Trigger = GraphName, Event = GameEvents.MapLoaded.Value },
                }));

            Assert.That(
                () => registry.Install(config, collectionKeys, filters, intents, Catalog(programs, "BoxSelect", "ModifierAdd")),
                Throws.Nothing);

            int profileId = registry.ProfileIdRegistry.GetId(ProfileId);
            Assert.That(registry.TryGetDefinition(profileId, out InteractionContextProfileDefinition definition), Is.True);
            Assert.That(definition.Bindings, Is.EqualTo(new[] { "BoxSelect", "ModifierAdd" }));
            Assert.That(definition.Triggers![0].Trigger, Is.EqualTo(GraphName));
        }

        [Test]
        public void Loader_RejectsBlankContinuousQueryGraph()
        {
            var config = Config(Profile(ContinuousQuery: new InteractionContextContinuousQuery { Graph = " " }));
            Assert.That(
                () => InteractionContextProfileConfigLoader.Validate(config, "test"),
                Throws.InvalidOperationException.With.Message.Contains("continuousQuery.graph"));
        }

        [Test]
        public void Install_ContinuousQuery_NonQueryKind_FailsFast()
        {
            InteractionContextProfileRegistry registry = NewRegistry(out var collectionKeys, out var filters, out var intents);
            var programs = new GraphProgramRegistry();
            RegisterProgram(programs, OtherGraphName, GraphKind.TriggerGraph, HaltProgram(), ProbeEntries());
            var schemas = new GraphOutputSchemaRegistry();
            var config = Config(Profile(ContinuousQuery: new InteractionContextContinuousQuery { Graph = OtherGraphName }));

            Assert.That(
                () => registry.Install(
                    config,
                    collectionKeys,
                    filters,
                    intents,
                    Catalog(programs, schemas)),
                Throws.InvalidOperationException.With.Message.Contains("Query"));
        }

        [Test]
        public void Install_ContinuousQuery_ResolvesGraphId()
        {
            InteractionContextProfileRegistry registry = NewRegistry(out var collectionKeys, out var filters, out var intents);
            var programs = new GraphProgramRegistry();
            const string queryName = "Graph.Query.ContextPreview";
            int graphId = GraphIdRegistry.Register(queryName);
            programs.Register(graphId, HaltProgram(), GraphKind.Query);
            var schemas = new GraphOutputSchemaRegistry();
            schemas.Register(
                graphId,
                new GraphOutputSchema(
                    new[]
                    {
                        new GraphOutputBinding(
                            "preview",
                            GraphOutputDestinationKind.EntityCollection,
                            GraphOutputValueKind.TargetList,
                            0,
                            0,
                            string.Empty,
                            "test.preview",
                            EntityCollectionRoleKind.AcquisitionPreview,
                            string.Empty,
                            string.Empty),
                    }));
            var config = Config(Profile(ContinuousQuery: new InteractionContextContinuousQuery { Graph = queryName }));

            Assert.That(
                () => registry.Install(config, collectionKeys, filters, intents, Catalog(programs, schemas)),
                Throws.Nothing);

            int profileId = registry.ProfileIdRegistry.GetId(ProfileId);
            Assert.That(registry.TryGetContinuousQueryGraphId(profileId, out int resolved), Is.True);
            Assert.That(resolved, Is.EqualTo(graphId));
        }

        private static InteractionContextProfileRegistry NewRegistry(
            out StringIntRegistry collectionKeys,
            out StringIntRegistry filters,
            out StringIntRegistry intents)
        {
            collectionKeys = new StringIntRegistry(capacity: 8, startId: 1, invalidId: 0, comparer: StringComparer.Ordinal);
            filters = new StringIntRegistry(capacity: 8, startId: 1, invalidId: 0, comparer: StringComparer.Ordinal);
            intents = new StringIntRegistry(capacity: 8, startId: 1, invalidId: 0, comparer: StringComparer.Ordinal);
            return new InteractionContextProfileRegistry(
                new StringIntRegistry(capacity: 8, startId: 1, invalidId: 0, comparer: StringComparer.Ordinal));
        }

        private static InteractionContextProfileReferenceCatalog Catalog(
            GraphProgramRegistry? programs = null,
            params string[] inputActionIds)
        {
            return new InteractionContextProfileReferenceCatalog(
                programs ?? new GraphProgramRegistry(),
                inputActionIds);
        }

        private static InteractionContextProfileReferenceCatalog Catalog(
            GraphProgramRegistry programs,
            GraphOutputSchemaRegistry outputSchemas,
            params string[] inputActionIds)
        {
            return new InteractionContextProfileReferenceCatalog(
                programs,
                inputActionIds,
                outputSchemas);
        }

        private static InteractionContextProfilesConfig Config(InteractionContextProfileDefinition profile)
            => new() { Profiles = new List<InteractionContextProfileDefinition> { profile } };

        private static InteractionContextProfileDefinition Profile(
            List<string>? Bindings = null,
            List<InteractionContextTriggerMount>? Triggers = null,
            InteractionContextContinuousQuery? ContinuousQuery = null)
            => new()
            {
                Id = ProfileId,
                ActiveCollectionKey = "test.context.collection",
                ActiveEntityViewKey = "test.context.view",
                Bindings = Bindings,
                Triggers = Triggers,
                ContinuousQuery = ContinuousQuery,
            };

        private static GraphInstruction[] HaltProgram()
            => new[] { new GraphInstruction { Op = (ushort)GraphNodeOp.HaltReturnInt, A = 0 } };

        private static TriggerGraphEntry[] ProbeEntries()
            => new[] { new TriggerGraphEntry("probe", GameEvents.MapLoaded.Value, 0, once: false) };

        private static void RegisterProgram(
            GraphProgramRegistry programs,
            string name,
            GraphKind kind,
            GraphInstruction[] program,
            TriggerGraphEntry[]? entries)
        {
            int id = GraphIdRegistry.Register(name);
            programs.Register(id, program, kind, GraphInstructionSourceMap.Empty, null, entries);
        }
    }
}
