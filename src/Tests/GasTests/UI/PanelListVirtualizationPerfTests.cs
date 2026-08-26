using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using Arch.Core;
using Ludots.Core.Components;
using Ludots.Core.EntityCollections;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.NodeLibraries.GASGraph;
using Ludots.Core.Registry;
using Ludots.Core.UI.PanelHosting;
using Ludots.Core.UI.PanelProjection;
using NUnit.Framework;

namespace Ludots.Tests.GasTests.UI
{
    [TestFixture]
    [Category("benchmark")]
    public sealed class PanelListVirtualizationPerfTests
    {
        private const int StressCount = 1000;
        private const int VisibleBudget = 20;

        [SetUp]
        public void SetUp()
        {
            AttributeRegistry.Clear();
            TagRegistry.Clear();
        }

        [TearDown]
        public void TearDown()
        {
            AttributeRegistry.Clear();
            TagRegistry.Clear();
        }

        [Test]
        public void Project_1000Entities_WindowedIsFarCheaperThanFull()
        {
            AttributeRegistry.Register("Health");
            TagRegistry.Register("Status.Stunned");

            PanelTemplate element = PanelTemplateLoader.Load("""
            {
              "id": "panel.unit.roster",
              "subject": "Entity",
              "graph": "g.card",
              "pins": [
                { "name": "health", "key": "unit.roster.health", "default": 0 },
                { "name": "stunned", "key": "unit.roster.stunned", "default": 0 }
              ],
              "layout": {
                "controls": [
                  { "type": "label", "bind": "displayName" },
                  { "type": "badge", "bind": "stunned", "text": "晕眩", "showWhen": true }
                ]
              }
            }
            """);

            PanelTemplate host = PanelTemplateLoader.Load("""
            {
              "id": "tests.panel.list",
              "graph": "g",
              "pins": [ { "name": "n", "key": "k" } ],
              "collections": [
                { "name": "units", "collectionKey": "tests.roster", "template": "panel.unit.roster" }
              ],
              "layout": {
                "controls": [
                  {
                    "type": "list",
                    "bind": "units",
                    "viewportHeight": 360,
                    "itemExtent": 56,
                    "virtualize": true
                  }
                ]
              }
            }
            """);

            var registry = new PanelTemplateRegistry();
            registry.Register(element);
            registry.Register(host);
            registry.Freeze();
            PanelListProjector.BindElements(host, registry);

            using World world = World.Create();
            Entity owner = world.Create();
            var entities = new Entity[StressCount];
            var values = new GraphOutputValueStore(new StringIntRegistry(64, 1, 0, StringComparer.Ordinal), StressCount * 2);
            for (int i = 0; i < StressCount; i++)
            {
                entities[i] = CreateUnit(world, $"单位{i:0000}");
                values.SetFloat(entities[i], "unit.roster.health", 50f + (i % 50));
                values.SetBool(entities[i], "unit.roster.stunned", i % 17 == 0);
            }

            var keyRegistry = new StringIntRegistry(8, 1, 0, StringComparer.Ordinal);
            var store = new EntityCollectionStore(keyRegistry, 8, StressCount + 8);
            var descriptor = EntityCollectionDescriptor.Create(
                "tests.roster",
                EntityCollectionSourceKind.GasGraphResult,
                EntityCollectionRoleKind.Display);
            store.Replace(owner, descriptor, entities);

            var reader = new PanelProjectionReader(world, values);
            var projector = new PanelListProjector(world, store, reader, graphEvaluator: null);

            // Warmup
            _ = projector.Project(owner, host, new PanelListViewWindow(0, VisibleBudget));
            _ = projector.Project(owner, host, PanelListViewWindow.All);

            long fullAllocBefore = GC.GetAllocatedBytesForCurrentThread();
            var fullSw = Stopwatch.StartNew();
            IReadOnlyList<PanelListProjection> full = projector.Project(owner, host, PanelListViewWindow.All);
            fullSw.Stop();
            long fullAlloc = GC.GetAllocatedBytesForCurrentThread() - fullAllocBefore;

            long windowAllocBefore = GC.GetAllocatedBytesForCurrentThread();
            var windowSw = Stopwatch.StartNew();
            IReadOnlyList<PanelListProjection> windowed = projector.Project(
                owner, host, new PanelListViewWindow(0, VisibleBudget));
            windowSw.Stop();
            long windowAlloc = GC.GetAllocatedBytesForCurrentThread() - windowAllocBefore;

            Assert.That(full[0].TotalCount, Is.EqualTo(StressCount));
            Assert.That(full[0].Items.Count, Is.EqualTo(StressCount));
            Assert.That(windowed[0].TotalCount, Is.EqualTo(StressCount));
            Assert.That(windowed[0].Items.Count, Is.EqualTo(VisibleBudget));

            Assert.That(windowed[0].Items.Count, Is.LessThan(full[0].Items.Count / 10),
                "Virtual window must compose far fewer rows than the full 1000 set.");
            Assert.That(windowAlloc, Is.LessThan(fullAlloc / 5),
                $"Windowed alloc {windowAlloc}B must be << full alloc {fullAlloc}B.");
            Assert.That(windowSw.Elapsed.TotalMilliseconds, Is.LessThan(fullSw.Elapsed.TotalMilliseconds),
                $"Windowed {windowSw.Elapsed.TotalMilliseconds:F2}ms should beat full {fullSw.Elapsed.TotalMilliseconds:F2}ms.");
            Assert.That(windowSw.Elapsed.TotalMilliseconds, Is.LessThan(25.0),
                $"Windowed project of {VisibleBudget}/{StressCount} must stay under 25ms (was {windowSw.Elapsed.TotalMilliseconds:F2}ms).");

            string reportDir = Path.Combine(FindRepoRoot(), "artifacts", "benchmark", "panel_list_virtualization");
            Directory.CreateDirectory(reportDir);
            File.WriteAllText(
                Path.Combine(reportDir, "project-1000.md"),
                $"""
                # Panel list projection 1000 stress

                | Mode | Rows | ms | alloc bytes |
                |---|---:|---:|---:|
                | full | {full[0].Items.Count} | {fullSw.Elapsed.TotalMilliseconds:F3} | {fullAlloc} |
                | window 0..{VisibleBudget} | {windowed[0].Items.Count} | {windowSw.Elapsed.TotalMilliseconds:F3} | {windowAlloc} |
                """);
        }

        [Test]
        public void Template_VirtualizeRequiresViewportHeight()
        {
            const string json = """
            {
              "id": "tests.panel.badvirt",
              "graph": "g",
              "pins": [ { "name": "n", "key": "k" } ],
              "collections": [
                { "name": "units", "collectionKey": "c", "template": "panel.unit.roster" }
              ],
              "layout": {
                "controls": [
                  { "type": "list", "bind": "units", "virtualize": true }
                ]
              }
            }
            """;

            Assert.That(
                () => PanelTemplateLoader.Load(json),
                Throws.InvalidOperationException.With.Message.Contains("viewportHeight"));
        }

        private static Entity CreateUnit(World world, string name)
        {
            Entity entity = world.Create();
            world.Add(entity, new Name { Value = name });
            world.Add(entity, new AttributeBuffer());
            world.Add(entity, new GameplayTagContainer());
            return entity;
        }

        private static string FindRepoRoot()
        {
            string dir = TestContext.CurrentContext.TestDirectory;
            while (!string.IsNullOrEmpty(dir))
            {
                if (File.Exists(Path.Combine(dir, "showcase.registry.json")))
                {
                    return dir;
                }

                dir = Path.GetDirectoryName(dir)!;
            }

            throw new InvalidOperationException("Repo root not found.");
        }
    }
}
