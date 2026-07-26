using System;
using Ludots.Core.Navigation.NavMesh.LayeredSpan;
using NUnit.Framework;

namespace ArchitectureTests
{
    [TestFixture]
    public sealed class LayeredSpanColumnIndexContractTests
    {
        [Test]
        public void FindColumnOfSpan_HandlesEmptyColumnsFirstLastAndBounds()
        {
            int[] offsets = { 0, 0, 2, 2, 5, 5 };

            Assert.That(LayeredSpanColumnIndex.FindColumnOfSpan(-1, offsets, 5), Is.EqualTo(-1));
            Assert.That(LayeredSpanColumnIndex.FindColumnOfSpan(0, offsets, 5), Is.EqualTo(1));
            Assert.That(LayeredSpanColumnIndex.FindColumnOfSpan(1, offsets, 5), Is.EqualTo(1));
            Assert.That(LayeredSpanColumnIndex.FindColumnOfSpan(2, offsets, 5), Is.EqualTo(3));
            Assert.That(LayeredSpanColumnIndex.FindColumnOfSpan(4, offsets, 5), Is.EqualTo(3));
            Assert.That(LayeredSpanColumnIndex.FindColumnOfSpan(5, offsets, 5), Is.EqualTo(-1));
        }

        [Test]
        public void AdvanceToColumnOfSpan_TracksAscendingSpansAcrossEmptyColumns()
        {
            int[] offsets = { 0, 0, 2, 2, 5, 5 };
            int cursor = 0;

            Assert.That(LayeredSpanColumnIndex.AdvanceToColumnOfSpan(0, offsets, 5, ref cursor), Is.EqualTo(1));
            Assert.That(cursor, Is.EqualTo(1));
            Assert.That(LayeredSpanColumnIndex.AdvanceToColumnOfSpan(1, offsets, 5, ref cursor), Is.EqualTo(1));
            Assert.That(LayeredSpanColumnIndex.AdvanceToColumnOfSpan(2, offsets, 5, ref cursor), Is.EqualTo(3));
            Assert.That(LayeredSpanColumnIndex.AdvanceToColumnOfSpan(4, offsets, 5, ref cursor), Is.EqualTo(3));
            Assert.That(LayeredSpanColumnIndex.AdvanceToColumnOfSpan(5, offsets, 5, ref cursor), Is.EqualTo(-1));
            Assert.That(cursor, Is.EqualTo(5));
        }

        [Test]
        public void ColumnLookup_RejectsMalformedCsrShape()
        {
            int[] tooShort = { 0, 1 };

            Assert.Throws<ArgumentException>(
                () => LayeredSpanColumnIndex.FindColumnOfSpan(0, tooShort, 2));

            int cursor = 0;
            Assert.Throws<ArgumentException>(
                () => LayeredSpanColumnIndex.AdvanceToColumnOfSpan(0, tooShort, 2, ref cursor));
        }
    }
}
