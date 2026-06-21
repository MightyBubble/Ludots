using System;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using Arch.Core;
using Ludots.Core.Components;
using Ludots.Core.Config;
using Ludots.Core.EntityCollections;
using Ludots.Core.EntityQueries;
using Ludots.Core.Gameplay.Components;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.Gameplay.Relationships;
using Ludots.Core.Gameplay.Relationships.Config;
using Ludots.Core.Gameplay.Spawning;
using Ludots.Core.GraphRuntime;
using Ludots.Core.Mathematics;
using Ludots.Core.Modding;
using Ludots.Core.NodeLibraries.GASGraph;
using Ludots.Core.NodeLibraries.GASGraph.Host;
using Ludots.Core.Registry;
using Ludots.Core.Scripting;
using NUnit.Framework;

namespace Ludots.Tests.GAS
{
    [TestFixture]
    [NonParallelizable]
    public sealed class EntitySetQueryRuntimeTests
    {
        private string? _tempRoot;

        [TearDown]
        public void TearDown()
        {
            GraphIdRegistry.Clear();

            if (!string.IsNullOrWhiteSpace(_tempRoot))
            {
                try
                {
                    Directory.Delete(_tempRoot, recursive: true);
                }
                catch
                {
                    // Best-effort cleanup for temp test data.
                }
            }

            _tempRoot = null;
        }

        [Test]
        public void CodeApi_FiltersSortsAndAggregatesTeamTemplateTagsAndAttributes()
        {
            using var world = World.Create();
            QueryRuntimeSetup setup = CreateQueryRuntime(world);
            int productionId = GetOrRegisterAttribute("Health");
            int goldId = GetOrRegisterAttribute("Mana");
            int blockedTagId = GetOrRegisterTag("Tests.EntityQuery.Blocked");
            int cityTemplateId = setup.TemplateKeys.Register("tests.entity-query.city");
            int siteTemplateId = setup.TemplateKeys.Register("tests.entity-query.site");

            Entity low = CreateMapEntity(world, teamId: 1, cityTemplateId, productionId, 10f, goldId, 3f);
            Entity high = CreateMapEntity(world, teamId: 1, cityTemplateId, productionId, 30f, goldId, 5f);
            CreateMapEntity(world, teamId: 1, cityTemplateId, productionId, 50f, goldId, 9f, blockedTagId);
            CreateMapEntity(world, teamId: 2, cityTemplateId, productionId, 80f, goldId, 11f);
            CreateMapEntity(world, teamId: 1, siteTemplateId, productionId, 100f, goldId, 13f);

            Span<Entity> entities = stackalloc Entity[GraphVmLimits.MaxTargets];
            int count = setup.EntityQueries.CollectMapEntities(entities);
            count = setup.EntityQueries.FilterTeam(entities, count, teamId: 1);
            count = setup.EntityQueries.FilterTemplate(entities, count, cityTemplateId);
            count = setup.EntityQueries.FilterTagNone(entities, count, blockedTagId);
            count = setup.EntityQueries.FilterAttributeRange(entities, count, productionId, minInclusive: 0f, maxInclusive: 40f);
            setup.EntityQueries.SortByAttribute(entities, count, productionId, descending: true);

            ReadOnlySpan<Entity> result = entities.Slice(0, count);
            Assert.That(count, Is.EqualTo(2));
            Assert.That(result[0], Is.EqualTo(high));
            Assert.That(result[1], Is.EqualTo(low));
            Assert.That(setup.EntityQueries.SumAttribute(result, productionId), Is.EqualTo(40f));
            Assert.That(setup.EntityQueries.AverageAttribute(result, productionId), Is.EqualTo(20f));
            Assert.That(setup.EntityQueries.MaxAttribute(result, productionId), Is.EqualTo(30f));
            Assert.That(setup.EntityQueries.MinAttribute(result, productionId), Is.EqualTo(10f));
            Assert.That(setup.EntityQueries.SumAttribute(result, goldId), Is.EqualTo(8f));
            Assert.That(setup.EntityQueries.TryMaxEntityByAttribute(result, productionId, out Entity best, out float bestValue), Is.True);
            Assert.That(best, Is.EqualTo(high));
            Assert.That(bestValue, Is.EqualTo(30f));
        }

        [Test]
        public void CodeApi_FiltersSortsAndAggregatesRelationshipMetricsAndFlags()
        {
            using var world = World.Create();
            QueryRuntimeSetup setup = CreateQueryRuntime(world);
            int typeId = setup.RelationshipTypes.Register("Diplomacy");
            int trustId = setup.RelationshipMetrics.Register("Trust", minValue: -100, maxValue: 100, defaultValue: 0);
            int pactFlagId = setup.RelationshipFlags.Register("Pact");

            Entity source = world.Create();
            Entity low = world.Create();
            Entity high = world.Create();
            Entity mid = world.Create();

            setup.Relationships.SetMetric(source, low, typeId, trustId, 10);
            setup.Relationships.SetMetric(source, high, typeId, trustId, 70);
            setup.Relationships.SetMetric(source, mid, typeId, trustId, 45);
            setup.Relationships.SetFlag(source, low, typeId, pactFlagId, enabled: true);
            setup.Relationships.SetFlag(source, high, typeId, pactFlagId, enabled: true);

            Span<Entity> entities = stackalloc Entity[8];
            entities[0] = low;
            entities[1] = high;
            entities[2] = mid;
            int count = 3;
            count = setup.EntityQueries.FilterRelationshipFlag(entities, count, source, typeId, pactFlagId, expected: true);
            count = setup.EntityQueries.FilterRelationshipMetricRange(entities, count, source, typeId, trustId, minInclusive: 0, maxInclusive: 80);
            setup.EntityQueries.SortByRelationshipMetric(entities, count, source, typeId, trustId, descending: true);

            ReadOnlySpan<Entity> result = entities.Slice(0, count);
            Assert.That(count, Is.EqualTo(2));
            Assert.That(result[0], Is.EqualTo(high));
            Assert.That(result[1], Is.EqualTo(low));
            Assert.That(setup.EntityQueries.SumRelationshipMetric(result, source, typeId, trustId), Is.EqualTo(80));
            Assert.That(setup.EntityQueries.AverageRelationshipMetric(result, source, typeId, trustId), Is.EqualTo(40));
            Assert.That(setup.EntityQueries.MaxRelationshipMetric(result, source, typeId, trustId), Is.EqualTo(70));
            Assert.That(setup.EntityQueries.MinRelationshipMetric(result, source, typeId, trustId), Is.EqualTo(10));
            Assert.That(setup.EntityQueries.TryMaxEntityByRelationshipMetric(result, source, typeId, trustId, out Entity best, out int bestValue), Is.True);
            Assert.That(best, Is.EqualTo(high));
            Assert.That(bestValue, Is.EqualTo(70));
            Assert.That(setup.EntityQueries.TryMinEntityByRelationshipMetric(result, source, typeId, trustId, out Entity worst, out int worstValue), Is.True);
            Assert.That(worst, Is.EqualTo(low));
            Assert.That(worstValue, Is.EqualTo(10));
        }

        [Test]
        public void CodeApi_RelationshipQueriesIgnoreMissingEdgesInsteadOfUsingMetricDefaults()
        {
            using var world = World.Create();
            QueryRuntimeSetup setup = CreateQueryRuntime(world);
            int typeId = setup.RelationshipTypes.Register("ThreatIntel");
            int threatId = setup.RelationshipMetrics.Register("Threat", minValue: 0, maxValue: 100, defaultValue: 50);
            int priorityFlagId = setup.RelationshipFlags.Register("Priority");

            Entity source = world.Create();
            Entity linked = world.Create();
            Entity linkedFalseFlag = world.Create();
            Entity missing = world.Create();

            setup.Relationships.SetMetric(source, linked, typeId, threatId, 50);
            setup.Relationships.SetFlag(source, linked, typeId, priorityFlagId, enabled: true);
            setup.Relationships.SetMetric(source, linkedFalseFlag, typeId, threatId, 30);

            Span<Entity> entities = stackalloc Entity[4];
            entities[0] = linked;
            entities[1] = missing;
            entities[2] = linkedFalseFlag;

            int metricCount = setup.EntityQueries.FilterRelationshipMetricRange(entities, 3, source, typeId, threatId, minInclusive: 50, maxInclusive: 50);
            Assert.That(metricCount, Is.EqualTo(1));
            Assert.That(entities[0], Is.EqualTo(linked));

            entities[0] = linked;
            entities[1] = missing;
            entities[2] = linkedFalseFlag;
            int falseFlagCount = setup.EntityQueries.FilterRelationshipFlag(entities, 3, source, typeId, priorityFlagId, expected: false);
            Assert.That(falseFlagCount, Is.EqualTo(1));
            Assert.That(entities[0], Is.EqualTo(linkedFalseFlag));

            entities[0] = linked;
            entities[1] = missing;
            entities[2] = linkedFalseFlag;
            ReadOnlySpan<Entity> all = entities.Slice(0, 3);
            Assert.That(setup.EntityQueries.SumRelationshipMetric(all, source, typeId, threatId), Is.EqualTo(80));
            Assert.That(setup.EntityQueries.AverageRelationshipMetric(all, source, typeId, threatId), Is.EqualTo(40));
            Assert.That(setup.EntityQueries.MinRelationshipMetric(all, source, typeId, threatId), Is.EqualTo(30));
            Assert.That(setup.EntityQueries.TryMaxEntityByRelationshipMetric(all, source, typeId, threatId, out Entity best, out int value), Is.True);
            Assert.That(best, Is.EqualTo(linked));
            Assert.That(value, Is.EqualTo(50));
        }

        [Test]
        public void CodeApi_TargetListUtilitiesShareTheSameSpanQueryPath()
        {
            using var world = World.Create();
            QueryRuntimeSetup setup = CreateQueryRuntime(world);
            int productionId = GetOrRegisterAttribute("Tests.EntityQuery.UtilityProduction");
            int goldId = GetOrRegisterAttribute("Tests.EntityQuery.UtilityGold");
            int cityTemplateId = setup.TemplateKeys.Register("tests.entity-query.utility-city");

            Entity far = CreateMapEntity(world, teamId: 1, cityTemplateId, productionId, 10f, goldId, 1f);
            Entity near = CreateMapEntity(world, teamId: 1, cityTemplateId, productionId, 20f, goldId, 1f);
            Entity offLayer = CreateMapEntity(world, teamId: 1, cityTemplateId, productionId, 30f, goldId, 1f);

            world.Add(far, new EntityLayer(category: 0b0010, mask: uint.MaxValue));
            world.Add(near, new EntityLayer(category: 0b0010, mask: uint.MaxValue));
            world.Add(offLayer, new EntityLayer(category: 0b0100, mask: uint.MaxValue));
            world.Add(far, new Position { GridPos = new IntVector2(20, 0) });
            world.Add(near, new Position { GridPos = new IntVector2(2, 0) });
            world.Add(offLayer, new Position { GridPos = new IntVector2(1, 0) });

            Span<Entity> entities = stackalloc Entity[8];
            entities[0] = offLayer;
            entities[1] = near;
            entities[2] = far;
            entities[3] = near;

            int count = setup.EntityQueries.FilterLayer(entities, count: 4, requiredMask: 0b0010);
            count = setup.EntityQueries.FilterNotEntity(entities, count, far);
            count = setup.EntityQueries.SortStableDedup(entities, count);
            count = setup.EntityQueries.Limit(entities, count, limit: 1);

            Assert.That(count, Is.EqualTo(1));
            Assert.That(entities[0], Is.EqualTo(near));
            Assert.That(setup.EntityQueries.TryMinEntityByDistance(entities.Slice(0, count), new IntVector2(0, 0), out Entity closest, out long distanceSquared), Is.True);
            Assert.That(closest, Is.EqualTo(near));
            Assert.That(distanceSquared, Is.EqualTo(4));
        }

        [Test]
        public void GraphConfig_QueryOutputsWriteCollectionAndSummaryThroughSharedCodeApi()
        {
            using var world = World.Create();
            QueryRuntimeSetup setup = CreateQueryRuntime(world);
            int productionId = GetOrRegisterAttribute("Health");
            int goldId = GetOrRegisterAttribute("Mana");
            int blockedTagId = GetOrRegisterTag("Tests.GraphQuery.Blocked");
            int cityTemplateId = setup.TemplateKeys.Register("tests.graph.city");
            setup.TemplateKeys.Register("tests.graph.site");

            Entity owner = world.Create();
            Entity low = CreateMapEntity(world, teamId: 1, cityTemplateId, productionId, 10f, goldId, 3f);
            Entity high = CreateMapEntity(world, teamId: 1, cityTemplateId, productionId, 30f, goldId, 5f);
            CreateMapEntity(world, teamId: 1, cityTemplateId, productionId, 50f, goldId, 9f, blockedTagId);
            CreateMapEntity(world, teamId: 2, cityTemplateId, productionId, 80f, goldId, 11f);

            GraphRuntimeSetup graph = CreateGraphRuntime(setup, GraphConfigJson);
            var api = new GasGraphRuntimeApi(
                world,
                tagOps: setup.TagOps,
                relationshipRuntime: setup.Relationships,
                typeRegistry: setup.RelationshipTypes,
                metricRegistry: setup.RelationshipMetrics,
                flagRegistry: setup.RelationshipFlags,
                reasonRegistry: setup.RelationshipReasons,
                targetDispatchPresets: setup.TargetDispatchPresets,
                entityCollections: graph.Collections,
                entityQueries: setup.EntityQueries);
            var writer = new GraphReturnWriter(
                world,
                graph.Programs,
                graph.OutputSchemas,
                GasGraphOpHandlerTable.Instance,
                graph.Collections,
                graph.OutputValues);

            int graphId = GraphIdRegistry.GetId(GraphId);
            writer.ExecuteAndWrite(graphId, owner, owner, Entity.Null, Entity.Null, default, randomSeed: 1u, api);

            Assert.That(graph.Collections.TryGet(owner, GraphCollectionKey, out EntityCollectionHandle collection), Is.True);
            Span<Entity> copied = stackalloc Entity[4];
            Assert.That(graph.Collections.CopyEntities(collection, 0, copied), Is.EqualTo(2));
            Assert.That(copied[0], Is.EqualTo(high));
            Assert.That(copied[1], Is.EqualTo(low));

            AssertSummaryInt(graph.OutputValues, owner, "tests.graph.cityCount", 2);
            AssertSummaryFloat(graph.OutputValues, owner, "tests.graph.totalProduction", 40f);
            AssertSummaryFloat(graph.OutputValues, owner, "tests.graph.totalGold", 8f);
            AssertSummaryEntity(graph.OutputValues, owner, "tests.graph.bestProductionCity", high);
        }

        [Test]
        public void FourXShowcaseGraphs_CompileThroughLoaderWithSharedQueryAndRelationshipSymbols()
        {
            using var world = World.Create();
            QueryRuntimeSetup setup = CreateQueryRuntime(world);
            string repoRoot = FindRepoRoot();
            string fourXRoot = Path.Combine(repoRoot, "mods", "FourXDemoMod");
            string graphsPath = Path.Combine(fourXRoot, "assets", "GAS", "graphs.json");
            string catalogPath = Path.Combine(fourXRoot, "assets", "Relationships", "catalog.json");
            string templatesPath = Path.Combine(fourXRoot, "assets", "Entities", "templates.json");

            Assert.That(File.Exists(graphsPath), Is.True);
            Assert.That(File.Exists(catalogPath), Is.True);
            Assert.That(File.Exists(templatesPath), Is.True);

            RegisterFourXGraphSymbols();
            RegisterTemplateKeysFromFile(setup.TemplateKeys, templatesPath);
            RelationshipCatalogConfig relationshipCatalog = JsonSerializer.Deserialize<RelationshipCatalogConfig>(
                File.ReadAllText(catalogPath),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
            RelationshipCatalogInstaller.Install(
                relationshipCatalog,
                setup.RelationshipTypes,
                setup.RelationshipMetrics,
                setup.RelationshipFlags,
                setup.RelationshipBands,
                setup.RelationshipReasons,
                setup.Collections);

            GraphRuntimeSetup graph = CreateGraphRuntime(setup, File.ReadAllText(graphsPath));
            int cityGraphId = GraphIdRegistry.GetId("fourx.graph.cityEconomyQuery");
            int tradeGraphId = GraphIdRegistry.GetId("fourx.graph.tradePartnersQuery");

            Assert.That(cityGraphId, Is.GreaterThan(0));
            Assert.That(tradeGraphId, Is.GreaterThan(0));
            Assert.That(graph.OutputSchemas.Get(cityGraphId).Bindings, Has.Length.EqualTo(5));
            Assert.That(graph.OutputSchemas.Get(tradeGraphId).Bindings, Has.Length.EqualTo(6));
        }

        [Test]
        public void GraphConfig_QueryFromCollectionUsesEntityCollectionKeyRegistryAsSingleSourceOfTruth()
        {
            using var world = World.Create();
            QueryRuntimeSetup setup = CreateQueryRuntime(world);
            Entity owner = world.Create();
            Entity first = world.Create();
            Entity second = world.Create();

            GraphRuntimeSetup graph = CreateGraphRuntime(setup, GraphCollectionQueryJson);
            int graphId = GraphIdRegistry.GetId(CollectionQueryGraphId);
            int collectionKeyId = graph.Collections.KeyRegistry.GetId(GraphCollectionKey);
            Assert.That(collectionKeyId, Is.GreaterThan(0));
            Assert.That(graph.Programs.TryGetProgram(graphId, out ReadOnlySpan<GraphInstruction> program), Is.True);
            Assert.That(program.Length, Is.EqualTo(2));
            Assert.That((GraphNodeOp)program[0].Op, Is.EqualTo(GraphNodeOp.LoadCaster));
            Assert.That((GraphNodeOp)program[1].Op, Is.EqualTo(GraphNodeOp.QueryFromCollection));
            Assert.That(program[1].Imm, Is.EqualTo(collectionKeyId));

            var descriptor = EntityCollectionDescriptor.Create(
                GraphCollectionKey,
                EntityCollectionSourceKind.Explicit,
                EntityCollectionRoleKind.Display);
            graph.Collections.Replace(owner, descriptor, new[] { first, second });

            var api = new GasGraphRuntimeApi(
                world,
                tagOps: setup.TagOps,
                relationshipRuntime: setup.Relationships,
                typeRegistry: setup.RelationshipTypes,
                metricRegistry: setup.RelationshipMetrics,
                flagRegistry: setup.RelationshipFlags,
                reasonRegistry: setup.RelationshipReasons,
                targetDispatchPresets: setup.TargetDispatchPresets,
                entityCollections: graph.Collections,
                entityQueries: setup.EntityQueries);

            Span<float> floats = stackalloc float[GraphVmLimits.MaxFloatRegisters];
            Span<int> ints = stackalloc int[GraphVmLimits.MaxIntRegisters];
            Span<byte> bools = stackalloc byte[GraphVmLimits.MaxBoolRegisters];
            Span<Entity> entities = stackalloc Entity[GraphVmLimits.MaxEntityRegisters];
            Span<Entity> targets = stackalloc Entity[GraphVmLimits.MaxTargets];
            var targetList = new GraphTargetList(targets);
            var state = new GraphExecutionState
            {
                World = world,
                Caster = owner,
                Api = api,
                F = floats,
                I = ints,
                B = bools,
                E = entities,
                Targets = targets,
                TargetList = targetList,
            };

            GasGraphOpHandlerTable.Execute(ref state, program, GasGraphOpHandlerTable.Instance);

            Assert.That(state.TargetList.Count, Is.EqualTo(2));
            Assert.That(targets[0], Is.EqualTo(first));
            Assert.That(targets[1], Is.EqualTo(second));
        }

        [Test]
        public void GraphReturnWriter_MissingOutputSchemaFailsExplicitly()
        {
            using var world = World.Create();
            QueryRuntimeSetup setup = CreateQueryRuntime(world);
            var programs = new GraphProgramRegistry();
            var schemas = new GraphOutputSchemaRegistry();
            var collections = new EntityCollectionStore(new StringIntRegistry(capacity: 8, startId: 1, invalidId: 0, comparer: StringComparer.Ordinal));
            var outputValues = new GraphOutputValueStore(new StringIntRegistry(capacity: 8, startId: 1, invalidId: 0, comparer: StringComparer.Ordinal));
            int graphId = GraphIdRegistry.Register("tests.graph.missing-schema");
            programs.Register(graphId, new[] { new GraphInstruction { Op = (ushort)GraphNodeOp.QueryAllMapEntities } });
            var writer = new GraphReturnWriter(world, programs, schemas, GasGraphOpHandlerTable.Instance, collections, outputValues);
            var api = new GasGraphRuntimeApi(world, tagOps: setup.TagOps, relationshipRuntime: setup.Relationships, entityQueries: setup.EntityQueries);
            Entity owner = world.Create();

            InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() =>
                writer.ExecuteAndWrite(graphId, owner, owner, Entity.Null, Entity.Null, default, randomSeed: 0u, api))!;

            Assert.That(ex.Message, Does.Contain("no output schema"));
        }

        [Test]
        public void Benchmark_CodeApiFilterSortAggregateZeroAllocAfterWarmup()
        {
            using var world = World.Create();
            QueryRuntimeSetup setup = CreateQueryRuntime(world);
            int productionId = GetOrRegisterAttribute("Health");
            int goldId = GetOrRegisterAttribute("Mana");
            int blockedTagId = GetOrRegisterTag("Tests.EntityQuery.BenchmarkBlocked");
            int cityTemplateId = setup.TemplateKeys.Register("tests.entity-query.benchmark-city");

            const int entityCount = GraphVmLimits.MaxTargets;
            for (int i = 0; i < entityCount; i++)
            {
                int teamId = (i & 1) == 0 ? 1 : 2;
                int tagId = i % 17 == 0 ? blockedTagId : 0;
                CreateMapEntity(world, teamId, cityTemplateId, productionId, i % 100, goldId, 0f, tagId);
            }

            Span<Entity> entities = stackalloc Entity[GraphVmLimits.MaxTargets];
            for (int i = 0; i < 256; i++)
            {
                RunBenchmarkQuery(setup.EntityQueries, entities, productionId, blockedTagId, cityTemplateId);
            }

            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            GC.GetAllocatedBytesForCurrentThread();

            const int iterations = 4_000;
            long before = GC.GetAllocatedBytesForCurrentThread();
            long start = Stopwatch.GetTimestamp();
            float checksum = 0f;
            for (int i = 0; i < iterations; i++)
            {
                checksum += RunBenchmarkQuery(setup.EntityQueries, entities, productionId, blockedTagId, cityTemplateId);
            }

            long stop = Stopwatch.GetTimestamp();
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            double totalMs = Stopwatch.GetElapsedTime(start, stop).TotalMilliseconds;

            Console.WriteLine("[Benchmark] EntitySetQueryRuntime.FilterSortAggregate:");
            Console.WriteLine($"  Entities: {entityCount}");
            Console.WriteLine($"  Iterations: {iterations}");
            Console.WriteLine($"  TotalMs: {totalMs:F2}");
            Console.WriteLine($"  PerQueryUs: {totalMs * 1000.0 / iterations:F4}");
            Console.WriteLine($"  AllocatedBytes(CurrentThread): {allocated}");

            Assert.That(checksum, Is.GreaterThan(0f));
            Assert.That(allocated, Is.LessThanOrEqualTo(64));
        }

        private static float RunBenchmarkQuery(
            EntitySetQueryRuntime queries,
            Span<Entity> entities,
            int productionId,
            int blockedTagId,
            int cityTemplateId)
        {
            int count = queries.CollectMapEntities(entities);
            count = queries.FilterTeam(entities, count, teamId: 1);
            count = queries.FilterTemplate(entities, count, cityTemplateId);
            count = queries.FilterTagNone(entities, count, blockedTagId);
            count = queries.FilterAttributeRange(entities, count, productionId, 10f, 95f);
            queries.SortByAttribute(entities, count, productionId, descending: true);
            return queries.SumAttribute(entities.Slice(0, count), productionId) +
                   queries.MaxAttribute(entities.Slice(0, count), productionId);
        }

        private GraphRuntimeSetup CreateGraphRuntime(QueryRuntimeSetup setup, string graphJson)
        {
            _tempRoot = Path.Combine(Path.GetTempPath(), "Ludots_GraphQueryTests", Guid.NewGuid().ToString("N"));
            string coreRoot = Path.Combine(_tempRoot, "Core");
            string graphDir = Path.Combine(coreRoot, "Configs", "GAS");
            Directory.CreateDirectory(graphDir);
            File.WriteAllText(Path.Combine(graphDir, "graphs.json"), graphJson);

            var vfs = new VirtualFileSystem();
            vfs.Mount("Core", coreRoot);
            var modLoader = new ModLoader(vfs, new FunctionRegistry(), new TriggerManager());
            var pipeline = new ConfigPipeline(vfs, modLoader);
            var catalog = new ConfigCatalog();
            catalog.Add(new ConfigCatalogEntry("GAS/graphs.json", ConfigMergePolicy.ArrayById, "id"));

            var programs = new GraphProgramRegistry();
            var schemas = new GraphOutputSchemaRegistry();
            var outputKeys = new StringIntRegistry(capacity: 16, startId: 1, invalidId: 0, comparer: StringComparer.Ordinal);
            var outputValues = new GraphOutputValueStore(outputKeys, initialCapacity: 16);
            var symbolResolver = new GasGraphSymbolResolver(
                setup.RelationshipTypes,
                setup.RelationshipMetrics,
                setup.RelationshipFlags,
                setup.RelationshipReasons,
                setup.TargetDispatchPresets,
                setup.TemplateKeys);
            var loader = new GraphProgramConfigLoader(pipeline, programs, symbolResolver, schemas, outputKeys, setup.Collections);
            var packages = loader.LoadIdsAndCompile(catalog, relativePath: "GAS/graphs.json");
            loader.PatchAndRegister(packages);

            return new GraphRuntimeSetup(programs, schemas, setup.Collections, outputValues);
        }

        private static QueryRuntimeSetup CreateQueryRuntime(World world)
        {
            var tagOps = new TagOps(new TagRuleRegistry(), new GasBudget());
            var typeRegistry = new RelationshipTypeRegistry();
            var metricRegistry = new RelationshipMetricRegistry();
            var flagRegistry = new RelationshipFlagRegistry();
            var bandRegistry = new RelationshipBandRegistry();
            var reasonRegistry = new RelationshipReasonRegistry();
            var changeBuffer = new RelationshipChangeBuffer();
            var relationships = new RelationshipRuntime(world, typeRegistry, metricRegistry, flagRegistry, bandRegistry, changeBuffer);
            var entityQueries = new EntitySetQueryRuntime(world, tagOps, relationships);
            var templateKeys = new EntityTemplateKeyRegistry();
            var targetDispatchPresets = new TargetDispatchPresetRegistry();
            var collectionKeys = new StringIntRegistry(capacity: 16, startId: 1, invalidId: 0, comparer: StringComparer.Ordinal);
            var collections = new EntityCollectionStore(collectionKeys, initialCollectionCapacity: 4, initialRowCapacity: 16);
            return new QueryRuntimeSetup(
                tagOps,
                relationships,
                typeRegistry,
                metricRegistry,
                flagRegistry,
                bandRegistry,
                reasonRegistry,
                entityQueries,
                templateKeys,
                targetDispatchPresets,
                collections);
        }

        private static Entity CreateMapEntity(
            World world,
            int teamId,
            int templateKeyId,
            int productionId,
            float production,
            int goldId,
            float gold,
            int tagId = 0)
        {
            Entity entity = world.Create(
                new MapEntity(),
                new Team { Id = teamId },
                new EntityTemplateKeyRef { TemplateKeyId = templateKeyId },
                new AttributeBuffer(),
                new GameplayTagContainer());
            ref AttributeBuffer attributes = ref world.Get<AttributeBuffer>(entity);
            attributes.SetBase(productionId, production);
            attributes.SetBase(goldId, gold);

            if (tagId > 0)
            {
                ref GameplayTagContainer tags = ref world.Get<GameplayTagContainer>(entity);
                tags.AddTag(tagId);
            }

            return entity;
        }

        private static int GetOrRegisterAttribute(string name)
        {
            int id = AttributeRegistry.GetId(name);
            return id != AttributeRegistry.InvalidId ? id : AttributeRegistry.Register(name);
        }

        private static int GetOrRegisterTag(string name)
        {
            int id = TagRegistry.GetId(name);
            return id != TagRegistry.InvalidId ? id : TagRegistry.Register(name);
        }

        private static void RegisterFourXGraphSymbols()
        {
            GetOrRegisterAttribute("Health");
            GetOrRegisterAttribute("Production");
            GetOrRegisterAttribute("Gold");
            GetOrRegisterAttribute("TechProgress");
            GetOrRegisterAttribute("FoodProduction");

            GetOrRegisterTag("State.4X.Blockaded");
            GetOrRegisterTag("State.4X.Razed");
            GetOrRegisterTag("State.4X.Relationship.Trusted");
            GetOrRegisterTag("State.4X.Relationship.TradePact");
            GetOrRegisterTag("State.4X.Relationship.AtWar");
        }

        private static void RegisterTemplateKeysFromFile(EntityTemplateKeyRegistry templateKeys, string templatesPath)
        {
            JsonArray templates = JsonNode.Parse(File.ReadAllText(templatesPath)) as JsonArray
                ?? throw new InvalidOperationException($"Template config '{templatesPath}' must be a JSON array.");

            for (int i = 0; i < templates.Count; i++)
            {
                if (templates[i] is not JsonObject obj ||
                    !obj.TryGetPropertyValue("id", out JsonNode? idNode) ||
                    idNode == null)
                {
                    throw new InvalidOperationException($"Template config '{templatesPath}' entry {i} must declare id.");
                }

                templateKeys.Register(idNode.ToString());
            }
        }

        private static string FindRepoRoot()
        {
            string current = TestContext.CurrentContext.WorkDirectory;
            while (!string.IsNullOrWhiteSpace(current))
            {
                if (Directory.Exists(Path.Combine(current, "mods")) &&
                    File.Exists(Path.Combine(current, "gitbook", "contributing", "ai-assisted-development.md")))
                {
                    return current;
                }

                DirectoryInfo? parent = Directory.GetParent(current);
                if (parent == null)
                {
                    break;
                }

                current = parent.FullName;
            }

            throw new InvalidOperationException("Could not locate repository root for FourX graph config test.");
        }

        private static void AssertSummaryInt(GraphOutputValueStore values, Entity owner, string key, int expected)
        {
            Assert.That(values.TryGet(owner, key, out GraphOutputValueHandle handle), Is.True);
            Assert.That(values.TryGetView(handle, out GraphOutputValueView view), Is.True);
            Assert.That(view.Kind, Is.EqualTo(GraphOutputValueKind.Int));
            Assert.That(view.IntValue, Is.EqualTo(expected));
        }

        private static void AssertSummaryFloat(GraphOutputValueStore values, Entity owner, string key, float expected)
        {
            Assert.That(values.TryGet(owner, key, out GraphOutputValueHandle handle), Is.True);
            Assert.That(values.TryGetView(handle, out GraphOutputValueView view), Is.True);
            Assert.That(view.Kind, Is.EqualTo(GraphOutputValueKind.Float));
            Assert.That(view.FloatValue, Is.EqualTo(expected));
        }

        private static void AssertSummaryEntity(GraphOutputValueStore values, Entity owner, string key, Entity expected)
        {
            Assert.That(values.TryGet(owner, key, out GraphOutputValueHandle handle), Is.True);
            Assert.That(values.TryGetView(handle, out GraphOutputValueView view), Is.True);
            Assert.That(view.Kind, Is.EqualTo(GraphOutputValueKind.Entity));
            Assert.That(view.EntityValue, Is.EqualTo(expected));
        }

        private const string GraphId = "tests.graph.4x.cityEconomy";
        private const string CollectionQueryGraphId = "tests.graph.collectionQuery";
        private const string GraphCollectionKey = "tests.graph.collection.cities";

        private const string GraphConfigJson = """
[
  {
    "id": "tests.graph.4x.cityEconomy",
    "entry": "minProduction",
    "nodes": [
      { "id": "minProduction", "op": "ConstFloat", "floatValue": 0, "next": "maxProduction" },
      { "id": "maxProduction", "op": "ConstFloat", "floatValue": 100, "next": "allMapEntities" },
      { "id": "allMapEntities", "op": "QueryAllMapEntities", "next": "team" },
      { "id": "team", "op": "QueryFilterTeam", "teamId": 1, "next": "template" },
      { "id": "template", "op": "QueryFilterTemplate", "template": "tests.graph.city", "next": "notBlocked" },
      { "id": "notBlocked", "op": "QueryFilterTagNone", "tag": "Tests.GraphQuery.Blocked", "next": "productionRange" },
      { "id": "productionRange", "op": "QueryFilterAttributeRange", "attribute": "Health", "inputs": [ "minProduction", "maxProduction" ], "next": "sortProduction" },
      { "id": "sortProduction", "op": "QuerySortByAttribute", "attribute": "Health", "descending": true, "next": "countCities" },
      { "id": "countCities", "op": "AggCount", "next": "sumProduction" },
      { "id": "sumProduction", "op": "AggSumAttribute", "attribute": "Health", "next": "sumGold" },
      { "id": "sumGold", "op": "AggSumAttribute", "attribute": "Mana", "next": "bestProductionCity" },
      { "id": "bestProductionCity", "op": "AggMaxEntityByAttribute", "attribute": "Health" }
    ],
    "outputs": [
      {
        "id": "cities",
        "destination": "EntityCollection",
        "type": "TargetList",
        "collectionKey": "tests.graph.collection.cities",
        "role": "Display",
        "title": "Cities",
        "summary": "Filtered 4X economy cities"
      },
      { "id": "cityCount", "destination": "Summary", "type": "Int", "source": "countCities", "key": "tests.graph.cityCount" },
      { "id": "totalProduction", "destination": "Summary", "type": "Float", "source": "sumProduction", "key": "tests.graph.totalProduction" },
      { "id": "totalGold", "destination": "Summary", "type": "Float", "source": "sumGold", "key": "tests.graph.totalGold" },
      { "id": "bestProductionCity", "destination": "Summary", "type": "Entity", "source": "bestProductionCity", "key": "tests.graph.bestProductionCity" }
    ]
  }
]
""";

        private const string GraphCollectionQueryJson = """
[
  {
    "id": "tests.graph.collectionQuery",
    "entry": "owner",
    "nodes": [
      { "id": "owner", "op": "LoadCaster", "next": "fromCollection" },
      { "id": "fromCollection", "op": "QueryFromCollection", "collectionKey": "tests.graph.collection.cities", "inputs": [ "owner" ] }
    ]
  }
]
""";

        private sealed record QueryRuntimeSetup(
            TagOps TagOps,
            RelationshipRuntime Relationships,
            RelationshipTypeRegistry RelationshipTypes,
            RelationshipMetricRegistry RelationshipMetrics,
            RelationshipFlagRegistry RelationshipFlags,
            RelationshipBandRegistry RelationshipBands,
            RelationshipReasonRegistry RelationshipReasons,
            EntitySetQueryRuntime EntityQueries,
            EntityTemplateKeyRegistry TemplateKeys,
            TargetDispatchPresetRegistry TargetDispatchPresets,
            EntityCollectionStore Collections);

        private sealed record GraphRuntimeSetup(
            GraphProgramRegistry Programs,
            GraphOutputSchemaRegistry OutputSchemas,
            EntityCollectionStore Collections,
            GraphOutputValueStore OutputValues);
    }
}
