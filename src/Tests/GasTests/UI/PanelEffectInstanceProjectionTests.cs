using System;
using Arch.Core;
using Ludots.Core.EntityCollections;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.Gameplay.Items;
using Ludots.Core.NodeLibraries.GASGraph;
using Ludots.Core.NodeLibraries.GASGraph.Host;
using Ludots.Core.Registry;
using Ludots.Core.TypedCollections;
using Ludots.Core.UI.PanelHosting;
using Ludots.Core.UI.PanelProjection;
using NUnit.Framework;

namespace Ludots.Tests.GasTests.UI
{
    [TestFixture]
    public sealed class PanelEffectInstanceProjectionTests
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
        public void Load_RequiresCollectionSource()
        {
            const string json = """
            {
              "id": "tests.panel.nosource",
              "graph": "g",
              "pins": [ { "name": "n", "key": "k" } ],
              "collections": [
                { "name": "effects", "collectionKey": "c", "template": "panel.effect.chip" }
              ]
            }
            """;

            Assert.That(
                () => PanelTemplateLoader.Load(json),
                Throws.InvalidOperationException.With.Message.Contains("source"));
        }

        [Test]
        public void Load_EffectInstanceSubject_AllowsDisplayNameBind()
        {
            PanelTemplate element = PanelTemplateLoader.Load("""
            {
              "id": "panel.effect.chip",
              "subject": "EffectInstance",
              "graph": "g",
              "pins": [
                { "name": "remaining", "key": "k.r" },
                { "name": "total", "key": "k.t" }
              ],
              "layout": {
                "controls": [
                  { "type": "label", "bind": "displayName" },
                  { "type": "progressBar", "current": "remaining", "max": "total" }
                ]
              }
            }
            """);

            Assert.That(element.Subject, Is.EqualTo(PanelSubjectKind.EffectInstance));
        }

        [Test]
        public void Project_FillsEffectDisplayNameFromTemplateRegistry()
        {
            int blessingId = EffectTemplateIdRegistry.Register("祝福");

            PanelTemplate element = PanelTemplateLoader.Load("""
            {
              "id": "panel.effect.chip",
              "subject": "EffectInstance",
              "graph": "g.chip",
              "pins": [
                { "name": "remaining", "key": "effect.chip.remaining", "default": 0 }
              ],
              "layout": {
                "controls": [ { "type": "label", "bind": "displayName" } ]
              }
            }
            """);

            PanelTemplate host = PanelTemplateLoader.Load("""
            {
              "id": "tests.panel.effects",
              "graph": "g",
              "pins": [ { "name": "n", "key": "k" } ],
              "collections": [
                {
                  "name": "effects",
                  "source": "selfGraph",
                  "collectionKey": "tests.effects",
                  "template": "panel.effect.chip"
                }
              ]
            }
            """);

            var registry = new PanelTemplateRegistry();
            registry.Register(element);
            registry.Register(host);
            registry.Freeze();
            PanelListProjector.BindElements(host, registry);

            using World world = World.Create();
            Entity owner = world.Create();
            Entity effect = GameplayEffectFactory.CreateEffect(
                world,
                source: owner,
                target: owner,
                durationTicks: 100,
                lifetimeKind: EffectLifetimeKind.Infinite);
            world.Get<GameplayEffect>(effect).RemainingTicks = 55;
            world.Get<GameplayEffect>(effect).TotalTicks = 100;
            world.Add(effect, new EffectTemplateRef { TemplateId = blessingId });

            var values = new GraphOutputValueStore(new StringIntRegistry(8, 1, 0, StringComparer.Ordinal), 8);
            values.SetFloat(effect, "effect.chip.remaining", 55f);

            var keyRegistry = new StringIntRegistry(8, 1, 0, StringComparer.Ordinal);
            var store = new EntityCollectionStore(keyRegistry, 8, 16);
            var intIdStore = new IntIdCollectionStore(keyRegistry, 8, 16);
            var descriptor = EntityCollectionDescriptor.Create(
                "tests.effects",
                EntityCollectionSourceKind.GasGraphResult,
                EntityCollectionRoleKind.Display);
            store.Replace(owner, descriptor, new[] { effect });

            var reader = new PanelProjectionReader(world, values);
            var projector = new PanelListProjector(
                world,
                store,
                intIdStore,
                new ItemDefinitionRegistry(),
                reader,
                graphEvaluator: null);
            var lists = projector.Project(owner, host);

            Assert.That(lists[0].Items.Count, Is.EqualTo(1));
            Assert.That(lists[0].Items[0].Strings["displayName"], Is.EqualTo("祝福"));
            Assert.That(lists[0].Items[0].Floats["remaining"], Is.EqualTo(55f));
        }
    }
}
