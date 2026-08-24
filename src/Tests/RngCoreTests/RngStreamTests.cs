using Ludots.Core.Engine.Randomization;
using NUnit.Framework;

namespace RngCoreTests
{
    [TestFixture]
    [Category("ci-gate")]
    public class RngStreamTests
    {
        private static RngStreamService CreateServiceWithStream(string streamId, uint seed)
        {
            var service = new RngStreamService();
            service.DeclareStream(streamId, seed);
            return service;
        }

        [Test]
        public void RngStream_SameSeed_ProducesIdenticalSequence()
        {
            var first = CreateServiceWithStream("luck", 12345u).GetStream("luck");
            var second = CreateServiceWithStream("replay", 12345u).GetStream("replay");

            var firstValues = new uint[1000];
            var secondValues = new uint[1000];
            for (var i = 0; i < firstValues.Length; i++)
            {
                firstValues[i] = first.NextUInt();
                secondValues[i] = second.NextUInt();
            }

            Assert.That(firstValues, Is.EqualTo(secondValues));
        }

        [Test]
        public void RngStream_DifferentSeeds_ProduceDivergentSequences()
        {
            var first = CreateServiceWithStream("a", 1u).GetStream("a");
            var second = CreateServiceWithStream("b", 2u).GetStream("b");

            var identical = true;
            for (var i = 0; i < 100; i++)
            {
                if (first.NextUInt() != second.NextUInt())
                {
                    identical = false;
                    break;
                }
            }

            Assert.That(identical, Is.False);
        }

        [Test]
        public void RngStream_ZeroSeed_MatchesEscapeSeedSequence()
        {
            var zeroSeeded = CreateServiceWithStream("zero", 0u).GetStream("zero");
            var escapeSeeded = CreateServiceWithStream("escape", 2463534242u).GetStream("escape");

            var zeroValues = new uint[16];
            var escapeValues = new uint[16];
            for (var i = 0; i < zeroValues.Length; i++)
            {
                zeroValues[i] = zeroSeeded.NextUInt();
                escapeValues[i] = escapeSeeded.NextUInt();
            }

            Assert.That(zeroValues, Is.EqualTo(escapeValues));
        }

        [Test]
        public void RngStream_SnapshotRestore_ReplaysIdenticalSequence()
        {
            var stream = CreateServiceWithStream("luck", 777u).GetStream("luck");

            for (var i = 0; i < 50; i++)
            {
                stream.NextUInt();
            }

            var snapshot = stream.CaptureSnapshot();

            var diverged = new uint[50];
            for (var i = 0; i < diverged.Length; i++)
            {
                diverged[i] = stream.NextUInt();
            }

            stream.RestoreSnapshot(in snapshot);

            var replayed = new uint[50];
            for (var i = 0; i < replayed.Length; i++)
            {
                replayed[i] = stream.NextUInt();
            }

            Assert.That(replayed, Is.EqualTo(diverged));
            Assert.That(stream.Position, Is.EqualTo(100));
        }

        [Test]
        public void RngStream_RestoreSnapshot_ForeignStreamId_Throws()
        {
            var stream = CreateServiceWithStream("luck", 1u).GetStream("luck");
            var foreign = CreateServiceWithStream("other", 2u).GetStream("other");
            var foreignSnapshot = foreign.CaptureSnapshot();

            Assert.That(
                () => stream.RestoreSnapshot(in foreignSnapshot),
                Throws.InvalidOperationException.With.Message.Contains("other"));
        }

        [Test]
        public void RngStream_NextUInt_AdvancesPositionByOne()
        {
            var stream = CreateServiceWithStream("luck", 42u).GetStream("luck");

            stream.NextUInt();

            Assert.That(stream.Position, Is.EqualTo(1));
        }

        [Test]
        public void RngStream_NextFloat01_StaysWithinUnitRange()
        {
            var stream = CreateServiceWithStream("luck", 42u).GetStream("luck");

            var min = float.MaxValue;
            var max = float.MinValue;
            for (var i = 0; i < 1000; i++)
            {
                var value = stream.NextFloat01();
                min = Math.Min(min, value);
                max = Math.Max(max, value);
            }

            Assert.That(min, Is.GreaterThanOrEqualTo(0f));
            Assert.That(max, Is.LessThanOrEqualTo(1f));
        }

        [Test]
        public void RngStream_NextInt_BoundedWithinExclusiveRange()
        {
            var stream = CreateServiceWithStream("luck", 4242u).GetStream("luck");

            var distinctValues = new HashSet<int>();
            for (var i = 0; i < 1000; i++)
            {
                var value = stream.NextInt(5, 10);
                Assert.That(value, Is.GreaterThanOrEqualTo(5), "Value fell below the lower bound.");
                Assert.That(value, Is.LessThan(10), "Value reached the exclusive upper bound.");
                distinctValues.Add(value);
            }

            Assert.That(distinctValues.SetEquals(new HashSet<int> { 5, 6, 7, 8, 9 }), Is.True, "Fixed-seed draws should cover every value in the range.");
        }

        [Test]
        public void RngStream_NextInt_EmptyRange_Throws()
        {
            var stream = CreateServiceWithStream("luck", 1u).GetStream("luck");

            Assert.That(() => stream.NextInt(5, 5), Throws.TypeOf<ArgumentOutOfRangeException>());
            Assert.That(() => stream.NextInt(5, 4), Throws.TypeOf<ArgumentOutOfRangeException>());
        }

        [Test]
        public void RngStream_Advance_SkipsSameAmountAsDrawing()
        {
            var advancing = CreateServiceWithStream("advance", 99u).GetStream("advance");
            var drawing = CreateServiceWithStream("draw", 99u).GetStream("draw");

            advancing.Advance(3);
            for (var i = 0; i < 3; i++)
            {
                drawing.NextUInt();
            }

            Assert.That(advancing.NextUInt(), Is.EqualTo(drawing.NextUInt()));
            Assert.That(advancing.Position, Is.EqualTo(drawing.Position));
        }

        [Test]
        public void RngStream_Advance_NegativeSteps_Throws()
        {
            var stream = CreateServiceWithStream("luck", 1u).GetStream("luck");

            Assert.That(() => stream.Advance(-1), Throws.TypeOf<ArgumentOutOfRangeException>());
        }
    }
}
