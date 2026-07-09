using System;
using Arch.Core;
using Ludots.Core.Association;
using Ludots.Core.Knowledge;
using Ludots.Core.Mathematics;

namespace Ludots.Core.Vision
{
    public enum CellVisibility : byte
    {
        Unseen = 0,
        Explored = 1,
        Visible = 2,
        Denied = 3
    }

    public enum VisionPolarity : byte
    {
        Reveal = 0,
        Deny = 1
    }

    public enum VisionApertureKind : byte
    {
        Disk = 0,
        Cone = 1,
        Box = 2,
        Line = 3
    }

    public enum FogDenyMode : byte
    {
        DenyDominates = 0,
        RevealDominates = 1
    }

    public readonly struct FogLayerId : IEquatable<FogLayerId>
    {
        public FogLayerId(int value)
        {
            if (value <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(value), "Fog layer id must be positive.");
            }

            Value = value;
        }

        public readonly int Value;

        public bool Equals(FogLayerId other) => Value == other.Value;
        public override bool Equals(object? obj) => obj is FogLayerId other && Equals(other);
        public override int GetHashCode() => Value;
        public static bool operator ==(FogLayerId left, FogLayerId right) => left.Equals(right);
        public static bool operator !=(FogLayerId left, FogLayerId right) => !left.Equals(right);
    }

    public readonly struct FogCell : IEquatable<FogCell>
    {
        public FogCell(int x, int y)
        {
            X = x;
            Y = y;
        }

        public readonly int X;
        public readonly int Y;

        public bool Equals(FogCell other) => X == other.X && Y == other.Y;
        public override bool Equals(object? obj) => obj is FogCell other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(X, Y);
        public static bool operator ==(FogCell left, FogCell right) => left.Equals(right);
        public static bool operator !=(FogCell left, FogCell right) => !left.Equals(right);
    }

    public readonly struct FogLayerDefinition
    {
        public FogLayerDefinition(FogLayerId id, string key, int cellSizeCm, int updateHz)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                throw new ArgumentException("Fog layer key is required.", nameof(key));
            }

            if (cellSizeCm <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(cellSizeCm), "Fog layer cell size must be positive.");
            }

            if (updateHz <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(updateHz), "Fog layer update frequency must be positive.");
            }

            Id = id;
            Key = key;
            CellSizeCm = cellSizeCm;
            UpdateHz = updateHz;
        }

        public readonly FogLayerId Id;
        public readonly string Key;
        public readonly int CellSizeCm;
        public readonly int UpdateHz;
    }

    public readonly struct VisionAperture
    {
        public VisionAperture(VisionApertureKind kind, int rangeCm, int halfAngleDeg = 0, int halfWidthCm = 0)
        {
            if (rangeCm < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(rangeCm));
            }

            if (halfAngleDeg < 0 || halfAngleDeg > 180)
            {
                throw new ArgumentOutOfRangeException(nameof(halfAngleDeg));
            }

            if (halfWidthCm < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(halfWidthCm));
            }

            Kind = kind;
            RangeCm = rangeCm;
            HalfAngleDeg = halfAngleDeg;
            HalfWidthCm = halfWidthCm;
        }

        public readonly VisionApertureKind Kind;
        public readonly int RangeCm;
        public readonly int HalfAngleDeg;
        public readonly int HalfWidthCm;

        public static VisionAperture Disk(int rangeCm) => new(VisionApertureKind.Disk, rangeCm);
        public static VisionAperture Cone(int rangeCm, int halfAngleDeg) => new(VisionApertureKind.Cone, rangeCm, halfAngleDeg);
        public static VisionAperture Box(int halfWidthCm, int halfHeightCm) => new(VisionApertureKind.Box, halfHeightCm, 0, halfWidthCm);
        public static VisionAperture Line(int lengthCm, int halfWidthCm) => new(VisionApertureKind.Line, lengthCm, 0, halfWidthCm);
    }

    public readonly struct VisionScope
    {
        public VisionScope(int scopeKeyId, Entity host = default)
        {
            if (scopeKeyId <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(scopeKeyId), "Vision scope requires a registered ScopeKey id.");
            }

            ScopeKey = ScopeKey.Named(scopeKeyId, host);
        }

        public readonly ScopeKey ScopeKey;

        public int ScopeKeyId => ScopeKey.ScopeKeyId;
        public Entity Host => ScopeKey.ScopeHost;
    }

    public struct VisionEmitterCm
    {
        public int ScopeKeyId;
        public uint LayerMask;
        public VisionPolarity Polarity;
        public VisionAperture Aperture;
        public int AltitudeBand;
        public int Priority;
        public int TargetScopeSelectorId;
        public byte UpdatePolicyId;
        public byte DetectionStrength;
        public byte TrueSightStrength;
    }

    public struct FogOccupantCm
    {
        public uint ExposeLayerMask;
        public int AltitudeBand;
        public byte StealthLevel;
    }

    public readonly struct VisionEmitter
    {
        public VisionEmitter(
            int scopeKeyId,
            WorldCmInt2 position,
            int facingDeg,
            uint layerMask,
            VisionPolarity polarity,
            VisionAperture aperture,
            int altitudeBand = 0,
            int priority = 0,
            int targetScopeSelectorId = 0,
            byte detectionStrength = 0,
            byte trueSightStrength = 0)
        {
            if (scopeKeyId <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(scopeKeyId));
            }

            ScopeKeyId = scopeKeyId;
            Position = position;
            FacingDeg = facingDeg;
            LayerMask = layerMask;
            Polarity = polarity;
            Aperture = aperture;
            AltitudeBand = altitudeBand;
            Priority = priority;
            TargetScopeSelectorId = targetScopeSelectorId;
            DetectionStrength = detectionStrength;
            TrueSightStrength = trueSightStrength;
        }

        public readonly int ScopeKeyId;
        public readonly WorldCmInt2 Position;
        public readonly int FacingDeg;
        public readonly uint LayerMask;
        public readonly VisionPolarity Polarity;
        public readonly VisionAperture Aperture;
        public readonly int AltitudeBand;
        public readonly int Priority;
        public readonly int TargetScopeSelectorId;
        public readonly byte DetectionStrength;
        public readonly byte TrueSightStrength;
    }

    public readonly struct FogOccupant
    {
        public FogOccupant(
            Entity entity,
            WorldCmInt2 position,
            uint exposeLayerMask,
            int altitudeBand = 0,
            byte stealthLevel = 0)
            : this(entity, position, exposeLayerMask, altitudeBand, stealthLevel, default, cellSizeCm: 0)
        {
        }

        public FogOccupant(
            Entity entity,
            WorldCmInt2 position,
            uint exposeLayerMask,
            int altitudeBand,
            byte stealthLevel,
            FogCell cell,
            int cellSizeCm)
        {
            if (entity == Entity.Null)
            {
                throw new ArgumentException("Fog occupant entity is required.", nameof(entity));
            }

            Entity = entity;
            Position = position;
            ExposeLayerMask = exposeLayerMask;
            AltitudeBand = altitudeBand;
            StealthLevel = stealthLevel;
            Cell = cell;
            CellSizeCm = cellSizeCm;
        }

        public readonly Entity Entity;
        public readonly WorldCmInt2 Position;
        public readonly uint ExposeLayerMask;
        public readonly int AltitudeBand;
        public readonly byte StealthLevel;
        public readonly FogCell Cell;
        public readonly int CellSizeCm;

        public FogCell ResolveCell(int cellSizeCm)
        {
            return CellSizeCm == cellSizeCm
                ? Cell
                : new FogCell(
                    MathUtil.FloorDiv(Position.X, cellSizeCm),
                    MathUtil.FloorDiv(Position.Y, cellSizeCm));
        }
    }

    public readonly struct FogDisclosurePolicy
    {
        public FogDisclosurePolicy(
            in KnowledgeIdMask256 attributeMask,
            in KnowledgeIdMask256 relationshipTypeMask,
            in KnowledgeIdMask256 tagMask,
            int ttlTicks,
            bool trueSightRevealsConcealment)
        {
            if (ttlTicks < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(ttlTicks));
            }

            AttributeMask = attributeMask;
            RelationshipTypeMask = relationshipTypeMask;
            TagMask = tagMask;
            TtlTicks = ttlTicks;
            TrueSightRevealsConcealment = trueSightRevealsConcealment;
        }

        public readonly KnowledgeIdMask256 AttributeMask;
        public readonly KnowledgeIdMask256 RelationshipTypeMask;
        public readonly KnowledgeIdMask256 TagMask;
        public readonly int TtlTicks;
        public readonly bool TrueSightRevealsConcealment;

        public static FogDisclosurePolicy None => new(
            KnowledgeIdMask256.Empty,
            KnowledgeIdMask256.Empty,
            KnowledgeIdMask256.Empty,
            ttlTicks: 0,
            trueSightRevealsConcealment: true);
    }

    public readonly struct FogRulesPolicy
    {
        public FogRulesPolicy(
            bool verticalEnabled = true,
            bool lineOfSightEnabled = true,
            bool concealmentEnabled = true,
            int upTolerance = 0,
            FogDenyMode denyMode = FogDenyMode.DenyDominates)
        {
            VerticalEnabled = verticalEnabled;
            LineOfSightEnabled = lineOfSightEnabled;
            ConcealmentEnabled = concealmentEnabled;
            UpTolerance = upTolerance;
            DenyMode = denyMode;
        }

        public readonly bool VerticalEnabled;
        public readonly bool LineOfSightEnabled;
        public readonly bool ConcealmentEnabled;
        public readonly int UpTolerance;
        public readonly FogDenyMode DenyMode;

        public static FogRulesPolicy Default => new(
            verticalEnabled: true,
            lineOfSightEnabled: true,
            concealmentEnabled: true);
    }

    public readonly struct FogProjectionPolicy
    {
        public FogProjectionPolicy(
            FogDisclosurePolicy disclosure,
            int memoryTtlTicks,
            bool concealmentEnabled = true,
            bool trueSightRevealsConcealment = true)
        {
            if (memoryTtlTicks < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(memoryTtlTicks));
            }

            Disclosure = disclosure;
            MemoryTtlTicks = memoryTtlTicks;
            ConcealmentEnabled = concealmentEnabled;
            TrueSightRevealsConcealment = trueSightRevealsConcealment;
        }

        public readonly FogDisclosurePolicy Disclosure;
        public readonly int MemoryTtlTicks;
        public readonly bool ConcealmentEnabled;
        public readonly bool TrueSightRevealsConcealment;

        public static FogProjectionPolicy Default => new(FogDisclosurePolicy.None, memoryTtlTicks: 0);
    }

    public readonly struct FogProjectionOccupant
    {
        public FogProjectionOccupant(Entity entity, FogCell cell, uint layerMask, byte stealthLevel = 0)
        {
            if (entity == Entity.Null)
            {
                throw new ArgumentException("Fog occupant entity is required.", nameof(entity));
            }

            Entity = entity;
            Cell = cell;
            LayerMask = layerMask;
            StealthLevel = stealthLevel;
        }

        public readonly Entity Entity;
        public readonly FogCell Cell;
        public readonly uint LayerMask;
        public readonly byte StealthLevel;
    }

    public readonly struct FogRelationshipRule
    {
        public FogRelationshipRule(Entity sourceScopeHost, int relationshipTypeId)
        {
            if (sourceScopeHost == Entity.Null)
            {
                throw new ArgumentException("Relationship-gated fog sharing requires a source scope host.", nameof(sourceScopeHost));
            }

            if (relationshipTypeId < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(relationshipTypeId));
            }

            SourceScopeHost = sourceScopeHost;
            RelationshipTypeId = relationshipTypeId;
        }

        public readonly Entity SourceScopeHost;
        public readonly int RelationshipTypeId;
    }

    public readonly struct FogScopeTarget
    {
        public FogScopeTarget(int scopeKeyId, Entity scopeHost)
        {
            if (scopeKeyId <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(scopeKeyId));
            }

            if (scopeHost == Entity.Null)
            {
                throw new ArgumentException("Fog scope target requires a scope host entity.", nameof(scopeHost));
            }

            ScopeKeyId = scopeKeyId;
            ScopeHost = scopeHost;
        }

        public readonly int ScopeKeyId;
        public readonly Entity ScopeHost;
    }
}
