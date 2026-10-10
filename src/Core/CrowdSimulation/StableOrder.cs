using System;

namespace Ludots.Core.CrowdSimulation;

/// <summary>升序键 + 输入序号。序号是平局时的次序，与稳定排序的输入序一致。</summary>
internal readonly struct StableInt : IComparable<StableInt>
{
    public readonly int Key;
    public readonly int Seq;

    public StableInt(int key, int seq)
    {
        Key = key;
        Seq = seq;
    }

    public int CompareTo(StableInt other)
    {
        int c = Key.CompareTo(other.Key);
        return c != 0 ? c : Seq.CompareTo(other.Seq);
    }
}

/// <summary>长整型升序键 + 输入序号。</summary>
internal readonly struct StableLong : IComparable<StableLong>
{
    public readonly long Key;
    public readonly int Seq;

    public StableLong(long key, int seq)
    {
        Key = key;
        Seq = seq;
    }

    public int CompareTo(StableLong other)
    {
        int c = Key.CompareTo(other.Key);
        return c != 0 ? c : Seq.CompareTo(other.Seq);
    }
}

internal static class StableOrder
{
    public static void Ensure<T>(ref T[] buffer, int count)
    {
        if (buffer.Length >= count) return;
        int grown = buffer.Length == 0 ? 8 : buffer.Length * 2;
        if (grown < count) grown = count;
        buffer = new T[grown];
    }

    public static void Sort(StableInt[] items, int count) => Array.Sort(items, 0, count);

    public static void Sort(StableLong[] items, int count) => Array.Sort(items, 0, count);
}
