using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Numerics;
using System.Text.Json;
using Arch.Core;
using Ludots.Core.Components;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.Components;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Orders;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.Input.CommandSources;
using Ludots.Core.Input.Interaction;
using Ludots.Core.Input.Orders;
using Ludots.Core.EntityCollections;
using Ludots.Core.Registry;
using Ludots.Core.Input.Config;
using Ludots.Core.Input.Runtime;
using Ludots.Core.Scripting;
using Ludots.UI;
using Ludots.UI.Skia;
using NUnit.Framework;

namespace Ludots.Tests.GAS.Production
{
    [NonParallelizable]
    [TestFixture]
    [Category("acceptance")]
    public sealed class CityRallyGarrisonAcceptanceTests
    {
        private const float DeltaTime = 1f / 60f;
        private const string MapId = "city_rally_webui";

        private static readonly string[] AcceptanceMods =
        {
            "LudotsCoreMod",
            "CoreInputMod",
            "EntityCommandPanelMod",
            "CityRallyWebUiShowcaseMod",
        };

        [Test]
        public void Peasant_RightClickCity_Garrisons()
        {
            var frameTimesMs = new List<double>();
            using var engine = CreateEngine();
            LoadMap(engine, MapId, frameTimesMs);

            World world = engine.World;
            Entity city = FindEntity(world, "城池");
            Entity peasant = FindEntity(world, "平民 A");

            SubmitSetSpawnTargetEntity(engine, peasant, city);
            TickUntil(
                engine,
                frameTimesMs,
                () => world.Has<ChildOf>(peasant) && world.Get<ChildOf>(peasant).Parent == city,
                maxFrames: 30,
                "平民右键城池后应进驻（ChildOf 城池）。");

            Assert.That(world.Get<ChildrenBuffer>(city).Contains(peasant), Is.True,
                "城池 ChildrenBuffer 应包含进驻的平民。");
        }

        [Test]
        public void Hero_RightClickCity_BecomesGovernor()
        {
            var frameTimesMs = new List<double>();
            using var engine = CreateEngine();
            LoadMap(engine, MapId, frameTimesMs);

            World world = engine.World;
            Entity city = FindEntity(world, "城池");
            Entity hero = FindEntity(world, "英雄");

            SubmitSetSpawnTargetEntity(engine, hero, city);
            TickUntil(
                engine,
                frameTimesMs,
                () => world.Has<GameplayTagContainer>(hero) &&
                      HasTag(engine, hero, "Role.CityRally.Governor"),
                maxFrames: 30,
                "英雄右键城池后就任太守（打 Governor 标签）。");

            Assert.That(world.Has<ChildOf>(hero), Is.True, "就任太守的英雄应进驻城池。");
            Assert.That(HasTag(engine, hero, "Role.CityRally.GovernorCandidate"), Is.False,
                "就任后应移除候选标签。");
        }

        [Test]
        public void Peasant_RightClickGround_LeavesCity()
        {
            var frameTimesMs = new List<double>();
            using var engine = CreateEngine();
            LoadMap(engine, MapId, frameTimesMs);

            World world = engine.World;
            Entity city = FindEntity(world, "城池");
            Entity peasant = FindEntity(world, "平民 A");

            // 先进驻。
            SubmitSetSpawnTargetEntity(engine, peasant, city);
            TickUntil(
                engine,
                frameTimesMs,
                () => world.Has<ChildOf>(peasant),
                maxFrames: 30,
                "平民应先进驻。");

            // 再右键地板出城。
            Vector3 target = new(4000f, 0f, 3000f);
            SubmitSetSpawnTarget(engine, peasant, target);
            int moveToOrderTypeId = RequireOrderTypeId(engine, "moveTo");
            TickUntil(
                engine,
                frameTimesMs,
                () => !world.Has<ChildOf>(peasant) &&
                      world.Has<OrderBuffer>(peasant) &&
                      world.Get<OrderBuffer>(peasant).HasActive &&
                      world.Get<OrderBuffer>(peasant).ActiveOrder.Order.OrderTypeId == moveToOrderTypeId,
                maxFrames: 30,
                "平民右键地板后应出城并收到 moveTo 命令。");
        }

        [Test]
        public void Governor_RightClickGround_BeginsFlagPlanting()
        {
            var frameTimesMs = new List<double>();
            using var engine = CreateEngine();
            LoadMap(engine, MapId, frameTimesMs);

            World world = engine.World;
            Entity city = FindEntity(world, "城池");
            Entity hero = FindEntity(world, "英雄");

            // 就任太守。
            SubmitSetSpawnTargetEntity(engine, hero, city);
            TickUntil(
                engine,
                frameTimesMs,
                () => HasTag(engine, hero, "Role.CityRally.Governor"),
                maxFrames: 30,
                "英雄应就任太守。");

            // 右键地板 → 进入插旗引导（打 Planting 标签 + 记录目标）。
            Vector3 target = new(5000f, 0f, 4000f);
            SubmitSetSpawnTarget(engine, hero, target);
            TickUntil(
                engine,
                frameTimesMs,
                () => HasTag(engine, hero, "Status.CityRally.Planting"),
                maxFrames: 30,
                "太守右键地板后应进入插旗引导（Planting 标签）。");

            Assert.That(world.Has<BlackboardSpatialBuffer>(hero), Is.True);
            Assert.That(world.Get<BlackboardSpatialBuffer>(hero).TryGetPoint(
                OrderBlackboardKeys.Cast_TargetPosition, out Vector3 stored), Is.True);
            Assert.That(stored.X, Is.EqualTo(target.X).Within(1f));
            Assert.That(stored.Z, Is.EqualTo(target.Z).Within(1f));
        }

        [Test]
        public void Governor_SecondRightClick_CompletesFlagAndLeaves()
        {
            var frameTimesMs = new List<double>();
            using var engine = CreateEngine();
            LoadMap(engine, MapId, frameTimesMs);

            World world = engine.World;
            Entity city = FindEntity(world, "城池");
            Entity hero = FindEntity(world, "英雄");

            SubmitSetSpawnTargetEntity(engine, hero, city);
            TickUntil(
                engine,
                frameTimesMs,
                () => HasTag(engine, hero, "Role.CityRally.Governor"),
                maxFrames: 30,
                "英雄应就任太守。");

            Vector3 target = new(5000f, 0f, 4000f);
            SubmitSetSpawnTarget(engine, hero, target);
            TickUntil(
                engine,
                frameTimesMs,
                () => HasTag(engine, hero, "Status.CityRally.Planting"),
                maxFrames: 30,
                "太守应进入插旗引导。");

            // 再次右键同一目标 → 完成立旗并出城。
            SubmitSetSpawnTarget(engine, hero, target);
            TickUntil(
                engine,
                frameTimesMs,
                () => !world.Has<ChildOf>(hero) && !HasTag(engine, hero, "Status.CityRally.Planting"),
                maxFrames: 30,
                "再次右键后应完成插旗并出城。");

            Assert.That(CountEntitiesByName(world, "旗帜"), Is.GreaterThanOrEqualTo(1),
                "立旗完成后应生成旗子实体。");
        }

        [Test]
        public void Peasant_MoveToOrder_ActuallyMoves()
        {
            var frameTimesMs = new List<double>();
            using var engine = CreateEngine();
            LoadMap(engine, MapId, frameTimesMs);

            World world = engine.World;
            Entity peasant = FindEntity(world, "平民 A");
            Vector2 start = ReadWorldPosition(world, peasant);

            Vector3 target = new(3000f, 0f, 2500f);
            var orderQueue = engine.GetService(CoreServiceKeys.OrderQueue) as OrderQueue
                ?? throw new InvalidOperationException("OrderQueue service is missing.");
            bool enqueued = orderQueue.TryEnqueue(new Order
            {
                OrderTypeId = RequireOrderTypeId(engine, "moveTo"),
                PlayerId = 1,
                Actor = peasant,
                Args = new OrderArgs
                {
                    Spatial = new OrderSpatial
                    {
                        Kind = OrderSpatialKind.WorldCm,
                        Mode = OrderCollectionMode.Single,
                        WorldCm = target,
                    },
                },
                SubmitMode = OrderSubmitMode.Immediate,
            });
            Assert.That(enqueued, Is.True, "moveTo order should enqueue.");

            TickUntil(
                engine,
                frameTimesMs,
                () => Vector2.Distance(ReadWorldPosition(world, peasant), start) > 50f,
                maxFrames: 120,
                "平民收到 moveTo 后应开始移动（位置变化 > 50cm）。");

            Vector2 after = ReadWorldPosition(world, peasant);
            Assert.That(Vector2.Distance(after, start), Is.GreaterThan(50f),
                $"平民应从 {start} 开始移动，实际仍在 {after}。");
        }

        [Test]
        public void CustomCommandIntent_RoutesRoleActorsToSpawnTarget()
        {
            string repoRoot = FindRepoRoot();
            string profilePath = Path.Combine(
                repoRoot,
                "mods", "showcases", "city_rally_webui", "CityRallyWebUiShowcaseMod",
                "Assets", "Input", "command_intent_profiles.json");
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var config = System.Text.Json.JsonSerializer.Deserialize<Ludots.Core.Input.Interaction.CommandIntentProfilesConfig>(
                    File.ReadAllText(profilePath), options)
                ?? throw new InvalidOperationException("City rally command intent profile config failed to parse.");

            Ludots.Core.Input.Interaction.CommandIntentProfileDefinition profile = config.Profiles.Single(p =>
                string.Equals(p.Id, "intent.command.default", StringComparison.Ordinal));

            Assert.That(
                profile.Rules.Any(rule =>
                    string.Equals(rule.Actor?.HasAbilityWithTag, "Ability.CityRally.Leave", StringComparison.Ordinal) &&
                    string.Equals(rule.Route?.OrderTypeKey, "setCityRallySpawnTarget", StringComparison.Ordinal)),
                Is.True,
                "带 Leave 能力的平民/太守右键应路由到 setCityRallySpawnTarget。");

            Assert.That(
                profile.Rules.Any(rule =>
                    string.Equals(rule.Actor?.HasAbilityWithTag, "Ability.CityRally.Enter", StringComparison.Ordinal) &&
                    rule.Target?.HasEntity == true &&
                    string.Equals(rule.Route?.OrderTypeKey, "setCityRallySpawnTarget", StringComparison.Ordinal)),
                Is.True,
                "带 Enter 能力的单位右键己方城池应路由到 setCityRallySpawnTarget。");

            Assert.That(
                profile.Rules.Any(rule =>
                    rule.Actor?.HasAbilityWithTag == null &&
                    string.Equals(rule.Route?.OrderTypeKey, "moveTo", StringComparison.Ordinal)),
                Is.True,
                "无角色能力的实体右键仍应 moveTo 兜底。");
        }

        private static void SetGroundCommandTargetFacts(InputOrderMappingSystem system)
        {
            system.SetCommandIntentTargetFactsProvider((InputOrderMapping mapping, out CommandIntentTargetFacts facts) =>
            {
                facts = new CommandIntentTargetFacts(Entity.Null, HasEntity: false);
                return true;
            });
        }

        [Test]
        public void EngineLoadedIntentProfile_RoutesLeaveActorsToSpawnTarget()
        {
            var frameTimesMs = new List<double>();
            using var engine = CreateEngine();
            LoadMap(engine, MapId, frameTimesMs);

            World world = engine.World;
            Entity peasant = FindEntity(world, "平民 A");

            var intents = engine.GetService(CoreServiceKeys.CommandIntentProfileRegistry)
                as CommandIntentProfileRegistry
                ?? throw new InvalidOperationException("CommandIntentProfileRegistry missing.");
            int profileId = intents.ProfileIdRegistry.GetId("intent.command.default");
            Assert.That(profileId, Is.GreaterThan(0), "intent.command.default 应已注册。");

            var groundFacts = new CommandIntentTargetFacts(Entity.Null, HasEntity: false);
            bool routed = intents.TryRoute(profileId, peasant, peasant, in groundFacts, out CommandIntentRoute route);
            Assert.That(routed, Is.True, "平民（有 Leave 能力）右键地板应命中规则。");
            var orderTypes = engine.GetService(CoreServiceKeys.OrderTypeRegistry) as OrderTypeRegistry
                ?? throw new InvalidOperationException("OrderTypeRegistry missing.");
            int setSpawnId = orderTypes.GetId("setCityRallySpawnTarget");
            Assert.That(route.OrderTypeId, Is.EqualTo(setSpawnId),
                $"平民右键应路由到 setCityRallySpawnTarget({setSpawnId})，实际 {route.OrderTypeId}。");
            Assert.That(route.RouteKind, Is.EqualTo(CommandIntentRouteKinds.ByAbilityTag),
                "路由应携带 Leave 能力标签定位槽位。");
        }

        private static bool HasTag(GameEngine engine, Entity entity, string tagName)
        {
            var world = engine.World;
            if (!world.Has<GameplayTagContainer>(entity))
            {
                return false;
            }

            var tagOps = engine.GetService(CoreServiceKeys.TagOps) as TagOps;
            int tagId = TagRegistry.GetId(tagName);
            if (tagOps == null || tagId <= 0)
            {
                return false;
            }

            ref var tags = ref world.Get<GameplayTagContainer>(entity);
            return tagOps.HasTag(ref tags, tagId, TagSense.Effective);
        }

        private static int RequireOrderTypeId(GameEngine engine, string key)
        {
            var orderTypes = engine.GetService(CoreServiceKeys.OrderTypeRegistry) as OrderTypeRegistry
                ?? throw new InvalidOperationException("OrderTypeRegistry service is missing.");
            return orderTypes.GetId(key);
        }

        private static void SubmitSetSpawnTargetEntity(GameEngine engine, Entity actor, Entity target)
        {
            var orderQueue = engine.GetService(CoreServiceKeys.OrderQueue) as OrderQueue
                ?? throw new InvalidOperationException("OrderQueue service is missing.");

            bool enqueued = orderQueue.TryEnqueue(new Order
            {
                OrderTypeId = RequireOrderTypeId(engine, "setCityRallySpawnTarget"),
                PlayerId = 1,
                Actor = actor,
                Target = target,
                SubmitMode = OrderSubmitMode.Immediate,
            });

            Assert.That(enqueued, Is.True, "setCityRallySpawnTarget entity order should enqueue.");
        }

        private static void SubmitSetSpawnTarget(GameEngine engine, Entity actor, Vector3 rallyPoint)
        {
            var orderQueue = engine.GetService(CoreServiceKeys.OrderQueue) as OrderQueue
                ?? throw new InvalidOperationException("OrderQueue service is missing.");

            bool enqueued = orderQueue.TryEnqueue(new Order
            {
                OrderTypeId = RequireOrderTypeId(engine, "setCityRallySpawnTarget"),
                PlayerId = 1,
                Actor = actor,
                Args = new OrderArgs
                {
                    Spatial = new OrderSpatial
                    {
                        Kind = OrderSpatialKind.WorldCm,
                        Mode = OrderCollectionMode.Single,
                        WorldCm = rallyPoint,
                    },
                },
                SubmitMode = OrderSubmitMode.Immediate,
            });

            Assert.That(enqueued, Is.True, "setCityRallySpawnTarget order should enqueue.");
        }

        private static GameEngine CreateEngine()
        {
            string repoRoot = FindRepoRoot();
            string assetsRoot = Path.Combine(repoRoot, "assets");
            var modPaths = RepoModPaths.ResolveExplicit(repoRoot, AcceptanceMods);

            var engine = new GameEngine();
            engine.InitializeWithConfigPipeline(modPaths, assetsRoot);
            InstallDummyInput(engine);
            var uiRoot = new UIRoot(new SkiaUiRenderer());
            uiRoot.Resize(1920f, 1080f);
            engine.SetService(CoreServiceKeys.UIRoot, uiRoot);
            engine.SetService(CoreServiceKeys.UiTextMeasurer, new SkiaTextMeasurer());
            engine.SetService(CoreServiceKeys.UiImageSizeProvider, new SkiaImageSizeProvider());
            engine.Start();
            return engine;
        }

        private static void LoadMap(GameEngine engine, string mapId, List<double> frameTimesMs)
        {
            engine.LoadMap(mapId);
            Tick(engine, 5, frameTimesMs);
            Assert.That(engine.TriggerManager.Errors.Count, Is.EqualTo(0));
        }

        private static void Tick(GameEngine engine, int frames, List<double> frameTimesMs)
        {
            var stepPolicy = engine.GetService(CoreServiceKeys.GasClockStepPolicy);
            for (int i = 0; i < frames; i++)
            {
                if (stepPolicy.Mode == GasStepMode.Manual)
                {
                    stepPolicy.RequestStep(1);
                }

                var stopwatch = Stopwatch.StartNew();
                engine.Tick(DeltaTime);
                stopwatch.Stop();
                frameTimesMs.Add(stopwatch.Elapsed.TotalMilliseconds);
            }
        }

        private static void TickUntil(
            GameEngine engine,
            List<double> frameTimesMs,
            Func<bool> condition,
            int maxFrames,
            string because)
        {
            for (int i = 0; i < maxFrames; i++)
            {
                if (condition())
                {
                    return;
                }

                Tick(engine, 1, frameTimesMs);
            }

            Assert.Fail(because);
        }

        private static Vector2 ReadWorldPosition(World world, Entity entity)
        {
            ref readonly var position = ref world.Get<WorldPositionCm>(entity);
            return new Vector2(position.Value.X.ToFloat(), position.Value.Y.ToFloat());
        }

        private static Entity FindEntity(World world, string entityName)
        {
            Entity result = Entity.Null;
            var query = new QueryDescription().WithAll<Name>();
            world.Query(in query, (Entity entity, ref Name name) =>
            {
                if (result == Entity.Null && string.Equals(name.Value, entityName, StringComparison.OrdinalIgnoreCase))
                {
                    result = entity;
                }
            });

            if (result == Entity.Null)
            {
                throw new InvalidOperationException($"Missing entity '{entityName}'.");
            }

            return result;
        }

        private static int CountEntitiesByName(World world, string entityName)
        {
            int count = 0;
            var query = new QueryDescription().WithAll<Name>();
            world.Query(in query, (Entity _, ref Name name) =>
            {
                if (string.Equals(name.Value, entityName, StringComparison.OrdinalIgnoreCase))
                {
                    count++;
                }
            });
            return count;
        }

        private static void InstallDummyInput(GameEngine engine)
        {
            var inputConfig = new InputConfigPipelineLoader(engine.ConfigPipeline).Load();
            var inputHandler = new PlayerInputHandler(new NullInputBackend(), inputConfig);
            engine.SetService(CoreServiceKeys.InputHandler, inputHandler);
            engine.SetService(CoreServiceKeys.UiCaptured, false);
        }

        private sealed class NullInputBackend : IInputBackend
        {
            public float GetAxis(string devicePath) => 0f;
            public bool GetButton(string devicePath) => false;
            public Vector2 GetMousePosition() => Vector2.Zero;
            public float GetMouseWheel() => 0f;
            public void EnableIME(bool enable) { }
            public void SetIMECandidatePosition(int x, int y) { }
            public string GetCharBuffer() => string.Empty;
        }

        private static string FindRepoRoot()
        {
            string dir = TestContext.CurrentContext.TestDirectory;
            while (!string.IsNullOrWhiteSpace(dir))
            {
                if (File.Exists(Path.Combine(dir, "src", "Core", "Ludots.Core.csproj")))
                {
                    return dir;
                }

                dir = Path.GetDirectoryName(dir);
            }

            throw new DirectoryNotFoundException("Could not locate repository root.");
        }
    }
}
