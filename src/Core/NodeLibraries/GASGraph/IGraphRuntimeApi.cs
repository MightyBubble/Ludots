using System;
using Arch.Core;
using Ludots.Core.Mathematics;

namespace Ludots.Core.NodeLibraries.GASGraph
{
    public readonly struct EffectArgs
    {
        public readonly byte FloatCount;
        public readonly float F0;
        public readonly float F1;

        public EffectArgs(byte floatCount, float f0, float f1)
        {
            FloatCount = floatCount;
            F0 = f0;
            F1 = f1;
        }

        public static EffectArgs None => default;
    }

    /// <summary>
    /// Protocol constants for <see cref="IGraphRuntimeApi.GetRelationship"/>.
    /// Decouples Graph VM from concrete TeamRelationship enum.
    /// </summary>
    public static class GraphRelationship
    {
        public const int Neutral = 0;
        public const int Friendly = 1;
        public const int Hostile = 2;
    }

    public interface IGraphRuntimeApi
    {
        bool TryGetGridPos(Entity entity, out IntVector2 gridPos);
        bool HasTag(Entity entity, int tagId);
        bool TryGetAttributeCurrent(Entity entity, int attributeId, out float value);
        int QueryRadius(IntVector2 center, float radius, Span<Entity> buffer);
        int QueryCone(IntVector2 origin, int directionDeg, int halfAngleDeg, float rangeCm, Span<Entity> buffer);
        int QueryRectangle(IntVector2 center, int halfWidthCm, int halfHeightCm, int rotationDeg, Span<Entity> buffer);
        int QueryLine(IntVector2 origin, int directionDeg, int lengthCm, int halfWidthCm, Span<Entity> buffer);

        // ── Hex spatial queries ──
        int QueryHexRange(IntVector2 center, int hexRadius, Span<Entity> buffer);
        int QueryHexRing(IntVector2 center, int hexRadius, Span<Entity> buffer);
        int QueryHexNeighbors(IntVector2 center, Span<Entity> buffer);

        int GetTeamId(Entity entity);
        /// <summary>Get the EntityLayer.Category bits for an entity. Returns 0 if no EntityLayer.</summary>
        uint GetEntityLayerCategory(Entity entity);
        /// <summary>
        /// Get relationship between two teams.
        /// Returns one of the <see cref="GraphRelationship"/> constants.
        /// </summary>
        int GetRelationship(int teamA, int teamB);
        void ApplyEffectTemplate(Entity caster, Entity target, int templateId);
        void ApplyEffectTemplate(Entity caster, Entity target, int templateId, in EffectArgs args);
        void ModifyAttributeAdd(Entity caster, Entity target, int attributeId, float delta);
        void SendEvent(Entity caster, Entity target, int eventTagId, float magnitude);

        // ── Blackboard immediate read/write ──

        bool TryReadBlackboardFloat(Entity entity, int keyId, out float value);
        bool TryReadBlackboardInt(Entity entity, int keyId, out int value);
        bool TryReadBlackboardEntity(Entity entity, int keyId, out Entity value);
        void WriteBlackboardFloat(Entity entity, int keyId, float value);
        void WriteBlackboardInt(Entity entity, int keyId, int value);
        void WriteBlackboardEntity(Entity entity, int keyId, Entity value);

        // ── Config parameter reading (from current EffectTemplate context) ──

        bool TryLoadConfigFloat(int keyId, out float value);
        bool TryLoadConfigInt(int keyId, out int value);
    }

    /// <summary>
    /// Resolves symbolic names (tags, attributes, effect templates) to runtime integer ids.
    /// Injected into <see cref="Host.GraphProgramLoader"/> to decouple it from concrete registries.
    /// </summary>
    public interface IGraphSymbolResolver
    {
        int ResolveTag(string name);
        int ResolveAttribute(string name);
        int ResolveEffectTemplate(string name);
    }
}
