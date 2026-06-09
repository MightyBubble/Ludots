using System;
using System.IO;
using System.Text;
using Ludots.Core.Mathematics;

namespace Ludots.Core.Presentation.Terrain
{
    internal sealed class VisualHeightmapFileReader : IVisualHeightmapSampleAccessor, IDisposable
    {
        private const string Magic = "VHTM";
        private const int Version = 2;

        private readonly Stream _stream;
        private readonly BinaryReader _reader;
        private readonly object _lock = new();
        private readonly long _sampleDataOffset;
        private readonly bool _usesRawUInt16Samples;
        private readonly RowCache _rowA = new();
        private readonly RowCache _rowB = new();
        private bool _preferRowA = true;
        private bool _disposed;

        private VisualHeightmapFileReader(Stream stream)
        {
            if (stream == null) throw new ArgumentNullException(nameof(stream));
            if (!stream.CanSeek) throw new ArgumentException("Visual heightmap stream must be seekable.", nameof(stream));

            _stream = stream;
            _reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true);
            ReadHeader();
            _sampleDataOffset = _stream.Position;
            _usesRawUInt16Samples = StorageLayout == VisualHeightmapStorageLayout.RowMajorUInt16Scaled ||
                                    StorageLayout == VisualHeightmapStorageLayout.ChunkedRowMajorUInt16Scaled;
        }

        public WorldAabbCm Bounds { get; private set; }

        public int SampleColumns { get; private set; }

        public int SampleRows { get; private set; }

        public VisualHeightmapStorageLayout StorageLayout { get; private set; }

        public int DefaultLayerIndex { get; private set; }

        public VisualHeightmapInterpolationMode InterpolationMode { get; private set; }

        public VisualHeightSampleScale SampleScale { get; private set; }

        public VisualHeightmapLayerDefinition[] Layers { get; private set; } = Array.Empty<VisualHeightmapLayerDefinition>();

        public int SampleCount { get; private set; }

        public static VisualHeightmapFileReader Open(Stream stream)
        {
            return new VisualHeightmapFileReader(stream);
        }

        public bool TrySampleHeightCm(float worldXCm, float worldYCm, out float heightCm, int layerIndex = 0)
        {
            heightCm = default;
            WorldAabbCm bounds = Bounds;
            return TryResolveLayer(layerIndex, out VisualHeightmapLayerDefinition layer) &&
                   VisualHeightmapQueries.TrySampleHeightCm(
                       this,
                       in bounds,
                       SampleColumns,
                       SampleRows,
                       InterpolationMode,
                       layer.SampleOffset,
                       worldXCm,
                       worldYCm,
                       out heightCm);
        }

        bool IVisualHeightmapSampleAccessor.TryReadSampleCm(int layerSampleOffset, int sampleX, int sampleY, out float heightCm)
        {
            heightCm = default;
            if (_disposed) throw new ObjectDisposedException(nameof(VisualHeightmapFileReader));
            if ((uint)sampleX >= (uint)SampleColumns || (uint)sampleY >= (uint)SampleRows)
            {
                return false;
            }

            int sampleIndex = layerSampleOffset + checked(sampleY * SampleColumns) + sampleX;
            if ((uint)sampleIndex >= (uint)SampleCount)
            {
                return false;
            }

            lock (_lock)
            {
                if (TryGetCachedSample(layerSampleOffset, sampleX, sampleY, out heightCm))
                {
                    return true;
                }

                RowCache target = _preferRowA ? _rowA : _rowB;
                _preferRowA = !_preferRowA;
                LoadRow(target, layerSampleOffset, sampleY);
                heightCm = target.Samples[sampleX];
                return true;
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _reader.Dispose();
            _disposed = true;
        }

        private void ReadHeader()
        {
            string magic = Encoding.ASCII.GetString(ReadExact(4));
            if (!string.Equals(magic, Magic, StringComparison.Ordinal))
            {
                throw new InvalidDataException("Invalid visual heightmap binary magic.");
            }

            int version = _reader.ReadInt32();
            if (version != 1 && version != Version)
            {
                throw new InvalidDataException($"Unsupported visual heightmap binary version: {version}.");
            }

            Bounds = new WorldAabbCm(
                _reader.ReadInt32(),
                _reader.ReadInt32(),
                _reader.ReadInt32(),
                _reader.ReadInt32());

            SampleColumns = _reader.ReadInt32();
            SampleRows = _reader.ReadInt32();
            StorageLayout = (VisualHeightmapStorageLayout)_reader.ReadInt32();
            DefaultLayerIndex = _reader.ReadInt32();
            InterpolationMode = version >= 2
                ? (VisualHeightmapInterpolationMode)_reader.ReadInt32()
                : VisualHeightmapInterpolationMode.BilinearHeightfield;
            SampleScale = version >= 2
                ? new VisualHeightSampleScale(_reader.ReadInt32(), _reader.ReadInt32(), _reader.ReadInt32())
                : VisualHeightSampleScale.IdentityCentimeters;

            if (SampleColumns <= 0 || SampleRows <= 0)
            {
                throw new InvalidDataException("Visual heightmap dimensions must be positive.");
            }

            if (Bounds.Width <= 0 || Bounds.Height <= 0)
            {
                throw new InvalidDataException("Visual heightmap bounds must be positive.");
            }

            if ((uint)DefaultLayerIndex >= int.MaxValue)
            {
                throw new InvalidDataException("Visual heightmap default layer index is invalid.");
            }

            if (StorageLayout != VisualHeightmapStorageLayout.RowMajorInt16Centimeters &&
                StorageLayout != VisualHeightmapStorageLayout.ChunkedRowMajorInt16Centimeters &&
                StorageLayout != VisualHeightmapStorageLayout.RowMajorUInt16Scaled &&
                StorageLayout != VisualHeightmapStorageLayout.ChunkedRowMajorUInt16Scaled)
            {
                throw new InvalidDataException($"Unsupported visual heightmap storage layout: {StorageLayout}.");
            }

            SampleScale.Validate();

            int layerCount = _reader.ReadInt32();
            if (layerCount <= 0)
            {
                throw new InvalidDataException("Visual heightmap binary must contain at least one layer.");
            }

            var layers = new VisualHeightmapLayerDefinition[layerCount];
            for (int i = 0; i < layerCount; i++)
            {
                layers[i] = new VisualHeightmapLayerDefinition(
                    _reader.ReadInt32(),
                    _reader.ReadString(),
                    _reader.ReadInt32(),
                    _reader.ReadInt32());
            }

            if ((uint)DefaultLayerIndex >= (uint)layers.Length)
            {
                throw new InvalidDataException("Visual heightmap default layer index is out of range.");
            }

            SampleCount = _reader.ReadInt32();
            if (SampleCount < 0)
            {
                throw new InvalidDataException("Visual heightmap binary sample count cannot be negative.");
            }

            int samplesPerLayer = checked(SampleColumns * SampleRows);
            for (int i = 0; i < layers.Length; i++)
            {
                VisualHeightmapLayerDefinition layer = layers[i];
                if (layer.SampleOffset < 0 ||
                    layer.SampleCount != samplesPerLayer ||
                    layer.SampleOffset + layer.SampleCount > SampleCount)
                {
                    throw new InvalidDataException($"Visual heightmap layer {i} does not match the declared sample layout.");
                }
            }

            Layers = layers;

            long requiredBytes = checked((long)SampleCount * sizeof(ushort));
            if (_stream.CanSeek && _stream.Length - _stream.Position < requiredBytes)
            {
                throw new EndOfStreamException();
            }
        }

        private bool TryResolveLayer(int layerIndex, out VisualHeightmapLayerDefinition layer)
        {
            int resolvedLayer = layerIndex >= 0 ? layerIndex : DefaultLayerIndex;
            if ((uint)resolvedLayer >= (uint)Layers.Length)
            {
                layer = default;
                return false;
            }

            layer = Layers[resolvedLayer];
            return true;
        }

        private bool TryGetCachedSample(int layerSampleOffset, int sampleX, int sampleY, out float heightCm)
        {
            if (_rowA.IsFor(layerSampleOffset, sampleY))
            {
                heightCm = _rowA.Samples[sampleX];
                return true;
            }

            if (_rowB.IsFor(layerSampleOffset, sampleY))
            {
                heightCm = _rowB.Samples[sampleX];
                return true;
            }

            heightCm = default;
            return false;
        }

        private void LoadRow(RowCache target, int layerSampleOffset, int sampleY)
        {
            target.EnsureCapacity(SampleColumns);
            int rowStart = checked(layerSampleOffset + (sampleY * SampleColumns));
            long offset = checked(_sampleDataOffset + ((long)rowStart * sizeof(ushort)));
            int rowBytes = checked(SampleColumns * sizeof(ushort));
            byte[] raw = ReadExactAt(offset, rowBytes);

            if (_usesRawUInt16Samples)
            {
                var samples = new ushort[SampleColumns];
                Buffer.BlockCopy(raw, 0, samples, 0, raw.Length);
                for (int i = 0; i < samples.Length; i++)
                {
                    target.Samples[i] = SampleScale.Decode(samples[i]);
                }
            }
            else
            {
                var samples = new short[SampleColumns];
                Buffer.BlockCopy(raw, 0, samples, 0, raw.Length);
                for (int i = 0; i < samples.Length; i++)
                {
                    target.Samples[i] = samples[i];
                }
            }

            target.LayerSampleOffset = layerSampleOffset;
            target.SampleY = sampleY;
        }

        private byte[] ReadExactAt(long offset, int byteCount)
        {
            _stream.Position = offset;
            return ReadExact(byteCount);
        }

        private byte[] ReadExact(int byteCount)
        {
            byte[] bytes = _reader.ReadBytes(byteCount);
            if (bytes.Length != byteCount) throw new EndOfStreamException();
            return bytes;
        }

        private sealed class RowCache
        {
            public int LayerSampleOffset = -1;

            public int SampleY = -1;

            public float[] Samples = Array.Empty<float>();

            public bool IsFor(int layerSampleOffset, int sampleY)
            {
                return LayerSampleOffset == layerSampleOffset && SampleY == sampleY;
            }

            public void EnsureCapacity(int sampleColumns)
            {
                if (Samples.Length != sampleColumns)
                {
                    Samples = new float[sampleColumns];
                }
            }
        }
    }
}
