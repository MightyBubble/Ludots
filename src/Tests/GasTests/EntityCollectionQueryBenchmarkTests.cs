using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using Arch.Core;
using EntityCommandPanelMod;
using Ludots.Core.Components;
using Ludots.Core.Engine;
using Ludots.Core.EntityCollections;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Orders;
using Ludots.Core.Input.Config;
using Ludots.Core.Input.Orders;
using Ludots.Core.Input.Runtime;
using Ludots.Core.Modding;
using Ludots.Core.Registry;
using Ludots.Core.Scripting;
using Ludots.Core.UI.EntityCommandPanels;
using Ludots.UI;
using Ludots.UI.Skia;
using NUnit.Framework;

namespace Ludots.Tests.GAS
{
    [TestFixture]
    [NonParallelizable]
    public sealed class EntityCollectionQueryBenchmarkTests
    {
        private const string CollectionSourceId = "gas.collection-ability-slots";
        private const string BenchmarkQueryId = "tests.benchmark.command-source";

        [Test]
        public void Benchmark_EntityCollectionStore_Replace100kAndWindowZeroAlloc()
        {
            using var world = World.Create();
            Entity owner = world.Create();
            const int rowCount = 100_000;
            var entities = new Entity[rowCount];
            for (int i = 0; i < rowCount; i++)
            {
                entities[i] = world.Create();
            }

            var store = new EntityCollectionStore(
                new StringIntRegistry(capacity: 8, startId: 1, invalidId: 0, comparer: StringComparer.Ordinal),
                initialCollectionCapacity: 4,
                initialRowCapacity: rowCount);
            var descriptor = EntityCollectionDescriptor.Create(
                "benchmark.collection.100k",
                EntityCollectionSourceKind.Debug,
                EntityCollectionRoleKind.Display,
                owner,
                entities[0],
                "Benchmark 100k",
                "100000 rows");

            EntityCollectionHandle handle = store.Replace(owner, descriptor, entities);
            store.Replace(owner, descriptor, entities);

            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            GC.GetAllocatedBytesForCurrentThread();

            const int replaceIterations = 16;
            long replaceBefore = GC.GetAllocatedBytesForCurrentThread();
            long replaceStart = Stopwatch.GetTimestamp();
            for (int i = 0; i < replaceIterations; i++)
            {
                handle = store.Replace(owner, descriptor, entities);
            }
            long replaceStop = Stopwatch.GetTimestamp();
            double replaceTotalMs = Stopwatch.GetElapsedTime(replaceStart, replaceStop).TotalMilliseconds;
            long replaceAllocated = GC.GetAllocatedBytesForCurrentThread() - replaceBefore;

            Span<Entity> entityWindow = stackalloc Entity[64];
            Span<int> ordinalWindow = stackalloc int[64];
            Span<int> roleWindow = stackalloc int[64];
            Span<EntityCollectionRowFlags> flagWindow = stackalloc EntityCollectionRowFlags[64];
            store.CopyWindow(handle, 512, entityWindow, ordinalWindow, roleWindow, flagWindow);

            const int windowIterations = 200_000;
            long windowBefore = GC.GetAllocatedBytesForCurrentThread();
            long windowStart = Stopwatch.GetTimestamp();
            int copied = 0;
            for (int i = 0; i < windowIterations; i++)
            {
                copied += store.CopyWindow(handle, i % (rowCount - entityWindow.Length), entityWindow, ordinalWindow, roleWindow, flagWindow);
            }
            long windowStop = Stopwatch.GetTimestamp();
            double windowTotalMs = Stopwatch.GetElapsedTime(windowStart, windowStop).TotalMilliseconds;
            long windowAllocated = GC.GetAllocatedBytesForCurrentThread() - windowBefore;

            Console.WriteLine("[Benchmark] EntityCollectionStore.Replace 100k:");
            Console.WriteLine($"  Rows: {rowCount}");
            Console.WriteLine($"  Iterations: {replaceIterations}");
            Console.WriteLine($"  TotalMs: {replaceTotalMs:F2}");
            Console.WriteLine($"  PerReplaceMs: {replaceTotalMs / replaceIterations:F4}");
            Console.WriteLine($"  AllocatedBytes(CurrentThread): {replaceAllocated}");
            Console.WriteLine("[Benchmark] EntityCollectionStore.CopyWindow 64:");
            Console.WriteLine($"  Iterations: {windowIterations}");
            Console.WriteLine($"  TotalMs: {windowTotalMs:F2}");
            Console.WriteLine($"  PerWindowUs: {windowTotalMs * 1000.0 / windowIterations:F4}");
            Console.WriteLine($"  AllocatedBytes(CurrentThread): {windowAllocated}");

            Assert.That(copied, Is.EqualTo(windowIterations * entityWindow.Length));
            Assert.That(replaceAllocated, Is.LessThanOrEqualTo(64));
            Assert.That(windowAllocated, Is.EqualTo(0));
        }

        [Test]
        public void Benchmark_CommandCollectionAggregation_ZeroAllocAfterWarmup()
        {
            using var engine = CreateEngineWithCommandPanelMod();
            for (int abilityId = 1; abilityId <= 8; abilityId++)
            {
                RegisterAbility(engine, abilityId, $"Ability {abilityId}", $"Detail {abilityId}");
            }

            const int ownerCount = 128;
            var owners = new Entity[ownerCount];
            for (int ownerIndex = 0; ownerIndex < ownerCount; ownerIndex++)
            {
                owners[ownerIndex] = CreateActor(
                    engine.World,
                    $"Owner {ownerIndex}",
                    1,
                    2 + ownerIndex % 3,
                    5 + ownerIndex % 4,
                    8);
            }

            Entity collectionOwner = engine.World.Create();
            var store = engine.GetService(CoreServiceKeys.EntityCollectionStore)
                ?? throw new InvalidOperationException("EntityCollectionStore missing.");
            store.Replace(
                collectionOwner,
                EntityCollectionDescriptor.Create(
                    EntityCollectionKeys.CommandSource,
                    EntityCollectionSourceKind.Debug,
                    EntityCollectionRoleKind.CommandSource,
                    collectionOwner,
                    owners[0],
                    "Benchmark Command Owners",
                    $"{ownerCount} owners"),
                owners);

            var queries = engine.GetService(CoreServiceKeys.EntityCommandPanelCollectionQueryConfigRegistry)
                ?? throw new InvalidOperationException("EntityCommandPanelCollectionQueryConfigRegistry missing.");
            queries.Register(new EntityCommandPanelCollectionQueryConfig
            {
                Id = BenchmarkQueryId,
                CollectionKey = EntityCollectionKeys.CommandSource,
                Filter = EntityCommandPanelCollectionFilter.Any,
                Sort = EntityCommandPanelCollectionSortKind.OwnerCountThenSlotThenLabel
            });

            engine.SetService(CoreServiceKeys.ActiveInputOrderMapping, CreateMappingSystem());
            IEntityCommandPanelSource source = ResolveCollectionSource(engine);
            var context = new EntityCommandPanelSourceContext(collectionOwner, CollectionSourceId, BenchmarkQueryId);
            var slots = new EntityCommandPanelSlotView[8];

            for (int i = 0; i < 512; i++)
            {
                EntityCommandPanelSourceDispatch.CopySlots(source, in context, 0, slots);
            }

            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            GC.GetAllocatedBytesForCurrentThread();

            const int iterations = 20_000;
            long before = GC.GetAllocatedBytesForCurrentThread();
            long start = Stopwatch.GetTimestamp();
            int totalCopied = 0;
            for (int i = 0; i < iterations; i++)
            {
                totalCopied += EntityCommandPanelSourceDispatch.CopySlots(source, in context, 0, slots);
            }
            long stop = Stopwatch.GetTimestamp();
            double totalMs = Stopwatch.GetElapsedTime(start, stop).TotalMilliseconds;
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

            Console.WriteLine("[Benchmark] CollectionGasEntityCommandPanelSource.CopySlots:");
            Console.WriteLine($"  Owners: {ownerCount}");
            Console.WriteLine($"  Iterations: {iterations}");
            Console.WriteLine($"  TotalMs: {totalMs:F2}");
            Console.WriteLine($"  PerCopyUs: {totalMs * 1000.0 / iterations:F4}");
            Console.WriteLine($"  AllocatedBytes(CurrentThread): {allocated}");

            Assert.That(totalCopied, Is.GreaterThan(0));
            Assert.That(allocated, Is.LessThanOrEqualTo(64));
        }

        private static GameEngine CreateEngineWithCommandPanelMod()
        {
            string repoRoot = FindRepoRoot();
            var engine = new GameEngine();
            engine.InitializeWithConfigPipeline(
                RepoModPaths.ResolveExplicit(repoRoot, new[] { "LudotsCoreMod" }),
                Path.Combine(repoRoot, "assets"));
            InstallUiServices(engine);

            var context = new ModContext(
                "EntityCommandPanelMod",
                engine.VFS,
                engine.FunctionRegistry,
                engine.TriggerManager,
                engine.SystemFactoryRegistry,
                engine.TriggerDecoratorRegistry,
                new ModExtensionHub());
            new EntityCommandPanelModEntry().OnLoad(context);
            engine.TriggerManager.FireEvent(GameEvents.GameStart, engine.CreateContext());
            Assert.That(engine.TriggerManager.Errors.Count, Is.EqualTo(0));
            return engine;
        }

        private static void InstallUiServices(GameEngine engine)
        {
            var uiRoot = new UIRoot(new SkiaUiRenderer());
            uiRoot.Resize(1920f, 1080f);
            engine.SetService(CoreServiceKeys.UIRoot, uiRoot);
            engine.SetService(CoreServiceKeys.UiTextMeasurer, (object)new SkiaTextMeasurer());
            engine.SetService(CoreServiceKeys.UiImageSizeProvider, (object)new SkiaImageSizeProvider());
        }

        private static void RegisterAbility(GameEngine engine, int abilityId, string label, string detail)
        {
            var registry = engine.GetService(CoreServiceKeys.AbilityDefinitionRegistry)
                ?? throw new InvalidOperationException("AbilityDefinitionRegistry missing.");
            var definition = new AbilityDefinition
            {
                HasPresentation = true,
                Presentation = new AbilityPresentationConfig
                {
                    DisplayName = label,
                    HintText = detail
                }
            };
            registry.Register(abilityId, in definition, "EntityCollectionQueryBenchmarkTests");
        }

        private static Entity CreateActor(World world, string name, params int[] abilityIds)
        {
            Entity actor = world.Create(new Name { Value = name }, new AbilityStateBuffer());
            ref var abilities = ref world.Get<AbilityStateBuffer>(actor);
            for (int i = 0; i < abilityIds.Length; i++)
            {
                abilities.AddAbility(abilityIds[i]);
            }

            return actor;
        }

        private static InputOrderMappingSystem CreateMappingSystem()
        {
            var mapping = new InputOrderMappingSystem(new FrozenInputActionReader(), new InputOrderMappingConfig
            {
                InteractionMode = InteractionModeType.TargetFirst,
                Mappings = new List<InputOrderMapping>
                {
                    CreateSkillMapping("SkillQ", 0),
                    CreateSkillMapping("SkillW", 1),
                    CreateSkillMapping("SkillE", 2),
                    CreateSkillMapping("SkillR", 3)
                }
            });
            mapping.SetOrderTypeKeyResolver(key => string.Equals(key, "castAbility", StringComparison.Ordinal) ? 100 : 0);
            mapping.SetOrderSubmitHandler((in Order _) => { });
            return mapping;
        }

        private static InputOrderMapping CreateSkillMapping(string actionId, int slotIndex)
        {
            return new InputOrderMapping
            {
                ActionId = actionId,
                Trigger = InputTriggerType.PressedThisFrame,
                OrderTypeKey = "castAbility",
                ArgsTemplate = new OrderArgsTemplate { I0 = slotIndex },
                RequireSelection = false,
                SelectionType = OrderSelectionType.None,
                IsSkillMapping = true
            };
        }

        private static IEntityCommandPanelSource ResolveCollectionSource(GameEngine engine)
        {
            var registry = engine.GetService(CoreServiceKeys.EntityCommandPanelSourceRegistry)
                ?? throw new InvalidOperationException("EntityCommandPanelSourceRegistry missing.");
            Assert.That(registry.TryGet(CollectionSourceId, out IEntityCommandPanelSource source), Is.True);
            return source;
        }

        private static string FindRepoRoot()
        {
            string dir = TestContext.CurrentContext.TestDirectory;
            while (!string.IsNullOrEmpty(dir))
            {
                if (File.Exists(Path.Combine(dir, "README.md")) && Directory.Exists(Path.Combine(dir, "mods")))
                {
                    return dir;
                }

                dir = Directory.GetParent(dir)?.FullName ?? string.Empty;
            }

            throw new DirectoryNotFoundException("Could not locate repository root.");
        }
    }
}
