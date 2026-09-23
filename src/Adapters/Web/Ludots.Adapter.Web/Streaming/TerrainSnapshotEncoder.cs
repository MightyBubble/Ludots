using System;
using System.Buffers.Binary;
using Ludots.Adapter.Web.Protocol;
using Ludots.Core.Presentation.Terrain;

namespace Ludots.Adapter.Web.Streaming
{
    public static class TerrainSnapshotEncoder
    {
        public static byte[] Encode(IVisualHeightmapRenderSource source)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            if (source.ChunkColumns <= 0 || source.ChunkRows <= 0)
            {
                throw new InvalidOperationException(
                    $"Visual terrain source has invalid chunk shape {source.ChunkColumns}x{source.ChunkRows}.");
            }

            int chunkCount = checked(source.ChunkColumns * source.ChunkRows);
            var chunks = new VisualHeightmapRenderChunk[chunkCount];
            int totalSize = TerrainSnapshotProtocol.HeaderSize;
            int chunkIndex = 0;
            for (int chunkY = 0; chunkY < source.ChunkRows; chunkY++)
            {
                for (int chunkX = 0; chunkX < source.ChunkColumns; chunkX++)
                {
                    if (!source.TryGetChunk(chunkX, chunkY, out VisualHeightmapRenderChunk chunk))
                    {
                        throw new InvalidOperationException(
                            $"Visual terrain chunk ({chunkX},{chunkY}) is missing from the declared render source.");
                    }

                    if (chunk.ChunkX != chunkX || chunk.ChunkY != chunkY)
                    {
                        throw new InvalidOperationException(
                            $"Visual terrain chunk ({chunkX},{chunkY}) returned mismatched coordinates ({chunk.ChunkX},{chunk.ChunkY}).");
                    }

                    int sampleCount = checked(chunk.SampleColumns * chunk.SampleRows);
                    totalSize = checked(totalSize + TerrainSnapshotProtocol.ChunkHeaderSize);
                    totalSize = checked(totalSize + (sampleCount * TerrainSnapshotProtocol.HeightSampleSize));
                    chunks[chunkIndex++] = chunk;
                }
            }

            var message = new byte[totalSize];
            Span<byte> destination = message;
            int cursor = 0;
            destination[cursor++] = FrameProtocol.MsgTypeTerrainSnapshot;
            WriteUInt16(destination, ref cursor, TerrainSnapshotProtocol.Version);
            WriteInt32(destination, ref cursor, source.Revision);
            WriteInt32(destination, ref cursor, source.Bounds.X);
            WriteInt32(destination, ref cursor, source.Bounds.Y);
            WriteInt32(destination, ref cursor, source.Bounds.Width);
            WriteInt32(destination, ref cursor, source.Bounds.Height);
            WriteInt32(destination, ref cursor, source.ChunkColumns);
            WriteInt32(destination, ref cursor, source.ChunkRows);
            WriteInt32(destination, ref cursor, source.SamplesPerChunkColumn);
            WriteInt32(destination, ref cursor, source.SamplesPerChunkRow);
            WriteInt32(destination, ref cursor, source.DefaultLayerIndex);
            WriteInt32(destination, ref cursor, chunkCount);

            for (int i = 0; i < chunks.Length; i++)
            {
                VisualHeightmapRenderChunk chunk = chunks[i];
                int sampleCount = checked(chunk.SampleColumns * chunk.SampleRows);
                WriteInt32(destination, ref cursor, chunk.ChunkX);
                WriteInt32(destination, ref cursor, chunk.ChunkY);
                WriteInt32(destination, ref cursor, chunk.Revision);
                WriteInt32(destination, ref cursor, chunk.Bounds.X);
                WriteInt32(destination, ref cursor, chunk.Bounds.Y);
                WriteInt32(destination, ref cursor, chunk.Bounds.Width);
                WriteInt32(destination, ref cursor, chunk.Bounds.Height);
                WriteInt32(destination, ref cursor, chunk.SampleColumns);
                WriteInt32(destination, ref cursor, chunk.SampleRows);
                WriteSingle(destination, ref cursor, chunk.SampleStepXCm);
                WriteSingle(destination, ref cursor, chunk.SampleStepYCm);
                WriteInt32(destination, ref cursor, sampleCount);

                for (int sampleY = 0; sampleY < chunk.SampleRows; sampleY++)
                {
                    for (int sampleX = 0; sampleX < chunk.SampleColumns; sampleX++)
                    {
                        if (!chunk.TryReadHeightCm(sampleX, sampleY, out float heightCm) || !float.IsFinite(heightCm))
                        {
                            throw new InvalidOperationException(
                                $"Visual terrain chunk ({chunk.ChunkX},{chunk.ChunkY}) has an invalid height at sample ({sampleX},{sampleY}).");
                        }

                        WriteSingle(destination, ref cursor, heightCm);
                    }
                }
            }

            if (cursor != message.Length)
            {
                throw new InvalidOperationException(
                    $"Visual terrain snapshot encoded {cursor} bytes but allocated {message.Length} bytes.");
            }

            return message;
        }

        private static void WriteUInt16(Span<byte> destination, ref int cursor, ushort value)
        {
            BinaryPrimitives.WriteUInt16LittleEndian(destination.Slice(cursor, sizeof(ushort)), value);
            cursor += sizeof(ushort);
        }

        private static void WriteInt32(Span<byte> destination, ref int cursor, int value)
        {
            BinaryPrimitives.WriteInt32LittleEndian(destination.Slice(cursor, sizeof(int)), value);
            cursor += sizeof(int);
        }

        private static void WriteSingle(Span<byte> destination, ref int cursor, float value)
        {
            BinaryPrimitives.WriteSingleLittleEndian(destination.Slice(cursor, sizeof(float)), value);
            cursor += sizeof(float);
        }
    }
}
