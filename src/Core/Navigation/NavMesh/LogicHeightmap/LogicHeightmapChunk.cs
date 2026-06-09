using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Ludots.Core.Navigation.NavMesh.LogicHeightmap
{
    public sealed class LogicHeightmapChunk
    {
        public const int ChunkSize = 64;
        public const int ChunkSizeMask = 63;
        public const int ChunkSizeShift = 6;
        public const int TotalCells = ChunkSize * ChunkSize;

        private readonly int[] _heightCm = new int[TotalCells];
        private readonly int[] _waterHeightCm = new int[TotalCells];
        private readonly byte[] _areaIds = new byte[TotalCells];
        private readonly ulong[] _blockedFlags = new ulong[TotalCells / 64];
        private readonly ulong[] _rampFlags = new ulong[TotalCells / 64];

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int GetIndex(int localX, int localY)
        {
            return (localY << ChunkSizeShift) | localX;
        }

        public int GetHeightCm(int localX, int localY) => _heightCm[GetIndex(localX, localY)];

        public void SetHeightCm(int localX, int localY, int heightCm)
        {
            _heightCm[GetIndex(localX, localY)] = heightCm;
        }

        public int GetWaterHeightCm(int localX, int localY) => _waterHeightCm[GetIndex(localX, localY)];

        public void SetWaterHeightCm(int localX, int localY, int heightCm)
        {
            _waterHeightCm[GetIndex(localX, localY)] = heightCm;
        }

        public byte GetAreaId(int localX, int localY) => _areaIds[GetIndex(localX, localY)];

        public void SetAreaId(int localX, int localY, byte areaId)
        {
            _areaIds[GetIndex(localX, localY)] = areaId;
        }

        public bool IsBlocked(int localX, int localY) => GetFlag(_blockedFlags, GetIndex(localX, localY));

        public void SetBlocked(int localX, int localY, bool blocked)
        {
            SetFlag(_blockedFlags, GetIndex(localX, localY), blocked);
        }

        public bool IsRamp(int localX, int localY) => GetFlag(_rampFlags, GetIndex(localX, localY));

        public void SetRamp(int localX, int localY, bool isRamp)
        {
            SetFlag(_rampFlags, GetIndex(localX, localY), isRamp);
        }

        public void LoadRaw(
            ReadOnlySpan<int> heightCm,
            ReadOnlySpan<int> waterHeightCm,
            ReadOnlySpan<byte> areaIds,
            ReadOnlySpan<byte> blockedFlagsBytes,
            ReadOnlySpan<byte> rampFlagsBytes)
        {
            if (heightCm.Length != TotalCells) throw new ArgumentException(nameof(heightCm));
            if (waterHeightCm.Length != TotalCells) throw new ArgumentException(nameof(waterHeightCm));
            if (areaIds.Length != TotalCells) throw new ArgumentException(nameof(areaIds));

            int flagBytes = _blockedFlags.Length * sizeof(ulong);
            if (blockedFlagsBytes.Length != flagBytes) throw new ArgumentException(nameof(blockedFlagsBytes));
            if (rampFlagsBytes.Length != flagBytes) throw new ArgumentException(nameof(rampFlagsBytes));

            heightCm.CopyTo(_heightCm);
            waterHeightCm.CopyTo(_waterHeightCm);
            areaIds.CopyTo(_areaIds);
            blockedFlagsBytes.CopyTo(MemoryMarshal.AsBytes(_blockedFlags.AsSpan()));
            rampFlagsBytes.CopyTo(MemoryMarshal.AsBytes(_rampFlags.AsSpan()));
        }

        public void CopyRawTo(
            Span<int> heightCm,
            Span<int> waterHeightCm,
            Span<byte> areaIds,
            Span<byte> blockedFlagsBytes,
            Span<byte> rampFlagsBytes)
        {
            if (heightCm.Length != TotalCells) throw new ArgumentException(nameof(heightCm));
            if (waterHeightCm.Length != TotalCells) throw new ArgumentException(nameof(waterHeightCm));
            if (areaIds.Length != TotalCells) throw new ArgumentException(nameof(areaIds));

            int flagBytes = _blockedFlags.Length * sizeof(ulong);
            if (blockedFlagsBytes.Length != flagBytes) throw new ArgumentException(nameof(blockedFlagsBytes));
            if (rampFlagsBytes.Length != flagBytes) throw new ArgumentException(nameof(rampFlagsBytes));

            _heightCm.AsSpan().CopyTo(heightCm);
            _waterHeightCm.AsSpan().CopyTo(waterHeightCm);
            _areaIds.AsSpan().CopyTo(areaIds);
            MemoryMarshal.AsBytes(_blockedFlags.AsSpan()).CopyTo(blockedFlagsBytes);
            MemoryMarshal.AsBytes(_rampFlags.AsSpan()).CopyTo(rampFlagsBytes);
        }

        private static bool GetFlag(ulong[] flags, int index)
        {
            int word = index >> 6;
            int bit = index & 0x3F;
            return (flags[word] & (1UL << bit)) != 0;
        }

        private static void SetFlag(ulong[] flags, int index, bool value)
        {
            int word = index >> 6;
            int bit = index & 0x3F;
            ulong mask = 1UL << bit;
            if (value)
            {
                flags[word] |= mask;
            }
            else
            {
                flags[word] &= ~mask;
            }
        }
    }
}
