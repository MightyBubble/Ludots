using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using System.Text.Json;
using Arch.Core;
using Ludots.Core.Components;
using Ludots.Core.Input.Interaction;
using Ludots.Core.Registry;
using NUnit.Framework;

namespace Ludots.Tests.GAS
{
    /// <summary>
    /// RFC-0065 DSP-1/2/4 (DEC-9/DEC-11, M8): CastDispatchProfile kernel — all/topN/cycle selectors,
    /// parallel/sequential routers, utility scorer over the consideration delegate table, cycle
    /// state keyed by an opaque group key, and load-time fail-fast. Profile ids and event keys
    /// are test data, never Core concepts.
    /// </summary>
    [TestFixture]
    [NonParallelizable]
    public sealed class CastDispatchProfileTests
    {
        private const string AllTogetherProfileId = "dispatch.all_together";
        private const string OneByOneProfileId = "dispatch.one_by_one";
        private const string NearestTopNProfileId = "dispatch.nearest_top_n";
        private const string OrderAcceptedEventKey = "orderAccepted";
        private const long GroupKeyA = 42L;

        [Test]
        public void SelectDispatchTargets_AllTogether_SelectsEveryActorWithAtomicFanOut()
        {
            using var world = World.Create();
            Harness harness = Harness.Create();
            harness.InstallBuiltInProfiles();

            Span<Entity> actors = stackalloc Entity[5];
            for (int i = 0; i < actors.Length; i++)
            {
                actors[i] = world.Create(WorldPositionCm.FromCm(i * 100, 0));
            }

            Span<Entity> selected = stackalloc Entity[5];
            var ctx = new CastDispatchContext(world, new Vector3(0f, 0f, 0f), GroupKeyA);
            int count = harness.Dispatch.SelectDispatchTargets(
                harness.ProfileId(AllTogetherProfileId), actors, in ctx, selected, out CastDispatchRouting routing);

            Assert.That(count, Is.EqualTo(5));
            for (int i = 0; i < actors.Length; i++)
            {
                Assert.That(selected[i], Is.EqualTo(actors[i]));
            }

            Assert.That(routing.SharedOrderId, Is.True, "all_together fans out as one atomic batch.");
            Assert.That(routing.Sequential, Is.False);
        }

        [Test]
        public void SelectDispatchTargets_OneByOne_PointerAdvancesOnlyOnMatchingNotifyAdvance()
        {
            using var world = World.Create();
            Harness harness = Harness.Create();
            harness.InstallBuiltInProfiles();
            int profileId = harness.ProfileId(OneByOneProfileId);
            int advanceEventId = harness.Dispatch.AdvanceEventIdRegistry.GetId(OrderAcceptedEventKey);
            int unrelatedEventId = harness.Dispatch.AdvanceEventIdRegistry.Register("someOtherEvent");

            Span<Entity> actors = stackalloc Entity[3];
            for (int i = 0; i < actors.Length; i++)
            {
                actors[i] = world.Create(WorldPositionCm.FromCm(i * 100, 0));
            }

            Span<Entity> selected = stackalloc Entity[3];
            var ctx = new CastDispatchContext(world, default, GroupKeyA);

            // Select is read-only: without NotifyAdvance the pointer stays put.
            Assert.That(harness.Dispatch.SelectDispatchTargets(profileId, actors, in ctx, selected, out CastDispatchRouting routing), Is.EqualTo(1));
            Assert.That(selected[0], Is.EqualTo(actors[0]));
            Assert.That(routing.Sequential, Is.True);
            Assert.That(routing.SharedOrderId, Is.False);
            Assert.That(harness.Dispatch.SelectDispatchTargets(profileId, actors, in ctx, selected, out _), Is.EqualTo(1));
            Assert.That(selected[0], Is.EqualTo(actors[0]), "no accepted order yet, pointer must not move.");

            // A mismatched event id never advances the cursor.
            harness.Dispatch.NotifyAdvance(profileId, GroupKeyA, unrelatedEventId);
            harness.Dispatch.SelectDispatchTargets(profileId, actors, in ctx, selected, out _);
            Assert.That(selected[0], Is.EqualTo(actors[0]));

            harness.Dispatch.NotifyAdvance(profileId, GroupKeyA, advanceEventId);
            Assert.That(harness.Dispatch.SelectDispatchTargets(profileId, actors, in ctx, selected, out _), Is.EqualTo(1));
            Assert.That(selected[0], Is.EqualTo(actors[1]));

            harness.Dispatch.NotifyAdvance(profileId, GroupKeyA, advanceEventId);
            Assert.That(harness.Dispatch.SelectDispatchTargets(profileId, actors, in ctx, selected, out _), Is.EqualTo(1));
            Assert.That(selected[0], Is.EqualTo(actors[2]));

            // Cycle state is per group key: an unrelated group still points at its first member.
            var otherCtx = new CastDispatchContext(world, default, groupKey: 7L);
            harness.Dispatch.SelectDispatchTargets(profileId, actors, in otherCtx, selected, out _);
            Assert.That(selected[0], Is.EqualTo(actors[0]));
        }

        [Test]
        public void SelectDispatchTargets_OneByOne_PointerRemainsValidAfterMembershipShrinks()
        {
            using var world = World.Create();
            Harness harness = Harness.Create();
            harness.InstallBuiltInProfiles();
            int profileId = harness.ProfileId(OneByOneProfileId);
            int advanceEventId = harness.Dispatch.AdvanceEventIdRegistry.GetId(OrderAcceptedEventKey);

            var members = new Entity[5];
            for (int i = 0; i < members.Length; i++)
            {
                members[i] = world.Create(WorldPositionCm.FromCm(i * 100, 0));
            }

            Span<Entity> selected = stackalloc Entity[5];
            var ctx = new CastDispatchContext(world, default, GroupKeyA);
            for (int i = 0; i < 4; i++)
            {
                harness.Dispatch.NotifyAdvance(profileId, GroupKeyA, advanceEventId);
            }

            Assert.That(harness.Dispatch.SelectDispatchTargets(profileId, members, in ctx, selected, out _), Is.EqualTo(1));
            Assert.That(selected[0], Is.EqualTo(members[4]), "cursor 4 over 5 members points at the last one.");

            // Two members died; the caller passes the surviving span and the pointer stays valid.
            ReadOnlySpan<Entity> survivors = members.AsSpan(0, 3);
            Assert.That(harness.Dispatch.SelectDispatchTargets(profileId, survivors, in ctx, selected, out _), Is.EqualTo(1));
            Assert.That(selected[0], Is.EqualTo(members[1]), "cursor 4 modulo 3 survivors selects index 1.");
        }

        [Test]
        public void SelectDispatchTargets_TopN_SelectsThreeNearestOfFive()
        {
            using var world = World.Create();
            Harness harness = Harness.Create();
            harness.InstallBuiltInProfiles();

            // Target T at (1000, 1000) cm; distances: e2 < e0 < e4 < e1 < e3.
            var e0 = world.Create(WorldPositionCm.FromCm(1000, 1300));
            var e1 = world.Create(WorldPositionCm.FromCm(1000, 2500));
            var e2 = world.Create(WorldPositionCm.FromCm(1100, 1000));
            var e3 = world.Create(WorldPositionCm.FromCm(5000, 5000));
            var e4 = world.Create(WorldPositionCm.FromCm(1000, 400));

            Span<Entity> actors = stackalloc Entity[] { e0, e1, e2, e3, e4 };
            Span<Entity> selected = stackalloc Entity[5];
            var ctx = new CastDispatchContext(world, new Vector3(1000f, 0f, 1000f), GroupKeyA);
            int count = harness.Dispatch.SelectDispatchTargets(
                harness.ProfileId(NearestTopNProfileId), actors, in ctx, selected, out CastDispatchRouting routing);

            Assert.That(count, Is.EqualTo(3));
            Assert.That(selected[0], Is.EqualTo(e2), "nearest first (invert flips distance ordering).");
            Assert.That(selected[1], Is.EqualTo(e0));
            Assert.That(selected[2], Is.EqualTo(e4));
            Assert.That(routing.SharedOrderId, Is.True);
            Assert.That(routing.Sequential, Is.False);
        }

        [Test]
        public void SelectDispatchTargets_TopN_EqualScoresTieBreakByEntityIdStably()
        {
            using var world = World.Create();
            Harness harness = Harness.Create();
            harness.InstallBuiltInProfiles();

            // Four actors equidistant from T, one clearly farther.
            var equidistant = new Entity[4];
            equidistant[0] = world.Create(WorldPositionCm.FromCm(1200, 1000));
            equidistant[1] = world.Create(WorldPositionCm.FromCm(800, 1000));
            equidistant[2] = world.Create(WorldPositionCm.FromCm(1000, 1200));
            equidistant[3] = world.Create(WorldPositionCm.FromCm(1000, 800));
            var far = world.Create(WorldPositionCm.FromCm(9000, 9000));

            // Present actors in scrambled order to prove input order does not decide ties.
            Span<Entity> actors = stackalloc Entity[] { equidistant[3], far, equidistant[1], equidistant[0], equidistant[2] };
            Span<Entity> first = stackalloc Entity[5];
            Span<Entity> second = stackalloc Entity[5];
            var ctx = new CastDispatchContext(world, new Vector3(1000f, 0f, 1000f), GroupKeyA);
            int profileId = harness.ProfileId(NearestTopNProfileId);

            int firstCount = harness.Dispatch.SelectDispatchTargets(profileId, actors, in ctx, first, out _);
            int secondCount = harness.Dispatch.SelectDispatchTargets(profileId, actors, in ctx, second, out _);

            Assert.That(firstCount, Is.EqualTo(3));
            Assert.That(secondCount, Is.EqualTo(3));
            for (int i = 0; i < 3; i++)
            {
                Assert.That(second[i], Is.EqualTo(first[i]), "tie-broken selection must be deterministic across calls.");
            }

            // Entity id ascending breaks the score tie among the equidistant four.
            Assert.That(first[0], Is.EqualTo(equidistant[0]));
            Assert.That(first[1], Is.EqualTo(equidistant[1]));
            Assert.That(first[2], Is.EqualTo(equidistant[2]));
        }

        [Test]
        public void Install_UnknownSelectorKind_Throws()
        {
            Harness harness = Harness.Create();
            Assert.Throws<InvalidOperationException>(() => harness.Dispatch.Install(Harness.Config(new CastDispatchProfileDefinition
            {
                Id = "dispatch.bad.selector",
                Selector = new CastDispatchSelectorDefinition { Kind = "roundRobin" },
                Router = new CastDispatchRouterDefinition { Kind = "parallel" },
            })));
        }

        [Test]
        public void Install_UnknownRouterKind_Throws()
        {
            Harness harness = Harness.Create();
            Assert.Throws<InvalidOperationException>(() => harness.Dispatch.Install(Harness.Config(new CastDispatchProfileDefinition
            {
                Id = "dispatch.bad.router",
                Selector = new CastDispatchSelectorDefinition { Kind = "all" },
                Router = new CastDispatchRouterDefinition { Kind = "staggered" },
            })));
        }

        [Test]
        public void Install_UnknownScorerKindOrConsideration_Throws()
        {
            Harness harness = Harness.Create();
            Assert.Throws<InvalidOperationException>(() => harness.Dispatch.Install(Harness.Config(new CastDispatchProfileDefinition
            {
                Id = "dispatch.bad.scorer_kind",
                Selector = new CastDispatchSelectorDefinition { Kind = "topN", N = 1 },
                Scorer = new CastDispatchScorerDefinition { Kind = "neural", Considerations = new List<string> { "distanceToTarget" } },
                Router = new CastDispatchRouterDefinition { Kind = "parallel" },
            })));

            Assert.Throws<InvalidOperationException>(() => harness.Dispatch.Install(Harness.Config(new CastDispatchProfileDefinition
            {
                Id = "dispatch.bad.consideration",
                Selector = new CastDispatchSelectorDefinition { Kind = "topN", N = 1 },
                Scorer = new CastDispatchScorerDefinition { Kind = "utility", Considerations = new List<string> { "threatLevel" } },
                Router = new CastDispatchRouterDefinition { Kind = "parallel" },
            })));

            Assert.Throws<InvalidOperationException>(() => harness.Dispatch.Install(Harness.Config(new CastDispatchProfileDefinition
            {
                Id = "dispatch.bad.modifier",
                Selector = new CastDispatchSelectorDefinition { Kind = "topN", N = 1 },
                Scorer = new CastDispatchScorerDefinition { Kind = "utility", Considerations = new List<string> { "distanceToTarget:desc" } },
                Router = new CastDispatchRouterDefinition { Kind = "parallel" },
            })));
        }

        [Test]
        public void Install_TopNWithoutScorerOrWithoutN_Throws()
        {
            Harness harness = Harness.Create();

            // Ranking without a scoring basis is a configuration error.
            Assert.Throws<InvalidOperationException>(() => harness.Dispatch.Install(Harness.Config(new CastDispatchProfileDefinition
            {
                Id = "dispatch.bad.topn_no_scorer",
                Selector = new CastDispatchSelectorDefinition { Kind = "topN", N = 3 },
                Router = new CastDispatchRouterDefinition { Kind = "parallel" },
            })));

            Assert.Throws<InvalidOperationException>(() => harness.Dispatch.Install(Harness.Config(new CastDispatchProfileDefinition
            {
                Id = "dispatch.bad.topn_no_n",
                Selector = new CastDispatchSelectorDefinition { Kind = "topN" },
                Scorer = new CastDispatchScorerDefinition { Kind = "utility", Considerations = new List<string> { "distanceToTarget" } },
                Router = new CastDispatchRouterDefinition { Kind = "parallel" },
            })));
        }

        [Test]
        public void Install_CycleWithoutAdvanceOn_Throws()
        {
            Harness harness = Harness.Create();
            Assert.Throws<InvalidOperationException>(() => harness.Dispatch.Install(Harness.Config(new CastDispatchProfileDefinition
            {
                Id = "dispatch.bad.cycle",
                Selector = new CastDispatchSelectorDefinition { Kind = "cycle" },
                Router = new CastDispatchRouterDefinition { Kind = "sequential" },
            })));
        }

        [Test]
        public void Install_SequentialRouterWithAtomicFanOutFlag_Throws()
        {
            Harness harness = Harness.Create();
            Assert.Throws<InvalidOperationException>(() => harness.Dispatch.Install(Harness.Config(new CastDispatchProfileDefinition
            {
                Id = "dispatch.bad.sequential_shared",
                Selector = new CastDispatchSelectorDefinition { Kind = "all" },
                Router = new CastDispatchRouterDefinition { Kind = "sequential", SharedOrderId = true },
            })));
        }

        [Test]
        public void DefaultConfigFile_DeserializesValidatesAndInstalls()
        {
            string configPath = Path.Combine(FindRepoRoot(), "assets", "Input", "cast_dispatch_profiles.json");
            Assert.That(File.Exists(configPath), Is.True, $"Missing {configPath}");

            var config = JsonSerializer.Deserialize<CastDispatchProfilesConfig>(
                File.ReadAllText(configPath),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            Assert.That(config, Is.Not.Null);
            CastDispatchProfileConfigLoader.Validate(config, "assets");

            Harness harness = Harness.Create();
            harness.Dispatch.Install(config);
            Assert.That(harness.Dispatch.IsInstalled(harness.ProfileId(AllTogetherProfileId)), Is.True);
            Assert.That(harness.Dispatch.IsInstalled(harness.ProfileId(OneByOneProfileId)), Is.True);
            Assert.That(harness.Dispatch.IsInstalled(harness.ProfileId(NearestTopNProfileId)), Is.True);
            Assert.That(harness.Dispatch.AdvanceEventIdRegistry.Contains(OrderAcceptedEventKey), Is.True,
                "advanceOn keys register into the advance event id space at install.");
        }

        [Test]
        public void SelectAndNotifyAdvance_SteadyState_AreAllocationFree()
        {
            using var world = World.Create();
            Harness harness = Harness.Create();
            harness.InstallBuiltInProfiles();
            int allId = harness.ProfileId(AllTogetherProfileId);
            int cycleId = harness.ProfileId(OneByOneProfileId);
            int topNId = harness.ProfileId(NearestTopNProfileId);
            int advanceEventId = harness.Dispatch.AdvanceEventIdRegistry.GetId(OrderAcceptedEventKey);

            var actors = new Entity[8];
            for (int i = 0; i < actors.Length; i++)
            {
                actors[i] = world.Create(WorldPositionCm.FromCm(i * 173, i * 89));
            }

            var selected = new Entity[8];
            var ctx = new CastDispatchContext(world, new Vector3(500f, 0f, 500f), GroupKeyA);

            // Warmup: first NotifyAdvance seeds the cycle cursor entry for the group key.
            harness.Dispatch.SelectDispatchTargets(allId, actors, in ctx, selected, out _);
            harness.Dispatch.SelectDispatchTargets(cycleId, actors, in ctx, selected, out _);
            harness.Dispatch.SelectDispatchTargets(topNId, actors, in ctx, selected, out _);
            harness.Dispatch.NotifyAdvance(cycleId, GroupKeyA, advanceEventId);

            long allocated = MeasureSteadyStateAllocations(harness, allId, cycleId, topNId, advanceEventId, actors, selected, in ctx);
            allocated = Math.Min(
                allocated,
                MeasureSteadyStateAllocations(harness, allId, cycleId, topNId, advanceEventId, actors, selected, in ctx));
            Assert.That(allocated, Is.EqualTo(0), "Steady-state select + advance must be allocation free.");
        }

        private static long MeasureSteadyStateAllocations(
            Harness harness,
            int allId,
            int cycleId,
            int topNId,
            int advanceEventId,
            Entity[] actors,
            Entity[] selected,
            in CastDispatchContext ctx)
        {
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 10_000; i++)
            {
                harness.Dispatch.SelectDispatchTargets(allId, actors, in ctx, selected, out _);
                harness.Dispatch.SelectDispatchTargets(cycleId, actors, in ctx, selected, out _);
                harness.Dispatch.SelectDispatchTargets(topNId, actors, in ctx, selected, out _);
                harness.Dispatch.NotifyAdvance(cycleId, ctx.GroupKey, advanceEventId);
            }

            return GC.GetAllocatedBytesForCurrentThread() - before;
        }

        private static string FindRepoRoot()
        {
            var current = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);
            while (current != null)
            {
                if (File.Exists(Path.Combine(current.FullName, "src", "Core", "Ludots.Core.csproj")))
                {
                    return current.FullName;
                }

                current = current.Parent;
            }

            throw new DirectoryNotFoundException("Could not locate repo root containing src/Core/Ludots.Core.csproj");
        }

        internal sealed class Harness
        {
            public CastDispatchProfileRegistry Dispatch = null!;
            public StringIntRegistry ProfileIds = null!;

            public static Harness Create()
            {
                var profileIds = new StringIntRegistry(capacity: 16, startId: 1, invalidId: 0, comparer: StringComparer.Ordinal);
                var advanceEventIds = new StringIntRegistry(capacity: 16, startId: 1, invalidId: 0, comparer: StringComparer.Ordinal);
                return new Harness
                {
                    Dispatch = new CastDispatchProfileRegistry(profileIds, advanceEventIds),
                    ProfileIds = profileIds,
                };
            }

            public int ProfileId(string name) => ProfileIds.GetId(name);

            /// <summary>The three §5.8 exemplars, mirroring <c>Input/cast_dispatch_profiles.json</c>.</summary>
            public void InstallBuiltInProfiles()
            {
                Dispatch.Install(Config(
                    new CastDispatchProfileDefinition
                    {
                        Id = AllTogetherProfileId,
                        Selector = new CastDispatchSelectorDefinition { Kind = "all" },
                        Router = new CastDispatchRouterDefinition { Kind = "parallel", SharedOrderId = true },
                    },
                    new CastDispatchProfileDefinition
                    {
                        Id = OneByOneProfileId,
                        Selector = new CastDispatchSelectorDefinition { Kind = "cycle", AdvanceOn = OrderAcceptedEventKey },
                        Router = new CastDispatchRouterDefinition { Kind = "sequential" },
                    },
                    new CastDispatchProfileDefinition
                    {
                        Id = NearestTopNProfileId,
                        Selector = new CastDispatchSelectorDefinition { Kind = "topN", N = 3 },
                        Scorer = new CastDispatchScorerDefinition
                        {
                            Kind = "utility",
                            Considerations = new List<string> { "distanceToTarget:invert" },
                        },
                        Router = new CastDispatchRouterDefinition { Kind = "parallel", SharedOrderId = true },
                    }));
            }

            public static CastDispatchProfilesConfig Config(params CastDispatchProfileDefinition[] profiles)
            {
                return new CastDispatchProfilesConfig { Profiles = new List<CastDispatchProfileDefinition>(profiles) };
            }
        }
    }
}
