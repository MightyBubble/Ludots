using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using Arch.Core;
using CapabilityStandardCrowdPhysicsArenaMod;
using CapabilityStandardCrowdPhysicsArenaMod.Runtime;
using CapabilityStandardCrowdPhysicsArenaMod.Systems;
using Ludots.Core.Components;
using Ludots.Core.Engine;
using Ludots.Core.EntityCollections;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.Gameplay.Relationships;
using Ludots.Core.Gameplay.Spawning;
using Ludots.Core.Input.Config;
using Ludots.Core.Input.Orders;
using Ludots.Core.Input.Runtime;
using Ludots.Core.MassNavigation;
using Ludots.Core.MassNavigation.Runtime;
using Ludots.Core.Mathematics;
using Ludots.Core.Movement;
using Ludots.Core.Movement.Physics2DBridge;
using Ludots.Core.Physics2D;
using Ludots.Core.Physics2D.Components;
using Ludots.Core.Presentation.Config;
using Ludots.Core.Presentation.Minimap;
using Ludots.Core.Presentation.Performers;
using Ludots.Core.Scripting;
using Ludots.Platform.Abstractions;
using NUnit.Framework;

namespace Ludots.Tests.Presentation
{
    [TestFixture]
    public sealed class CapabilityStandardCrowdPhysicsArenaProductionPathTests
    {
        private const float FixedDeltaSeconds = 1f / 60f;
        private const int MaxWarmupFrames = 240;
        private const int SettleFrames = 90;
        private const int MarchBudgetFrames = 3600;
        private const float ArrivalToleranceCm = 800f;
        private const int ExpectedSquadSize = 48;
        private const string MouseRightButtonPath = "<Mouse>/RightButton";
        private const string SkillQKeyPath = "<Keyboard>/q";
        private const string SkillEKeyPath = "<Keyboard>/e";
        private const string CrateName = "CrowdPhysicsArena.Crate";
        private const string BoulderName = "CrowdPhysicsArena.Boulder";

        private static readonly QueryDescription DisplacementQuery = new QueryDescription().WithAll<DisplacementState>();

        private static readonly string[] ShowcaseMods =
        {
            "LudotsCoreMod",
            "CoreInputMod",
            "MassNavigationMod",
            "CapabilityStandardCrowdPhysicsArenaMod"
        };

        [SetUp]
        public void SetUp()
        {
            AttributeRegistry.Clear();
            TagRegistry.Clear();
            PerformerScopeTagRegistry.Clear();
        }

        [TearDown]
        public void TearDown()
        {
            AttributeRegistry.Clear();
            TagRegistry.Clear();
            PerformerScopeTagRegistry.Clear();
        }

        [Test]
        public void Showcase_BootsArenaWithKinematicSquadsDrivenByBridge()
        {
            GC.KeepAlive(typeof(CapabilityStandardCrowdPhysicsArenaModEntry).Assembly);

            using var engine = CreateEngine();
            StartStartupMap(engine);

            MassNavigationSimulationRuntime simulation = RequireMassNavigationSimulation(engine);
            int expectedAgents = checked(simulation.Config.Scenario.Teams.Length * simulation.Config.Scenario.AgentsPerTeam);
            Assert.That(expectedAgents, Is.EqualTo(96));

            WaitForScenarioAgents(engine, simulation, expectedAgents);

            // Every squad agent must be a kinematic physics participant driven by the massnav bridge.
            var feedSystem = RequireService(engine, MovementPhysics2DBridgeKeys.KinematicPoseFeedSystem);
            TickFrames(engine, 2);
            Assert.That(feedSystem.LastFedParticipantCount, Is.EqualTo(expectedAgents),
                "All arena squad agents must be fed into the kinematic pose buffer every fixed step.");

            int kinematicAgents = 0;
            var agentQuery = new QueryDescription()
                .WithAll<MassNavigationAgentIndex, MovementParticipation, Mass2D, Position2D, WorldPositionCm>();
            engine.World.Query(in agentQuery, (
                Entity entity,
                ref MassNavigationAgentIndex agentIndex,
                ref MovementParticipation participation,
                ref Mass2D mass,
                ref Position2D position,
                ref WorldPositionCm worldPosition) =>
            {
                Assert.That(participation.PhysicsPresence, Is.EqualTo(PhysicsPresenceKind.Kinematic));
                Assert.That(mass.IsKinematic, Is.True);
                Vector2 bodyCm = new((float)position.Value.X, (float)position.Value.Y);
                Vector2 committedCm = worldPosition.Value.ToVector2();
                Assert.That(Vector2.Distance(bodyCm, committedCm), Is.LessThanOrEqualTo(1f),
                    $"Agent {agentIndex.Value}: kinematic body must mirror the committed WorldPositionCm.");
                kinematicAgents++;
            });
            Assert.That(kinematicAgents, Is.EqualTo(expectedAgents));
        }

        [Test]
        public void Showcase_MarchThroughCrates_CratesDisplaceAndSquadArrives()
        {
            GC.KeepAlive(typeof(CapabilityStandardCrowdPhysicsArenaModEntry).Assembly);

            using var engine = CreateEngine();
            StartStartupMap(engine);
            MassNavigationSimulationRuntime simulation = RequireMassNavigationSimulation(engine);
            WaitForScenarioAgents(engine, simulation, 96);
            ParkAllScenarioAgents(engine);

            List<Entity> squad = CollectAgents(engine, controllable: true);
            Assert.That(squad, Has.Count.EqualTo(ExpectedSquadSize));

            List<(Entity Entity, Vector2 PositionCm)> cratesBefore = CaptureNamedBodies(engine, CrateName);
            Assert.That(cratesBefore, Has.Count.EqualTo(6), "The arena map authors six crates in the corridor.");

            var backend = RequireBackend(engine);
            Entity localPlayer = RequireService(engine, CoreServiceKeys.LocalPlayerEntity);
            ReplaceCommandSource(engine, localPlayer, squad.ToArray());

            // The crate corridor sits at x 4250..4510; marching from the west spawn (~2400) to just
            // past it forces the squad through the dynamic crate stack.
            Vector2 target = DriveRightClickCommand(engine, backend, new Vector2(4900f, 5000f));

            WaitUntil(
                engine,
                MarchBudgetFrames,
                () => CountAgentsWithin(engine, squad, target, ArrivalToleranceCm) == squad.Count,
                () => $"Only {CountAgentsWithin(engine, squad, target, ArrivalToleranceCm)} of {squad.Count} agents " +
                    $"arrived within {ArrivalToleranceCm}cm of {target}. " +
                    $"lastOrderMemberCount={simulation.LastOrderMemberCount}, firstAgent={ReadAgentPositionCm(engine, squad[0])}, " +
                    $"centroid={ComputeCentroid(engine, squad)}, " +
                    $"lastOrder={ReadDebugGlobal(engine, "CoreInputMod.Debug.LastOrder")}, " +
                    $"lastGround={ReadDebugGlobal(engine, "CoreInputMod.Debug.LastGroundWorldCm")}, " +
                    $"lastActivation={DescribeLastActivation(engine)}");

            float maxCrateDisplacementCm = 0f;
            foreach ((Entity crate, Vector2 before) in cratesBefore)
            {
                Vector2 after = ReadBodyPositionCm(engine, crate);
                maxCrateDisplacementCm = MathF.Max(maxCrateDisplacementCm, Vector2.Distance(before, after));
            }

            Assert.That(maxCrateDisplacementCm, Is.GreaterThan(5f),
                "Marching through the corridor must physically displace at least one dynamic crate.");
        }

        [Test]
        public void Showcase_PressurePlate_CountsAgentBeginsAndOpensDoorAtThreshold()
        {
            GC.KeepAlive(typeof(CapabilityStandardCrowdPhysicsArenaModEntry).Assembly);

            using var engine = CreateEngine();
            StartStartupMap(engine);
            MassNavigationSimulationRuntime simulation = RequireMassNavigationSimulation(engine);
            WaitForScenarioAgents(engine, simulation, 96);
            ParkAllScenarioAgents(engine);

            CrowdPhysicsArenaPressurePlateDoorSystem plate =
                RequireService(engine, CapabilityStandardCrowdPhysicsArenaModEntry.PressurePlateDoorSystemKey);
            Assert.That(plate.AgentContactBeginCount, Is.Zero, "No agent may touch the plate before any orders.");
            Assert.That(plate.OpenedDoorCount, Is.Zero);

            List<Entity> squad = CollectAgents(engine, controllable: true);
            Assert.That(squad, Has.Count.EqualTo(ExpectedSquadSize));
            var backend = RequireBackend(engine);
            Entity localPlayer = RequireService(engine, CoreServiceKeys.LocalPlayerEntity);

            // Phase 1: send exactly four units straight across the plate (plate box: x 5560..5640,
            // y 4580..5420), each on its own lane with its own arrival point so the runners never
            // jostle each other back onto the plate. Expect exactly four Begin events and, once
            // everyone left the plate, Begin == End with the door still closed (4 < threshold 20).
            List<Entity> fourAcross = PickAgentsNearestToY(engine, squad, 5000f, 4);
            fourAcross.Sort((a, b) => ReadAgentPositionCm(engine, a).Y.CompareTo(ReadAgentPositionCm(engine, b).Y));
            float[] laneYs = { 4900f, 4970f, 5030f, 5100f };
            var laneTargets = new Vector2[fourAcross.Count];
            for (int i = 0; i < fourAcross.Count; i++)
            {
                ReplaceCommandSource(engine, localPlayer, new[] { fourAcross[i] });
                laneTargets[i] = DriveRightClickCommand(engine, backend, new Vector2(6000f, laneYs[i]));
            }

            WaitUntil(
                engine,
                MarchBudgetFrames,
                () => CountArrivedLaneRunners(engine, fourAcross, laneTargets, 300f) == fourAcross.Count,
                () => $"Only {CountArrivedLaneRunners(engine, fourAcross, laneTargets, 300f)} of {fourAcross.Count} plate " +
                    $"runners arrived at their lanes. plateBegin={plate.AgentContactBeginCount}, plateEnd={plate.AgentContactEndCount}");

            // Runners keep creeping toward their exact lane targets after entering the arrival
            // tolerance; wait until the last one has fully stepped off the plate.
            WaitUntil(
                engine,
                600,
                () => plate.AgentContactEndCount == plate.AgentContactBeginCount,
                () => $"Plate contacts did not reconcile after the runners left. " +
                    $"plateBegin={plate.AgentContactBeginCount}, plateEnd={plate.AgentContactEndCount}");

            Assert.That(plate.AgentContactBeginCount, Is.EqualTo(fourAcross.Count),
                "Each crossing agent must produce exactly one ContactBegin.");
            Assert.That(plate.AgentContactEndCount, Is.EqualTo(plate.AgentContactBeginCount),
                "After all runners left the plate, Begin and End counts must reconcile.");
            Assert.That(plate.OpenedDoorCount, Is.Zero, "Four crossings are below the authored threshold of 20.");
            Assert.That(ReadDoorOpenThreshold(engine), Is.EqualTo(20));

            // Phase 2: march the whole squad across the plate. Begins are deduplicated per
            // agent ("N units cross -> exactly N Begins"), so one full-squad crossing counts
            // 48 distinct agents and clears the authored threshold of 20.
            ReplaceCommandSource(engine, localPlayer, squad.ToArray());
            DriveRightClickCommand(engine, backend, new Vector2(6000f, 5000f));
            WaitUntil(
                engine,
                MarchBudgetFrames,
                () => plate.OpenedDoorCount >= 1,
                () => $"Door did not open after a full-squad crossing. plateBegin={plate.AgentContactBeginCount}, " +
                    $"plateEnd={plate.AgentContactEndCount}, centroid={ComputeCentroid(engine, squad)}, " +
                    $"lastActivation={DescribeLastActivation(engine)}");
            Assert.That(plate.AgentContactBeginCount, Is.GreaterThanOrEqualTo(20));
            AssertDoorObstacleSinksCleared(engine);
        }

        [Test]
        public void Showcase_ShockwaveKnockback_WindowsRecoverAndSecondCastIsSafe()
        {
            GC.KeepAlive(typeof(CapabilityStandardCrowdPhysicsArenaModEntry).Assembly);

            using var engine = CreateEngine();
            StartStartupMap(engine);
            MassNavigationSimulationRuntime simulation = RequireMassNavigationSimulation(engine);
            WaitForScenarioAgents(engine, simulation, 96);
            ParkAllScenarioAgents(engine);

            List<Entity> squad = CollectAgents(engine, controllable: true);
            Assert.That(squad, Has.Count.EqualTo(ExpectedSquadSize));
            var backend = RequireBackend(engine);
            Entity localPlayer = RequireService(engine, CoreServiceKeys.LocalPlayerEntity);
            PoseAuthorityArbiter arbiter = RequireService(engine, CoreServiceKeys.PoseAuthorityArbiter);

            // March into the open south-western field: the knockback scenario asserts full-squad
            // recovery + arrival, so the route must not entangle the squad with the crate stack.
            ReplaceCommandSource(engine, localPlayer, squad.ToArray());
            Vector2 target = DriveRightClickCommand(engine, backend, new Vector2(3600f, 6600f));

            // Let the squad march for ~2 simulated seconds, then detonate Q on its centroid.
            TickFrames(engine, 120);
            Vector2 centroid = ComputeCentroid(engine, squad);
            DriveAbilityCast(engine, backend, SkillQKeyPath, centroid);

            WaitUntil(
                engine,
                60,
                () => CountDisplacements(engine) > 0,
                () => "Q shockwave did not create any displacement effects.");

            int hitCount = CountDisplacements(engine);
            int peakWindows = arbiter.ActiveWindowCount;
            for (int frame = 0; frame < 300 && (CountDisplacements(engine) > 0 || arbiter.ActiveWindowCount > 0); frame++)
            {
                hitCount = Math.Max(hitCount, CountDisplacements(engine));
                peakWindows = Math.Max(peakWindows, arbiter.ActiveWindowCount);
                TickFrames(engine, 1);
            }

            Assert.That(hitCount, Is.GreaterThan(0).And.LessThanOrEqualTo(squad.Count));
            Assert.That(peakWindows, Is.EqualTo(hitCount),
                "Every displaced agent must hold exactly one pose-authority window (M hits -> M windows).");
            Assert.That(arbiter.ActiveWindowCount, Is.Zero, "All displacement windows must be handed back.");
            Assert.That(CountDisplacements(engine), Is.Zero, "All displacement effects must complete.");

            // Recovered agents must resume their original move order and reach the target. The
            // full 48-agent formation plus post-knockback congestion settle spreads the tail of
            // the crowd slightly wider than a clean march, hence the wider knockback tolerance.
            const float knockbackArrivalToleranceCm = 1200f;
            WaitUntil(
                engine,
                MarchBudgetFrames,
                () => CountAgentsWithin(engine, squad, target, knockbackArrivalToleranceCm) == squad.Count,
                () => $"After knockback recovery only {CountAgentsWithin(engine, squad, target, knockbackArrivalToleranceCm)} of " +
                    $"{squad.Count} agents resumed and arrived near {target}. Stragglers: {DescribeStragglers(engine, squad, target, knockbackArrivalToleranceCm)}");

            // Second cast: the arrival march far exceeds the 45-tick Q cooldown (the stacking guard requires
            // cooldown 2250ms > displacement.maxDurationMs 2000ms), so no unit is still inside a window.
            Assert.That(arbiter.ActiveWindowCount, Is.Zero,
                "No agent may still hold a displacement window before the second Q cast (stacking guard).");
            Vector2 secondCentroid = ComputeCentroid(engine, squad);
            DriveAbilityCast(engine, backend, SkillQKeyPath, secondCentroid);

            WaitUntil(
                engine,
                60,
                () => CountDisplacements(engine) > 0,
                () => "Second Q cast did not create any displacement effects (cooldown should have expired).");

            for (int frame = 0; frame < 300 && (CountDisplacements(engine) > 0 || arbiter.ActiveWindowCount > 0); frame++)
            {
                TickFrames(engine, 1);
            }

            Assert.That(arbiter.ActiveWindowCount, Is.Zero,
                "Second shockwave must also hand every window back without exceptions.");
            Assert.That(CountDisplacements(engine), Is.Zero);
        }

        [Test]
        public void Showcase_BoulderWall_BoulderRepelledWhileUnitsStayBitwiseStill()
        {
            GC.KeepAlive(typeof(CapabilityStandardCrowdPhysicsArenaModEntry).Assembly);

            using var engine = CreateEngine();
            StartStartupMap(engine);
            MassNavigationSimulationRuntime simulation = RequireMassNavigationSimulation(engine);
            WaitForScenarioAgents(engine, simulation, 96);
            ParkAllScenarioAgents(engine);

            // The parked eastern squad forms the human wall around its spawn slots (~7600, 5000).
            List<Entity> wall = CollectAgents(engine, controllable: false);
            Assert.That(wall, Has.Count.EqualTo(ExpectedSquadSize));

            Dictionary<int, (long X, long Y)> baseline = SnapshotAgentPositionsRaw(engine, wall);
            TickFrames(engine, 90);
            AssertAgentPositionsBitwiseEqual(engine, wall, baseline,
                "Idle wall agents drifted before the boulder was released; the scenario baseline must be settled.");

            var backend = RequireBackend(engine);
            float wallMinX = float.MaxValue;
            foreach (Entity agent in wall)
            {
                wallMinX = MathF.Min(wallMinX, ReadAgentPositionCm(engine, agent).X);
            }

            // E spawns the boulder at the cursor with an authored initial velocity of (-900, 0),
            // rolling it westward into the wall.
            DriveAbilityCast(engine, backend, SkillEKeyPath, new Vector2(8400f, 5000f));

            Entity boulder = default;
            WaitUntil(
                engine,
                60,
                () => TryFindNamedBody(engine, BoulderName, out boulder),
                () => "E cast did not spawn the boulder.");

            float initialSpeed = ReadBodySpeedCmPerSec(engine, boulder);
            Assert.That(initialSpeed, Is.GreaterThan(500f), "The boulder template authors a 900cm/s initial velocity.");

            bool decayedOrRepelled = false;
            float minBoulderX = float.MaxValue;
            for (int frame = 0; frame < 900; frame++)
            {
                TickFrames(engine, 1);
                float speed = ReadBodySpeedCmPerSec(engine, boulder);
                float velocityX = (float)engine.World.Get<Velocity2D>(boulder).Linear.X;
                minBoulderX = MathF.Min(minBoulderX, ReadBodyPositionCm(engine, boulder).X);
                if (speed < initialSpeed * 0.5f || velocityX > 0f)
                {
                    decayedOrRepelled = true;
                    break;
                }
            }

            Assert.That(decayedOrRepelled, Is.True,
                $"Boulder must decay or bounce against the kinematic wall (initial={initialSpeed}cm/s, " +
                $"final={ReadBodySpeedCmPerSec(engine, boulder)}cm/s).");
            Assert.That(minBoulderX, Is.GreaterThan(wallMinX),
                "The boulder must not plow through the entire human wall.");

            TickFrames(engine, 60);
            AssertAgentPositionsBitwiseEqual(engine, wall, baseline,
                "Kinematic wall agents must remain bitwise unmoved by the dynamic boulder impact.");
        }

        [Test]
        public void Showcase_KinematicBudgetBelowUnitCount_FailsFastOnStartup()
        {
            GC.KeepAlive(typeof(CapabilityStandardCrowdPhysicsArenaModEntry).Assembly);

            string tempModDir = Path.Combine(
                Path.GetTempPath(),
                "crowd_arena_kinematic_budget_" + Guid.NewGuid().ToString("N"));
            try
            {
                WriteKinematicBudgetOverrideMod(tempModDir, kinematicBodyCapacity: 32);

                string repoRoot = FindRepoRoot();
                List<string> modPaths = RepoModPaths.ResolveExplicit(repoRoot, ShowcaseMods);
                modPaths.Add(tempModDir);

                using var engine = new GameEngine();
                engine.InitializeWithConfigPipeline(modPaths, Path.Combine(repoRoot, "assets"));
                ApplyHostAssets(engine);
                InstallInput(engine);
                HeadlessPresentationTestHost.Install(engine);

                var exception = Assert.Throws<InvalidOperationException>(() =>
                {
                    engine.Start();
                    engine.LoadStartupMap();
                    for (int frame = 0; frame < MaxWarmupFrames * 3; frame++)
                    {
                        TickFrames(engine, 1);
                    }
                });

                Assert.That(exception!.Message, Does.Contain("kinematicBodyCapacity"),
                    "The massnav->kinematic bridge must fail fast when the kinematic budget is below the unit count.");
            }
            finally
            {
                if (Directory.Exists(tempModDir))
                {
                    Directory.Delete(tempModDir, recursive: true);
                }
            }
        }

        private static void WriteKinematicBudgetOverrideMod(string modDir, int kinematicBodyCapacity)
        {
            Directory.CreateDirectory(Path.Combine(modDir, "assets", "Configs", "Physics2D"));
            File.WriteAllText(
                Path.Combine(modDir, "mod.json"),
                "{\n" +
                "  \"name\": \"CrowdArenaKinematicBudgetOverrideTestMod\",\n" +
                "  \"version\": \"1.0.0\",\n" +
                "  \"description\": \"Test-only asset mod lowering kinematicBodyCapacity below the arena unit count to verify startup fail-fast.\",\n" +
                "  \"priority\": -3000,\n" +
                "  \"dependencies\": {\n" +
                "    \"CapabilityStandardCrowdPhysicsArenaMod\": \"^1.0.0\"\n" +
                "  },\n" +
                "  \"author\": \"ProductionPathTests\"\n" +
                "}\n");
            File.WriteAllText(
                Path.Combine(modDir, "assets", "Configs", "Physics2D", "kinematic.json"),
                $"{{\n  \"kinematicBodyCapacity\": {kinematicBodyCapacity}\n}}\n");
        }

        private static void WaitForScenarioAgents(
            GameEngine engine,
            MassNavigationSimulationRuntime simulation,
            int expectedAgents)
        {
            for (int frame = 0; frame < MaxWarmupFrames; frame++)
            {
                if (simulation.NavigationAgentCount >= expectedAgents)
                {
                    return;
                }

                TickFrames(engine, 1);
            }

            Assert.Fail(
                $"Arena scenario did not spawn {expectedAgents} agents within {MaxWarmupFrames} frames " +
                $"(current: {simulation.NavigationAgentCount}).");
        }

        private static string DescribeLastActivation(GameEngine engine)
        {
            InputOrderMappingSystem? mapping = engine.GetService(CoreServiceKeys.ActiveInputOrderMapping);
            if (mapping == null)
            {
                return "<no mapping installed>";
            }

            var result = mapping.LastActivationResult;
            return $"state={result.State}, rejection={result.Rejection}, actor={result.Actor.Id}, orderId={result.OrderId}";
        }

        private static string ReadDebugGlobal(GameEngine engine, string key)
        {
            return engine.GlobalContext.TryGetValue(key, out object? value) ? value?.ToString() ?? "<null>" : "<unset>";
        }

        private static void ParkAllScenarioAgents(GameEngine engine)
        {
            // Freshly spawned squads spread out from the spawn ring for a few simulated seconds.
            // Wait until every agent's committed position is bitwise stable across consecutive
            // frames so scenario phases start from a fully parked baseline.
            const int parkBudgetFrames = 1800;
            const int requiredStableFrames = 30;
            List<Entity> agents = CollectAllAgents(engine);
            Dictionary<int, (long X, long Y)> previous = SnapshotAgentPositionsRaw(engine, agents);
            int stableFrames = 0;
            for (int frame = 0; frame < parkBudgetFrames; frame++)
            {
                TickFrames(engine, 1);
                Dictionary<int, (long X, long Y)> current = SnapshotAgentPositionsRaw(engine, agents);
                stableFrames = PositionsBitwiseEqual(current, previous) ? stableFrames + 1 : 0;
                previous = current;
                if (stableFrames >= requiredStableFrames)
                {
                    return;
                }
            }

            Assert.Fail(
                $"Scenario agents did not park (bitwise-stable positions for {requiredStableFrames} consecutive frames) " +
                $"within {parkBudgetFrames} frames.");
        }

        private static bool PositionsBitwiseEqual(
            IReadOnlyDictionary<int, (long X, long Y)> a,
            IReadOnlyDictionary<int, (long X, long Y)> b)
        {
            foreach (KeyValuePair<int, (long X, long Y)> pair in a)
            {
                if (!b.TryGetValue(pair.Key, out (long X, long Y) other) ||
                    pair.Value.X != other.X ||
                    pair.Value.Y != other.Y)
                {
                    return false;
                }
            }

            return true;
        }

        private static List<Entity> CollectAllAgents(GameEngine engine)
        {
            var result = new List<Entity>();
            var query = new QueryDescription().WithAll<MassNavigationAgent, MassNavigationAgentIndex, WorldPositionCm>();
            engine.World.Query(in query, (Entity entity, ref MassNavigationAgent _, ref MassNavigationAgentIndex _, ref WorldPositionCm _) =>
            {
                result.Add(entity);
            });
            return result;
        }

        private static void TickFrames(GameEngine engine, int frames)
        {
            for (int i = 0; i < frames; i++)
            {
                engine.SetService(CoreServiceKeys.UiCaptured, false);
                engine.Tick(FixedDeltaSeconds);
                HeadlessPresentationTestHost.UpdateCamera(engine);
            }
        }

        private static void WaitUntil(GameEngine engine, int maxFrames, Func<bool> condition, Func<string> failMessage)
        {
            for (int frame = 0; frame < maxFrames; frame++)
            {
                if (condition())
                {
                    return;
                }

                TickFrames(engine, 1);
            }

            if (!condition())
            {
                Assert.Fail(failMessage());
            }
        }

        private static List<Entity> CollectAgents(GameEngine engine, bool controllable)
        {
            Entity localPlayer = RequireService(engine, CoreServiceKeys.LocalPlayerEntity);
            ControlDomainQuery domains = RequireService(engine, CoreServiceKeys.ControlDomainQuery);
            var result = new List<Entity>();
            var query = new QueryDescription().WithAll<MassNavigationAgent, MassNavigationAgentIndex, WorldPositionCm>();
            engine.World.Query(in query, (Entity entity, ref MassNavigationAgent _, ref MassNavigationAgentIndex _, ref WorldPositionCm _) =>
            {
                if (domains.IsControllableBy(localPlayer, entity) == controllable)
                {
                    result.Add(entity);
                }
            });
            return result;
        }

        private static int CountArrivedLaneRunners(
            GameEngine engine,
            List<Entity> runners,
            Vector2[] laneTargets,
            float toleranceCm)
        {
            int count = 0;
            for (int i = 0; i < runners.Count; i++)
            {
                if (Vector2.Distance(ReadAgentPositionCm(engine, runners[i]), laneTargets[i]) <= toleranceCm)
                {
                    count++;
                }
            }

            return count;
        }

        private static List<Entity> PickAgentsNearestToY(GameEngine engine, List<Entity> agents, float targetY, int count)
        {
            var sorted = new List<Entity>(agents);
            sorted.Sort((a, b) =>
                MathF.Abs(ReadAgentPositionCm(engine, a).Y - targetY)
                    .CompareTo(MathF.Abs(ReadAgentPositionCm(engine, b).Y - targetY)));
            return sorted.GetRange(0, count);
        }

        private static Vector2 ReadAgentPositionCm(GameEngine engine, Entity agent)
        {
            return engine.World.Get<WorldPositionCm>(agent).Value.ToVector2();
        }

        private static int CountAgentsWithin(GameEngine engine, List<Entity> agents, Vector2 target, float toleranceCm)
        {
            int count = 0;
            foreach (Entity agent in agents)
            {
                if (Vector2.Distance(ReadAgentPositionCm(engine, agent), target) <= toleranceCm)
                {
                    count++;
                }
            }

            return count;
        }

        private static string DescribeStragglers(GameEngine engine, List<Entity> agents, Vector2 target, float toleranceCm)
        {
            var parts = new List<string>();
            foreach (Entity agent in agents)
            {
                Vector2 position = ReadAgentPositionCm(engine, agent);
                if (Vector2.Distance(position, target) > toleranceCm)
                {
                    parts.Add($"agent {agent.Id} at {position} (dist {Vector2.Distance(position, target):0})");
                }
            }

            return string.Join("; ", parts);
        }

        private static Vector2 ComputeCentroid(GameEngine engine, List<Entity> agents)
        {
            Vector2 sum = Vector2.Zero;
            foreach (Entity agent in agents)
            {
                sum += ReadAgentPositionCm(engine, agent);
            }

            return sum / agents.Count;
        }

        private static Dictionary<int, (long X, long Y)> SnapshotAgentPositionsRaw(GameEngine engine, List<Entity> agents)
        {
            var snapshot = new Dictionary<int, (long X, long Y)>(agents.Count);
            foreach (Entity agent in agents)
            {
                var position = engine.World.Get<WorldPositionCm>(agent).Value;
                snapshot[agent.Id] = (position.X.RawValue, position.Y.RawValue);
            }

            return snapshot;
        }

        private static void AssertAgentPositionsBitwiseEqual(
            GameEngine engine,
            List<Entity> agents,
            IReadOnlyDictionary<int, (long X, long Y)> baseline,
            string message)
        {
            foreach (Entity agent in agents)
            {
                var position = engine.World.Get<WorldPositionCm>(agent).Value;
                (long X, long Y) expected = baseline[agent.Id];
                Assert.That(position.X.RawValue, Is.EqualTo(expected.X), $"{message} (agent {agent.Id} X)");
                Assert.That(position.Y.RawValue, Is.EqualTo(expected.Y), $"{message} (agent {agent.Id} Y)");
            }
        }

        private static List<(Entity Entity, Vector2 PositionCm)> CaptureNamedBodies(GameEngine engine, string name)
        {
            var result = new List<(Entity, Vector2)>();
            var query = new QueryDescription().WithAll<Name, Position2D>();
            engine.World.Query(in query, (Entity entity, ref Name entityName, ref Position2D position) =>
            {
                if (string.Equals(entityName.Value, name, StringComparison.Ordinal))
                {
                    result.Add((entity, new Vector2((float)position.Value.X, (float)position.Value.Y)));
                }
            });
            return result;
        }

        private static bool TryFindNamedBody(GameEngine engine, string name, out Entity found)
        {
            List<(Entity Entity, Vector2 PositionCm)> bodies = CaptureNamedBodies(engine, name);
            if (bodies.Count > 0)
            {
                found = bodies[0].Entity;
                return true;
            }

            found = default;
            return false;
        }

        private static Vector2 ReadBodyPositionCm(GameEngine engine, Entity body)
        {
            var position = engine.World.Get<Position2D>(body).Value;
            return new Vector2((float)position.X, (float)position.Y);
        }

        private static float ReadBodySpeedCmPerSec(GameEngine engine, Entity body)
        {
            var velocity = engine.World.Get<Velocity2D>(body).Linear;
            return new Vector2((float)velocity.X, (float)velocity.Y).Length();
        }

        private static int CountDisplacements(GameEngine engine)
        {
            return engine.World.CountEntities(in DisplacementQuery);
        }

        private static int ReadDoorOpenThreshold(GameEngine engine)
        {
            int threshold = 0;
            var query = new QueryDescription().WithAll<CrowdPhysicsArenaDoor>();
            engine.World.Query(in query, (ref CrowdPhysicsArenaDoor door) =>
            {
                threshold = door.OpenThresholdContacts;
            });
            Assert.That(threshold, Is.GreaterThan(0), "The arena map must author one door with a positive threshold.");
            return threshold;
        }

        private static void AssertDoorObstacleSinksCleared(GameEngine engine)
        {
            int doors = 0;
            var query = new QueryDescription().WithAll<CrowdPhysicsArenaDoor, ManifestationObstacleIntent2D>();
            engine.World.Query(in query, (ref CrowdPhysicsArenaDoor _, ref ManifestationObstacleIntent2D intent) =>
            {
                doors++;
                Assert.That(intent.SinkPhysicsCollider, Is.Zero, "Opened door must sink no physics collider.");
                Assert.That(intent.SinkNavigationObstacle, Is.Zero, "Opened door must sink no navigation obstacle.");
            });
            Assert.That(doors, Is.EqualTo(1), "The arena map authors exactly one door.");
        }

        private static void ReplaceCommandSource(GameEngine engine, Entity owner, ReadOnlySpan<Entity> members)
        {
            EntityCollectionStore collections = RequireService(engine, CoreServiceKeys.EntityCollectionStore);
            var descriptor = EntityCollectionDescriptor.Create(
                EntityCollectionKeys.CommandSource,
                EntityCollectionSourceKind.Explicit,
                EntityCollectionRoleKind.CommandSource,
                owner,
                members.Length > 0 ? members[0] : Entity.Null,
                "Command source",
                $"{members.Length} entity(s)");
            collections.Replace(owner, descriptor, members, owner);
        }

        private static Vector2 DriveRightClickCommand(GameEngine engine, HeadlessInputBackend backend, Vector2 worldCm)
        {
            Vector2 resolved = PointMouseAtGround(engine, backend, worldCm);
            backend.SetButton(MouseRightButtonPath, false);
            TickFrames(engine, 1);
            backend.SetButton(MouseRightButtonPath, true);
            TickFrames(engine, 1);
            backend.SetButton(MouseRightButtonPath, false);
            TickFrames(engine, 2);
            return resolved;
        }

        private static Vector2 DriveAbilityCast(GameEngine engine, HeadlessInputBackend backend, string keyPath, Vector2 worldCm)
        {
            Vector2 resolved = PointMouseAtGround(engine, backend, worldCm);
            backend.SetButton(keyPath, false);
            TickFrames(engine, 1);
            backend.SetButton(keyPath, true);
            TickFrames(engine, 1);
            backend.SetButton(keyPath, false);
            TickFrames(engine, 2);
            return resolved;
        }

        private static Vector2 PointMouseAtGround(GameEngine engine, HeadlessInputBackend backend, Vector2 worldCm)
        {
            // The relief heightmap sits hundreds of meters above y=0; projecting the ground point at
            // its sampled terrain height keeps it in front of the camera (finite screen coordinates).
            var heightmap = RequireService(engine, CoreServiceKeys.VisualHeightmap);
            Assert.That(heightmap.TrySampleHeightCm(worldCm.X, worldCm.Y, out float groundHeightCm), Is.True,
                $"Visual heightmap does not cover ground point {worldCm}.");
            var projector = RequireService(engine, CoreServiceKeys.ScreenProjector);
            Vector2 screen = projector.WorldToScreen(new Vector3(worldCm.X / 100f, groundHeightCm / 100f, worldCm.Y / 100f));
            Assert.That(
                AuthoritativeGroundPointerHelper.TryResolveFromScreen(engine.GlobalContext, screen, out WorldCmInt2 resolved),
                Is.True,
                $"Ground point {worldCm} did not resolve from screen point {screen}.");
            MassNavigationSimulationRuntime simulation = RequireMassNavigationSimulation(engine);
            Assert.That(simulation.ContainsWorldPoint(resolved.X, resolved.Y), Is.True,
                $"Resolved ground point ({resolved.X}, {resolved.Y}) is outside the massnav solver window.");
            backend.SetMousePosition(screen);
            return new Vector2(resolved.X, resolved.Y);
        }

        private static HeadlessInputBackend RequireBackend(GameEngine engine)
        {
            return RequireService(engine, CoreServiceKeys.InputBackend) as HeadlessInputBackend
                ?? throw new InvalidOperationException("Arena production path test requires the headless input backend.");
        }

        private static void StartStartupMap(GameEngine engine)
        {
            Assert.That(engine.MergedConfig.StartupMapId, Is.EqualTo("crowd_physics_arena"));
            Assert.That(engine.MergedConfig.StartupLocalPlayerId, Is.GreaterThan(0));

            engine.Start();
            engine.LoadStartupMap();
            WaitForMassNavigationRuntimeReady(engine);

            // The showcase enables a large RTS full-map minimap panel whose input-capturing rect
            // overlaps the screen projection of the arena landmarks (crate corridor, plate, ramp).
            // These tests drive world-ground clicks, not minimap interactions, so hide the panel.
            if (engine.GetService(CoreServiceKeys.MinimapRuntime) is MinimapRuntime minimap)
            {
                minimap.Visible = false;
            }
        }

        private static void WaitForMassNavigationRuntimeReady(GameEngine engine)
        {
            for (int frame = 0; frame < MaxWarmupFrames; frame++)
            {
                if (MassNavigationIds.IsCurrentNavigationRuntimeReady(engine))
                {
                    return;
                }

                TickFrames(engine, 1);
            }

            MassNavigationRuntimeBinding binding = RequireService(engine, MassNavigationKeys.RuntimeBinding);
            Assert.Fail(
                $"MassNavigation runtime did not become prepared within {MaxWarmupFrames} frames. " +
                $"currentMap={engine.CurrentMapSession?.MapId.Value ?? "<none>"}, bindingMap={binding.CurrentMapId.Value ?? "<none>"}, revision={binding.Revision}, preparedRevision={binding.PreparedRevision}.");
        }

        private static MassNavigationSimulationRuntime RequireMassNavigationSimulation(GameEngine engine)
        {
            return RequireService(engine, MassNavigationKeys.RuntimeBinding).RequireCurrent();
        }

        private static GameEngine CreateEngine()
        {
            string repoRoot = FindRepoRoot();
            var engine = new GameEngine();
            engine.InitializeWithConfigPipeline(
                RepoModPaths.ResolveExplicit(repoRoot, ShowcaseMods),
                Path.Combine(repoRoot, "assets"));
            ApplyHostAssets(engine);
            InstallInput(engine);
            HeadlessPresentationTestHost.Install(engine);
            return engine;
        }

        private static void ApplyHostAssets(GameEngine engine)
        {
            var meshAssets = RequireService(engine, CoreServiceKeys.PresentationMeshAssetRegistry);
            var materialAssets = RequireService(engine, CoreServiceKeys.PresentationMaterialRegistry);
            new PresentationHostAssetConfigLoader(engine.ConfigPipeline, meshAssets, materialAssets)
                .Apply("raylib", engine.ConfigCatalog, engine.ConfigConflictReport);
        }

        private static void InstallInput(GameEngine engine)
        {
            var inputConfig = new InputConfigPipelineLoader(engine.ConfigPipeline).Load();
            var backend = new HeadlessInputBackend();
            var inputHandler = new PlayerInputHandler(backend, inputConfig);
            for (int i = 0; i < engine.MergedConfig.StartupInputContexts.Count; i++)
            {
                inputHandler.PushContext(engine.MergedConfig.StartupInputContexts[i]);
            }

            engine.SetService(CoreServiceKeys.InputHandler, inputHandler);
            engine.SetService(CoreServiceKeys.InputBackend, (IInputBackend)backend);
            engine.SetService(CoreServiceKeys.UiCaptured, false);
        }

        private static T RequireService<T>(GameEngine engine, ServiceKey<T> key)
        {
            T value = engine.GetService(key);
            return value ?? throw new InvalidOperationException($"{key.Name} service is missing.");
        }

        private static string FindRepoRoot()
        {
            string current = TestContext.CurrentContext.WorkDirectory;
            while (!string.IsNullOrEmpty(current))
            {
                if (Directory.Exists(Path.Combine(current, "mods")) &&
                    File.Exists(Path.Combine(current, "AGENTS.md")))
                {
                    return current;
                }

                current = Path.GetDirectoryName(current)!;
            }

            throw new DirectoryNotFoundException("Repository root not found from test work directory.");
        }

        private sealed class HeadlessInputBackend : IInputBackend
        {
            private readonly HashSet<string> _pressedButtons = new(StringComparer.Ordinal);
            private Vector2 _mousePosition;

            public float GetAxis(string devicePath) => 0f;
            public bool GetButton(string devicePath) => _pressedButtons.Contains(devicePath);
            public Vector2 GetMousePosition() => _mousePosition;
            public float GetMouseWheel() => 0f;
            public void EnableIME(bool enable) { }
            public void SetIMECandidatePosition(int x, int y) { }
            public string GetCharBuffer() => string.Empty;

            public void SetMousePosition(Vector2 mousePosition)
            {
                _mousePosition = mousePosition;
            }

            public void SetButton(string devicePath, bool pressed)
            {
                if (pressed)
                {
                    _pressedButtons.Add(devicePath);
                    return;
                }

                _pressedButtons.Remove(devicePath);
            }
        }
    }
}
