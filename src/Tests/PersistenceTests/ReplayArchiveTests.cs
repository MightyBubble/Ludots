using System;
using System.Numerics;
using System.Text.Json.Nodes;
using Ludots.Core.Persistence;
using NUnit.Framework;

namespace PersistenceTests
{
    [TestFixture]
    public sealed class ReplayArchiveTests
    {
        [Test]
        public void StreamRejectsGapAndBackwardsTick()
        {
            var stream = new AuthoritativeFrameStream(lastTick: 10);
            stream.Append(Frame(0, 11, "move"));
            var gap = Assert.Throws<SaveContextException>(() => stream.Append(Frame(2, 12, "move")));
            Assert.That(gap!.Message, Does.Contain("not contiguous"));
            var backwards = Assert.Throws<SaveContextException>(() => stream.Append(Frame(1, 10, "move")));
            Assert.That(backwards!.Message, Does.Contain("backwards"));
        }

        [Test]
        public void CodecRoundTripAndTamperAreExplicit()
        {
            WorldSaveSnapshot checkpoint = Checkpoint();
            var archive = new ReplayArchive(
                new ReplayHeader(1, "mods", "registry", "map", 10, 0),
                checkpoint,
                new[] { Frame(0, 11, "move") });
            var codec = new ReplayArchiveCodec();
            ReplayArchive decoded = codec.Decode(codec.Encode(archive));
            Assert.That(decoded.Frames, Has.Count.EqualTo(1));
            byte[] tampered = codec.Encode(archive);
            tampered[^1] ^= 0x01;
            Assert.That(Assert.Throws<SaveContextException>(() => codec.Decode(tampered))!.Message, Does.Contain("hash"));
        }

        [Test]
        public void ArchiveRejectsCheckpointContextDrift()
        {
            WorldSaveSnapshot checkpoint = Checkpoint() with { Header = Checkpoint().Header with { MapId = "other" } };
            var archive = new ReplayArchive(
                new ReplayHeader(1, "mods", "registry", "map", 10, 0), checkpoint, Array.Empty<AuthoritativeFrame>());
            Assert.That(Assert.Throws<SaveContextException>(() => archive.Validate())!.Message, Does.Contain("does not match"));
        }

        [Test]
        public void BatchStreamRejectsGapDuplicateAndWrongStart()
        {
            var batches = new AuthoritativeFrameBatchStream();
            batches.Append(new AuthoritativeFrameBatch(0, 0, new[] { Frame(0, 1, "move") }));
            Assert.That(Assert.Throws<SaveContextException>(() => batches.Append(
                new AuthoritativeFrameBatch(2, 1, new[] { Frame(1, 2, "move") })))!.Message, Does.Contain("batch sequence"));
            Assert.That(Assert.Throws<SaveContextException>(() => batches.Append(
                new AuthoritativeFrameBatch(1, 3, new[] { Frame(3, 2, "move") })))!.Message, Does.Contain("wrong").IgnoreCase);
        }

        [Test]
        public void StreamRejectsNonFiniteActionValue()
        {
            var stream = new AuthoritativeFrameStream();
            var frame = new AuthoritativeFrame(
                0,
                1,
                new[] { new AuthoritativeAction("move", new Vector3(float.NaN, 0, 0), true, true, false) });

            Assert.That(Assert.Throws<SaveContextException>(() => stream.Append(frame))!.Message, Does.Contain("non-finite"));
        }

        private static AuthoritativeFrame Frame(long sequence, int tick, string action)
        {
            return new AuthoritativeFrame(sequence, tick, new[] { new AuthoritativeAction(action, Vector3.UnitX, true, true, false) });
        }

        private static WorldSaveSnapshot Checkpoint()
        {
            return new WorldSaveSnapshot(
                new SaveContextHeader(1, "mods", "registry", "map", 10, DateTimeOffset.UnixEpoch, "test"),
                new JsonObject { ["core"] = new JsonObject() },
                new byte[] { 1, 2, 3 });
        }
    }
}
