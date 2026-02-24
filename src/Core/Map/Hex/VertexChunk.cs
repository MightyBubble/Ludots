using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Ludots.Core.Map.Hex
{
    /// <summary>
    /// Represents a 64x64 chunk of vertex data in the dual grid system.
    /// Uses Structure of Arrays (SoA) and bit-packing for maximum memory efficiency.
    /// </summary>
    public class VertexChunk
    {
        public const int ChunkSize = 64;
        public const int ChunkSizeMask = 63;
        public const int ChunkSizeShift = 6;
        public const int TotalCells = ChunkSize * ChunkSize;
        public const int DerivedEdgesPerCell = 3;
        public const int DerivedCliffStraightenBitsPerEdge = 1;
        public const int DerivedCliffStraightenTotalBits = TotalCells * DerivedEdgesPerCell * DerivedCliffStraightenBitsPerEdge;
        public const int DerivedCliffStraightenBytes = DerivedCliffStraightenTotalBits / 8;
        public const int ExtraFlagLayers = 3;
        public const int ExtraByteLayers = 1;

        // Storage: 4KB for Terrain (High 4 bits) + Height (Low 4 bits)
        // Index = row * ChunkSize + col
        private readonly byte[] _packedData;
        
        private readonly byte[] _layer2; // Water + Vegetation

        // Storage: 512 Bytes for 1-bit flags (e.g., IsBlocked)
        // 64 ulongs * 64 bits = 4096 bits
        private readonly ulong[] _flags;
        
        // Storage: 512 Bytes for Ramp flags
        private readonly ulong[] _rampFlags;

        // Storage: 4KB for Faction ID (0-255)
        // Supports up to 256 factions per map
        private readonly byte[] _factions;

        private readonly ulong[][] _extraFlags;
        private readonly byte[][] _extraBytes;
        private readonly byte[] _cliffStraightenFlags;

        public VertexChunk()
        {
            _packedData = new byte[TotalCells]; // Layer 1: Biome + Height
            _layer2 = new byte[TotalCells];     // Layer 2: Veg + Water
            _flags = new ulong[TotalCells / 64];
            _rampFlags = new ulong[TotalCells / 64];
            _factions = new byte[TotalCells];
            _extraFlags = new ulong[ExtraFlagLayers][];
            for (int i = 0; i < _extraFlags.Length; i++) _extraFlags[i] = new ulong[TotalCells / 64];
            _extraBytes = new byte[ExtraByteLayers][];
            for (int i = 0; i < _extraBytes.Length; i++) _extraBytes[i] = new byte[TotalCells];
            _cliffStraightenFlags = new byte[DerivedCliffStraightenBytes];
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private int GetIndex(int localX, int localY)
        {
            return (localY << ChunkSizeShift) | localX;
        }

        // --- Layer 1: Height & Biome ---

        public byte GetHeight(int localX, int localY)
        {
            int index = GetIndex(localX, localY);
            return (byte)(_packedData[index] & 0x0F);
        }

        public void SetHeight(int localX, int localY, byte height)
        {
            if (height > 15) height = 15;
            int index = GetIndex(localX, localY);
            // Clear low nibble (height) and OR in new height
            _packedData[index] = (byte)((_packedData[index] & 0xF0) | (height & 0x0F));
        }

        /// <summary>
        /// Gets the Biome type (0-15).
        /// </summary>
        public byte GetBiome(int localX, int localY)
        {
            int index = GetIndex(localX, localY);
            return (byte)((_packedData[index] >> 4) & 0x0F);
        }

        public void SetBiome(int localX, int localY, byte biome)
        {
            if (biome > 15) biome = 15;
            int index = GetIndex(localX, localY);
            // Clear high nibble (biome) and OR in new biome
            _packedData[index] = (byte)((_packedData[index] & 0x0F) | (biome << 4));
        }

        // --- Layer 2: Water & Vegetation ---

        public byte GetWaterHeight(int localX, int localY)
        {
            int index = GetIndex(localX, localY);
            return (byte)(_layer2[index] & 0x0F);
        }

        public void SetWaterHeight(int localX, int localY, byte height)
        {
            if (height > 15) height = 15;
            int index = GetIndex(localX, localY);
            _layer2[index] = (byte)((_layer2[index] & 0xF0) | (height & 0x0F));
        }

        public byte GetVegetation(int localX, int localY)
        {
            int index = GetIndex(localX, localY);
            return (byte)((_layer2[index] >> 4) & 0x0F);
        }

        public void SetVegetation(int localX, int localY, byte veg)
        {
            if (veg > 15) veg = 15;
            int index = GetIndex(localX, localY);
            _layer2[index] = (byte)((_layer2[index] & 0x0F) | (veg << 4));
        }


        public bool GetFlag(int localX, int localY)
        {
            int index = GetIndex(localX, localY);
            int ulongIndex = index >> 6; // index / 64
            int bitIndex = index & 0x3F; // index % 64
            
            return (_flags[ulongIndex] & (1UL << bitIndex)) != 0;
        }

        public void SetFlag(int localX, int localY, bool value)
        {
            int index = GetIndex(localX, localY);
            int ulongIndex = index >> 6;
            int bitIndex = index & 0x3F;
            
            if (value)
            {
                _flags[ulongIndex] |= (1UL << bitIndex);
            }
            else
            {
                _flags[ulongIndex] &= ~(1UL << bitIndex);
            }
        }
        
        public bool GetRamp(int localX, int localY)
        {
            int index = GetIndex(localX, localY);
            int ulongIndex = index >> 6; 
            int bitIndex = index & 0x3F; 
            
            return (_rampFlags[ulongIndex] & (1UL << bitIndex)) != 0;
        }

        public void SetRamp(int localX, int localY, bool value)
        {
            int index = GetIndex(localX, localY);
            int ulongIndex = index >> 6;
            int bitIndex = index & 0x3F;
            
            if (value)
            {
                _rampFlags[ulongIndex] |= (1UL << bitIndex);
            }
            else
            {
                _rampFlags[ulongIndex] &= ~(1UL << bitIndex);
            }
        }

        public byte GetFaction(int localX, int localY)
        {
            int index = GetIndex(localX, localY);
            return _factions[index];
        }

        public void SetFaction(int localX, int localY, byte factionId)
        {
            int index = GetIndex(localX, localY);
            _factions[index] = factionId;
        }

        public bool GetExtraFlag(int localX, int localY, int layerIndex)
        {
            if ((uint)layerIndex >= (uint)_extraFlags.Length) throw new ArgumentOutOfRangeException(nameof(layerIndex));
            int index = GetIndex(localX, localY);
            int ulongIndex = index >> 6;
            int bitIndex = index & 0x3F;
            return (_extraFlags[layerIndex][ulongIndex] & (1UL << bitIndex)) != 0;
        }

        public void SetExtraFlag(int localX, int localY, int layerIndex, bool value)
        {
            if ((uint)layerIndex >= (uint)_extraFlags.Length) throw new ArgumentOutOfRangeException(nameof(layerIndex));
            int index = GetIndex(localX, localY);
            int ulongIndex = index >> 6;
            int bitIndex = index & 0x3F;
            if (value) _extraFlags[layerIndex][ulongIndex] |= (1UL << bitIndex);
            else _extraFlags[layerIndex][ulongIndex] &= ~(1UL << bitIndex);
        }

        public byte GetExtraByte(int localX, int localY, int layerIndex)
        {
            if ((uint)layerIndex >= (uint)_extraBytes.Length) throw new ArgumentOutOfRangeException(nameof(layerIndex));
            int index = GetIndex(localX, localY);
            return _extraBytes[layerIndex][index];
        }

        public void SetExtraByte(int localX, int localY, int layerIndex, byte value)
        {
            if ((uint)layerIndex >= (uint)_extraBytes.Length) throw new ArgumentOutOfRangeException(nameof(layerIndex));
            int index = GetIndex(localX, localY);
            _extraBytes[layerIndex][index] = value;
        }

        public bool GetCliffStraightenEdge(int localX, int localY, int edgeIndex)
        {
            if ((uint)edgeIndex >= DerivedEdgesPerCell) throw new ArgumentOutOfRangeException(nameof(edgeIndex));
            int cellIndex = GetIndex(localX, localY);
            int bitIndex = cellIndex * DerivedEdgesPerCell + edgeIndex;
            int byteIndex = bitIndex >> 3;
            int shift = bitIndex & 7;
            return (_cliffStraightenFlags[byteIndex] & (1 << shift)) != 0;
        }

        public void SetCliffStraightenEdge(int localX, int localY, int edgeIndex, bool value)
        {
            if ((uint)edgeIndex >= DerivedEdgesPerCell) throw new ArgumentOutOfRangeException(nameof(edgeIndex));
            int cellIndex = GetIndex(localX, localY);
            int bitIndex = cellIndex * DerivedEdgesPerCell + edgeIndex;
            int byteIndex = bitIndex >> 3;
            int shift = bitIndex & 7;
            byte mask = (byte)(1 << shift);
            if (value) _cliffStraightenFlags[byteIndex] |= mask;
            else _cliffStraightenFlags[byteIndex] &= (byte)~mask;
        }

        /// <summary>
        /// Direct access to packed byte for serialization or bulk ops
        /// </summary>
        public byte GetPackedByte(int index) => _packedData[index];
        public void SetPackedByte(int index, byte val) => _packedData[index] = val;

        public void LoadRaw(
            ReadOnlySpan<byte> packedData,
            ReadOnlySpan<byte> layer2,
            ReadOnlySpan<byte> flagsBytes,
            ReadOnlySpan<byte> rampFlagsBytes,
            ReadOnlySpan<byte> factions,
            ReadOnlySpan<byte> extraFlagsBytes0,
            ReadOnlySpan<byte> extraFlagsBytes1,
            ReadOnlySpan<byte> extraFlagsBytes2,
            ReadOnlySpan<byte> extraByteLayer0,
            ReadOnlySpan<byte> cliffStraightenFlagsBytes)
        {
            if (packedData.Length != TotalCells) throw new ArgumentException(nameof(packedData));
            if (layer2.Length != TotalCells) throw new ArgumentException(nameof(layer2));
            if (factions.Length != TotalCells) throw new ArgumentException(nameof(factions));
            if (extraByteLayer0.Length != TotalCells) throw new ArgumentException(nameof(extraByteLayer0));

            int flagsBytesExpected = _flags.Length * sizeof(ulong);
            if (flagsBytes.Length != flagsBytesExpected) throw new ArgumentException(nameof(flagsBytes));
            if (rampFlagsBytes.Length != flagsBytesExpected) throw new ArgumentException(nameof(rampFlagsBytes));
            if (extraFlagsBytes0.Length != flagsBytesExpected) throw new ArgumentException(nameof(extraFlagsBytes0));
            if (extraFlagsBytes1.Length != flagsBytesExpected) throw new ArgumentException(nameof(extraFlagsBytes1));
            if (extraFlagsBytes2.Length != flagsBytesExpected) throw new ArgumentException(nameof(extraFlagsBytes2));
            if (cliffStraightenFlagsBytes.Length != DerivedCliffStraightenBytes) throw new ArgumentException(nameof(cliffStraightenFlagsBytes));

            packedData.CopyTo(_packedData);
            layer2.CopyTo(_layer2);
            factions.CopyTo(_factions);
            extraByteLayer0.CopyTo(_extraBytes[0]);
            flagsBytes.CopyTo(MemoryMarshal.AsBytes(_flags.AsSpan()));
            rampFlagsBytes.CopyTo(MemoryMarshal.AsBytes(_rampFlags.AsSpan()));
            extraFlagsBytes0.CopyTo(MemoryMarshal.AsBytes(_extraFlags[0].AsSpan()));
            extraFlagsBytes1.CopyTo(MemoryMarshal.AsBytes(_extraFlags[1].AsSpan()));
            extraFlagsBytes2.CopyTo(MemoryMarshal.AsBytes(_extraFlags[2].AsSpan()));
            cliffStraightenFlagsBytes.CopyTo(_cliffStraightenFlags);
        }

        public void CopyRawTo(
            Span<byte> packedData,
            Span<byte> layer2,
            Span<byte> flagsBytes,
            Span<byte> rampFlagsBytes,
            Span<byte> factions,
            Span<byte> extraFlagsBytes0,
            Span<byte> extraFlagsBytes1,
            Span<byte> extraFlagsBytes2,
            Span<byte> extraByteLayer0,
            Span<byte> cliffStraightenFlagsBytes)
        {
            if (packedData.Length != TotalCells) throw new ArgumentException(nameof(packedData));
            if (layer2.Length != TotalCells) throw new ArgumentException(nameof(layer2));
            if (factions.Length != TotalCells) throw new ArgumentException(nameof(factions));
            if (extraByteLayer0.Length != TotalCells) throw new ArgumentException(nameof(extraByteLayer0));

            int flagsBytesExpected = _flags.Length * sizeof(ulong);
            if (flagsBytes.Length != flagsBytesExpected) throw new ArgumentException(nameof(flagsBytes));
            if (rampFlagsBytes.Length != flagsBytesExpected) throw new ArgumentException(nameof(rampFlagsBytes));
            if (extraFlagsBytes0.Length != flagsBytesExpected) throw new ArgumentException(nameof(extraFlagsBytes0));
            if (extraFlagsBytes1.Length != flagsBytesExpected) throw new ArgumentException(nameof(extraFlagsBytes1));
            if (extraFlagsBytes2.Length != flagsBytesExpected) throw new ArgumentException(nameof(extraFlagsBytes2));
            if (cliffStraightenFlagsBytes.Length != DerivedCliffStraightenBytes) throw new ArgumentException(nameof(cliffStraightenFlagsBytes));

            _packedData.AsSpan().CopyTo(packedData);
            _layer2.AsSpan().CopyTo(layer2);
            MemoryMarshal.AsBytes(_flags.AsSpan()).CopyTo(flagsBytes);
            MemoryMarshal.AsBytes(_rampFlags.AsSpan()).CopyTo(rampFlagsBytes);
            _factions.AsSpan().CopyTo(factions);
            MemoryMarshal.AsBytes(_extraFlags[0].AsSpan()).CopyTo(extraFlagsBytes0);
            MemoryMarshal.AsBytes(_extraFlags[1].AsSpan()).CopyTo(extraFlagsBytes1);
            MemoryMarshal.AsBytes(_extraFlags[2].AsSpan()).CopyTo(extraFlagsBytes2);
            _extraBytes[0].AsSpan().CopyTo(extraByteLayer0);
            _cliffStraightenFlags.AsSpan().CopyTo(cliffStraightenFlagsBytes);
        }
    }
}
