using System;
using Arch.Core;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.NodeLibraries.GASGraph;
using Ludots.Core.NodeLibraries.GASGraph.Host;
using Ludots.Core.Registry;
using NUnit.Framework;

namespace Ludots.Tests.GasTests.Graph
{
    [TestFixture]
    public sealed class QueryCollectTypedBagsTests
    {
        [SetUp]
        public void SetUp()
        {
            EffectTemplateIdRegistry.Clear();
            AbilityIdRegistry.Clear();
            TagRegistry.Clear();
        }

        [Test]
        public void CollectEffectTemplateIds_ReturnsRegisteredIdsSorted()
        {
            int blessing = EffectTemplateIdRegistry.Register("祝福");
            int swift = EffectTemplateIdRegistry.Register("迅捷");
            var api = new GasGraphRuntimeApi(World.Create());

            Span<int> buffer = stackalloc int[8];
            int count = api.CollectEffectTemplateIds(buffer);

            Assert.That(count, Is.EqualTo(2));
            Assert.That(buffer[0], Is.EqualTo(Math.Min(blessing, swift)));
            Assert.That(buffer[1], Is.EqualTo(Math.Max(blessing, swift)));
        }

        [Test]
        public void CollectAbilitySlots_ListsResolvedSlotsInOrder()
        {
            using World world = World.Create();
            int fireball = AbilityIdRegistry.Register("火球");
            Entity hero = world.Create();
            var buffer = new AbilityStateBuffer();
            buffer.AddAbility(fireball);
            buffer.AddAbility(fireball);
            world.Add(hero, buffer);

            var api = new GasGraphRuntimeApi(world);
            Span<int> slots = stackalloc int[8];
            int count = api.CollectAbilitySlots(hero, slots);

            Assert.That(count, Is.EqualTo(2));
            Assert.That(slots[0], Is.EqualTo(0));
            Assert.That(slots[1], Is.EqualTo(1));
        }

        [Test]
        public void CollectPresentTags_ReadsTagCountContainer()
        {
            using World world = World.Create();
            int buff = TagRegistry.Register("状态.增益");
            Entity hero = world.Create();
            var counts = new TagCountContainer();
            Assert.That(counts.AddCount(buff, 1), Is.True);
            world.Add(hero, counts);

            var api = new GasGraphRuntimeApi(world);
            Span<int> tags = stackalloc int[8];
            int count = api.CollectPresentTags(hero, tags);

            Assert.That(count, Is.EqualTo(1));
            Assert.That(tags[0], Is.EqualTo(buff));
        }

        [Test]
        public void CollectAbilityHolders_FiltersCandidatesByAbility()
        {
            using World world = World.Create();
            int fireball = AbilityIdRegistry.Register("火球");
            Entity holder = world.Create();
            Entity other = world.Create();
            var holderSlots = new AbilityStateBuffer();
            holderSlots.AddAbility(fireball);
            world.Add(holder, holderSlots);
            world.Add(other, new AbilityStateBuffer());

            var api = new GasGraphRuntimeApi(world);
            Span<Entity> candidates = stackalloc Entity[2];
            candidates[0] = other;
            candidates[1] = holder;
            Span<Entity> outBuffer = stackalloc Entity[2];
            int count = api.CollectAbilityHolders(fireball, candidates, outBuffer);

            Assert.That(count, Is.EqualTo(1));
            Assert.That(outBuffer[0], Is.EqualTo(holder));
        }
    }
}
