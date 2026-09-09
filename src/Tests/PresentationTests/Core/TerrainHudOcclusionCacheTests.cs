using System;
using System.Diagnostics;
using Ludots.Core.Presentation.Hud;
using NUnit.Framework;

namespace Ludots.Tests.Presentation
{
    [TestFixture]
    public sealed class TerrainHudOcclusionCacheTests
    {
        [Test]
        public void ExactPositionKeyHitAndMiss()
        {
            var cache = new TerrainHudOcclusionCache(16);
            long key = TerrainHudOcclusionCache.ComposeKey(1, -3, 7, 12, -5, 3);
            Assert.That(cache.TryGet(key, out bool before), Is.False);
            cache.Set(key, visible: false);
            Assert.That(cache.TryGet(key, out bool after), Is.True);
            Assert.That(after, Is.False);
            Assert.That(cache.EntryCount, Is.EqualTo(1));
        }

        [Test]
        public void KeyComponentsDoNotCollide()
        {
            // 每个分量都占用独立位段：仅差一个分量的键必须可区分。
            long a = TerrainHudOcclusionCache.ComposeKey(1, 0, 0, 0, 0, 0);
            long b = TerrainHudOcclusionCache.ComposeKey(2, 0, 0, 0, 0, 0);
            long c = TerrainHudOcclusionCache.ComposeKey(1, 1, 0, 0, 0, 0);
            long d = TerrainHudOcclusionCache.ComposeKey(1, 0, 1, 0, 0, 0);
            long e = TerrainHudOcclusionCache.ComposeKey(1, 0, 0, 1, 0, 0);
            long f = TerrainHudOcclusionCache.ComposeKey(1, 0, 0, 0, 1, 0);
            long g = TerrainHudOcclusionCache.ComposeKey(1, 0, 0, 0, 0, 1);
            Assert.That(new[] { a, b, c, d, e, f, g }, Is.Unique);
        }

        [Test]
        public void OverflowClearsExplicitlyAndKeepsCounters()
        {
            var cache = new TerrainHudOcclusionCache(4);
            for (int i = 0; i < 8; i++)
            {
                cache.Set(TerrainHudOcclusionCache.ComposeKey(i, 0, 0, 0, 0, 0), true);
            }

            Assert.That(cache.OverflowClearCount, Is.GreaterThan(0), "容量满时必须显式清空计数，而不是静默丢弃。");
            Assert.That(cache.EntryCount, Is.LessThanOrEqualTo(4));
        }

        [Test]
        public void QueryAndSetOnHotPathDoNotAllocate()
        {
            var cache = new TerrainHudOcclusionCache(1024);
            long key = TerrainHudOcclusionCache.ComposeKey(7, 1, 2, 3, 4, 5);
            cache.Set(key, true);
            long allocated = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 10_000; i++)
            {
                cache.TryGet(key, out bool visible);
                cache.Set(key, visible);
            }

            Assert.That(GC.GetAllocatedBytesForCurrentThread() - allocated, Is.Zero);
        }

        [Test]
        public void UpdatePositionBumpsPositionRevisionAndPublishesDirtyStableId()
        {
            var buffer = new WorldHudBatchBuffer(4);
            var item = new WorldHudItem
            {
                StableId = 42,
                DirtySerial = 1,
                Kind = WorldHudItemKind.Bar,
                WorldPosition = System.Numerics.Vector3.One,
                Width = 64,
                Height = 8,
            };
            Assert.That(buffer.TryAdd(in item), Is.True);
            Assert.That(buffer.PositionRevision, Is.EqualTo(0), "新增不 bump 位置修订，结构变化走全量重建。");
            Assert.That(buffer.StructuralRevision, Is.EqualTo(1));

            var moved = item;
            moved.WorldPosition = new System.Numerics.Vector3(2, 2, 2);
            buffer.UpdatePosition(moved.StableId, in moved.WorldPosition);

            Assert.That(buffer.PositionRevision, Is.EqualTo(1));
            var dirty = buffer.GetPositionDirtyStableIdSpan();
            Assert.That(dirty.Length, Is.EqualTo(1));
            Assert.That(dirty[0], Is.EqualTo(42));
            buffer.ClearPositionDeltas();
            Assert.That(buffer.GetPositionDirtyStableIdSpan().Length, Is.Zero);
        }

        [Test]
        public void RemoveDoesNotBumpPositionRevisionButBumpsStructural()
        {
            var buffer = new WorldHudBatchBuffer(4);
            var item = new WorldHudItem
            {
                StableId = 9,
                Kind = WorldHudItemKind.Text,
                WorldPosition = System.Numerics.Vector3.One,
            };
            buffer.TryAdd(in item);
            buffer.Remove(9);
            Assert.That(buffer.PositionRevision, Is.EqualTo(0), "移除必须走全量重建，不进位置增量路径。");
            Assert.That(buffer.StructuralRevision, Is.EqualTo(2));
        }

        [Test]
        public void SamePositionUpdateDoesNotBumpRevisionAgain()
        {
            var buffer = new WorldHudBatchBuffer(4);
            var item = new WorldHudItem
            {
                StableId = 3,
                Kind = WorldHudItemKind.Bar,
                WorldPosition = System.Numerics.Vector3.One,
            };
            buffer.TryAdd(in item);
            var pos = new System.Numerics.Vector3(5, 5, 5);
            buffer.UpdatePosition(3, in pos);
            int rev = buffer.PositionRevision;
            buffer.UpdatePosition(3, in pos);
            Assert.That(buffer.PositionRevision, Is.EqualTo(rev), "位置未变时必须幂等。");
        }
    }
}
