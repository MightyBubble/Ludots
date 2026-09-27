using System;
using Arch.Core;
using Ludots.Core.Gameplay.Relationships;

namespace Ludots.Core.Gameplay.GAS;

/// <summary>
/// Effect-phase staging for relationship link, metric, and flag writes.
/// The store stays unchanged until commit; reads during the phase see this log.
/// Rollback drops the log. A commit that later fails restores the snapshotted edges.
/// </summary>
internal sealed class RelationshipLinkSideEffectJournal
{
    private enum OpKind : byte
    {
        Ensure = 1,
        Remove = 2,
        SetMetric = 3,
        SetFlag = 4,
    }

    private struct Op
    {
        public OpKind Kind;
        public Entity Source;
        public Entity Target;
        public int TypeId;
        public int OperandId;
        public int Value;
    }

    private struct Snapshot
    {
        public Entity Source;
        public Entity Target;
        public int TypeId;
        public bool Existed;
        public RelationshipEdge Edge;
    }

    private readonly Op[] _ops;
    private readonly Snapshot[] _snapshots;
    private int _count;
    private int _snapshotCount;
    private int _changeCheckpoint;
    private bool _applied;
    private RelationshipRuntime? _runtime;

    public RelationshipLinkSideEffectJournal(int capacity)
    {
        if (capacity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(capacity));
        }

        _ops = new Op[capacity];
        _snapshots = new Snapshot[capacity];
    }

    public bool HasPending => _count > 0;

    public void Clear()
    {
        _count = 0;
        _snapshotCount = 0;
        _changeCheckpoint = 0;
        _applied = false;
        _runtime = null;
    }

    public void StageEnsure(RelationshipRuntime runtime, Entity source, Entity target, int typeId)
    {
        Bind(runtime);
        runtime.RequireLinkEndpoints(source, target);
        Append(OpKind.Ensure, source, target, runtime.RequireRelationshipTypeId(typeId), operandId: 0, value: 0);
    }

    public void StageRemove(RelationshipRuntime runtime, Entity source, Entity target, int typeId)
    {
        Bind(runtime);
        if (!runtime.AreLinkEndpointsAlive(source, target))
        {
            return;
        }

        Append(OpKind.Remove, source, target, runtime.RequireRelationshipTypeId(typeId), operandId: 0, value: 0);
    }

    public short StageSetMetric(RelationshipRuntime runtime, Entity source, Entity target, int typeId, int metricId, int value)
    {
        Bind(runtime);
        runtime.RequireLinkEndpoints(source, target);
        short clamped = runtime.ClampMetric(metricId, value);
        Append(OpKind.SetMetric, source, target, runtime.RequireRelationshipTypeId(typeId), metricId, clamped);
        return clamped;
    }

    public short StageAddMetric(RelationshipRuntime runtime, Entity source, Entity target, int typeId, int metricId, int delta)
    {
        short current = TryReadMetric(runtime, source, target, metricId, typeId, out short staged)
            ? staged
            : runtime.GetMetric(source, target, typeId, metricId);
        return StageSetMetric(runtime, source, target, typeId, metricId, current + delta);
    }

    public void StageSetFlag(RelationshipRuntime runtime, Entity source, Entity target, int typeId, int flagId, bool enabled)
    {
        Bind(runtime);
        runtime.RequireLinkEndpoints(source, target);
        runtime.RequireFlagMask(flagId);
        Append(OpKind.SetFlag, source, target, runtime.RequireRelationshipTypeId(typeId), flagId, enabled ? 1 : 0);
    }

    public bool TryReadHasLink(RelationshipRuntime runtime, Entity source, Entity target, int typeId, out bool hasLink)
    {
        hasLink = false;
        if (!CanRead(runtime) || !Touches(source, target, typeId))
        {
            return false;
        }

        hasLink = FoldHasLink(runtime, source, target, typeId);
        return true;
    }

    public bool TryReadMetric(RelationshipRuntime runtime, Entity source, Entity target, int metricId, int typeId, out short value)
    {
        value = 0;
        if (!CanRead(runtime) || !Touches(source, target, typeId))
        {
            return false;
        }

        FoldEdge(runtime, source, target, typeId, out bool live, out RelationshipEdge edge);
        value = live ? edge.GetMetric(metricId) : runtime.MetricDefault(metricId);
        return true;
    }

    public bool TryReadFlag(RelationshipRuntime runtime, Entity source, Entity target, int flagId, int typeId, out bool enabled)
    {
        enabled = false;
        if (!CanRead(runtime) || !Touches(source, target, typeId))
        {
            return false;
        }

        FoldEdge(runtime, source, target, typeId, out bool live, out RelationshipEdge edge);
        enabled = live && (edge.Flags & runtime.RequireFlagMask(flagId)) != 0;
        return true;
    }

    public void AdjustOutgoing(RelationshipRuntime runtime, Entity source, int typeId, Span<Entity> buffer, ref int count, ref int dropped)
    {
        if (!CanRead(runtime))
        {
            return;
        }

        int write = 0;
        for (int i = 0; i < count; i++)
        {
            if (FoldHasLink(runtime, source, buffer[i], typeId))
            {
                buffer[write++] = buffer[i];
            }
        }

        count = write;
        for (int i = 0; i < _count; i++)
        {
            ref readonly Op op = ref _ops[i];
            if (op.Kind == OpKind.Remove || op.Source != source || !TypeMatches(typeId, op.TypeId))
            {
                continue;
            }

            if (FoldHasLink(runtime, source, op.Target, typeId))
            {
                AddUnique(buffer, ref count, ref dropped, op.Target);
            }
        }
    }

    public void AdjustIncoming(RelationshipRuntime runtime, Entity target, int typeId, Span<Entity> buffer, ref int count, ref int dropped)
    {
        if (!CanRead(runtime))
        {
            return;
        }

        int write = 0;
        for (int i = 0; i < count; i++)
        {
            if (FoldHasLink(runtime, buffer[i], target, typeId))
            {
                buffer[write++] = buffer[i];
            }
        }

        count = write;
        for (int i = 0; i < _count; i++)
        {
            ref readonly Op op = ref _ops[i];
            if (op.Kind == OpKind.Remove || op.Target != target || !TypeMatches(typeId, op.TypeId))
            {
                continue;
            }

            if (FoldHasLink(runtime, op.Source, target, typeId))
            {
                AddUnique(buffer, ref count, ref dropped, op.Source);
            }
        }
    }

    public void RebuildBetweenPair(RelationshipRuntime runtime, Entity source, Entity target, int typeId, Span<Entity> buffer, out int count, out int dropped)
    {
        count = 0;
        dropped = 0;
        if (FoldHasLink(runtime, source, target, typeId))
        {
            AddUnique(buffer, ref count, ref dropped, target);
        }

        if (FoldHasLink(runtime, target, source, typeId))
        {
            AddUnique(buffer, ref count, ref dropped, source);
        }
    }

    public void KeepMutual(RelationshipRuntime runtime, Entity second, int typeId, Span<Entity> buffer, ref int count)
    {
        int write = 0;
        for (int i = 0; i < count; i++)
        {
            Entity candidate = buffer[i];
            if (FoldHasLink(runtime, candidate, second, typeId) && FoldHasLink(runtime, second, candidate, typeId))
            {
                buffer[write++] = candidate;
            }
        }

        count = write;
    }

    public void Commit()
    {
        if (_count == 0)
        {
            return;
        }

        if (_runtime == null)
        {
            throw new InvalidOperationException(
                $"{EffectPhaseSideEffectTransaction.ScopeNotActiveError}: relationship staging has no runtime.");
        }

        _changeCheckpoint = _runtime.CaptureChangeCount();
        CaptureSnapshots();
        _applied = true;
        for (int i = 0; i < _count; i++)
        {
            Apply(_ops[i]);
        }
    }

    public void RollbackIfApplied()
    {
        if (!_applied || _runtime == null)
        {
            return;
        }

        for (int i = 0; i < _snapshotCount; i++)
        {
            ref readonly Snapshot snapshot = ref _snapshots[i];
            _runtime.RestoreEdge(snapshot.Source, snapshot.Target, snapshot.TypeId, snapshot.Existed, in snapshot.Edge);
        }

        _runtime.TruncateChanges(_changeCheckpoint);
        _applied = false;
    }

    private void Apply(in Op op)
    {
        switch (op.Kind)
        {
            case OpKind.Ensure:
                _runtime!.EnsureLink(op.Source, op.Target, op.TypeId);
                return;
            case OpKind.Remove:
                _runtime!.RemoveLink(op.Source, op.Target, op.TypeId);
                return;
            case OpKind.SetMetric:
                _runtime!.SetMetric(op.Source, op.Target, op.TypeId, op.OperandId, op.Value);
                return;
            case OpKind.SetFlag:
                _runtime!.SetFlag(op.Source, op.Target, op.TypeId, op.OperandId, op.Value != 0);
                return;
            default:
                throw new InvalidOperationException(
                    $"{EffectPhaseSideEffectTransaction.UnsupportedSideEffectError}: relationship-op={(byte)op.Kind}.");
        }
    }

    private void CaptureSnapshots()
    {
        _snapshotCount = 0;
        for (int i = 0; i < _count; i++)
        {
            ref readonly Op op = ref _ops[i];
            if (FindSnapshot(op.Source, op.Target, op.TypeId) >= 0)
            {
                continue;
            }

            if (_snapshotCount >= _snapshots.Length)
            {
                throw new InvalidOperationException(
                    $"{EffectPhaseSideEffectTransaction.CapacityExceededError}: destination=RelationshipLinkSnapshot, capacity={_snapshots.Length}.");
            }

            ref Snapshot snapshot = ref _snapshots[_snapshotCount++];
            snapshot.Source = op.Source;
            snapshot.Target = op.Target;
            snapshot.TypeId = op.TypeId;
            snapshot.Existed = _runtime!.HasLink(op.Source, op.Target, op.TypeId);
            snapshot.Edge = default;
            if (snapshot.Existed && !_runtime.TryCopyEdge(op.Source, op.Target, op.TypeId, out snapshot.Edge))
            {
                throw new InvalidOperationException(
                    $"Relationship edge {op.Source.Id}->{op.Target.Id} type {op.TypeId} could not be snapshotted.");
            }
        }
    }

    private int FindSnapshot(Entity source, Entity target, int typeId)
    {
        for (int i = 0; i < _snapshotCount; i++)
        {
            ref readonly Snapshot snapshot = ref _snapshots[i];
            if (snapshot.Source == source && snapshot.Target == target && snapshot.TypeId == typeId)
            {
                return i;
            }
        }

        return -1;
    }

    private bool CanRead(RelationshipRuntime runtime)
        => _count > 0 && ReferenceEquals(_runtime, runtime);

    private void Bind(RelationshipRuntime runtime)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        if (_runtime != null && !ReferenceEquals(_runtime, runtime))
        {
            throw new InvalidOperationException(
                $"{EffectPhaseSideEffectTransaction.ScopeAlreadyActiveError}: relationship staging is bound to another runtime.");
        }

        _runtime = runtime;
    }

    private void Append(OpKind kind, Entity source, Entity target, int typeId, int operandId, int value)
    {
        if (_count >= _ops.Length)
        {
            throw new InvalidOperationException(
                $"{EffectPhaseSideEffectTransaction.CapacityExceededError}: destination=RelationshipLink, capacity={_ops.Length}.");
        }

        _ops[_count++] = new Op
        {
            Kind = kind,
            Source = source,
            Target = target,
            TypeId = typeId,
            OperandId = operandId,
            Value = value,
        };
    }

    private bool Touches(Entity source, Entity target, int typeId)
    {
        for (int i = 0; i < _count; i++)
        {
            ref readonly Op op = ref _ops[i];
            if (op.Source == source && op.Target == target && TypeMatches(typeId, op.TypeId))
            {
                return true;
            }
        }

        return false;
    }

    private static bool TypeMatches(int requestedTypeId, int opTypeId)
        => requestedTypeId == RelationshipTypeRegistry.AnyTypeId || requestedTypeId == opTypeId;

    private bool FoldHasLink(RelationshipRuntime runtime, Entity source, Entity target, int typeId)
    {
        if (typeId != RelationshipTypeRegistry.AnyTypeId)
        {
            FoldEdge(runtime, source, target, typeId, out bool live, out _);
            return live;
        }

        Span<int> typeIds = stackalloc int[64];
        int typeCount = runtime.CopyLinkTypeIds(source, target, typeIds);
        for (int i = 0; i < _count; i++)
        {
            ref readonly Op op = ref _ops[i];
            if (op.Source != source || op.Target != target || ContainsType(typeIds, typeCount, op.TypeId))
            {
                continue;
            }

            if (typeCount >= typeIds.Length)
            {
                throw new InvalidOperationException(
                    $"{EffectPhaseSideEffectTransaction.CapacityExceededError}: destination=RelationshipTypeFold, capacity={typeIds.Length}.");
            }

            typeIds[typeCount++] = op.TypeId;
        }

        for (int i = 0; i < typeCount; i++)
        {
            FoldEdge(runtime, source, target, typeIds[i], out bool live, out _);
            if (live)
            {
                return true;
            }
        }

        return false;
    }

    private static bool ContainsType(ReadOnlySpan<int> typeIds, int count, int typeId)
    {
        for (int i = 0; i < count; i++)
        {
            if (typeIds[i] == typeId)
            {
                return true;
            }
        }

        return false;
    }

    private void FoldEdge(
        RelationshipRuntime runtime,
        Entity source,
        Entity target,
        int typeId,
        out bool live,
        out RelationshipEdge edge)
    {
        live = runtime.HasLink(source, target, typeId);
        edge = default;
        if (live && !runtime.TryCopyEdge(source, target, typeId, out edge))
        {
            throw new InvalidOperationException(
                $"Relationship edge {source.Id}->{target.Id} type {typeId} is visible but could not be read.");
        }

        for (int i = 0; i < _count; i++)
        {
            ref readonly Op op = ref _ops[i];
            if (op.Source != source || op.Target != target || op.TypeId != typeId)
            {
                continue;
            }

            switch (op.Kind)
            {
                case OpKind.Ensure:
                    if (!live)
                    {
                        edge = runtime.CreateDefaultEdge();
                        live = true;
                    }

                    break;
                case OpKind.Remove:
                    live = false;
                    edge = default;
                    break;
                case OpKind.SetMetric:
                    if (!live)
                    {
                        edge = runtime.CreateDefaultEdge();
                        live = true;
                    }

                    edge.SetMetric(op.OperandId, (short)op.Value);
                    break;
                case OpKind.SetFlag:
                    if (!live)
                    {
                        edge = runtime.CreateDefaultEdge();
                        live = true;
                    }

                    uint mask = runtime.RequireFlagMask(op.OperandId);
                    edge.Flags = op.Value != 0 ? edge.Flags | mask : edge.Flags & ~mask;
                    break;
            }
        }
    }

    private static void AddUnique(Span<Entity> buffer, ref int count, ref int dropped, Entity entity)
    {
        for (int i = 0; i < count; i++)
        {
            if (buffer[i] == entity)
            {
                return;
            }
        }

        if (count < buffer.Length)
        {
            buffer[count++] = entity;
        }
        else
        {
            dropped++;
        }
    }
}
