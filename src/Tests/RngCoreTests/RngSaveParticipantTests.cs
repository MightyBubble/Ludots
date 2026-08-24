using System.Collections.Generic;
using System.Text.Json.Nodes;
using Ludots.Core.Engine.Randomization;
using Ludots.Core.Gameplay.Rng;
using Ludots.Core.Persistence;
using NUnit.Framework;

namespace RngCoreTests
{
    [TestFixture]
    [Category("ci-gate")]
    public class RngSaveParticipantTests
    {
        private const uint LootSeed = 4242u;
        private const uint CombatSeed = 777u;

        private static DistributionTable CreateLootTable()
        {
            return new DistributionTable("test.loot", "loot", new[]
            {
                new DistributionEntryConfig("common", 60, true, false, null),
                new DistributionEntryConfig("rare", 30, true, false, null),
                new DistributionEntryConfig("epic", 10, true, true, null)
            });
        }

        private static RngPickService CreatePickService(RngStreamService streams)
        {
            return new RngPickService(streams, new[] { CreateLootTable() });
        }

        private static RngStreamService CreateDeclaredStreams()
        {
            var streams = new RngStreamService();
            streams.DeclareStream("loot", LootSeed);
            streams.DeclareStream("combat", CombatSeed);
            return streams;
        }

        [Test]
        public void RngSave_SaveRestoreContinue_PickSequenceMatchesUninterruptedRun()
        {
            var reference = CreateDeclaredStreams();
            var referencePicks = CreatePickService(reference);
            referencePicks.Pick("test.loot");
            referencePicks.Pick("test.loot");
            reference.GetStream("combat").NextUInt();
            var expected = new List<int>();
            for (var i = 0; i < 16; i++)
            {
                expected.Add(referencePicks.Pick("test.loot"));
            }

            var saved = CreateDeclaredStreams();
            var savedPicks = CreatePickService(saved);
            savedPicks.Pick("test.loot");
            savedPicks.Pick("test.loot");
            saved.GetStream("combat").NextUInt();
            JsonNode snapshot = CoreSaveParticipants.CreateRngParticipant(saved).CaptureState();

            var restored = CreateDeclaredStreams();
            CoreSaveParticipants.CreateRngParticipant(restored).RestoreState(snapshot.DeepClone());
            var restoredPicks = CreatePickService(restored);
            var actual = new List<int>();
            for (var i = 0; i < 16; i++)
            {
                actual.Add(restoredPicks.Pick("test.loot"));
            }

            Assert.That(actual, Is.EqualTo(expected));
            Assert.That(restored.GetStream("combat").NextUInt(), Is.EqualTo(reference.GetStream("combat").NextUInt()));
        }

        [Test]
        public void RngSave_RestoreWithStreamRemoved_FailsClosedWithStreamName()
        {
            var saved = CreateDeclaredStreams();
            saved.GetStream("loot").NextUInt();
            JsonObject snapshot = CoreSaveParticipants.CreateRngParticipant(saved).CaptureState().AsObject();
            var streams = (JsonArray)snapshot["streams"]!;
            for (var i = 0; i < streams.Count; i++)
            {
                if (string.Equals(streams[i]!["stream"]!.GetValue<string>(), "combat", System.StringComparison.Ordinal))
                {
                    streams.RemoveAt(i);
                    break;
                }
            }

            var restored = CreateDeclaredStreams();
            Assert.That(
                () => CoreSaveParticipants.CreateRngParticipant(restored).RestoreState(snapshot),
                Throws.TypeOf<SaveContextException>().With.Message.Contains("combat"));
        }

        [Test]
        public void RngSave_RestoreWithTamperedSeed_FailsClosedWithStreamName()
        {
            var saved = CreateDeclaredStreams();
            JsonObject snapshot = CoreSaveParticipants.CreateRngParticipant(saved).CaptureState().AsObject();
            var streams = (JsonArray)snapshot["streams"]!;
            foreach (JsonNode? node in streams)
            {
                var entry = (JsonObject)node!;
                if (string.Equals(entry["stream"]!.GetValue<string>(), "loot", System.StringComparison.Ordinal))
                {
                    entry["seed"] = LootSeed + 1u;
                }
            }

            var restored = CreateDeclaredStreams();
            Assert.That(
                () => CoreSaveParticipants.CreateRngParticipant(restored).RestoreState(snapshot),
                Throws.TypeOf<SaveContextException>().With.Message.Contains("loot"));
        }

        [Test]
        public void RngSave_RestoreWithExtraStream_FailsClosedWithStreamName()
        {
            var saved = CreateDeclaredStreams();
            JsonObject snapshot = CoreSaveParticipants.CreateRngParticipant(saved).CaptureState().AsObject();
            var streams = (JsonArray)snapshot["streams"]!;
            streams.Add(new JsonObject
            {
                ["stream"] = "ghost",
                ["seed"] = 1u,
                ["state"] = 1u,
                ["position"] = 0L
            });

            var restored = CreateDeclaredStreams();
            Assert.That(
                () => CoreSaveParticipants.CreateRngParticipant(restored).RestoreState(snapshot),
                Throws.TypeOf<SaveContextException>().With.Message.Contains("ghost"));
        }
    }
}
