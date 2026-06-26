using Ludots.Core.Map.Fields;
using Ludots.Core.Spatial;
using NUnit.Framework;

namespace ArchitectureTests
{
    [TestFixture]
    public sealed class BoardFieldStoreTests
    {
        [Test]
        public void SparseStore_DoesNotInstantiateUnwrittenChunks_AndTracksDirtyPerChunk()
        {
            var store = CreateStore(4096, 4096);

            Assert.That(store.ResidentChunkCount, Is.EqualTo(0));
            Assert.That(store.GetCell(2048, 2048), Is.EqualTo(TestFieldCell.Default));

            store.SetCell(64, 64, TestFieldCell.Default);
            Assert.That(store.ResidentChunkCount, Is.EqualTo(0));

            store.SetCell(0, 0, new TestFieldCell(1, 100, 3));
            store.SetCell(4095, 4095, new TestFieldCell(2, 200, 4));

            Assert.That(store.ResidentChunkCount, Is.EqualTo(2));
            Assert.That(store.IsChunkResident(0, 0), Is.True);
            Assert.That(store.IsChunkResident(63, 63), Is.True);
            Assert.That(store.IsChunkDirty(0, 0), Is.True);

            store.ClearChunkDirty(0, 0);
            Assert.That(store.IsChunkDirty(0, 0), Is.False);
            Assert.That(store.IsChunkDirty(63, 63), Is.True);

            Assert.That(store.RemoveChunk(63, 63), Is.True);
            Assert.That(store.ResidentChunkCount, Is.EqualTo(1));
            Assert.That(store.GetCell(4095, 4095), Is.EqualTo(TestFieldCell.Default));
        }

        [Test]
        public void SparseStore_RandomReadWrite_MatchesDenseReference()
        {
            const int width = 257;
            const int height = 131;
            var store = CreateStore(width, height);
            var dense = new TestFieldCell[width * height];
            Array.Fill(dense, TestFieldCell.Default);
            var rng = new Random(12345);

            for (int i = 0; i < 500; i++)
            {
                int col = rng.Next(width);
                int row = rng.Next(height);
                var value = new TestFieldCell((byte)rng.Next(16), (ushort)rng.Next(1024), (byte)rng.Next(256));
                store.SetCell(col, row, value);
                dense[row * width + col] = value;
            }

            for (int i = 0; i < 1000; i++)
            {
                int col = rng.Next(width);
                int row = rng.Next(height);
                Assert.That(store.GetCell(col, row), Is.EqualTo(dense[row * width + col]));
            }
        }

        [Test]
        public void SparseStore_SubscribeToLoadedChunks_RemovesResidentChunkOnUnload()
        {
            var store = CreateStore(128, 128);
            var loadedChunks = new MockLoadedChunks();
            store.SetCell(10, 10, new TestFieldCell(4, 9, 1));
            store.SubscribeToLoadedChunks(loadedChunks);

            loadedChunks.Unload(BoardFieldStore<TestFieldCell>.ChunkKey(0, 0));

            Assert.That(store.ResidentChunkCount, Is.EqualTo(0));
            Assert.That(store.GetCell(10, 10), Is.EqualTo(TestFieldCell.Default));
        }

        [Test]
        public void ResidentHotPaths_GetSetAndSample_AreAllocationFree()
        {
            var store = CreateStore(128, 128);
            var value = new TestFieldCell(7, 77, 5);
            store.SetCell(5, 5, value);
            store.ClearDirty();

            for (int i = 0; i < 64; i++)
            {
                store.SetCell(5, 5, value);
                _ = store.GetCell(5, 5);
                _ = store.SampleWorldCm(5 * SpatialScaleDefaults.CellCm, 5 * SpatialScaleDefaults.CellCm);
            }

            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            GC.GetAllocatedBytesForCurrentThread();
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 1024; i++)
            {
                store.SetCell(5, 5, value);
                _ = store.GetCell(5, 5);
                _ = store.SampleWorldCm(5 * SpatialScaleDefaults.CellCm, 5 * SpatialScaleDefaults.CellCm);
            }

            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.That(allocated, Is.EqualTo(0));
        }

        private static BoardFieldStore<TestFieldCell> CreateStore(int width, int height)
            => new BoardFieldStore<TestFieldCell>(
                width,
                height,
                SpatialScaleDefaults.CellCm,
                TestFieldCell.Default,
                TestFieldCellCodec.Instance);

        private readonly struct TestFieldCell : IEquatable<TestFieldCell>
        {
            public static readonly TestFieldCell Default = new TestFieldCell(0, 0, 0);

            public TestFieldCell(byte height, ushort region, byte flags)
            {
                Height = height;
                Region = region;
                Flags = flags;
            }

            public byte Height { get; }

            public ushort Region { get; }

            public byte Flags { get; }

            public bool Equals(TestFieldCell other)
                => Height == other.Height && Region == other.Region && Flags == other.Flags;

            public override bool Equals(object? obj)
                => obj is TestFieldCell other && Equals(other);

            public override int GetHashCode()
                => HashCode.Combine(Height, Region, Flags);
        }

        private sealed class TestFieldCellCodec : IBoardFieldChunkCodec<TestFieldCell>
        {
            public static readonly TestFieldCellCodec Instance = new TestFieldCellCodec();

            public BoardFieldChunk<TestFieldCell> CreateChunk(int cellCount, TestFieldCell defaultValue)
            {
                var chunk = new TestFieldChunk(cellCount);
                chunk.Fill(defaultValue);
                return chunk;
            }
        }

        private sealed class TestFieldChunk : BoardFieldChunk<TestFieldCell>
        {
            private readonly byte[] _height;
            private readonly ushort[] _region;
            private readonly byte[] _flags;

            public TestFieldChunk(int cellCount)
                : base(cellCount)
            {
                _height = new byte[cellCount];
                _region = new ushort[cellCount];
                _flags = new byte[cellCount];
            }

            public override TestFieldCell GetCell(int index)
                => new TestFieldCell(_height[index], _region[index], _flags[index]);

            public override void SetCell(int index, TestFieldCell value)
            {
                _height[index] = value.Height;
                _region[index] = value.Region;
                _flags[index] = value.Flags;
            }

            public override void Fill(TestFieldCell value)
            {
                Array.Fill(_height, value.Height);
                Array.Fill(_region, value.Region);
                Array.Fill(_flags, value.Flags);
            }
        }

        private sealed class MockLoadedChunks : ILoadedChunks
        {
            public IReadOnlyCollection<long> ActiveChunkKeys => Array.Empty<long>();

            public bool IsLoaded(long chunkKey) => false;

            public event Action<long>? ChunkLoaded;

            public event Action<long>? ChunkUnloaded;

            public void Load(long chunkKey)
            {
                ChunkLoaded?.Invoke(chunkKey);
            }

            public void Unload(long chunkKey)
            {
                ChunkUnloaded?.Invoke(chunkKey);
            }
        }
    }
}
