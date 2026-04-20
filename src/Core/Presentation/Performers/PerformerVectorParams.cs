using System.Numerics;
using System.Runtime.CompilerServices;

namespace Ludots.Core.Presentation.Performers
{
    public unsafe struct PerformerVectorParams
    {
        public const int MAX_ENTRIES = 8;
        public int Count;
        public fixed int Keys[MAX_ENTRIES];
        // Vector4 is 16 bytes; store as flat floats for fixed buffer
        public fixed float ValuesX[MAX_ENTRIES];
        public fixed float ValuesY[MAX_ENTRIES];
        public fixed float ValuesZ[MAX_ENTRIES];
        public fixed float ValuesW[MAX_ENTRIES];

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool TryGet(int key, out Vector4 value)
        {
            fixed (int* keys = Keys)
            fixed (float* vx = ValuesX) fixed (float* vy = ValuesY)
            fixed (float* vz = ValuesZ) fixed (float* vw = ValuesW)
            {
                for (int i = 0; i < Count; i++)
                {
                    if (keys[i] != key) continue;
                    value = new Vector4(vx[i], vy[i], vz[i], vw[i]);
                    return true;
                }
            }
            value = default;
            return false;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Set(int key, in Vector4 value)
        {
            fixed (int* keys = Keys)
            fixed (float* vx = ValuesX) fixed (float* vy = ValuesY)
            fixed (float* vz = ValuesZ) fixed (float* vw = ValuesW)
            {
                for (int i = 0; i < Count; i++)
                {
                    if (keys[i] != key) continue;
                    vx[i] = value.X; vy[i] = value.Y;
                    vz[i] = value.Z; vw[i] = value.W;
                    return;
                }
                if (Count >= MAX_ENTRIES) return;
                keys[Count] = key;
                vx[Count] = value.X; vy[Count] = value.Y;
                vz[Count] = value.Z; vw[Count] = value.W;
                Count++;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Clear(int key)
        {
            fixed (int* keys = Keys)
            fixed (float* vx = ValuesX) fixed (float* vy = ValuesY)
            fixed (float* vz = ValuesZ) fixed (float* vw = ValuesW)
            {
                for (int i = 0; i < Count; i++)
                {
                    if (keys[i] != key) continue;
                    Count--;
                    if (i < Count)
                    {
                        keys[i] = keys[Count];
                        vx[i] = vx[Count]; vy[i] = vy[Count];
                        vz[i] = vz[Count]; vw[i] = vw[Count];
                    }
                    return true;
                }
            }
            return false;
        }

        public void ClearAll() { Count = 0; }
    }

    public unsafe struct PerformerVectorDefaults
    {
        public const int MAX_ENTRIES = 8;
        public int Count;
        public fixed int Keys[MAX_ENTRIES];
        public fixed float ValuesX[MAX_ENTRIES];
        public fixed float ValuesY[MAX_ENTRIES];
        public fixed float ValuesZ[MAX_ENTRIES];
        public fixed float ValuesW[MAX_ENTRIES];

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool TryGet(int key, out Vector4 value)
        {
            fixed (int* keys = Keys)
            fixed (float* vx = ValuesX) fixed (float* vy = ValuesY)
            fixed (float* vz = ValuesZ) fixed (float* vw = ValuesW)
            {
                for (int i = 0; i < Count; i++)
                {
                    if (keys[i] != key) continue;
                    value = new Vector4(vx[i], vy[i], vz[i], vw[i]);
                    return true;
                }
            }
            value = default;
            return false;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Set(int key, in Vector4 value)
        {
            fixed (int* keys = Keys)
            fixed (float* vx = ValuesX) fixed (float* vy = ValuesY)
            fixed (float* vz = ValuesZ) fixed (float* vw = ValuesW)
            {
                for (int i = 0; i < Count; i++)
                {
                    if (keys[i] != key) continue;
                    vx[i] = value.X; vy[i] = value.Y;
                    vz[i] = value.Z; vw[i] = value.W;
                    return;
                }
                if (Count >= MAX_ENTRIES) return;
                keys[Count] = key;
                vx[Count] = value.X; vy[Count] = value.Y;
                vz[Count] = value.Z; vw[Count] = value.W;
                Count++;
            }
        }
    }
}
