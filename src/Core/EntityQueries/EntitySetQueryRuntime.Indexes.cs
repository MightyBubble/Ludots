using System;
using System.Collections.Generic;
using Arch.Core;
using Ludots.Core.Components;
using Ludots.Core.Map;
using Ludots.Core.NodeLibraries.GASGraph;
using Ludots.Core.EntityCollections;
using Ludots.Core.GraphRuntime;

namespace Ludots.Core.EntityQueries;

public sealed partial class EntitySetQueryRuntime : IDisposable
{
    private readonly List<QueryInstance> _indexes = new();
    private readonly Dictionary<int, Entity[]> _queryBuffers = new();
    private EntityCollectionStore? _boundStore;
    private int _executionDepth;
    public long InitialQueryVisitedCount { get; private set; }
    public long QueryReevaluatedCount
    {
        get
        {
            long total = 0;
            foreach (QueryInstance instance in _indexes) total += instance.Index.ReevaluatedCount;
            return total;
        }
    }
    public int QueryInstanceCount => _indexes.Count;

    public Span<Entity> QueryMap(GraphEntityQueryPlan? plan, MapId? map, scoped ReadOnlySpan<int> ints,
        scoped ReadOnlySpan<float> floats, int frameDepth)
    {
        int count = _world.CountEntities(in MapEntityQuery);
        Span<Entity> result = GetQueryBuffer(frameDepth, count);
        int written = CopyMapEntities(result, map);
        return result.Slice(0, written);
    }

    private DerivedEntityIndex GetIndex(GraphEntityQueryPlan plan, MapId? map, ReadOnlySpan<int> ints, ReadOnlySpan<float> floats, Func<bool>? isCurrent, Func<int>? seatedTeam)
    {
        if (plan.ReadsSeatedTeam && seatedTeam == null)
            throw new InvalidOperationException("ENTITY_QUERY.ERR.SeatTeamUnresolved");
        if (plan.ParameterCount > GraphVmLimits.MaxInstructionsPerExecution * 3)
            throw new InvalidOperationException("ENTITY_QUERY.ERR.ParameterBudget");
        Span<int> values = stackalloc int[plan.ParameterCount];
        plan.Bind(ints, floats, values);
        QueryInstance? instance = null;
        foreach (QueryInstance existing in _indexes)
        {
            if (ReferenceEquals(existing.Plan, plan) && existing.Map == map && values.SequenceEqual(existing.Values))
            {
                instance = existing;
                break;
            }
        }
        if (instance == null)
        {
            int[] captured = values.ToArray();
            int[]? seatedSlot = null;
            Func<int>? criterion = null;
            if (plan.ReadsSeatedTeam)
            {
                seatedSlot = new int[1];
                int[] slot = seatedSlot;
                Func<int> readTeam = seatedTeam!;
                criterion = () =>
                {
                    int teamId = readTeam();
                    slot[0] = teamId;
                    return teamId;
                };
            }

            var index = new DerivedEntityIndex(_world, map, plan.Description, plan.Dependencies,
                plan.CreatePredicate(this, captured, seatedSlot), isCurrent: isCurrent, criterion: criterion);
            InitialQueryVisitedCount += index.InitialVisitedCount;
            instance = new QueryInstance(plan, map, captured, index);
            _indexes.Add(instance);
        }
        return instance.Index;
    }

    public void BindCollection(EntityCollectionStore store, Entity owner, int keyId, GraphProgramRegistration registration, Func<bool>? isCurrent = null, Func<int>? seatedTeam = null)
    {
        if (_boundStore != null && !ReferenceEquals(_boundStore, store))
            throw new InvalidOperationException("ENTITY_QUERY.ERR.CollectionStoreMismatch");
        if (_boundStore == null)
        {
            _boundStore = store;
            _world.SubscribeEntityDestroyed(RemoveOwner);
        }
        if (!_world.IsAlive(owner) || !_world.Has<MapEntity>(owner)) throw new InvalidOperationException("ENTITY_QUERY.ERR.OwnerMapMissing");
        if (registration.Kind != GraphKind.Query || registration.EntityQueries.Count != 1)
            throw new InvalidOperationException("ENTITY_QUERY.ERR.ExpectedSingleQuerySource");
        Span<int> ints = stackalloc int[GraphVmLimits.MaxIntRegisters];
        Span<float> floats = stackalloc float[GraphVmLimits.MaxFloatRegisters];
        ints.Clear();
        floats.Clear();
        int pc = 0;
        GraphEntityQueryPlan? plan = null;
        bool sawLoadCaster = false;
        for (int step = 0; step < registration.Program.Length; step++)
        {
            if ((uint)pc >= (uint)registration.Program.Length) throw new InvalidOperationException("ENTITY_QUERY.ERR.InvalidPlan");
            GraphInstruction ins = registration.Program[pc++];
            switch ((GraphNodeOp)ins.Op)
            {
                case GraphNodeOp.Jump: pc += ins.Imm; break;
                case GraphNodeOp.ConstInt when plan == null: ints[ins.Dst] = ins.Imm; break;
                case GraphNodeOp.ConstFloat when plan == null: floats[ins.Dst] = ins.ImmF; break;
                case GraphNodeOp.LoadCaster when plan == null:
                    // The list is owned by the map context. This operand means the sole local seat, read again on use.
                    sawLoadCaster = true;
                    if (seatedTeam == null) throw new InvalidOperationException("ENTITY_QUERY.ERR.SeatTeamUnresolved");
                    seatedTeam();
                    break;
                case GraphNodeOp.QueryAllMapEntities:
                    plan = registration.EntityQueries[pc - 1];
                    pc = plan.ResumePc;
                    break;
                case GraphNodeOp.HaltReturnInt:
                    if (plan == null) throw new InvalidOperationException("ENTITY_QUERY.ERR.SourceMissing");
                    if (plan.ReadsSeatedTeam != sawLoadCaster)
                    {
                        throw new InvalidOperationException(plan.ReadsSeatedTeam
                            ? "ENTITY_QUERY.ERR.SeatTeamSourceMissing"
                            : "ENTITY_QUERY.ERR.UntrackedDependency: LoadCaster");
                    }

                    store.BindSource(owner, keyId, GetIndex(plan, _world.Get<MapEntity>(owner).MapId, ints, floats, isCurrent, seatedTeam));
                    return;
                default: throw new InvalidOperationException($"ENTITY_QUERY.ERR.UntrackedDependency: {(GraphNodeOp)ins.Op}");
            }
        }
        throw new InvalidOperationException("ENTITY_QUERY.ERR.CyclicPlan");
    }

    public Span<Entity> GetQueryBuffer(int frameDepth, int capacity)
    {
        if ((uint)frameDepth > GraphVmLimits.MaxInvokeDepth) throw new ArgumentOutOfRangeException(nameof(frameDepth));
        int bufferKey = checked(_executionDepth * (GraphVmLimits.MaxInvokeDepth + 1) + frameDepth);
        if (!_queryBuffers.TryGetValue(bufferKey, out Entity[]? buffer) || buffer.Length < capacity)
        {
            buffer = new Entity[Math.Max(GraphVmLimits.MaxTargets, capacity)];
            _queryBuffers[bufferKey] = buffer;
        }
        return buffer;
    }

    public void BeginExecution()
    {
        if (_executionDepth >= GraphVmLimits.MaxInvokeDepth) throw new InvalidOperationException("ENTITY_QUERY.ERR.ReentrantDepth");
        _executionDepth++;
    }

    public void EndExecution()
    {
        if (_executionDepth == 0) throw new InvalidOperationException("ENTITY_QUERY.ERR.ExecutionScopeMismatch");
        _executionDepth--;
    }

    public void ReleaseMap(MapId map)
    {
        for (int i = _indexes.Count - 1; i >= 0; i--)
        {
            if (_indexes[i].Map != map) continue;
            _boundStore?.RemoveSource(_indexes[i].Index);
            _indexes[i].Index.Dispose();
            _indexes.RemoveAt(i);
        }
    }

    public void Dispose()
    {
        if (_boundStore != null) _world.UnsubscribeEntityDestroyed(RemoveOwner);
        foreach (QueryInstance instance in _indexes)
        {
            _boundStore?.RemoveSource(instance.Index);
            instance.Index.Dispose();
        }
        _indexes.Clear();
        _queryBuffers.Clear();
        _boundStore = null;
    }

    private void RemoveOwner(in Entity entity) => _boundStore?.RemoveOwner(entity);

    private sealed record QueryInstance(GraphEntityQueryPlan Plan, MapId? Map, int[] Values, DerivedEntityIndex Index);
}
