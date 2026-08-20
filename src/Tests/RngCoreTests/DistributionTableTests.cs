using System;
using Ludots.Core.Engine.Randomization;
using NUnit.Framework;
using Ludots.Core.Gameplay.Rng;

namespace RngCoreTests
{
    [TestFixture]
    public class DistributionTableTests
    {
        private static DistributionEntryConfig Entry(string id, int weight, bool enabled = true, bool locked = false, DistributionModulationConfig? modulation = null)
        {
            return new DistributionEntryConfig(id, weight, enabled, locked, modulation);
        }

        private static DistributionTable CreateSampleTable()
        {
            return new DistributionTable("test.loot", "loot", new[]
            {
                Entry("common", 60),
                Entry("rare", 30),
                Entry("epic", 10, locked: true)
            });
        }

        [Test]
        public void DistributionTable_BaseShares_SumToOne()
        {
            var table = CreateSampleTable();

            var total = 0f;
            for (var i = 0; i < table.EntryCount; i++)
            {
                total += table.GetBaseShare(i);
            }

            Assert.That(total, Is.EqualTo(1f).Within(0.0001f));
        }

        [Test]
        public void DistributionTable_LockedEntry_ShareFrozenWhenUnlockedWeightsChange()
        {
            var table = CreateSampleTable();
            var lockedShareBefore = table.GetBaseShare(2);

            table.TrySetWeight(0, 10);

            Assert.That(table.GetBaseShare(2), Is.EqualTo(lockedShareBefore).Within(0.000001f));
        }

        [Test]
        public void DistributionTable_TrySetWeight_OnLockedEntry_Throws()
        {
            var table = CreateSampleTable();

            Assert.That(
                () => table.TrySetWeight(2, 50),
                Throws.InvalidOperationException.With.Message.Contains("locked"));
        }

        [Test]
        public void DistributionTable_ZeroWeightOnLastPositiveUnlocked_Throws()
        {
            var table = CreateSampleTable();
            table.TrySetWeight(0, 0);

            Assert.That(
                () => table.TrySetWeight(1, 0),
                Throws.InvalidOperationException.With.Message.Contains("only unlocked entry"));
        }

        [Test]
        public void DistributionTable_Pick_SameStreamSeed_ReplaysIdenticalSequence()
        {
            var firstService = new RngStreamService();
            var secondService = new RngStreamService();
            firstService.DeclareStream("loot", 4242u);
            secondService.DeclareStream("loot", 4242u);
            var first = CreateSampleTable();
            var second = CreateSampleTable();
            var firstStream = firstService.GetStream("loot");
            var secondStream = secondService.GetStream("loot");

            var firstPicks = new int[200];
            var secondPicks = new int[200];
            for (var i = 0; i < firstPicks.Length; i++)
            {
                firstPicks[i] = first.Pick(firstStream, 0f);
                secondPicks[i] = second.Pick(secondStream, 0f);
            }

            Assert.That(firstPicks, Is.EqualTo(secondPicks));
        }

        [Test]
        public void DistributionTable_Pick_AllEntriesDisabled_Throws()
        {
            var table = new DistributionTable("test.empty", "loot", new[]
            {
                Entry("a", 10, enabled: false),
                Entry("b", 10, enabled: false)
            });
            var service = new RngStreamService();
            service.DeclareStream("loot", 1u);

            Assert.That(
                () => table.Pick(service.GetStream("loot"), 0f),
                Throws.InvalidOperationException.With.Message.Contains("no pickable entry"));
        }

        [Test]
        public void DistributionTable_Modulation_ClampsWithinConfiguredPermille()
        {
            var table = new DistributionTable("test.mod", "loot", new[]
            {
                Entry("target", 50, modulation: new DistributionModulationConfig(600, 1500, false)),
                Entry("other", 50)
            });
            var baseShare = table.GetBaseShare(0);

            Assert.That(table.GetEffectiveShare(0, 1f), Is.EqualTo(baseShare * 1.5f).Within(0.000001f));
            Assert.That(table.GetEffectiveShare(0, -1f), Is.EqualTo(baseShare * 0.6f).Within(0.000001f));
            Assert.That(table.GetEffectiveShare(0, 0f), Is.EqualTo(baseShare).Within(0.000001f));
            Assert.That(table.GetEffectiveShare(0, 5f), Is.EqualTo(baseShare * 1.5f).Within(0.000001f));
            Assert.That(table.GetEffectiveShare(0, -5f), Is.EqualTo(baseShare * 0.6f).Within(0.000001f));
        }

        [Test]
        public void DistributionTable_Modulation_NaNOrInfinity_TreatedAsNeutral()
        {
            var table = new DistributionTable("test.mod", "loot", new[]
            {
                Entry("target", 50, modulation: new DistributionModulationConfig(600, 1500, false)),
                Entry("other", 50)
            });
            var baseShare = table.GetBaseShare(0);

            Assert.That(table.GetEffectiveShare(0, float.NaN), Is.EqualTo(baseShare).Within(0.000001f));
            Assert.That(table.GetEffectiveShare(0, float.PositiveInfinity), Is.EqualTo(baseShare).Within(0.000001f));
            Assert.That(table.GetEffectiveShare(0, float.NegativeInfinity), Is.EqualTo(baseShare).Within(0.000001f));
        }

        [Test]
        public void DistributionTable_Modulation_Inverted_SwapsDirection()
        {
            var table = new DistributionTable("test.mod", "loot", new[]
            {
                Entry("cursed", 50, modulation: new DistributionModulationConfig(600, 1500, true)),
                Entry("other", 50)
            });
            var baseShare = table.GetBaseShare(0);

            Assert.That(table.GetEffectiveShare(0, 1f), Is.EqualTo(baseShare * 0.6f).Within(0.000001f));
            Assert.That(table.GetEffectiveShare(0, -1f), Is.EqualTo(baseShare * 1.5f).Within(0.000001f));
        }

        [Test]
        public void DistributionTable_NegativeWeight_Throws()
        {
            Assert.That(
                () => new DistributionTable("test.bad", "loot", new[] { Entry("a", -1) }),
                Throws.InvalidOperationException.With.Message.Contains("negative weight"));
        }
    }

    [TestFixture]
    public class RngPickServiceTests
    {
        private static RngPickService CreateService(uint seed = 777u)
        {
            var streams = new RngStreamService();
            streams.DeclareStream("loot", seed);
            return new RngPickService(streams, new[]
            {
                new DistributionTable("test.loot", "loot", new[]
                {
                    new DistributionEntryConfig("common", 60, true, false, null),
                    new DistributionEntryConfig("rare", 30, true, false, null),
                    new DistributionEntryConfig("epic", 10, true, true, null)
                })
            });
        }

        [Test]
        public void RngPickService_GetDistribution_Undeclared_Throws()
        {
            var service = new RngPickService(new RngStreamService(), Array.Empty<DistributionTable>());

            Assert.That(
                () => service.GetDistribution("missing"),
                Throws.InvalidOperationException.With.Message.Contains("not declared"));
        }

        [Test]
        public void RngPickService_Pick_SnapshotRestore_ReplaysIdenticalSequence()
        {
            var service = CreateService();
            var stream = service.GetDistributionStream("test.loot");
            var snapshot = stream.CaptureSnapshot();

            var firstRun = new int[50];
            for (var i = 0; i < firstRun.Length; i++)
            {
                firstRun[i] = service.Pick("test.loot", 0f);
            }

            var divergedRun = new int[50];
            for (var i = 0; i < divergedRun.Length; i++)
            {
                divergedRun[i] = service.Pick("test.loot", 0.5f);
            }

            stream.RestoreSnapshot(in snapshot);

            var replayRun = new int[50];
            for (var i = 0; i < replayRun.Length; i++)
            {
                replayRun[i] = service.Pick("test.loot", 0f);
            }

            Assert.That(replayRun, Is.EqualTo(firstRun));
            Assert.That(divergedRun, Is.Not.EqualTo(firstRun), "Positive modulation must change the pick sequence.");
        }
    }
}
