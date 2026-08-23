using System;
using System.Runtime.CompilerServices;
using Arch.Core;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Knowledge;
using Ludots.Core.Mathematics.FixedPoint;

namespace Ludots.Core.EntityHistory;

public readonly struct EntityRef : IEquatable<EntityRef>
{
    public EntityRef(int id, int worldId, int version)
    {
        Id = id;
        WorldId = worldId;
        Version = version;
    }

    public int Id { get; }
    public int WorldId { get; }
    public int Version { get; }
    public bool IsNull => Id < 0 || Version < 0;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static EntityRef From(Entity entity) => new(entity.Id, entity.WorldId, entity.Version);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Entity ToEntity() => EntityUtil.Reconstruct(Id, WorldId, Version);

    public bool Equals(EntityRef other) => Id == other.Id && WorldId == other.WorldId && Version == other.Version;
    public override bool Equals(object? obj) => obj is EntityRef other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(Id, WorldId, Version);
    public static bool operator ==(EntityRef left, EntityRef right) => left.Equals(right);
    public static bool operator !=(EntityRef left, EntityRef right) => !left.Equals(right);
    public override string ToString() => $"{Id}:{WorldId}:{Version}";
}

public enum EntitySnapshotState : byte
{
    Live = 0,
    Destroyed = 1,
}

public unsafe struct EntitySnapshot
{
    public const int ValueCapacity = 8;

    public EntityRef Identity;
    public int CapturedTick;
    public EntitySnapshotState State;
    public Fix64Vec2 Position;
    public byte HasPosition;
    public KnowledgeIdMask256 AttributeMask;
    public KnowledgeIdMask256 TagMask;
    public int AttributeValueCount;
    public fixed int AttributeValueIds[ValueCapacity];
    public fixed long AttributeValueRaws[ValueCapacity];

    public bool TrySetAttributeValue(int id, Fix64 value)
    {
        if ((uint)id >= 256u)
            throw new ArgumentOutOfRangeException(nameof(id));

        for (int i = 0; i < AttributeValueCount; i++)
        {
            if (AttributeValueIds[i] == id)
            {
                AttributeValueRaws[i] = value.RawValue;
                return true;
            }
        }

        if (AttributeValueCount >= ValueCapacity)
            return false;

        AttributeValueIds[AttributeValueCount] = id;
        AttributeValueRaws[AttributeValueCount] = value.RawValue;
        AttributeValueCount++;
        return true;
    }

    public bool TryGetAttributeValue(int id, out Fix64 value)
    {
        for (int i = 0; i < AttributeValueCount; i++)
        {
            if (AttributeValueIds[i] == id)
            {
                value = Fix64.FromRaw(AttributeValueRaws[i]);
                return true;
            }
        }

        value = default;
        return false;
    }
}

public unsafe struct KnowledgeSnapshot
{
    public const int ValueCapacity = 8;

    public EntityRef Viewer;
    public EntityRef Target;
    public KnowledgePresence Presence;
    public KnowledgePositionAccess PositionAccess;
    public Fix64Vec2 Position;
    public byte HasPosition;
    public KnowledgeIdMask256 AttributeMask;
    public KnowledgeIdMask256 TagMask;
    public int ObservedTick;
    public int ExpiryTick;
    public int ConfidencePermille;
    public uint Revision;
    public int AttributeValueCount;
    public fixed int AttributeValueIds[ValueCapacity];
    public fixed long AttributeValueRaws[ValueCapacity];

    public bool IsExpired(int currentTick) => ExpiryTick > 0 && currentTick >= ExpiryTick;

    public bool TrySetAttributeValue(int id, Fix64 value)
    {
        if ((uint)id >= 256u)
            throw new ArgumentOutOfRangeException(nameof(id));

        for (int i = 0; i < AttributeValueCount; i++)
        {
            if (AttributeValueIds[i] == id)
            {
                AttributeValueRaws[i] = value.RawValue;
                return true;
            }
        }

        if (AttributeValueCount >= ValueCapacity)
            return false;

        AttributeValueIds[AttributeValueCount] = id;
        AttributeValueRaws[AttributeValueCount] = value.RawValue;
        AttributeValueCount++;
        return true;
    }

    public bool TryGetAttributeValue(int id, out Fix64 value)
    {
        for (int i = 0; i < AttributeValueCount; i++)
        {
            if (AttributeValueIds[i] == id)
            {
                value = Fix64.FromRaw(AttributeValueRaws[i]);
                return true;
            }
        }

        value = default;
        return false;
    }
}

public enum EffectTargetResolutionMode : byte
{
    Live = 0,
    LastKnown = 1,
    Point = 2,
    Cell = 3,
}

public readonly struct EffectTargetRef
{
    public EffectTargetRef(
        in EntityRef target,
        in EntityRef viewer,
        EffectTargetResolutionMode mode,
        int submittedTick,
        int knowledgeRevision,
        int expiryTick,
        in Fix64Vec2 point,
        int cell)
    {
        if (target.IsNull && (mode == EffectTargetResolutionMode.Live || mode == EffectTargetResolutionMode.LastKnown))
            throw new ArgumentException("Target identity is required.", nameof(target));
        if (mode == EffectTargetResolutionMode.LastKnown && viewer.IsNull)
            throw new ArgumentException("Viewer identity is required for knowledge resolution.", nameof(viewer));

        Target = target;
        Viewer = viewer;
        Mode = mode;
        SubmittedTick = submittedTick;
        KnowledgeRevision = knowledgeRevision;
        ExpiryTick = expiryTick;
        Point = point;
        Cell = cell;
    }

    public EntityRef Target { get; }
    public EntityRef Viewer { get; }
    public EffectTargetResolutionMode Mode { get; }
    public int SubmittedTick { get; }
    public int KnowledgeRevision { get; }
    public int ExpiryTick { get; }
    public Fix64Vec2 Point { get; }
    public int Cell { get; }
}

public enum EffectTargetResolveResult : byte
{
    Resolved = 0,
    LastKnown = 1,
    MissingValue = 2,
    Stale = 3,
    CapacityRejected = 4,
    MissingIdentity = 5,
}

public readonly struct EffectTargetResolveOutput
{
    public EffectTargetResolveOutput(EffectTargetResolveResult result, in EntityRef identity, in Fix64Vec2 point, int cell, uint knowledgeRevision)
    {
        Result = result;
        Identity = identity;
        Point = point;
        Cell = cell;
        KnowledgeRevision = knowledgeRevision;
    }

    public EffectTargetResolveResult Result { get; }
    public EntityRef Identity { get; }
    public Fix64Vec2 Point { get; }
    public int Cell { get; }
    public uint KnowledgeRevision { get; }
    public bool Succeeded => Result == EffectTargetResolveResult.Resolved || Result == EffectTargetResolveResult.LastKnown;
}

public readonly struct EffectExecutionRecord
{
    public EffectExecutionRecord(
        int rootId,
        int effectTemplateId,
        in EntityRef source,
        in EffectTargetRef target,
        int executedTick,
        EffectTargetResolveResult result,
        int delayTicks,
        int knowledgeTtlTicks,
        long attributeDeltaRaw,
        in KnowledgeIdMask256 tagAdded,
        in KnowledgeIdMask256 tagRemoved)
    {
        RootId = rootId;
        EffectTemplateId = effectTemplateId;
        Source = source;
        Target = target;
        ExecutedTick = executedTick;
        Result = result;
        DelayTicks = delayTicks;
        KnowledgeTtlTicks = knowledgeTtlTicks;
        AttributeDeltaRaw = attributeDeltaRaw;
        TagAdded = tagAdded;
        TagRemoved = tagRemoved;
    }

    public int RootId { get; }
    public int EffectTemplateId { get; }
    public EntityRef Source { get; }
    public EffectTargetRef Target { get; }
    public int ExecutedTick { get; }
    public EffectTargetResolveResult Result { get; }
    public int DelayTicks { get; }
    public int KnowledgeTtlTicks { get; }
    public long AttributeDeltaRaw { get; }
    public KnowledgeIdMask256 TagAdded { get; }
    public KnowledgeIdMask256 TagRemoved { get; }
}
