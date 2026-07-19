using System.Collections.Generic;
using System.Numerics;
using NUnit.Framework;
using Ludots.Core.Spatial;

namespace GasTests
{
    [TestFixture]
    public class LinearQuadTreeTests
    {
        // ── Rect.Intersects tests ──

        [Test]
        public void Rect_Intersects_Overlapping_ReturnsTrue()
        {
            var a = new LinearQuadTree<int>.Rect { X = 0, Y = 0, W = 10, H = 10 };
            var b = new LinearQuadTree<int>.Rect { X = 5, Y = 5, W = 10, H = 10 };
            Assert.That(a.Intersects(b), Is.True);
            Assert.That(b.Intersects(a), Is.True);
        }

        [Test]
        public void Rect_Intersects_NonOverlapping_ReturnsFalse()
        {
            var a = new LinearQuadTree<int>.Rect { X = 0, Y = 0, W = 5, H = 5 };
            var b = new LinearQuadTree<int>.Rect { X = 20, Y = 20, W = 5, H = 5 };
            Assert.That(a.Intersects(b), Is.False);
            Assert.That(b.Intersects(a), Is.False);
        }

        [Test]
        public void Rect_Intersects_Touching_ReturnsFalse()
        {
            var a = new LinearQuadTree<int>.Rect { X = 0, Y = 0, W = 10, H = 10 };
            var b = new LinearQuadTree<int>.Rect { X = 10, Y = 0, W = 10, H = 10 };
            Assert.That(a.Intersects(b), Is.False);
        }

        [Test]
        public void Rect_Intersects_Contained_ReturnsTrue()
        {
            var outer = new LinearQuadTree<int>.Rect { X = 0, Y = 0, W = 20, H = 20 };
            var inner = new LinearQuadTree<int>.Rect { X = 5, Y = 5, W = 5, H = 5 };
            Assert.That(outer.Intersects(inner), Is.True);
            Assert.That(inner.Intersects(outer), Is.True);
        }

        [Test]
        public void Rect_Intersects_DifferentSizes_Correct()
        {
            var wide = new LinearQuadTree<int>.Rect { X = 0, Y = 4, W = 20, H = 2 };
            var tall = new LinearQuadTree<int>.Rect { X = 4, Y = 0, W = 2, H = 20 };
            Assert.That(wide.Intersects(tall), Is.True);
        }

        // ── Insert and Query tests ──

        [Test]
        public void Insert_And_Query_ReturnsItem()
        {
            var bounds = new LinearQuadTree<int>.Rect { X = 0, Y = 0, W = 100, H = 100 };
            var tree = new LinearQuadTree<int>(bounds, maxDepth: 4);

            tree.Insert(42, new Vector2(25, 25), new Vector2(1, 1));
            var results = new List<int>();
            tree.Query(new LinearQuadTree<int>.Rect { X = 20, Y = 20, W = 10, H = 10 }, results);

            Assert.That(results, Does.Contain(42));
        }

        [Test]
        public void Query_LargeArea_ReturnsAll()
        {
            var bounds = new LinearQuadTree<int>.Rect { X = 0, Y = 0, W = 100, H = 100 };
            var tree = new LinearQuadTree<int>(bounds, maxDepth: 4);

            tree.Insert(1, new Vector2(10, 10), new Vector2(1, 1));
            tree.Insert(2, new Vector2(50, 50), new Vector2(1, 1));
            tree.Insert(3, new Vector2(90, 90), new Vector2(1, 1));

            var results = new List<int>();
            tree.Query(bounds, results);
            Assert.That(results.Count, Is.EqualTo(3));
        }

        [Test]
        public void Query_EmptyArea_ReturnsNone()
        {
            var bounds = new LinearQuadTree<int>.Rect { X = 0, Y = 0, W = 100, H = 100 };
            var tree = new LinearQuadTree<int>(bounds, maxDepth: 4);

            tree.Insert(1, new Vector2(10, 10), new Vector2(1, 1));

            var results = new List<int>();
            tree.Query(new LinearQuadTree<int>.Rect { X = 50, Y = 50, W = 10, H = 10 }, results);
            Assert.That(results.Count, Is.EqualTo(0));
        }
    }
}
