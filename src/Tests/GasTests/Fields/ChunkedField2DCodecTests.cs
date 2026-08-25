using System;
using System.Runtime.CompilerServices;
using Ludots.Core.Fields;
using NUnit.Framework;

namespace Ludots.Tests.GAS
{
    [TestFixture]
    public sealed class ChunkedField2DCodecTests
    {
        private static readonly FieldGridSpec2D Grid = new(cellSizeCm: 100, chunkSizeCells: 8);

        [Test]
        public void DefaultConstruction_PreservesStaticCodecBehavior()
        {
            var field = new ChunkedField2D<byte>(Grid, defaultValue: 0, initialChunkCapacity: 4);
            var cell = new FieldCell2D(0, 0);

            field.Set(cell, 7);
            Assert.That(field.Get(cell), Is.EqualTo(7));
            Assert.That(field.ChannelCount, Is.EqualTo(1));
            Assert.That(field.ChannelKind, Is.EqualTo(FieldChannelKind.Struct));
        }

        [Test]
        public void DiscreteIdField_UsesIntStructCodecDirectly()
        {
            var field = new ChunkedField2D<int>(Grid, defaultValue: 0, initialChunkCapacity: 4);
            var cell = new FieldCell2D(0, 0);

            Assert.That(field.Set(cell, 7), Is.True);
            Assert.That(field.Get(cell), Is.EqualTo(7));
            Assert.That(field.Set(cell, 7), Is.False);
            Assert.That(field.ChannelCount, Is.EqualTo(1));
            Assert.That(field.ChannelKind, Is.EqualTo(FieldChannelKind.Struct));
            Assert.That(field.NonDefaultCount, Is.EqualTo(1));
        }

        [Test]
        public void InjectedCodec_IsConsulted_AndMatchesDefaultBehavior()
        {
            var counting = new CountingCodec<float>(FieldValueCodec<float>.Instance);
            var injected = new ChunkedField2D<float>(Grid, defaultValue: 0f, initialChunkCapacity: 2, codec: counting);
            var reference = new ChunkedField2D<float>(Grid, defaultValue: 0f, initialChunkCapacity: 2);

            RunOperationSurface(injected);
            RunOperationSurface(reference);

            Assert.That(injected.ChannelCount, Is.EqualTo(reference.ChannelCount));
            Assert.That(injected.ChannelKind, Is.EqualTo(reference.ChannelKind));
            Assert.That(injected.NonDefaultCount, Is.EqualTo(reference.NonDefaultCount));
            Assert.That(injected.DirtyCount, Is.EqualTo(reference.DirtyCount));
            Assert.That(injected.Get(new FieldCell2D(0, 0)), Is.EqualTo(reference.Get(new FieldCell2D(0, 0))));
            Assert.That(injected.Get(new FieldCell2D(1, 1)), Is.EqualTo(reference.Get(new FieldCell2D(1, 1))));

            Assert.That(counting.ReadCount, Is.GreaterThan(0), "the injected codec reads must be exercised");
            Assert.That(counting.WriteCount, Is.GreaterThan(0), "the injected codec writes must be exercised");
            Assert.That(counting.ValueEqualsCount, Is.GreaterThan(0), "the injected codec equality must be exercised");
        }

        [Test]
        public void EqualitySemantics_AreDecidedByTheInjectedCodec()
        {
            var field = new ChunkedField2D<Pair>(Grid, defaultValue: default, initialChunkCapacity: 2, codec: new PairACodec());
            var cell = new FieldCell2D(0, 0);

            Assert.That(field.Set(cell, new Pair(5, 9)), Is.True);
            Assert.That(field.Get(cell), Is.EqualTo(new Pair(5, 9)));
            Assert.That(field.Set(cell, new Pair(5, 99)), Is.False, "pairs with equal A are equal even when B differs");
            Assert.That(field.Get(cell), Is.EqualTo(new Pair(5, 9)), "the rejected write must not overwrite");
            Assert.That(field.Set(cell, new Pair(7, 99)), Is.True);
            Assert.That(field.Get(cell), Is.EqualTo(new Pair(7, 99)));
            Assert.That(field.NonDefaultCount, Is.EqualTo(1));

            Assert.That(field.ReplaceValue(new Pair(7, 0), new Pair(8, 0)), Is.EqualTo(1), "ReplaceValue matches by the injected equality (A only)");
            Assert.That(field.Get(cell), Is.EqualTo(new Pair(8, 0)));
            Assert.That(field.NonDefaultCount, Is.EqualTo(1));
            Assert.That(field.ChannelCount, Is.EqualTo(2));
            Assert.That(field.ChannelKind, Is.EqualTo(FieldChannelKind.Struct));
        }

        [Test]
        public void FloatEquality_KeepsNanEqualsNanSemantics()
        {
            var field = new ChunkedField2D<float>(Grid, defaultValue: 0f);
            var cell = new FieldCell2D(0, 0);

            Assert.That(field.Set(cell, float.NaN), Is.True);
            Assert.That(field.Set(cell, float.NaN), Is.False, "NaN.Equals(NaN) is true, so a repeated NaN set is a no-op");
            Assert.That(float.IsNaN(field.Get(cell)), Is.True);
            Assert.That(field.NonDefaultCount, Is.EqualTo(1));

            Assert.That(field.ReplaceValue(float.NaN, 1f), Is.EqualTo(1));
            Assert.That(field.Get(cell), Is.EqualTo(1f));
            Assert.That(field.NonDefaultCount, Is.EqualTo(1));
        }

        [Test]
        public void InjectedCodec_HotPath_AllocatesZeroAfterWarmup()
        {
            var field = new ChunkedField2D<Pair>(
                new FieldGridSpec2D(cellSizeCm: 100, chunkSizeCells: 16),
                defaultValue: default,
                initialChunkCapacity: 4,
                codec: new PairACodec());

            for (int i = 0; i < 256; i++)
            {
                field.Set(new FieldCell2D(i & 15, i >> 4), new Pair((byte)(i + 1), (byte)i));
            }

            field.ClearDirty();
            var values = new FieldCellValue2D<Pair>[256];
            field.CopyNonDefaultCells(values);

            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            long allocated = MeasureInjectedCodecAllocations(field, values, out int observed);
            Assert.That(observed, Is.GreaterThan(0));
            Assert.That(allocated, Is.EqualTo(0), "the injected codec read/write path must stay allocation free");
        }

        private static void RunOperationSurface(ChunkedField2D<float> field)
        {
            var a = new FieldCell2D(0, 0);
            var b = new FieldCell2D(1, 1);
            field.Set(a, 2f);
            field.Set(b, 3f);
            FieldChunk2D<float> chunk = field.GetChunkAt(0);
            Assert.That(chunk.GetFloatChannel(0).Length, Is.EqualTo(Grid.ChunkSizeCells * Grid.ChunkSizeCells));
            Assert.That(field.Get(a), Is.EqualTo(2f));
            Assert.That(field.TryGet(b, out float got), Is.True);
            Assert.That(got, Is.EqualTo(3f));
            Assert.That(field.TryGet(new FieldCell2D(100, 100), out _), Is.False, "cells outside any existing chunk are absent");
            Assert.That(field.NonDefaultCount, Is.EqualTo(2));
            Assert.That(field.DirtyCount, Is.EqualTo(2));

            field.MarkDirty(b);
            Assert.That(field.DirtyCount, Is.EqualTo(2), "re-dirtying a dirty cell deduplicates");

            Assert.That(field.ReplaceValue(2f, 4f), Is.EqualTo(1));
            Assert.That(field.Get(a), Is.EqualTo(4f));

            Span<FieldCell2D> dirty = stackalloc FieldCell2D[16];
            Assert.That(field.EnumerateDirtyCells(dirty), Is.GreaterThan(0));
            Span<FieldCellValue2D<float>> values = stackalloc FieldCellValue2D<float>[16];
            Assert.That(field.CopyNonDefaultCells(values), Is.EqualTo(2));

            field.ClearDirty();
            Assert.That(field.DirtyCount, Is.EqualTo(0));

            field.Set(b, 2f);
            Assert.That(field.Set(b, 2f), Is.False, "setting the same value again is a no-op");
            field.Clear();
            Assert.That(field.NonDefaultCount, Is.EqualTo(0));
            Assert.That(field.DirtyCount, Is.EqualTo(0));
            Assert.That(field.Get(a), Is.EqualTo(0f));
            Assert.That(field.Get(b), Is.EqualTo(0f));
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static long MeasureInjectedCodecAllocations(
            ChunkedField2D<Pair> field,
            FieldCellValue2D<Pair>[] values,
            out int observed)
        {
            GC.GetAllocatedBytesForCurrentThread();
            long before = GC.GetAllocatedBytesForCurrentThread();
            observed = 0;
            for (int i = 0; i < 10_000; i++)
            {
                FieldCell2D cell = new(i & 15, (i >> 4) & 15);
                field.Set(cell, new Pair((byte)((i & 1) + 1), (byte)i));
                observed += field.Get(cell).A;
                field.CopyNonDefaultCells(values);
            }

            return GC.GetAllocatedBytesForCurrentThread() - before;
        }

        private sealed class CountingCodec<T> : IFieldValueCodec<T>
            where T : struct
        {
            private readonly IFieldValueCodec<T> _inner;

            public CountingCodec(IFieldValueCodec<T> inner)
            {
                _inner = inner;
            }

            public int ReadCount;
            public int WriteCount;
            public int ValueEqualsCount;

            public int ChannelCount => _inner.ChannelCount;
            public FieldChannelKind ChannelKind => _inner.ChannelKind;

            public Array[] CreateChannels(int cellCount, T defaultValue) => _inner.CreateChannels(cellCount, defaultValue);

            public T Read(Array[] channels, int localIndex)
            {
                ReadCount++;
                return _inner.Read(channels, localIndex);
            }

            public void Write(Array[] channels, int localIndex, T value)
            {
                WriteCount++;
                _inner.Write(channels, localIndex, value);
            }

            public bool ValueEquals(T left, T right)
            {
                ValueEqualsCount++;
                return _inner.ValueEquals(left, right);
            }

            public ReadOnlySpan<float> GetFloatChannel(Array[] channels, int channelIndex)
                => _inner.GetFloatChannel(channels, channelIndex);
        }

        private readonly struct Pair : IEquatable<Pair>
        {
            public Pair(byte a, byte b)
            {
                A = a;
                B = b;
            }

            public readonly byte A;
            public readonly byte B;

            public bool Equals(Pair other) => A == other.A && B == other.B;
            public override bool Equals(object? obj) => obj is Pair other && Equals(other);
            public override int GetHashCode() => HashCode.Combine(A, B);
        }

        private sealed class PairACodec : IFieldValueCodec<Pair>
        {
            public int ChannelCount => 2;
            public FieldChannelKind ChannelKind => FieldChannelKind.Struct;

            public Array[] CreateChannels(int cellCount, Pair defaultValue)
            {
                var a = new byte[cellCount];
                var b = new byte[cellCount];
                if (defaultValue.A != 0)
                {
                    Array.Fill(a, defaultValue.A);
                }

                if (defaultValue.B != 0)
                {
                    Array.Fill(b, defaultValue.B);
                }

                return new Array[] { a, b };
            }

            public Pair Read(Array[] channels, int localIndex)
            {
                return new Pair(
                    ((byte[])channels[0])[localIndex],
                    ((byte[])channels[1])[localIndex]);
            }

            public void Write(Array[] channels, int localIndex, Pair value)
            {
                ((byte[])channels[0])[localIndex] = value.A;
                ((byte[])channels[1])[localIndex] = value.B;
            }

            public bool ValueEquals(Pair left, Pair right) => left.A == right.A;

            public ReadOnlySpan<float> GetFloatChannel(Array[] channels, int channelIndex)
            {
                throw new InvalidOperationException("Pair fields do not expose float channels.");
            }
        }
    }
}
