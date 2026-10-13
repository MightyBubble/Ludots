using System;
using System.Diagnostics;

namespace Ludots.Core.CrowdSimulation.Nav;

/// <summary>
/// 重烘分段计时。未武装时热路径只读 <see cref="Armed"/>。
/// </summary>
internal static class CrowdRebakeProbe
{
    internal static bool Armed;

    internal static long Classify;
    internal static long Erode;
    internal static long BakeTiles;
    internal static long CacheScan;
    internal static long CacheBake;
    internal static long Components;
    internal static long Cells;
    internal static long Links;
    internal static long Reach;
    internal static long Hpa;
    internal static long HpaBake;
    internal static long HpaBlock;
    internal static long Incremental;
    internal static long Verify;
    internal static long Reaction;
    internal static long Publish;

    internal static int NavCalls;
    internal static int Passable;
    internal static int Cost;
    internal static int None;
    internal static int HpaBakeTiles;
    internal static int HpaBlocks;
    internal static int CacheHits;
    internal static int CacheMisses;

    internal struct NavNote
    {
        public int Id;
        public byte Kind;
        public int Tiles;
        public long Ticks;
    }

    internal static NavNote[] Notes = new NavNote[32];
    internal static int NoteCount;

    internal static void Reset()
    {
        Classify = Erode = BakeTiles = CacheScan = CacheBake = 0;
        Components = Cells = Links = Reach = Hpa = HpaBake = HpaBlock = 0;
        Incremental = Verify = Reaction = Publish = 0;
        NavCalls = Passable = Cost = None = HpaBakeTiles = HpaBlocks = CacheHits = CacheMisses = NoteCount = 0;
    }

    internal static long T() => Armed ? Stopwatch.GetTimestamp() : 0;

    internal static void Acc(ref long slot, long t0)
    {
        if (Armed && t0 != 0) slot += Stopwatch.GetTimestamp() - t0;
    }

    internal static void Note(int id, byte kind, int tiles, long t0)
    {
        if (!Armed || t0 == 0) return;
        if (NoteCount >= Notes.Length)
        {
            var grown = new NavNote[Notes.Length * 2];
            Array.Copy(Notes, grown, Notes.Length);
            Notes = grown;
        }

        Notes[NoteCount++] = new NavNote
        {
            Id = id,
            Kind = kind,
            Tiles = tiles,
            Ticks = Stopwatch.GetTimestamp() - t0,
        };
        NavCalls++;
        if (kind == CrowdNavRebake.KindPassable) Passable++;
        else if (kind == CrowdNavRebake.KindCost) Cost++;
        else None++;
    }

    internal static double Ms(long ticks) => ticks * 1000.0 / Stopwatch.Frequency;
}
