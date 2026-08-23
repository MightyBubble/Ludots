using Arch.Core;
using Ludots.Core.Knowledge;
using Ludots.Core.Mathematics.FixedPoint;

namespace Ludots.Core.EntityHistory;

public static class EffectTargetResolver
{
    public static EffectTargetResolveOutput Resolve(
        World world,
        in EffectTargetRef target,
        int currentTick,
        EntitySnapshotStore entitySnapshots,
        KnowledgeSnapshotStore knowledgeSnapshots)
    {
        EntityRef identity = target.Target;
        Fix64Vec2 point = target.Point;
        switch (target.Mode)
        {
            case EffectTargetResolutionMode.Point:
                return new EffectTargetResolveOutput(EffectTargetResolveResult.Resolved, in identity, in point, target.Cell, (uint)target.KnowledgeRevision);
            case EffectTargetResolutionMode.Cell:
                return new EffectTargetResolveOutput(EffectTargetResolveResult.Resolved, in identity, in point, target.Cell, (uint)target.KnowledgeRevision);
            case EffectTargetResolutionMode.Live:
                return ResolveLive(world, in identity, in target, entitySnapshots);
            case EffectTargetResolutionMode.LastKnown:
                return ResolveLastKnown(currentTick, in target, knowledgeSnapshots);
            default:
                return new EffectTargetResolveOutput(EffectTargetResolveResult.MissingIdentity, in identity, in point, target.Cell, 0u);
        }
    }

    private static EffectTargetResolveOutput ResolveLive(World world, in EntityRef identity, in EffectTargetRef target, EntitySnapshotStore snapshots)
    {
        Entity entity = identity.ToEntity();
        Fix64Vec2 point = target.Point;
        if (entity == Entity.Null || !world.IsAlive(entity))
        {
            return snapshots.TryGet(in identity, out EntitySnapshot snapshot)
                ? StaleOutput(in identity, in snapshot, target.Cell, (uint)target.KnowledgeRevision)
                : SnapshotOutput(EffectTargetResolveResult.MissingIdentity, in identity, in point, target.Cell, 0u);
        }

        return SnapshotOutput(EffectTargetResolveResult.Resolved, in identity, in point, target.Cell, (uint)target.KnowledgeRevision);
    }

    private static EffectTargetResolveOutput ResolveLastKnown(int currentTick, in EffectTargetRef target, KnowledgeSnapshotStore snapshots)
    {
        EntityRef identity = target.Target;
        EntityRef viewer = target.Viewer;
        Fix64Vec2 point = target.Point;
        if (target.Viewer.IsNull)
            return SnapshotOutput(EffectTargetResolveResult.MissingIdentity, in identity, in point, target.Cell, 0u);

        if (!snapshots.TryGet(in viewer, in identity, currentTick, out KnowledgeSnapshot snapshot))
        {
            return snapshots.TryGetExpired(in viewer, in identity, out _)
                ? SnapshotOutput(EffectTargetResolveResult.Stale, in identity, in point, target.Cell, 0u)
                : SnapshotOutput(EffectTargetResolveResult.MissingValue, in identity, in point, target.Cell, 0u);
        }

        if (target.KnowledgeRevision > 0 && snapshot.Revision != (uint)target.KnowledgeRevision)
        {
            Fix64Vec2 snapshotPoint = snapshot.Position;
            return SnapshotOutput(EffectTargetResolveResult.Stale, in identity, in snapshotPoint, target.Cell, snapshot.Revision);
        }

        if (snapshot.PositionAccess == KnowledgePositionAccess.None || snapshot.HasPosition == 0)
            return SnapshotOutput(EffectTargetResolveResult.MissingValue, in identity, in point, target.Cell, snapshot.Revision);

        Fix64Vec2 resolvedPoint = snapshot.Position;
        return SnapshotOutput(EffectTargetResolveResult.LastKnown, in identity, in resolvedPoint, target.Cell, snapshot.Revision);
    }

    private static EffectTargetResolveOutput SnapshotOutput(EffectTargetResolveResult result, in EntityRef identity, in Fix64Vec2 point, int cell, uint revision)
        => new(result, in identity, in point, cell, revision);

    private static EffectTargetResolveOutput StaleOutput(in EntityRef identity, in EntitySnapshot snapshot, int cell, uint revision)
    {
        Fix64Vec2 point = snapshot.Position;
        return SnapshotOutput(EffectTargetResolveResult.Stale, in identity, in point, cell, revision);
    }
}
