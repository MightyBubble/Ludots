using System;
using System.Buffers.Binary;
using Ludots.Adapter.Web.Protocol;
using Ludots.Adapter.Web.Streaming;
using Ludots.Core.Mathematics;
using Ludots.Core.Presentation.Terrain;
using NUnit.Framework;

namespace Ludots.Tests.ThreeC
{
    [TestFixture]
    public sealed class WebTerrainSnapshotProtocolTests
    {
        [Test]
        public void Encode_UsesVisualHeightmapRenderSourceAsTerrainTruth()
        {
            short[] heightsCm =
            {
                0, 100, 200,
                300, 400, 500,
                600, 700, 800,
            };
            IVisualHeightmapRenderSource source = new VisualHeightmapRuntime(
                VisualHeightmapAsset.CreateSingleLayer(
                    new WorldAabbCm(-1000, 2000, 2000, 4000),
                    sampleColumns: 3,
                    sampleRows: 3,
                    heightsCm));

            byte[] message = TerrainSnapshotEncoder.Encode(source);
            ReadOnlySpan<byte> span = message;

            Assert.That(span[0], Is.EqualTo(FrameProtocol.MsgTypeTerrainSnapshot));
            Assert.That(BinaryPrimitives.ReadUInt16LittleEndian(span.Slice(1, 2)), Is.EqualTo(TerrainSnapshotProtocol.Version));
            Assert.That(BinaryPrimitives.ReadInt32LittleEndian(span.Slice(3, 4)), Is.EqualTo(source.Revision));
            Assert.That(BinaryPrimitives.ReadInt32LittleEndian(span.Slice(7, 4)), Is.EqualTo(-1000));
            Assert.That(BinaryPrimitives.ReadInt32LittleEndian(span.Slice(11, 4)), Is.EqualTo(2000));
            Assert.That(BinaryPrimitives.ReadInt32LittleEndian(span.Slice(15, 4)), Is.EqualTo(2000));
            Assert.That(BinaryPrimitives.ReadInt32LittleEndian(span.Slice(19, 4)), Is.EqualTo(4000));
            Assert.That(BinaryPrimitives.ReadInt32LittleEndian(span.Slice(43, 4)), Is.EqualTo(1));

            int chunkOffset = TerrainSnapshotProtocol.HeaderSize;
            Assert.That(BinaryPrimitives.ReadInt32LittleEndian(span.Slice(chunkOffset, 4)), Is.EqualTo(0));
            Assert.That(BinaryPrimitives.ReadInt32LittleEndian(span.Slice(chunkOffset + 4, 4)), Is.EqualTo(0));
            Assert.That(BinaryPrimitives.ReadInt32LittleEndian(span.Slice(chunkOffset + 28, 4)), Is.EqualTo(3));
            Assert.That(BinaryPrimitives.ReadInt32LittleEndian(span.Slice(chunkOffset + 32, 4)), Is.EqualTo(3));
            Assert.That(BinaryPrimitives.ReadInt32LittleEndian(span.Slice(chunkOffset + 44, 4)), Is.EqualTo(9));

            int sampleOffset = chunkOffset + TerrainSnapshotProtocol.ChunkHeaderSize;
            for (int i = 0; i < heightsCm.Length; i++)
            {
                float actual = BinaryPrimitives.ReadSingleLittleEndian(span.Slice(sampleOffset + (i * 4), 4));
                Assert.That(actual, Is.EqualTo(heightsCm[i]).Within(0.001f), $"height sample {i}");
            }

            Assert.That(message.Length, Is.EqualTo(sampleOffset + (heightsCm.Length * 4)));
        }

        [Test]
        public void Encode_WhenDeclaredChunkIsMissing_FailsExplicitly()
        {
            var source = new MissingChunkSource();

            var ex = Assert.Throws<InvalidOperationException>(() => TerrainSnapshotEncoder.Encode(source));

            Assert.That(ex!.Message, Does.Contain("terrain chunk (0,0)").IgnoreCase);
            Assert.That(ex.Message, Does.Contain("missing").IgnoreCase);
        }

        private sealed class MissingChunkSource : IVisualHeightmapRenderSource
        {
            public WorldAabbCm Bounds => new(0, 0, 1000, 1000);
            public int ChunkColumns => 1;
            public int ChunkRows => 1;
            public int SamplesPerChunkColumn => 2;
            public int SamplesPerChunkRow => 2;
            public int DefaultLayerIndex => 0;
            public int Revision => 7;

            public bool TryGetChunk(int chunkX, int chunkY, out VisualHeightmapRenderChunk chunk)
            {
                chunk = default;
                return false;
            }
        }
    }
}
