using System;
using Ludots.Core.Mathematics;
using Ludots.Core.Spatial.Eqs;
using NUnit.Framework;

namespace Ludots.Tests.GasTests.Spatial
{
    [TestFixture]
    public class EqsSelectionTests
    {
        [Test]
        public void Best_ReturnsHighestScore()
        {
            Span<EqsItem> items = stackalloc EqsItem[3];
            items[0] = new EqsItem(new WorldCmInt2(0, 0)) { Score = 2f };
            items[1] = new EqsItem(new WorldCmInt2(1, 0)) { Score = 5f };
            items[2] = new EqsItem(new WorldCmInt2(2, 0)) { Score = 3f };

            bool found = EqsSelection.Best(items, out EqsItem best);
            Assert.That(found, Is.True);
            Assert.That(best.Score, Is.EqualTo(5f));
            Assert.That(best.Position.X, Is.EqualTo(1));
        }

        [Test]
        public void Best_SkipsFiltered()
        {
            Span<EqsItem> items = stackalloc EqsItem[2];
            items[0] = new EqsItem(new WorldCmInt2(0, 0)) { Score = 10f, Filtered = true };
            items[1] = new EqsItem(new WorldCmInt2(1, 0)) { Score = 3f, Filtered = false };

            bool found = EqsSelection.Best(items, out EqsItem best);
            Assert.That(found, Is.True);
            Assert.That(best.Score, Is.EqualTo(3f), "Should pick non-filtered even if lower score");
        }

        [Test]
        public void Best_ReturnsFalseWhenAllFiltered()
        {
            Span<EqsItem> items = stackalloc EqsItem[2];
            items[0] = new EqsItem(new WorldCmInt2(0, 0)) { Filtered = true };
            items[1] = new EqsItem(new WorldCmInt2(1, 0)) { Filtered = true };

            bool found = EqsSelection.Best(items, out _);
            Assert.That(found, Is.False);
        }

        [Test]
        public void TopN_ReturnsDescendingScores()
        {
            Span<EqsItem> items = stackalloc EqsItem[5];
            items[0] = new EqsItem(new WorldCmInt2(0, 0)) { Score = 3f };
            items[1] = new EqsItem(new WorldCmInt2(1, 0)) { Score = 7f };
            items[2] = new EqsItem(new WorldCmInt2(2, 0)) { Score = 1f };
            items[3] = new EqsItem(new WorldCmInt2(3, 0)) { Score = 9f };
            items[4] = new EqsItem(new WorldCmInt2(4, 0)) { Score = 5f };

            Span<EqsItem> top3 = stackalloc EqsItem[3];
            int count = EqsSelection.TopN(items, top3);

            Assert.That(count, Is.EqualTo(3));
            Assert.That(top3[0].Score, Is.EqualTo(9f));
            Assert.That(top3[1].Score, Is.EqualTo(7f));
            Assert.That(top3[2].Score, Is.EqualTo(5f));
        }

        [Test]
        public void CountAboveThreshold_FiltersCorrectly()
        {
            Span<EqsItem> items = stackalloc EqsItem[4];
            items[0] = new EqsItem(new WorldCmInt2(0, 0)) { Score = 2f };
            items[1] = new EqsItem(new WorldCmInt2(1, 0)) { Score = 5f };
            items[2] = new EqsItem(new WorldCmInt2(2, 0)) { Score = 8f };
            items[3] = new EqsItem(new WorldCmInt2(3, 0)) { Score = 3f, Filtered = true };

            int count = EqsSelection.CountAboveThreshold(items, threshold: 4f);
            Assert.That(count, Is.EqualTo(2), "Two non-filtered items >= 4f");
        }
    }
}
