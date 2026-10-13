using System;
using System.Collections.Generic;
using Ludots.Core.CrowdSimulation.Fog;
using Ludots.Core.CrowdSimulation.Movement;
using Ludots.Core.CrowdSimulation.Nav;
using Ludots.Core.CrowdSimulation.Structures;
using Ludots.Core.CrowdSimulation.Units;
using Ludots.Core.Mathematics.FixedPoint;
using Ludots.Core.Vision;

namespace Ludots.Core.CrowdSimulation.Diagnostics;

/// <summary>一个只读分区的名字和封口摘要。捕获时现算，不写进每 tick 的合计。</summary>
public readonly record struct CrowdSimPartition(string Name, ulong Hash);

/// <summary>
/// RT-13 的分区摘要。只读会话，不调用会构建认知变体的 <c>Get</c>。
/// 每 tick 合计仍只走 <see cref="CrowdSimChecksum.Compute"/>。
/// </summary>
public static class CrowdSimPartitions
{
    private const uint FieldDomain = 0x756E6974u;
    private const uint OrdersDomain = 0x6F726472u;
    private const uint PlannerDomain = 0x706C6E72u;
    private const uint FogDomain = 0x666F6720u;
    private const uint BeliefDomain = 0x62656C66u;
    private const uint StructuresDomain = 0x73747275u;
    private const uint NavDomain = 0x6E617620u;

    public static void Copy(CrowdSimSession sim, List<CrowdSimPartition> destination)
    {
        ArgumentNullException.ThrowIfNull(sim);
        ArgumentNullException.ThrowIfNull(destination);
        destination.Clear();
        var units = sim.Units;
        var movement = sim.Movement;
        int n = units.Count;
        AddDense(destination, "units.x", 0, n, i => CrowdSimChecksum.Position(sim, movement, i).X.RawValue);
        AddDense(destination, "units.y", 1, n, i => CrowdSimChecksum.Position(sim, movement, i).Y.RawValue);
        AddDense(destination, "units.vx", 2, n, i => CrowdSimChecksum.MotionRaw(sim, movement, i, 0));
        AddDense(destination, "units.vy", 3, n, i => CrowdSimChecksum.MotionRaw(sim, movement, i, 1));
        AddDense(destination, "units.slotX", 4, n, i => CrowdSimChecksum.MotionRaw(sim, movement, i, 2));
        AddDense(destination, "units.slotY", 5, n, i => CrowdSimChecksum.MotionRaw(sim, movement, i, 3));
        AddDense(destination, "units.blend", 6, n, i => CrowdSimChecksum.MotionRaw(sim, movement, i, 4));
        AddDense(destination, "units.stall", 7, n, i => CrowdSimChecksum.MotionRaw(sim, movement, i, 5));
        AddDense(destination, "units.state", 8, n, i => CrowdSimChecksum.State(sim, movement, i).State);
        AddDense(destination, "units.group", 9, n, i => CrowdSimChecksum.State(sim, movement, i).GroupId);
        AddDense(destination, "units.handle", 10, n, i => units.HandleAt(i));
        AddDense(destination, "units.order", 11, n, i => CrowdSimChecksum.State(sim, movement, i).Order);
        AddDense(destination, "units.mode", 12, n, i => CrowdSimChecksum.State(sim, movement, i).Mode);
        AddDense(destination, "units.level", 13, n, i => CrowdSimChecksum.State(sim, movement, i).Level);
        AddDense(destination, "units.sepX", 14, n, i => movement == null ? 0 : movement.Separation[i].X.RawValue);
        AddDense(destination, "units.sepY", 15, n, i => movement == null ? 0 : movement.Separation[i].Y.RawValue);
        AddDense(destination, "units.calm", 16, n, i => movement == null ? 0 : movement.Calm[i]);
        uint phase = CrowdSimChecksum.Mix(CrowdSimChecksum.Mix(FieldDomain, 17), (uint)n);
        phase = CrowdSimChecksum.Mix(phase, (uint)(movement?.Phase ?? 0));
        destination.Add(new CrowdSimPartition("units.phase", CrowdSimChecksum.Seal(phase)));
        destination.Add(new CrowdSimPartition(CrowdSimChecksum.HandlesSection, CrowdSimChecksum.MixHandles(sim)));
        destination.Add(new CrowdSimPartition("groups", CrowdSimChecksum.Seal(CrowdSimChecksum.MixGroupRows(sim))));
        destination.Add(new CrowdSimPartition("orders", CrowdSimChecksum.Seal(
            CrowdSimChecksum.MixOrderBook(CrowdSimChecksum.Mix(OrdersDomain, 0), sim.Orders))));
        uint planner = sim.Planner == null
            ? CrowdSimChecksum.Mix(PlannerDomain, 0)
            : sim.Planner.MixInFlight(PlannerDomain);
        destination.Add(new CrowdSimPartition("planner", CrowdSimChecksum.Seal(planner)));
        destination.Add(new CrowdSimPartition("jump", CrowdSimChecksum.Seal(CrowdSimChecksum.MixJump(sim, movement, n))));
        destination.Add(new CrowdSimPartition("commands", CrowdSimChecksum.Seal(CrowdSimChecksum.MixCommands(sim))));
        destination.Add(new CrowdSimPartition("fog", CrowdSimChecksum.Seal(MixFog(sim))));
        destination.Add(new CrowdSimPartition("belief", CrowdSimChecksum.Seal(MixBelief(sim))));
        destination.Add(new CrowdSimPartition("structures", CrowdSimChecksum.Seal(MixStructures(sim))));
        AddNavs(sim, destination);
        AddBeliefNavs(sim, destination);
    }

    public static void CollectDifferences(
        IReadOnlyList<CrowdSimPartition> left,
        IReadOnlyList<CrowdSimPartition> right,
        List<string> names)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);
        ArgumentNullException.ThrowIfNull(names);
        var rightHash = new Dictionary<string, ulong>(right.Count, StringComparer.Ordinal);
        for (int i = 0; i < right.Count; i++) rightHash[right[i].Name] = right[i].Hash;
        var seen = new HashSet<string>(left.Count, StringComparer.Ordinal);
        for (int i = 0; i < left.Count; i++)
        {
            string name = left[i].Name;
            seen.Add(name);
            if (!rightHash.TryGetValue(name, out ulong hash) || hash != left[i].Hash) names.Add(name);
        }

        for (int i = 0; i < right.Count; i++)
        {
            if (!seen.Contains(right[i].Name)) names.Add(right[i].Name);
        }
    }

    private static void AddDense(List<CrowdSimPartition> destination, string name, int field, int count, Func<int, long> value)
    {
        uint h = CrowdSimChecksum.Mix(CrowdSimChecksum.Mix(FieldDomain, (uint)field), (uint)count);
        for (int i = 0; i < count; i++) h = CrowdSimChecksum.MixI64(h, value(i));
        destination.Add(new CrowdSimPartition(name, CrowdSimChecksum.Seal(h)));
    }

    private static uint MixFog(CrowdSimSession sim)
    {
        var fog = sim.Fog;
        uint h = CrowdSimChecksum.Mix(FogDomain, fog == null ? 0u : 1u);
        if (fog == null) return h;
        h = CrowdSimChecksum.Mix(h, (uint)fog.VisionGroupCount);
        h = CrowdSimChecksum.Mix(h, fog.TruthKey);
        h = CrowdSimChecksum.MixI64(h, fog.TruthRev);
        h = CrowdSimChecksum.Mix(h, fog.Terrain ? 1u : 0u);
        h = CrowdSimChecksum.Mix(h, fog.Los ? 1u : 0u);
        int cells = fog.FogCellsPerSide * fog.FogCellsPerSide;
        var ids = new List<int>();
        for (int g = 0; g < fog.VisionGroupCount; g++)
        {
            h = CrowdSimChecksum.Mix(h, fog.Key[g]);
            h = CrowdSimChecksum.Mix(h, fog.TileKey[g]);
            h = CrowdSimChecksum.Mix(h, (uint)fog.GhostCount[g]);
            h = CrowdSimChecksum.Mix(h, fog.Seen[g]);
            h = CrowdSimChecksum.Mix(h, fog.Gone[g]);
            h = CrowdSimChecksum.MixI64(h, fog.EntRev[g]);
            h = CrowdSimChecksum.MixI64(h, fog.Rev[g]);
            for (int c = 0; c < cells; c++)
                h = CrowdSimChecksum.Mix(h, (uint)fog.CellVisibilityOf(g, c));
            ids.Clear();
            foreach (int id in fog.Belief[g].Keys) ids.Add(id);
            ids.Sort();
            h = CrowdSimChecksum.Mix(h, (uint)ids.Count);
            for (int i = 0; i < ids.Count; i++)
            {
                int id = ids[i];
                h = CrowdSimChecksum.Mix(h, (uint)id);
                var snap = fog.Belief[g][id];
                h = CrowdSimChecksum.Mix(h, (uint)snap.TplIndex);
                h = MixFootprint(h, snap.Fp);
            }

            ids.Clear();
            foreach (int tile in fog.Tiles[g]) ids.Add(tile);
            ids.Sort();
            h = CrowdSimChecksum.Mix(h, (uint)ids.Count);
            for (int i = 0; i < ids.Count; i++) h = CrowdSimChecksum.Mix(h, (uint)ids[i]);
        }

        return h;
    }

    private static uint MixBelief(CrowdSimSession sim)
    {
        var belief = sim.Belief;
        uint h = CrowdSimChecksum.Mix(BeliefDomain, belief == null ? 0u : 1u);
        if (belief == null) return h;
        h = CrowdSimChecksum.Mix(h, (uint)belief.Seq);
        h = CrowdSimChecksum.Mix(h, (uint)belief.Switches);
        h = CrowdSimChecksum.MixI64(h, belief.LastTruth);
        h = CrowdSimChecksum.Mix(h, belief.LastTerrain ? 1u : 0u);
        h = CrowdSimChecksum.Mix(h, (uint)belief.Slot.Length);
        for (int i = 0; i < belief.Slot.Length; i++)
        {
            h = CrowdSimChecksum.Mix(h, (uint)belief.Slot[i]);
            h = CrowdSimChecksum.MixI64(h, belief.LastKey[i]);
            h = CrowdSimChecksum.MixI64(h, belief.LastEnt[i]);
            h = CrowdSimChecksum.MixI64(h, belief.LastCommit[i]);
        }

        var slots = new List<int>(belief.Entries.Count);
        foreach (var entry in belief.Entries.Values) slots.Add(entry.Slot);
        slots.Sort();
        h = CrowdSimChecksum.Mix(h, (uint)slots.Count);
        for (int i = 0; i < slots.Count; i++)
        {
            var entry = belief.Entries[slots[i]];
            h = CrowdSimChecksum.Mix(h, (uint)entry.Slot);
            h = CrowdSimChecksum.Mix(h, entry.Key);
            h = CrowdSimChecksum.Mix(h, entry.Dormant ? 1u : 0u);
            var ids = new List<int>(entry.Ids.Count);
            foreach (int id in entry.Ids) ids.Add(id);
            ids.Sort();
            h = CrowdSimChecksum.Mix(h, (uint)ids.Count);
            for (int k = 0; k < ids.Count; k++) h = CrowdSimChecksum.Mix(h, (uint)ids[k]);
            if (entry.Tiles == null)
            {
                h = CrowdSimChecksum.Mix(h, 0);
            }
            else
            {
                ids.Clear();
                foreach (int tile in entry.Tiles) ids.Add(tile);
                ids.Sort();
                h = CrowdSimChecksum.Mix(h, (uint)ids.Count);
                for (int k = 0; k < ids.Count; k++) h = CrowdSimChecksum.Mix(h, (uint)ids[k]);
            }
        }

        h = CrowdSimChecksum.Mix(h, (uint)belief.DormantList.Count);
        for (int i = 0; i < belief.DormantList.Count; i++)
            h = CrowdSimChecksum.Mix(h, (uint)belief.DormantList[i].Slot);
        return h;
    }

    private static uint MixStructures(CrowdSimSession sim)
    {
        var store = sim.Structures;
        uint h = CrowdSimChecksum.Mix(StructuresDomain, store == null ? 0u : 1u);
        if (store == null) return h;
        var ids = store.EntityIds;
        h = CrowdSimChecksum.Mix(h, (uint)ids.Count);
        for (int i = 0; i < ids.Count; i++)
        {
            int id = ids[i];
            h = CrowdSimChecksum.Mix(h, (uint)id);
            h = CrowdSimChecksum.Mix(h, store.TryGetTemplate(id, out int template) ? (uint)template : uint.MaxValue);
            if (store.TryGetFootprint(id, out var footprint)) h = MixFootprint(h, footprint);
            else h = CrowdSimChecksum.Mix(h, 0);
        }

        h = MixBytes(h, store.Blocked);
        h = MixBytes(h, store.Area);
        return h;
    }

    private static void AddNavs(CrowdSimSession sim, List<CrowdSimPartition> destination)
    {
        var ids = new int[sim.Navs.Count];
        int n = 0;
        foreach (int id in sim.Navs.Keys) ids[n++] = id;
        Array.Sort(ids, 0, n);
        for (int i = 0; i < n; i++)
        {
            NavContext nav = sim.Navs[ids[i]];
            uint h = MixNav(CrowdSimChecksum.Mix(NavDomain, (uint)nav.Id), nav);
            destination.Add(new CrowdSimPartition($"nav.{nav.Id}", CrowdSimChecksum.Seal(h)));
        }
    }

    private static void AddBeliefNavs(CrowdSimSession sim, List<CrowdSimPartition> destination)
    {
        if (sim.Beliefs == null || sim.Planner == null) return;
        var built = new List<(NavContext Nav, CrowdStructuresStore Field)>();
        sim.Beliefs.CopyMaterialized(built);
        if (built.Count == 0) return;
        built.Sort(static (a, b) => a.Nav.Id.CompareTo(b.Nav.Id));
        int index = 0;
        while (index < built.Count)
        {
            int slot = built[index].Nav.Id / CrowdBeliefNavs.BeliefStride;
            uint h = CrowdSimChecksum.Mix(BeliefDomain, (uint)slot);
            int end = index;
            while (end < built.Count && built[end].Nav.Id / CrowdBeliefNavs.BeliefStride == slot) end++;
            h = CrowdSimChecksum.Mix(h, (uint)(end - index));
            for (int i = index; i < end; i++)
            {
                h = MixNav(h, built[i].Nav);
                h = MixBytes(h, built[i].Field.Blocked);
                h = MixBytes(h, built[i].Field.Area);
            }

            destination.Add(new CrowdSimPartition($"belief.{slot}", CrowdSimChecksum.Seal(h)));
            index = end;
        }
    }

    private static uint MixNav(uint h, NavContext nav)
    {
        h = CrowdSimChecksum.Mix(h, (uint)nav.Id);
        h = CrowdSimChecksum.Mix(h, (uint)nav.LayerIndex);
        h = CrowdSimChecksum.Mix(h, (uint)nav.ClearanceNavCells);
        h = CrowdSimChecksum.Mix(h, (uint)nav.CellCount);
        h = CrowdSimChecksum.Mix(h, (uint)nav.CompCount);
        h = CrowdSimChecksum.Mix(h, (uint)nav.CompCountTotal);
        h = CrowdSimChecksum.MixI64(h, nav.MinCost.RawValue);
        h = MixFix(h, nav.Cost);
        h = MixFix(h, nav.SpeedDivisor);
        h = MixBytes(h, nav.Walk);
        h = MixBytes(h, nav.Passable);
        h = MixInts(h, nav.Comp);
        h = MixInts(h, nav.Cells, nav.CellCount);
        h = MixBytes(h, nav.UpPass);
        h = MixBytes(h, nav.Portal);
        h = MixBytes(h, nav.UpArea);
        h = MixFix(h, nav.UpCost);
        h = MixFix(h, nav.UpSpeedDivisor);
        h = nav.UpComp == null ? CrowdSimChecksum.Mix(h, 0) : MixInts(h, nav.UpComp);
        if (nav.Links == null)
        {
            h = CrowdSimChecksum.Mix(h, 0);
        }
        else
        {
            h = CrowdSimChecksum.Mix(h, (uint)nav.Links.Count);
            h = MixInts(h, nav.Links.From, nav.Links.Count);
            h = MixInts(h, nav.Links.To, nav.Links.Count);
            h = MixFix(h, nav.Links.Cost, nav.Links.Count);
        }

        h = CrowdSimChecksum.Mix(h, nav.Hpa == null ? 0u : 1u);
        if (nav.Hpa != null)
        {
            h = CrowdSimChecksum.Mix(h, (uint)nav.Hpa.NodeCount);
            h = CrowdSimChecksum.Mix(h, (uint)nav.Hpa.EdgeCount);
        }
        h = CrowdSimChecksum.Mix(h, nav.Tiles == null ? 0u : (uint)nav.Tiles.Length);
        h = CrowdSimChecksum.Mix(h, nav.UpperTiles == null ? 0u : (uint)nav.UpperTiles.Count);
        return h;
    }

    private static uint MixFootprint(uint h, CrowdStructureFootprint footprint)
    {
        h = CrowdSimChecksum.Mix(h, (uint)footprint.Shape);
        h = CrowdSimChecksum.MixI64(h, footprint.CenterXCm.RawValue);
        h = CrowdSimChecksum.MixI64(h, footprint.CenterYCm.RawValue);
        h = CrowdSimChecksum.MixI64(h, footprint.Hx.RawValue);
        h = CrowdSimChecksum.MixI64(h, footprint.Hy.RawValue);
        h = CrowdSimChecksum.MixI64(h, footprint.RadiusCm.RawValue);
        h = CrowdSimChecksum.MixI64(h, footprint.X0.RawValue);
        h = CrowdSimChecksum.MixI64(h, footprint.Y0.RawValue);
        h = CrowdSimChecksum.MixI64(h, footprint.X1.RawValue);
        h = CrowdSimChecksum.MixI64(h, footprint.Y1.RawValue);
        return CrowdSimChecksum.MixI64(h, footprint.WidthCm.RawValue);
    }

    private static uint MixBytes(uint h, byte[] bytes)
    {
        h = CrowdSimChecksum.Mix(h, (uint)bytes.Length);
        for (int i = 0; i < bytes.Length; i++) h = CrowdSimChecksum.Mix(h, bytes[i]);
        return h;
    }

    private static uint MixInts(uint h, int[] values, int count = -1)
    {
        int n = count < 0 ? values.Length : count;
        h = CrowdSimChecksum.Mix(h, (uint)n);
        for (int i = 0; i < n; i++) h = CrowdSimChecksum.Mix(h, (uint)values[i]);
        return h;
    }

    private static uint MixFix(uint h, Fix64[] values, int count = -1)
    {
        int n = count < 0 ? values.Length : count;
        h = CrowdSimChecksum.Mix(h, (uint)n);
        for (int i = 0; i < n; i++) h = CrowdSimChecksum.MixI64(h, values[i].RawValue);
        return h;
    }
}
