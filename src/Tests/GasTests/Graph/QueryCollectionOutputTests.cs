using System;
using Arch.Core;
using Ludots.Core.EntityCollections;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.GraphRuntime;
using Ludots.Core.NodeLibraries.GASGraph;
using Ludots.Core.NodeLibraries.GASGraph.Host;
using Ludots.Core.Registry;
using Ludots.Core.TypedCollections;
using NUnit.Framework;

namespace Ludots.Tests.GasTests.Graph
{
    [TestFixture]
    public sealed class QueryCollectionOutputTests
    {
        [SetUp]
        public void SetUp()
        {
            EffectTemplateIdRegistry.Clear();
        }

        [TearDown]
        public void TearDown()
        {
            EffectTemplateIdRegistry.Clear();
        }

        [Test]
        public void GraphReturnWriter_WritesEffectTemplateIdsToIntIdCollection()
        {
            using World world = World.Create();
            int blessing = EffectTemplateIdRegistry.Register("tests.effect.blessing");
            int swift = EffectTemplateIdRegistry.Register("tests.effect.swift");
            Entity owner = world.Create();
            const int graphId = 1;
            const string collectionKey = "tests.graph.effect-templates";

            var programs = new GraphProgramRegistry();
            programs.Register(
                graphId,
                new[]
                {
                    new GraphInstruction { Op = (ushort)GraphNodeOp.QueryCollectEffectTemplates },
                    new GraphInstruction { Op = (ushort)GraphNodeOp.HaltReturnInt },
                },
                GraphKind.Query);

            var schemas = new GraphOutputSchemaRegistry();
            schemas.Register(
                graphId,
                new GraphOutputSchema(
                    new[]
                    {
                        new GraphOutputBinding(
                            "effectTemplates",
                            GraphOutputDestinationKind.EffectTemplateCollection,
                            GraphOutputValueKind.IntIdList,
                            0,
                            0,
                            string.Empty,
                            collectionKey,
                            EntityCollectionRoleKind.Display,
                            string.Empty,
                            string.Empty),
                    }));

            var collectionKeys = new StringIntRegistry();
            var entityCollections = new EntityCollectionStore(collectionKeys);
            var intIdCollections = new IntIdCollectionStore(collectionKeys);
            var outputValues = new GraphOutputValueStore(new StringIntRegistry(), initialCapacity: 4);
            var writer = new GraphReturnWriter(
                world,
                programs,
                schemas,
                GasGraphOpHandlerTable.Instance,
                entityCollections,
                intIdCollections,
                outputValues);

            writer.ExecuteAndWrite(
                graphId,
                owner,
                owner,
                Entity.Null,
                Entity.Null,
                default,
                0u,
                new GasGraphRuntimeApi(world));

            Assert.That(intIdCollections.TryGet(owner, collectionKey, out IntIdCollectionHandle handle), Is.True);
            Span<int> ids = stackalloc int[2];
            Assert.That(intIdCollections.CopyIds(handle, 0, ids), Is.EqualTo(2));
            Assert.That(ids[0], Is.EqualTo(Math.Min(blessing, swift)));
            Assert.That(ids[1], Is.EqualTo(Math.Max(blessing, swift)));
        }

        [Test]
        public void Compiler_RejectsTargetListForEffectTemplateCollection()
        {
            AssertCollectionOutputTypeRejected(
                GraphNodeOp.QueryAllMapEntities,
                GraphOutputDestinationKind.EffectTemplateCollection,
                GraphOutputValueKind.TargetList,
                "IntIdList");
        }

        [Test]
        public void Compiler_RejectsIntIdListForEntityCollection()
        {
            AssertCollectionOutputTypeRejected(
                GraphNodeOp.QueryCollectEffectTemplates,
                GraphOutputDestinationKind.EntityCollection,
                GraphOutputValueKind.IntIdList,
                "TargetList");
        }

        private static void AssertCollectionOutputTypeRejected(
            GraphNodeOp op,
            GraphOutputDestinationKind destination,
            GraphOutputValueKind valueKind,
            string requiredType)
        {
            var document = new GraphControlFlowDocument
            {
                Id = $"tests.graph.reject.{destination}.{valueKind}",
                Kind = nameof(GraphKind.Query),
                Entry = "collect",
                Nodes =
                {
                    new GraphControlFlowNode { Id = "collect", Op = op.ToString() },
                },
                Outputs =
                {
                    new GraphOutputConfig
                    {
                        Id = "collection",
                        Destination = destination.ToString(),
                        Type = valueKind.ToString(),
                        CollectionKey = "tests.graph.rejected",
                    },
                },
            };

            var (package, _, diagnostics) = GraphControlFlowCompiler.CompileWithOutputs(document);

            Assert.That(package.HasValue, Is.False);
            Assert.That(diagnostics, Has.Some.Matches<GraphDiagnostic>(diagnostic =>
                diagnostic.Code == GraphDiagnosticCodes.TypeMismatch &&
                diagnostic.Message.Contains(requiredType, StringComparison.Ordinal)));
        }
    }
}
