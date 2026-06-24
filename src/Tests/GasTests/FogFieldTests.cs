using Ludots.Core.Mathematics;
using Ludots.Core.Vision;
using NUnit.Framework;

namespace Ludots.Tests.GAS
{
    [TestFixture]
    public sealed class FogFieldTests
    {
        [Test]
        public void FogField_KeepsScopeLayerStateIndependent()
        {
            var registry = new FogLayerRegistry();
            FogLayerId groundId = registry.Register("ground", cellSizeCm: 100, updateHz: 10);
            FogLayerId airId = registry.Register("air", cellSizeCm: 250, updateHz: 5);

            var ground = new FogField(scopeKeyId: 7, registry.Get(groundId));
            var air = new FogField(scopeKeyId: 7, registry.Get(airId));
            var otherScope = new FogField(scopeKeyId: 9, registry.Get(groundId));

            var cell = new FogCell(2, 3);
            ground.SetVisible(cell);
            air.SetDenied(cell);
            otherScope.SetExplored(cell);

            Assert.That(ground.GetVisibility(cell), Is.EqualTo(CellVisibility.Visible));
            Assert.That(air.GetVisibility(cell), Is.EqualTo(CellVisibility.Denied));
            Assert.That(otherScope.GetVisibility(cell), Is.EqualTo(CellVisibility.Explored));
            Assert.That(ground.ScopeKeyId, Is.EqualTo(7));
            Assert.That(ground.LayerId, Is.EqualTo(groundId));
            Assert.That(air.LayerId, Is.EqualTo(airId));
        }

        [Test]
        public void FogField_UsesLayerResolutionForWorldCellAddressing()
        {
            var registry = new FogLayerRegistry();
            FogLayerId fineId = registry.Register("fine", cellSizeCm: 100, updateHz: 10);
            FogLayerId coarseId = registry.Register("coarse", cellSizeCm: 250, updateHz: 4);

            var fine = new FogField(1, registry.Get(fineId));
            var coarse = new FogField(1, registry.Get(coarseId));

            var world = new WorldCmInt2(260, -10);

            Assert.That(fine.WorldToCell(world), Is.EqualTo(new FogCell(2, -1)));
            Assert.That(coarse.WorldToCell(world), Is.EqualTo(new FogCell(1, -1)));
            Assert.That(fine.CellCenterToWorld(new FogCell(2, -1)), Is.EqualTo(new WorldCmInt2(250, -50)));
            Assert.That(coarse.CellCenterToWorld(new FogCell(1, -1)), Is.EqualTo(new WorldCmInt2(375, -125)));
        }

        [Test]
        public void FogField_TracksDirtyCellsAndAgesVisibleToExplored()
        {
            var registry = new FogLayerRegistry();
            FogLayerId layerId = registry.Register("ground", cellSizeCm: 100, updateHz: 10);
            var field = new FogField(1, registry.Get(layerId));

            var first = new FogCell(1, 1);
            var second = new FogCell(17, 1);
            field.SetVisible(first);
            field.SetDenied(second);
            field.MarkDirtyRegion(new IntRect(4, 4, 2, 1));

            Span<FogCell> dirty = stackalloc FogCell[8];
            int dirtyCount = field.EnumerateDirtyCells(dirty);

            Assert.That(dirtyCount, Is.EqualTo(4));
            Assert.That(field.DirtyCount, Is.EqualTo(4));
            Assert.That(dirty[..dirtyCount].ToArray(), Does.Contain(first));
            Assert.That(dirty[..dirtyCount].ToArray(), Does.Contain(second));

            field.ClearDirty();
            field.AgeVisibleToExplored();

            Assert.That(field.GetVisibility(first), Is.EqualTo(CellVisibility.Explored));
            Assert.That(field.GetVisibility(second), Is.EqualTo(CellVisibility.Denied));
            Assert.That(field.DirtyCount, Is.EqualTo(1));
        }

        [Test]
        public void FogField_ReadWriteHotPathAllocatesZeroAfterWarmup()
        {
            var registry = new FogLayerRegistry();
            FogLayerId layerId = registry.Register("ground", cellSizeCm: 100, updateHz: 10);
            var field = new FogField(1, registry.Get(layerId), chunkSizeCells: 16, initialChunkCapacity: 16);

            for (int i = 0; i < 256; i++)
            {
                field.SetVisible(new FogCell(i & 15, i >> 4));
            }

            field.ClearDirty();
            Span<FogCell> dirty = stackalloc FogCell[256];
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            GC.GetAllocatedBytesForCurrentThread();

            long before = GC.GetAllocatedBytesForCurrentThread();
            int visible = 0;
            for (int i = 0; i < 10_000; i++)
            {
                FogCell cell = new(i & 15, (i >> 4) & 15);
                if (field.GetVisibility(cell) == CellVisibility.Visible)
                {
                    visible++;
                }

                field.EnumerateDirtyCells(dirty);
            }

            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.That(visible, Is.GreaterThan(0));
            Assert.That(allocated, Is.EqualTo(0));
        }
    }
}
