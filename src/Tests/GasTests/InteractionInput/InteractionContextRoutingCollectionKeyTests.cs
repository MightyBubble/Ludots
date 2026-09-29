using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;
using Arch.Core;
using Ludots.Core.EntityCollections;
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
    /// Routing collection keys are declared on submit-graph nodes and copied onto the
    /// entity interaction instance. A profile field beside that declaration is rejected.
    /// </summary>
    [TestFixture]
    [NonParallelizable]
    public sealed class InteractionContextRoutingCollectionKeyTests
    {
        private const string ProfileId = "interaction.context.test.route";
        private const string GraphName = "graph.test.route.command";
        private const string OtherGraphName = "graph.test.route.cast";

        [SetUp]
        public void SetUp() => GraphIdRegistry.Clear();

        [TearDown]
        public void TearDown() => GraphIdRegistry.Clear();

        [Test]
        public void Loader_RejectsArbitraryCollectionFields()
        {
            JsonObject root = Parse("""
                { "profiles": [ { "id": "interaction.context.test", "collection": "selected" } ] }
                """);

            Assert.That(
                () => InteractionContextProfileConfigLoader.RejectRetiredProfileFields(root, "test.json"),
                Throws.InvalidOperationException.With.Message.Contains("collection field 'collection'"));
        }

        [Test]
        public void Loader_RejectsProfileCollectionKey()
        {
            JsonObject root = Parse("""
                { "profiles": [ { "id": "interaction.context.test", "collectionKey": "selected" } ] }
                """);

            Assert.That(
                () => InteractionContextProfileConfigLoader.RejectRetiredProfileFields(root, "test.json"),
                Throws.InvalidOperationException.With.Message.Contains("collectionKey"));
        }

        [Test]
        public void Loader_RejectsRetiredViewKey()
        {
            JsonObject root = Parse("""
                { "profiles": [ { "id": "interaction.context.test", "activeEntityViewKey": "view.enemies" } ] }
                """);

            Assert.That(
                () => InteractionContextProfileConfigLoader.RejectRetiredProfileFields(root, "test.json"),
                Throws.InvalidOperationException.With.Message.Contains("activeEntityViewKey"));
        }

        [Test]
        public void Loader_AllowsProfileActiveCollectionKey()
        {
            JsonObject root = Parse("""
                { "profiles": [ { "id": "interaction.context.test", "activeCollectionKey": "selected" } ] }
                """);

            Assert.That(
                () => InteractionContextProfileConfigLoader.RejectRetiredProfileFields(root, "test.json"),
                Throws.Nothing);
        }

        [Test]
        public void Install_GraphCollectionKey_PersistsOnEntityInteractionInstance()
        {
            var collectionKeys = NewKeys();
            var store = new EntityCollectionStore(collectionKeys);
            var programs = new GraphProgramRegistry();
            RegisterSubmitGraph(programs, store, GraphName, "selected");
            InteractionContextProfileRegistry registry = NewRegistry();
            registry.Install(
                Config(Profile(graphName: GraphName)),
                collectionKeys,
                NewKeys(),
                NewKeys(),
                Catalog(programs));

            int profileId = registry.ProfileIdRegistry.GetId(ProfileId);
            Assert.That(registry.TryCreateActiveContext(
                profileId, default, InteractionContextInstanceSource.TemplateSpawn, out InteractionContextInstance instance), Is.True);
            Assert.That(instance.ActiveCollectionKeyId, Is.EqualTo(collectionKeys.GetId("selected")));
            Assert.That(registry.TryGetDefinition(profileId, out InteractionContextProfileDefinition definition), Is.True);
            Assert.That(definition.ActiveCollectionKey, Is.Empty);
        }

        [Test]
        public void Install_ProfileFieldBesideGraphKey_Fails()
        {
            var collectionKeys = NewKeys();
            var store = new EntityCollectionStore(collectionKeys);
            var programs = new GraphProgramRegistry();
            RegisterSubmitGraph(programs, store, GraphName, "selected");
            InteractionContextProfileRegistry registry = NewRegistry();
            InteractionContextProfileDefinition profile = Profile(graphName: GraphName);
            profile.ActiveCollectionKey = "selected";

            Assert.That(
                () => registry.Install(Config(profile), collectionKeys, NewKeys(), NewKeys(), Catalog(programs)),
                Throws.InvalidOperationException.With.Message.Contains("remove activeCollectionKey"));
        }

        [Test]
        public void Install_DisagreeingGraphKeys_Fails()
        {
            var collectionKeys = NewKeys();
            var store = new EntityCollectionStore(collectionKeys);
            var programs = new GraphProgramRegistry();
            RegisterSubmitGraph(programs, store, GraphName, "selected");
            RegisterSubmitGraph(programs, store, OtherGraphName, "other");
            InteractionContextProfileRegistry registry = NewRegistry();
            InteractionContextProfileDefinition profile = Profile(graphName: GraphName);
            profile.Triggers!.Add(new InteractionContextTriggerMount { Trigger = OtherGraphName });

            Assert.That(
                () => registry.Install(Config(profile), collectionKeys, NewKeys(), NewKeys(), Catalog(programs)),
                Throws.InvalidOperationException.With.Message.Contains("different collection keys"));
        }

        private static void RegisterSubmitGraph(
            GraphProgramRegistry programs,
            EntityCollectionStore store,
            string graphName,
            string collectionKey)
        {
            var document = new GraphControlFlowDocument
            {
                Id = graphName,
                Kind = "TriggerGraph",
                Entries = new List<TriggerGraphEntryConfig>
                {
                    new()
                    {
                        Label = "on_command",
                        Action = "Test.Command",
                        Start = "ready",
                        Refire = "restart",
                    },
                },
                Nodes = new List<GraphControlFlowNode>
                {
                    new() { Id = "ready", Op = "ReadCalendarEnabled" },
                    new() { Id = "submit", Op = "SubmitCommandIntent", CollectionKey = collectionKey },
                    new() { Id = "halt", Op = "HaltReturnInt" },
                },
                ControlEdges = new List<GraphControlFlowEdge>
                {
                    new("ready", GraphControlFlowPorts.Next, "submit"),
                    new("submit", GraphControlFlowPorts.Next, "halt"),
                },
                ValueEdges = new List<GraphControlFlowValueEdge>
                {
                    new("ready", GraphControlFlowPorts.Value, "submit", GraphControlFlowPorts.Condition),
                },
            };

            var (package, _, diagnostics) = GraphControlFlowCompiler.CompileWithOutputs(document);
            for (int i = 0; i < diagnostics.Count; i++)
            {
                Assert.That(diagnostics[i].Severity, Is.Not.EqualTo(GraphDiagnosticSeverity.Error), diagnostics[i].Message);
            }

            Assert.That(package.HasValue, Is.True, graphName);
            GraphProgramPackage compiled = package!.Value;
            GraphProgramSymbolPatcher.Patch(compiled.Symbols, compiled.Program, new UnusedSymbolResolver(), store);
            int graphId = GraphIdRegistry.Register(graphName);
            programs.Register(
                graphId,
                compiled.Program,
                compiled.Kind,
                GraphInstructionSourceMap.Empty,
                compiled.Symbols,
                compiled.TriggerGraphEntries);
        }

        private static StringIntRegistry NewKeys()
            => new(capacity: 8, startId: 1, invalidId: 0, comparer: StringComparer.Ordinal);

        private static InteractionContextProfileRegistry NewRegistry()
            => new(new StringIntRegistry(capacity: 8, startId: 1, invalidId: 0, comparer: StringComparer.Ordinal));

        private static InteractionContextProfileReferenceCatalog Catalog(GraphProgramRegistry programs)
            => new(programs, Array.Empty<string>());

        private static InteractionContextProfilesConfig Config(InteractionContextProfileDefinition profile)
            => new() { Profiles = new List<InteractionContextProfileDefinition> { profile } };

        private static InteractionContextProfileDefinition Profile(string graphName)
            => new()
            {
                Id = ProfileId,
                Triggers = new List<InteractionContextTriggerMount>
                {
                    new() { Trigger = graphName },
                },
            };

        private static JsonObject Parse(string json)
            => JsonNode.Parse(json) as JsonObject
               ?? throw new InvalidOperationException("test json must be an object");

        private sealed class UnusedSymbolResolver : IGraphSymbolResolver
        {
            public int ResolveTag(string name) => throw new InvalidOperationException(name);
            public int ResolveAttribute(string name) => throw new InvalidOperationException(name);
            public int ResolveEffectTemplate(string name) => throw new InvalidOperationException(name);
            public int ResolveRelationshipType(string name) => throw new InvalidOperationException(name);
            public int ResolveRelationshipMetric(string name) => throw new InvalidOperationException(name);
            public int ResolveRelationshipFlag(string name) => throw new InvalidOperationException(name);
            public int ResolveTargetDispatchPreset(string name) => throw new InvalidOperationException(name);
            public int ResolveEntityTemplate(string name) => throw new InvalidOperationException(name);
        }
    }
}
