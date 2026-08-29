using System;
using Ludots.Core.Fields;
using NUnit.Framework;

namespace Ludots.Tests.GAS
{
    [TestFixture]
    public sealed class FieldDirtyCursorTests
    {
        private static readonly FieldGridSpec2D Grid = new(cellSizeCm: 100, chunkSizeCells: 8);

        [Test]
        public void Cursor_DeliversChangedChunks_AndAdvances()
        {
            var field = new ChunkedField2D<int>(Grid, defaultValue: 0);
            var cursor = field.OpenDirtyCursor();
            Assert.That(cursor.PendingChunkCount, Is.EqualTo(0));

            field.Set(new FieldCell2D(0, 0), 1);
            Assert.That(cursor.PendingChunkCount, Is.EqualTo(1));
            Assert.That(cursor.TryTakeChangedChunk(out FieldChunk2D<int> chunk), Is.True);
            Assert.That(chunk.ChunkX, Is.EqualTo(0));
            Assert.That(chunk.ChunkY, Is.EqualTo(0));
            Assert.That(cursor.TryTakeChangedChunk(out _), Is.False, "nothing new after draining");
        }

        [Test]
        public void TwoCursors_ReceiveTheSameIncrement_Independently()
        {
            var field = new ChunkedField2D<int>(Grid, defaultValue: 0);
            var first = field.OpenDirtyCursor();
            var second = field.OpenDirtyCursor();

            field.Set(new FieldCell2D(3, 3), 5);

            Assert.That(first.TryTakeChangedChunk(out FieldChunk2D<int> firstChunk), Is.True);
            Assert.That(second.TryTakeChangedChunk(out FieldChunk2D<int> secondChunk), Is.True);
            Assert.That(firstChunk, Is.SameAs(secondChunk), "both consumers observe the same change");
            Assert.That(first.TryTakeChangedChunk(out _), Is.False);
            Assert.That(second.TryTakeChangedChunk(out _), Is.False);
        }

        [Test]
        public void Cursor_IsUnaffectedBy_ClearDirty()
        {
            var field = new ChunkedField2D<int>(Grid, defaultValue: 0);
            var cursor = field.OpenDirtyCursor();

            field.Set(new FieldCell2D(1, 1), 2);
            field.ClearDirty();
            Assert.That(field.DirtyCount, Is.EqualTo(0), "single-reader mask is cleared");

            Assert.That(cursor.PendingChunkCount, Is.EqualTo(1), "cursor still sees the change");
            Assert.That(cursor.TryTakeChangedChunk(out _), Is.True);
        }

        [Test]
        public void ChunkChangedAgain_IsRedelivered()
        {
            var field = new ChunkedField2D<int>(Grid, defaultValue: 0);
            var cursor = field.OpenDirtyCursor();

            field.Set(new FieldCell2D(0, 0), 1);
            cursor.TryTakeChangedChunk(out _);
            Assert.That(cursor.TryTakeChangedChunk(out _), Is.False);

            field.Set(new FieldCell2D(0, 1), 1);
            Assert.That(cursor.TryTakeChangedChunk(out _), Is.True, "a later change re-delivers the chunk");
        }

        [Test]
        public void DifferentChunks_DeliveredInAnyOrder_WithoutLosingOne()
        {
            var field = new ChunkedField2D<int>(Grid, defaultValue: 0);
            var cursor = field.OpenDirtyCursor();

            field.Set(new FieldCell2D(0, 0), 1);    // chunk (0,0)
            field.Set(new FieldCell2D(64, 0), 1);   // chunk (8,0) — interleaved stamps
            field.Set(new FieldCell2D(1, 0), 1);    // chunk (0,0) again

            int delivered = 0;
            while (cursor.TryTakeChangedChunk(out _))
            {
                delivered++;
            }

            Assert.That(delivered, Is.EqualTo(2), "each changed chunk is delivered exactly once per drain");
        }

        [Test]
        public void CursorOpenedAfterChanges_DoesNotSeeThem()
        {
            var field = new ChunkedField2D<int>(Grid, defaultValue: 0);
            field.Set(new FieldCell2D(2, 2), 3);

            var cursor = field.OpenDirtyCursor();
            Assert.That(cursor.PendingChunkCount, Is.EqualTo(0), "cursors subscribe from the moment they open");
        }
    }
}
