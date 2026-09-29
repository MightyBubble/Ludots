using System;
using System.IO;
using Arch.Core;
using Ludots.Core.Components;
using Ludots.Core.Gameplay.Components;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.Gameplay.Relationships;
using Ludots.Core.Mathematics.FixedPoint;
using Ludots.Core.NodeLibraries.GASGraph.Host;
using Ludots.Core.Scripting;
using NUnit.Framework;

namespace Ludots.Tests.GAS
{
    [TestFixture]
    public sealed class SupplyCircleAcceptanceTests
    {
        private const int HomeX = 200000;
        private const int HomeY = 200000;
        private const int NearCityY = 203000;
        private const int FarCityY = 207000;
        private const int DecisionY = 204500;
        private const int InsideCloseY = 200200;
        private const int InsideFarOnlyY = 200600;
        private const int BesideFarCityY = 206900;
        private const uint CityLayerCategory = 65536;

        [Test]
        public void SupplyCircles_CaptureEscapeDrawAndLeave()
        {
            string repoRoot = FindRepoRoot();
            using var engine = new Ludots.Core.Engine.GameEngine();
            engine.InitializeWithConfigPipeline(
                RepoModPaths.ResolveExplicit(
                    repoRoot,
                    new[] { "LudotsCoreMod", "SupplyCircleAcceptanceMod" }),
                Path.Combine(repoRoot, "assets"));

            int resolveId = EffectTemplateIdRegistry.GetId("Effect.Supply.Resolve");
            int commitId = EffectTemplateIdRegistry.GetId("Effect.Supply.Commit");
            int coverWeiId = EffectTemplateIdRegistry.GetId("Effect.Supply.Cover.Wei");
            int coverShuId = EffectTemplateIdRegistry.GetId("Effect.Supply.Cover.Shu");
            int pulseCloseId = EffectTemplateIdRegistry.GetId("Effect.Supply.Pulse.Close.Wei");
            int pulseFarId = EffectTemplateIdRegistry.GetId("Effect.Supply.Pulse.Far.Shu");
            Assert.That(resolveId, Is.GreaterThan(0));
            Assert.That(commitId, Is.GreaterThan(0));
            Assert.That(coverWeiId, Is.GreaterThan(0));
            Assert.That(coverShuId, Is.GreaterThan(0));
            Assert.That(pulseCloseId, Is.GreaterThan(0));
            Assert.That(pulseFarId, Is.GreaterThan(0));
            Assert.That(GraphIdRegistry.GetId("Graph.Supply.Pick"), Is.GreaterThan(0));
            Assert.That(GraphIdRegistry.GetId("Graph.Supply.Commit"), Is.GreaterThan(0));

            int isPersonId = AttributeRegistry.GetId("Supply.IsPerson");
            int factionId = AttributeRegistry.GetId("Supply.Faction");
            int weiId = AttributeRegistry.GetId("Supply.Cover.Wei");
            int shuId = AttributeRegistry.GetId("Supply.Cover.Shu");
            int wuId = AttributeRegistry.GetId("Supply.Cover.Wu");
            int pickedKey = ConfigKeyRegistry.GetId("Supply.Picked");
            int cityKey = ConfigKeyRegistry.GetId("Supply.City");
            Assert.That(isPersonId, Is.GreaterThan(0));
            Assert.That(factionId, Is.GreaterThan(0));
            Assert.That(weiId, Is.GreaterThan(0));
            Assert.That(shuId, Is.GreaterThan(0));
            Assert.That(wuId, Is.GreaterThan(0));
            Assert.That(pickedKey, Is.Not.EqualTo(ConfigKeyRegistry.InvalidId));
            Assert.That(cityKey, Is.Not.EqualTo(ConfigKeyRegistry.InvalidId));

            RelationshipRuntime relationships = engine.GetService(CoreServiceKeys.RelationshipRuntime)
                ?? throw new InvalidOperationException("RelationshipRuntime service is missing.");
            int holdId = relationships.TypeRegistry.GetId("SupplyHold");
            Assert.That(holdId, Is.GreaterThanOrEqualTo(0));

            EffectRequestQueue requests = engine.GetService(CoreServiceKeys.EffectRequestQueue)
                ?? throw new InvalidOperationException("EffectRequestQueue service is missing.");

            Entity marker = engine.World.Create();
            Entity nearCity = CreateCity(engine, HomeX, NearCityY);
            Entity farCity = CreateCity(engine, HomeX, FarCityY);
            Entity tower = CreateTower(engine, HomeX, HomeY);
            Entity idle = CreatePerson(engine, HomeX, DecisionY, faction: 1f, isPersonId, factionId, weiId, shuId, wuId, cityKey, marker);
            Entity enemy = CreatePerson(engine, HomeX, DecisionY + 40, faction: 2f, isPersonId, factionId, weiId, shuId, wuId, cityKey, marker);
            Entity drawer = CreatePerson(engine, HomeX, DecisionY + 80, faction: 3f, isPersonId, factionId, weiId, shuId, wuId, cityKey, marker);
            Entity insideClose = CreatePerson(engine, HomeX, InsideCloseY, faction: 2f, isPersonId, factionId, weiId, shuId, wuId, cityKey, marker);
            Entity insideFarOnly = CreatePerson(engine, HomeX, InsideFarOnlyY, faction: 1f, isPersonId, factionId, weiId, shuId, wuId, cityKey, marker);

            engine.Start();
            Tick(engine, 3);

            int root = 1;
            requests.Publish(new EffectRequest { RootId = root++, Source = idle, Target = idle, TemplateId = resolveId });
            Tick(engine, 4);
            AssertOutcome(engine, relationships, idle, pickedKey, cityKey, weiId, holdId, nearCity, farCity, 0, false, "野外没有圈，逃向最近的城");

            requests.Publish(new EffectRequest { RootId = root++, Source = tower, Target = idle, TemplateId = coverWeiId });
            Tick(engine, 4);
            AssertOutcome(engine, relationships, idle, pickedKey, cityKey, weiId, holdId, nearCity, farCity, 1, false, "只站在己方圈里，逃向最近的城");
            Assert.That(engine.World.Get<AttributeBuffer>(idle).GetCurrent(weiId), Is.EqualTo(1f).Within(0.001f));

            requests.Publish(new EffectRequest { RootId = root++, Source = tower, Target = enemy, TemplateId = coverWeiId });
            Tick(engine, 4);
            AssertOutcome(engine, relationships, enemy, pickedKey, cityKey, weiId, holdId, nearCity, farCity, 1, true, "只站在敌方圈里，被俘并去最近的城");

            requests.Publish(new EffectRequest { RootId = root++, Source = tower, Target = enemy, TemplateId = coverWeiId });
            Tick(engine, 2);
            Assert.That(engine.World.Get<AttributeBuffer>(enemy).GetCurrent(weiId), Is.EqualTo(1f).Within(0.001f), "同一方再罩一次，覆盖仍是有或无");

            engine.World.Get<AttributeBuffer>(drawer).SetBase(weiId, 1f);
            engine.World.Get<AttributeBuffer>(drawer).SetBase(shuId, 1f);
            engine.World.Get<AttributeBuffer>(drawer).SetBase(wuId, 0f);
            int weiWins = 0;
            int shuWins = 0;
            for (int trial = 0; trial < 36; trial++)
            {
                requests.Publish(new EffectRequest { RootId = root++, Source = drawer, Target = drawer, TemplateId = resolveId });
                Tick(engine, 4);
                int picked = ReadPicked(engine, drawer, pickedKey);
                if (picked == 1) weiWins++;
                else if (picked == 2) shuWins++;
                else Assert.Fail($"两方重叠抽到了圈外的势力 {picked}");
                AssertHeld(engine, relationships, drawer, nearCity, farCity, holdId, held: true);
            }

            TestContext.Out.WriteLine($"[supply] 魏蜀重叠 36 次: 魏 {weiWins}, 蜀 {shuWins}");
            Assert.That(weiWins, Is.GreaterThanOrEqualTo(8), "两方重叠时魏应占到一截");
            Assert.That(shuWins, Is.GreaterThanOrEqualTo(8), "两方重叠时蜀应占到一截");

            engine.World.Get<AttributeBuffer>(drawer).SetBase(factionId, 1f);
            engine.World.Get<AttributeBuffer>(drawer).SetBase(weiId, 1f);
            engine.World.Get<AttributeBuffer>(drawer).SetBase(shuId, 1f);
            engine.World.Get<AttributeBuffer>(drawer).SetBase(wuId, 1f);
            int[] wins = new int[4];
            for (int trial = 0; trial < 36; trial++)
            {
                requests.Publish(new EffectRequest { RootId = root++, Source = drawer, Target = drawer, TemplateId = resolveId });
                Tick(engine, 4);
                int picked = ReadPicked(engine, drawer, pickedKey);
                Assert.That(picked, Is.InRange(1, 3), "三方重叠只在三个圈里抽");
                wins[picked]++;
                bool held = picked != 1;
                AssertHeld(engine, relationships, drawer, nearCity, farCity, holdId, held);
                Assert.That(ReadCity(engine, drawer, cityKey), Is.EqualTo(nearCity));
            }

            TestContext.Out.WriteLine($"[supply] 三方重叠 36 次: 魏 {wins[1]}, 蜀 {wins[2]}, 吴 {wins[3]}");
            Assert.That(wins[1], Is.GreaterThanOrEqualTo(5));
            Assert.That(wins[2], Is.GreaterThanOrEqualTo(5));
            Assert.That(wins[3], Is.GreaterThanOrEqualTo(5));

            requests.Publish(new EffectRequest { RootId = root++, Source = tower, Target = tower, TemplateId = pulseCloseId });
            requests.Publish(new EffectRequest { RootId = root++, Source = tower, Target = tower, TemplateId = pulseFarId });
            Tick(engine, 16);
            Assert.That(engine.World.Get<AttributeBuffer>(insideFarOnly).GetCurrent(weiId), Is.EqualTo(0f).Within(0.001f), "人在近圈外面，魏的圈罩不到");
            Assert.That(engine.World.Get<AttributeBuffer>(insideFarOnly).GetCurrent(shuId), Is.EqualTo(1f).Within(0.001f), "人在远圈里面，蜀的圈罩得到");
            Assert.That(engine.World.Get<AttributeBuffer>(insideClose).GetCurrent(weiId), Is.EqualTo(1f).Within(0.001f), "人贴着塔，近圈罩得到");
            Assert.That(engine.World.Get<AttributeBuffer>(insideClose).GetCurrent(shuId), Is.EqualTo(1f).Within(0.001f), "人贴着塔，远圈也罩得到");
            AssertOutcome(engine, relationships, insideFarOnly, pickedKey, cityKey, shuId, holdId, nearCity, farCity, 2, true, "只被更大的敌方圈罩住，被俘去最近的城");

            int closePicked = ReadPicked(engine, insideClose, pickedKey);
            TestContext.Out.WriteLine($"[supply] 贴着塔，两个圈重叠，抽到 {closePicked}");
            Assert.That(closePicked, Is.EqualTo(1).Or.EqualTo(2));
            Assert.That(ReadCity(engine, insideClose, cityKey), Is.EqualTo(nearCity));
            AssertHeld(engine, relationships, insideClose, nearCity, farCity, holdId, held: closePicked != 2);
            ref WorldPositionCm moved = ref engine.World.Get<WorldPositionCm>(insideClose);
            moved = WorldPositionCm.FromCm(HomeX, BesideFarCityY);
            engine.World.Get<PreviousWorldPositionCm>(insideClose) = new PreviousWorldPositionCm
            {
                Value = Fix64Vec2.FromInt(HomeX, BesideFarCityY),
            };
            Tick(engine, 20);
            Assert.That(engine.World.Get<AttributeBuffer>(insideClose).GetCurrent(weiId), Is.EqualTo(0f).Within(0.001f), "走出圈后覆盖消失");
            Assert.That(engine.World.Get<AttributeBuffer>(insideClose).GetCurrent(shuId), Is.EqualTo(0f).Within(0.001f), "走出两个圈后都不再覆盖");
            AssertOutcome(engine, relationships, insideClose, pickedKey, cityKey, weiId, holdId, farCity, nearCity, 0, false, "离开补给圈，逃向现在最近的城");
        }

        private static void AssertOutcome(
            Ludots.Core.Engine.GameEngine engine,
            RelationshipRuntime relationships,
            Entity person,
            int pickedKey,
            int cityKey,
            int coverId,
            int holdId,
            Entity expectedCity,
            Entity otherCity,
            int expectedPicked,
            bool held,
            string step)
        {
            int picked = ReadPicked(engine, person, pickedKey);
            Entity city = ReadCity(engine, person, cityKey);
            float cover = engine.World.Get<AttributeBuffer>(person).GetCurrent(coverId);
            TestContext.Out.WriteLine($"[supply] {step}: picked={picked}, city={city.Id}, cover={cover}, held={relationships.HasLink(person, expectedCity, holdId)}");
            Assert.That(picked, Is.EqualTo(expectedPicked), step);
            Assert.That(city, Is.EqualTo(expectedCity), step);
            AssertHeld(engine, relationships, person, expectedCity, otherCity, holdId, held, step);
        }

        private static void AssertHeld(
            Ludots.Core.Engine.GameEngine engine,
            RelationshipRuntime relationships,
            Entity person,
            Entity expectedCity,
            Entity otherCity,
            int holdId,
            bool held,
            string step = "")
        {
            Assert.That(relationships.HasLink(person, expectedCity, holdId), Is.EqualTo(held), step);
            Assert.That(relationships.HasLink(person, otherCity, holdId), Is.False, step);
        }

        private static int ReadPicked(Ludots.Core.Engine.GameEngine engine, Entity person, int pickedKey)
        {
            bool found = engine.World.Get<BlackboardIntBuffer>(person).TryGet(pickedKey, out int picked);
            Assert.That(found, Is.True);
            return picked;
        }

        private static Entity ReadCity(Ludots.Core.Engine.GameEngine engine, Entity person, int cityKey)
        {
            bool found = engine.World.Get<BlackboardEntityBuffer>(person).TryGet(cityKey, out Entity city);
            Assert.That(found, Is.True);
            return city;
        }

        private static Entity CreatePerson(
            Ludots.Core.Engine.GameEngine engine,
            int x,
            int y,
            float faction,
            int isPersonId,
            int factionId,
            int weiId,
            int shuId,
            int wuId,
            int cityKey,
            Entity marker)
        {
            var attributes = new AttributeBuffer();
            attributes.SetBase(isPersonId, 1f);
            attributes.SetBase(factionId, faction);
            attributes.SetBase(weiId, 0f);
            attributes.SetBase(shuId, 0f);
            attributes.SetBase(wuId, 0f);
            Entity person = engine.World.Create(
                attributes,
                new ActiveEffectContainer(),
                new DirtyFlags(),
                new BlackboardEntityBuffer(),
                new BlackboardIntBuffer(),
                WorldPositionCm.FromCm(x, y),
                new PreviousWorldPositionCm { Value = Fix64Vec2.FromInt(x, y) });
            engine.World.Get<BlackboardEntityBuffer>(person).Set(cityKey, marker);
            return person;
        }

        private static Entity CreateCity(Ludots.Core.Engine.GameEngine engine, int x, int y)
        {
            return engine.World.Create(
                WorldPositionCm.FromCm(x, y),
                new PreviousWorldPositionCm { Value = Fix64Vec2.FromInt(x, y) },
                new EntityLayer(CityLayerCategory, CityLayerCategory));
        }

        private static Entity CreateTower(Ludots.Core.Engine.GameEngine engine, int x, int y)
        {
            return engine.World.Create(
                new ActiveEffectContainer(),
                new DirtyFlags(),
                WorldPositionCm.FromCm(x, y),
                new PreviousWorldPositionCm { Value = Fix64Vec2.FromInt(x, y) });
        }

        private static void Tick(Ludots.Core.Engine.GameEngine engine, int frames)
        {
            for (int frame = 0; frame < frames; frame++)
            {
                engine.Tick(1f / 60f);
            }
        }

        private static string FindRepoRoot()
        {
            var directory = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
            while (directory != null)
            {
                if (Directory.Exists(Path.Combine(directory.FullName, "assets")) &&
                    Directory.Exists(Path.Combine(directory.FullName, "mods")))
                {
                    return directory.FullName;
                }

                directory = directory.Parent;
            }

            throw new DirectoryNotFoundException("Could not locate repository root.");
        }
    }
}
