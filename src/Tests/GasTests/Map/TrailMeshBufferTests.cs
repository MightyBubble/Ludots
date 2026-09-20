using System;
using System.Numerics;
using Ludots.Platform.Abstractions;
using NUnit.Framework;

namespace Ludots.Tests.GAS
{
    [TestFixture]
    public sealed class TrailMeshBufferTests
    {
        [Test]
        public void TrailMeshBuffer_WhenCapacityIsNotPositive_Throws()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new TrailMeshBuffer(0));
            Assert.Throws<ArgumentOutOfRangeException>(() => new TrailMeshBuffer(-2));
        }

        [Test]
        public void TrailMeshBuffer_UpsertThenRead_RoundTripsSamplesAndColors()
        {
            var buffer = new TrailMeshBuffer(capacity: 2);
            TrailMeshSample[] samples =
            {
                new() { Base = new Vector3(1f, 2f, 3f), Tip = new Vector3(4f, 5f, 6f), Age01 = 0f },
                new() { Base = new Vector3(7f, 8f, 9f), Tip = new Vector3(10f, 11f, 12f), Age01 = 0.5f },
            };
            var head = new Vector4(1f, 0.5f, 0.25f, 1f);
            var tail = new Vector4(0f, 0f, 1f, 0f);

            Assert.That(buffer.Upsert(11, samples, in head, in tail), Is.True);

            Assert.That(buffer.Count, Is.EqualTo(1));
            Assert.That(buffer.GetStableId(0), Is.EqualTo(11));
            Assert.That(buffer.GetHeadColor(0), Is.EqualTo(head));
            Assert.That(buffer.GetTailColor(0), Is.EqualTo(tail));
            ReadOnlySpan<TrailMeshSample> read = buffer.GetSamples(0);
            Assert.That(read.Length, Is.EqualTo(2));
            Assert.That(read[0].Base, Is.EqualTo(new Vector3(1f, 2f, 3f)));
            Assert.That(read[0].Tip, Is.EqualTo(new Vector3(4f, 5f, 6f)));
            Assert.That(read[1].Age01, Is.EqualTo(0.5f));
        }

        [Test]
        public void TrailMeshBuffer_UpsertSameStableId_ReplacesInPlace()
        {
            var buffer = new TrailMeshBuffer(capacity: 2);
            TrailMeshSample[] first = { new() { Base = Vector3.Zero, Tip = Vector3.One, Age01 = 0f } };
            TrailMeshSample[] second =
            {
                new() { Base = Vector3.One, Tip = Vector3.One * 2f, Age01 = 0f },
                new() { Base = Vector3.Zero, Tip = Vector3.One, Age01 = 1f },
            };

            Assert.That(buffer.Upsert(7, first, Vector4.One, Vector4.Zero), Is.True);
            Assert.That(buffer.Upsert(7, second, Vector4.One, Vector4.Zero), Is.True);

            Assert.That(buffer.Count, Is.EqualTo(1));
            Assert.That(buffer.GetSamples(0).Length, Is.EqualTo(2));
        }

        [Test]
        public void TrailMeshBuffer_Remove_SwapsLastIntoGap()
        {
            var buffer = new TrailMeshBuffer(capacity: 4);
            TrailMeshSample[] samples = { new() { Base = Vector3.Zero, Tip = Vector3.UnitZ, Age01 = 0f } };
            buffer.Upsert(1, samples, Vector4.One, Vector4.Zero);
            buffer.Upsert(2, samples, Vector4.One, Vector4.Zero);
            buffer.Upsert(3, samples, Vector4.One, Vector4.Zero);

            buffer.Remove(1);

            Assert.That(buffer.Count, Is.EqualTo(2));
            var remaining = new[] { buffer.GetStableId(0), buffer.GetStableId(1) };
            Assert.That(remaining, Is.EquivalentTo(new[] { 2, 3 }));
            buffer.Remove(2);
            buffer.Remove(3);
            Assert.That(buffer.Count, Is.Zero);
        }

        [Test]
        public void TrailMeshBuffer_WhenFull_UpsertNewStableIdReturnsFalse()
        {
            var buffer = new TrailMeshBuffer(capacity: 1);
            TrailMeshSample[] samples = { new() { Base = Vector3.Zero, Tip = Vector3.UnitZ, Age01 = 0f } };

            Assert.That(buffer.Upsert(1, samples, Vector4.One, Vector4.Zero), Is.True);
            Assert.That(buffer.Upsert(2, samples, Vector4.One, Vector4.Zero), Is.False);
            Assert.That(buffer.Upsert(1, samples, Vector4.One, Vector4.Zero), Is.True, "existing retained entry must still update");
        }

        [Test]
        public void TrailMeshBuffer_RejectsInvalidIdentityAndSampleCounts()
        {
            var buffer = new TrailMeshBuffer(capacity: 1);
            TrailMeshSample[] samples = { new() { Base = Vector3.Zero, Tip = Vector3.UnitZ, Age01 = 0f } };

            Assert.Throws<ArgumentException>(() => buffer.Upsert(0, samples, Vector4.One, Vector4.Zero));
            Assert.Throws<ArgumentException>(() => buffer.Upsert(-1, samples, Vector4.One, Vector4.Zero));
            Assert.Throws<ArgumentException>(() => buffer.Upsert(1, ReadOnlySpan<TrailMeshSample>.Empty, Vector4.One, Vector4.Zero));
            Assert.Throws<ArgumentException>(() =>
            {
                var tooMany = new TrailMeshSample[TrailMeshBuffer.MaxSamplesPerTrail + 1];
                buffer.Upsert(1, tooMany, Vector4.One, Vector4.Zero);
            });
        }
    }
}
