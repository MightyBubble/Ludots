using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.Relationships;
using Ludots.Core.Gameplay.Teams;
using Ludots.Core.Mathematics;
using Ludots.Core.Navigation.GraphQuery;
using Ludots.Core.NodeLibraries.GASGraph;
using Ludots.Core.Spatial;

namespace CapabilityStandardGraphOpsBlackboardMod.Runtime;

internal sealed class LifecycleShowcaseGraphApi : IGraphRuntimeApi
{
    public int LifecycleTransactionStarts { get; private set; }
    public int BuiltinInvocations { get; private set; }
    public int LastBuiltinHandlerId { get; private set; }

    public void BeginLifecycleTransaction() => LifecycleTransactionStarts++;

    public void InvokeBuiltin(int builtinHandlerId)
    {
        BuiltinInvocations++;
        LastBuiltinHandlerId = builtinHandlerId;
    }

    public bool TryGetGridPos(Arch.Core.Entity entity, out IntVector2 gridPos)
    {
        gridPos = default;
        return false;
    }

    public bool HasTag(Arch.Core.Entity entity, int tagId) => false;

    public bool TryGetAttributeCurrent(Arch.Core.Entity entity, int attributeId, out float value)
    {
        value = 0f;
        return false;
    }

    public SpatialQueryResult QueryRadius(IntVector2 centerCm, float radiusCm, Span<Arch.Core.Entity> buffer)
        => new(0, 0);

    public SpatialQueryResult QueryCone(IntVector2 originCm, int directionDeg, int halfAngleDeg, float rangeCm, Span<Arch.Core.Entity> buffer)
        => new(0, 0);

    public SpatialQueryResult QueryRectangle(IntVector2 centerCm, int halfWidthCm, int halfHeightCm, int rotationDeg, Span<Arch.Core.Entity> buffer)
        => new(0, 0);

    public SpatialQueryResult QueryLine(IntVector2 originCm, int directionDeg, int lengthCm, int halfWidthCm, Span<Arch.Core.Entity> buffer)
        => new(0, 0);

    public int GetTeamId(Arch.Core.Entity entity) => 0;

    public uint GetEntityLayerCategory(Arch.Core.Entity entity) => 0u;

    public int GetRelationship(int teamA, int teamB) => GraphRelationship.Neutral;

    public void ApplyEffectTemplate(Arch.Core.Entity caster, Arch.Core.Entity target, int templateId) { }

    public void ApplyEffectTemplate(Arch.Core.Entity caster, Arch.Core.Entity target, int templateId, in EffectArgs args) { }

    public void RemoveEffectTemplate(Arch.Core.Entity target, int templateId) { }

  public void ModifyAttributeAdd(Arch.Core.Entity caster, Arch.Core.Entity target, int attributeId, float delta) { }

  public void ModifyAttributeSet(Arch.Core.Entity caster, Arch.Core.Entity target, int attributeId, float value) { }

  public SpatialQueryResult QueryHexRange(IntVector2 centerCm, int hexRadius, Span<Arch.Core.Entity> buffer)
    => new(0, 0);

  public SpatialQueryResult QueryHexRing(IntVector2 centerCm, int hexRadius, Span<Arch.Core.Entity> buffer)
    => new(0, 0);

  public SpatialQueryResult QueryHexNeighbors(IntVector2 centerCm, Span<Arch.Core.Entity> buffer)
    => new(0, 0);

  public void SendEvent(Arch.Core.Entity caster, Arch.Core.Entity target, int eventTagId, float magnitude) { }

    public bool TryReadBlackboardFloat(Arch.Core.Entity entity, int keyId, out float value)
    {
        value = 0f;
        return false;
    }

    public bool TryReadBlackboardInt(Arch.Core.Entity entity, int keyId, out int value)
    {
        value = 0;
        return false;
    }

    public bool TryReadBlackboardEntity(Arch.Core.Entity entity, int keyId, out Arch.Core.Entity value)
    {
        value = default;
        return false;
    }

    public void WriteBlackboardFloat(Arch.Core.Entity entity, int keyId, float value) { }

    public void WriteBlackboardInt(Arch.Core.Entity entity, int keyId, int value) { }

    public void WriteBlackboardEntity(Arch.Core.Entity entity, int keyId, Arch.Core.Entity value) { }

    public bool TryLoadConfigFloat(int keyId, out float value)
    {
        value = 0f;
        return false;
    }

    public bool TryLoadConfigInt(int keyId, out int value)
    {
        value = 0;
        return false;
    }

    public bool TrySnapTargetToNearestInCollection(
        Arch.Core.Entity owner,
        int collectionKeyId,
        ref IntVector2 targetPosCm,
        float maxDistanceCm,
        out Arch.Core.Entity snappedEntity)
    {
        snappedEntity = Arch.Core.Entity.Null;
        return false;
    }

    public bool TrySnapTargetToNearestGraphEdge(
        ref IntVector2 targetPosCm,
        float searchRadiusCm,
        out GraphEdgeProjection projection)
    {
        projection = default;
        return false;
    }
}
