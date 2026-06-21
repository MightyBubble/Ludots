using Arch.Core;
using Ludots.Core.Gameplay.Spawning;

namespace Ludots.Core.MassCrowd.Runtime;

public struct MassCrowdAgent
{
    public int ProfileId;
}

public struct MassCrowdAgentIndex
{
    public int Value;
}

public struct MassCrowdAgentProfile
{
    public int ProfileId;
    public bool Heavy;
    public float VisualScale;
    public float SpeedCmPerSecond;
}

public struct MassCrowdBlocker
{
    public float RadiusCm;
}

public struct MassCrowdBlockerProfile
{
    public float RadiusCm;
}

public unsafe struct MassFlowObstacleProjection
{
    public const int MaxPieces = CompoundObstacle2D.MaxPieces;

    public byte PieceCount;
    public int ShapeSignature;
    public int PoseSignature;
    public fixed byte ShapeValues[MaxPieces];
    public fixed int OffsetXCms[MaxPieces];
    public fixed int OffsetYCms[MaxPieces];
    public fixed int RadiusCms[MaxPieces];

    public void SetPiece(
        int pieceIndex,
        ManifestationObstacleShape2D shape,
        int offsetXCm,
        int offsetYCm,
        int radiusCm)
    {
        ValidatePieceIndex(pieceIndex);
        fixed (byte* shapeValues = ShapeValues)
        fixed (int* offsetXCms = OffsetXCms)
        fixed (int* offsetYCms = OffsetYCms)
        fixed (int* radiusCms = RadiusCms)
        {
            shapeValues[pieceIndex] = (byte)shape;
            offsetXCms[pieceIndex] = offsetXCm;
            offsetYCms[pieceIndex] = offsetYCm;
            radiusCms[pieceIndex] = radiusCm;
        }

        if (PieceCount < pieceIndex + 1)
        {
            PieceCount = (byte)(pieceIndex + 1);
        }
    }

    public readonly ManifestationObstacleShape2D GetShape(int pieceIndex)
    {
        ValidateDeclaredPieceIndex(pieceIndex);
        fixed (byte* shapeValues = ShapeValues)
        {
            return (ManifestationObstacleShape2D)shapeValues[pieceIndex];
        }
    }

    public readonly int GetOffsetXCm(int pieceIndex)
    {
        ValidateDeclaredPieceIndex(pieceIndex);
        fixed (int* offsetXCms = OffsetXCms)
        {
            return offsetXCms[pieceIndex];
        }
    }

    public readonly int GetOffsetYCm(int pieceIndex)
    {
        ValidateDeclaredPieceIndex(pieceIndex);
        fixed (int* offsetYCms = OffsetYCms)
        {
            return offsetYCms[pieceIndex];
        }
    }

    public readonly int GetRadiusCm(int pieceIndex)
    {
        ValidateDeclaredPieceIndex(pieceIndex);
        fixed (int* radiusCms = RadiusCms)
        {
            return radiusCms[pieceIndex];
        }
    }

    private static void ValidatePieceIndex(int pieceIndex)
    {
        if ((uint)pieceIndex >= MaxPieces)
        {
            throw new System.ArgumentOutOfRangeException(nameof(pieceIndex));
        }
    }

    private readonly void ValidateDeclaredPieceIndex(int pieceIndex)
    {
        if ((uint)pieceIndex >= PieceCount)
        {
            throw new System.ArgumentOutOfRangeException(nameof(pieceIndex));
        }
    }
}

public struct MassCrowdHotspotMarker
{
}

public struct SimulationAuthority
{
}

public struct SimulationResidencyPolicy
{
    public SimulationResidencyKind Kind;
}

public enum SimulationResidencyKind : byte
{
    AlwaysResident = 1,
    BudgetedResident = 2,
    Streamable = 3,
}

public struct CollisionParticipation
{
    public CollisionParticipationKind Kind;
}

public enum CollisionParticipationKind : byte
{
    CrowdOnly = 1,
    Physics2D = 2,
    Physics2DAndCrowd = 3,
}

public struct AvoidanceLane
{
    public AvoidanceLaneKind Kind;
}

public enum AvoidanceLaneKind : byte
{
    FormationPhysics = 1,
    MassCrowd = 2,
}

public struct MassCrowdFormationAnchor
{
    public int FormationId;
    public int SlotCount;
}

public struct MassCrowdFormationFollower
{
    public int FormationId;
    public Entity Anchor;
    public int SlotIndex;
    public float LocalOffsetXCm;
    public float LocalOffsetYCm;
}

public struct MassCrowdFollowerLocomotion
{
    public float TargetChangeEpsilonCm;
    public float FacingChangeEpsilonRadians;
}
