using System;
using System.IO;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;
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
        public void CodecRoundTripsActionPresence()
        {
            WorldSaveSnapshot checkpoint = Checkpoint();
            var archive = new ReplayArchive(
                new ReplayHeader(1, "mods", "registry", "map", 10, 0),
                checkpoint,
                new[]
                {
                    new AuthoritativeFrame(0, 11, new[]
                    {
                        new AuthoritativeAction("Move", Vector3.UnitX, true, false, false, Present: false),
                        new AuthoritativeAction("Stick", new Vector3(1f, 2f, 0f), false, false, false, Present: true),
                    }),
                });

            ReplayArchive decoded = new ReplayArchiveCodec().Decode(new ReplayArchiveCodec().Encode(archive));

            Assert.That(decoded.Frames[0].Actions[0].ActionId, Is.EqualTo("Move"));
            Assert.That(decoded.Frames[0].Actions[0].Present, Is.False);
            Assert.That(decoded.Frames[0].Actions[1].ActionId, Is.EqualTo("Stick"));
            Assert.That(decoded.Frames[0].Actions[1].Present, Is.True);
            Assert.That(decoded.Frames[0].Actions[1].Value, Is.EqualTo(new Vector3(1f, 2f, 0f)));
            Assert.That(decoded.Frames[0].Actions[0].IntFires, Is.Null);
        }

        [Test]
        public void CodecRoundTripsBindingIntFiresAndRejectsAFireWithBothEdges()
        {
            WorldSaveSnapshot checkpoint = Checkpoint();
            var archive = new ReplayArchive(
                new ReplayHeader(1, "mods", "registry", "map", 10, 0),
                checkpoint,
                new[]
                {
                    new AuthoritativeFrame(0, 11, new[]
                    {
                        new AuthoritativeAction(
                            "Aim.Slot",
                            Vector3.One,
                            true,
                            true,
                            false,
                            Present: true,
                            new[]
                            {
                                new InputBindingIntFire(0, Pressed: true, Released: false),
                                new InputBindingIntFire(5, Pressed: true, Released: false),
                            }),
                    }),
                });

            ReplayArchive decoded = new ReplayArchiveCodec().Decode(new ReplayArchiveCodec().Encode(archive));
            IReadOnlyList<InputBindingIntFire> fires = decoded.Frames[0].Actions[0].IntFires!;
            Assert.That(fires, Is.EqualTo(new[]
            {
                new InputBindingIntFire(0, Pressed: true, Released: false),
                new InputBindingIntFire(5, Pressed: true, Released: false),
            }));

            var both = new AuthoritativeFrame(0, 1, new[]
            {
                new AuthoritativeAction(
                    "Aim.Slot",
                    Vector3.One,
                    true,
                    true,
                    false,
                    Present: true,
                    new[] { new InputBindingIntFire(1, Pressed: true, Released: true) }),
            });
            Assert.That(
                Assert.Throws<SaveContextException>(() => new AuthoritativeFrameStream().Append(both))!.Message,
                Does.Contain("exactly one"));
        }

        [Test]
        public void CodecRejectsActionMissingPresent()
        {
            WorldSaveSnapshot checkpoint = Checkpoint();
            var archive = new ReplayArchive(
                new ReplayHeader(1, "mods", "registry", "map", 10, 0),
                checkpoint,
                new[] { Frame(0, 11, "move") });
            byte[] encoded = new ReplayArchiveCodec().Encode(archive);
            const int headerSize = 44;
            Assert.That(encoded[headerSize], Is.EqualTo((byte)'{'));
            JsonObject root = JsonNode.Parse(encoded.AsSpan(headerSize))!.AsObject();
            JsonObject action = root["Frames"]![0]!["Actions"]![0]!.AsObject();
            Assert.That(action.Remove("Present"), Is.True);
            byte[] payload = Encoding.UTF8.GetBytes(root.ToJsonString());

            SaveContextException ex = Assert.Throws<SaveContextException>(
                () => new ReplayArchiveCodec().Decode(Container(payload)))!;
            Assert.That(ex.Message, Does.Contain("move"));
            Assert.That(ex.Message, Does.Contain("Present"));
        }

        [Test]
        public void StreamRejectsNonFiniteActionValue()
        {
            var stream = new AuthoritativeFrameStream();
            var frame = new AuthoritativeFrame(
                0,
                1,
                new[] { new AuthoritativeAction("move", new Vector3(float.NaN, 0, 0), true, true, false, Present: true) });

            Assert.That(Assert.Throws<SaveContextException>(() => stream.Append(frame))!.Message, Does.Contain("non-finite"));
        }

        private static AuthoritativeFrame Frame(long sequence, int tick, string action)
        {
            return new AuthoritativeFrame(sequence, tick, new[]
            {
                new AuthoritativeAction(action, Vector3.UnitX, true, true, false, Present: true),
            });
        }

        private static byte[] Container(byte[] payload)
        {
            const int headerSize = 44;
            byte[] result = new byte[headerSize + payload.Length];
            var writer = new BinaryWriter(new MemoryStream(result));
            writer.Write(0x5254444Cu);
            writer.Write((ushort)1);
            writer.Write((ushort)0);
            writer.Write(payload.Length);
            writer.Flush();
            SHA256.HashData(payload).CopyTo(result, 12);
            payload.CopyTo(result, headerSize);
            return result;
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
