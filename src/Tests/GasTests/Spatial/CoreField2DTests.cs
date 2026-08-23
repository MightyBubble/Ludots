using System;
using System.Numerics;
using Ludots.Core.Fields;
using Ludots.Core.Mathematics;
using NUnit.Framework;
using Ludots.Platform.Abstractions;

namespace Ludots.Tests.GAS
{
    [TestFixture]
    public sealed class CoreField2DTests
    {
        [Test]
        public void ChunkedField2D_MapsWorldCellsAndChunksAcrossNegativeCoordinates()
        {
            var grid = new FieldGridSpec2D(cellSizeCm: 100, chunkSizeCells: 4);
            var field = new ChunkedField2D<byte>(grid, defaultValue: 0, initialChunkCapacity: 4);

            Assert.That(field.WorldToCell(new WorldCmInt2(260, -10)), Is.EqualTo(new FieldCell2D(2, -1)));
            Assert.That(field.CellCenterToWorld(new FieldCell2D(-1, -1)), Is.EqualTo(new WorldCmInt2(-50, -50)));

            field.Set(new FieldCell2D(-1, -1), 7);
            field.Set(new FieldCell2D(4, 0), 9);

            Assert.That(field.ChannelCount, Is.EqualTo(1));
            Assert.That(field.ChannelKind, Is.EqualTo(FieldChannelKind.Struct));
            Assert.That(field.NonDefaultCount, Is.EqualTo(2));
            Assert.That(field.Get(new FieldCell2D(-1, -1)), Is.EqualTo(7));
            Assert.That(field.Get(new FieldCell2D(4, 0)), Is.EqualTo(9));
            Assert.That(field.ChunkCount, Is.EqualTo(2));
        }

        [Test]
        public void ChunkedField2D_DirtyCellsDeduplicateAndCopyNonDefaultUsesCallerSpan()
        {
            var field = new ChunkedField2D<byte>(
                new FieldGridSpec2D(cellSizeCm: 100, chunkSizeCells: 4),
                defaultValue: 0,
                initialChunkCapacity: 4);

            field.Set(new FieldCell2D(0, 0), 2);
            field.Set(new FieldCell2D(-1, -1), 3);
            field.Set(new FieldCell2D(4, 0), 4);
            field.MarkDirtyRegion(new IntRect(0, 0, 2, 1));

            Assert.That(field.NonDefaultCount, Is.EqualTo(3));
            Span<FieldCell2D> dirty = stackalloc FieldCell2D[8];
            int dirtyCount = field.EnumerateDirtyCells(dirty);
            Assert.That(dirtyCount, Is.EqualTo(4));
            Assert.That(field.DirtyCount, Is.EqualTo(4));

            Span<FieldCellValue2D<byte>> values = stackalloc FieldCellValue2D<byte>[8];
            int valueCount = field.CopyNonDefaultCells(values);
            Assert.That(valueCount, Is.EqualTo(3));

            field.ClearDirty();
            field.MarkDirty(new FieldCell2D(-1, -1));
            field.MarkDirty(new FieldCell2D(-1, -1));
            Assert.That(field.DirtyCount, Is.EqualTo(1));

            field.Set(new FieldCell2D(-1, -1), 0);
            Assert.That(field.NonDefaultCount, Is.EqualTo(2));
        }

        [Test]
        public void ChunkedField2D_ReplacesValuesAndSupportsVectorLanes()
        {
            var scalar = new ChunkedField2D<float>(
                new FieldGridSpec2D(cellSizeCm: 100, chunkSizeCells: 4),
                defaultValue: 0f,
                initialChunkCapacity: 2);
            scalar.Set(new FieldCell2D(1, 1), 2f);
            scalar.Set(new FieldCell2D(2, 1), 2f);
            scalar.Set(new FieldCell2D(3, 1), 4f);
            scalar.ClearDirty();

            int replaced = scalar.ReplaceValue(2f, 5f);

            Assert.That(replaced, Is.EqualTo(2));
            Assert.That(scalar.Get(new FieldCell2D(1, 1)), Is.EqualTo(5f));
            Assert.That(scalar.Get(new FieldCell2D(3, 1)), Is.EqualTo(4f));
            Assert.That(scalar.DirtyCount, Is.EqualTo(2));
            Assert.That(scalar.ChannelKind, Is.EqualTo(FieldChannelKind.Float32));
            Assert.That(scalar.ChannelCount, Is.EqualTo(1));

            var vector = new ChunkedField2D<Vector2>(
                new FieldGridSpec2D(cellSizeCm: 100, chunkSizeCells: 4),
                defaultValue: Vector2.Zero,
                initialChunkCapacity: 2);
            vector.Set(new FieldCell2D(0, 0), new Vector2(3f, -1f));

            FieldChunk2D<Vector2> vectorChunk = vector.GetChunkAt(0);
            ReadOnlySpan<float> x = vectorChunk.GetFloatChannel(0);
            ReadOnlySpan<float> y = vectorChunk.GetFloatChannel(1);
            int local = vector.Grid.LocalIndex(0, 0);
            Assert.That(vector.ChannelKind, Is.EqualTo(FieldChannelKind.Float32));
            Assert.That(vector.ChannelCount, Is.EqualTo(2));
            Assert.That(x[local], Is.EqualTo(3f));
            Assert.That(y[local], Is.EqualTo(-1f));
            Assert.That(vector.Get(new FieldCell2D(0, 0)), Is.EqualTo(new Vector2(3f, -1f)));
        }

        [Test]
        public void ChunkedField2D_VectorSoAReadWriteHotPathAllocatesZeroAfterWarmup()
        {
            var field = new ChunkedField2D<Vector2>(
                new FieldGridSpec2D(cellSizeCm: 100, chunkSizeCells: 16),
                defaultValue: Vector2.Zero,
                initialChunkCapacity: 4);

            for (int i = 0; i < 256; i++)
            {
                field.Set(new FieldCell2D(i & 15, i >> 4), new Vector2(i, -i));
            }

            field.ClearDirty();
            var values = new FieldCellValue2D<Vector2>[256];
            field.CopyNonDefaultCells(values);

            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            long allocated = MeasureVectorFieldAllocations(field, values, out float observed);
            Assert.That(observed, Is.GreaterThan(0f));
            Assert.That(allocated, Is.EqualTo(0));
        }

        [Test]
        public void ChunkedField2D_ReadWriteDirtyAndCopyHotPathAllocatesZeroAfterWarmup()
        {
            var field = new ChunkedField2D<byte>(
                new FieldGridSpec2D(cellSizeCm: 100, chunkSizeCells: 16),
                defaultValue: 0,
                initialChunkCapacity: 4);

            for (int i = 0; i < 256; i++)
            {
                field.Set(new FieldCell2D(i & 15, i >> 4), 1);
            }

            field.ClearDirty();
            var dirty = new FieldCell2D[256];
            var values = new FieldCellValue2D<byte>[256];
            field.Set(new FieldCell2D(0, 0), 2);
            field.Set(new FieldCell2D(0, 0), 1);
            field.EnumerateDirtyCells(dirty);
            field.CopyNonDefaultCells(values);
            field.ClearDirty();

            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            long allocated = MeasureByteFieldAllocations(field, dirty, values, out int observed);
            Assert.That(observed, Is.GreaterThan(0));
            Assert.That(allocated, Is.EqualTo(0));
        }

        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        private static long MeasureVectorFieldAllocations(
            ChunkedField2D<Vector2> field,
            FieldCellValue2D<Vector2>[] values,
            out float observed)
        {
            GC.GetAllocatedBytesForCurrentThread();
            long before = GC.GetAllocatedBytesForCurrentThread();
            observed = 0f;
            for (int i = 0; i < 10_000; i++)
            {
                FieldCell2D cell = new(i & 15, (i >> 4) & 15);
                Vector2 value = new(i, i * 2f);
                field.Set(cell, value);
                observed += field.Get(cell).X;
                field.CopyNonDefaultCells(values);
            }

            return GC.GetAllocatedBytesForCurrentThread() - before;
        }

        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        private static long MeasureByteFieldAllocations(
            ChunkedField2D<byte> field,
            FieldCell2D[] dirty,
            FieldCellValue2D<byte>[] values,
            out int observed)
        {
            GC.GetAllocatedBytesForCurrentThread();
            long before = GC.GetAllocatedBytesForCurrentThread();
            observed = 0;
            for (int i = 0; i < 10_000; i++)
            {
                FieldCell2D cell = new(i & 15, (i >> 4) & 15);
                field.Set(cell, (byte)((i & 1) + 1));
                observed += field.Get(cell);
                field.EnumerateDirtyCells(dirty);
                field.CopyNonDefaultCells(values);
            }

            return GC.GetAllocatedBytesForCurrentThread() - before;
        }
    }
}
