using Arch.Core;
using Ludots.Core.Gameplay.Spawning;

namespace Ludots.Core.MassNavigation.Runtime;

public struct MassNavigationAgent
{
    public int ProfileId;
}

public struct MassNavigationAgentIndex
{
    public int Value;
}

public struct MassNavigationAgentProfile
{
    public int ProfileId;
    public bool Heavy;
    public float SpeedCmPerSecond;
}

/// <summary>
/// 挂接约定的 nav 成员身份挂起快照：attachment 挂接链上只允许一个 mass nav 成员
/// （独立移动的根）；子实体 attach 时摘除成员身份并存此快照，detach / 孤儿自愈时恢复。
/// 求解器槽位的回收与重播种由 MassNavigationAuthoredAgentBindingSystem 按组件在场感知，
/// 恢复时不复用旧 Index——按已提交位姿重新绑定。
/// </summary>
public struct SuspendedNavMembership
{
    public MassNavigationAgent Agent;
}

public static class MassNavigationMembership
{
    public const string SuspendedError = "MASSNAV.MEMBERSHIP.ERR.AlreadySuspended";

    public static bool IsMember(World world, Entity entity)
    {
        return world.Has<MassNavigationAgent>(entity);
    }

    public static void Suspend(World world, Entity entity)
    {
        if (!world.Has<MassNavigationAgent>(entity))
        {
            return;
        }

        if (world.Has<SuspendedNavMembership>(entity))
        {
            throw new System.InvalidOperationException(
                $"{SuspendedError}: entity={entity.Id}.");
        }

        world.Add(entity, new SuspendedNavMembership { Agent = world.Get<MassNavigationAgent>(entity) });
        if (world.Has<MassNavigationAgentIndex>(entity))
        {
            world.Remove<MassNavigationAgentIndex>(entity);
        }

        if (world.Has<MassNavigationAgentProfile>(entity))
        {
            world.Remove<MassNavigationAgentProfile>(entity);
        }

        world.Remove<MassNavigationAgent>(entity);
    }

    public static void Restore(World world, Entity entity)
    {
        if (!world.Has<SuspendedNavMembership>(entity))
        {
            return;
        }

        world.Add(entity, world.Get<SuspendedNavMembership>(entity).Agent);
        world.Remove<SuspendedNavMembership>(entity);
    }
}

public struct MassNavigationBlocker
{
    public float RadiusCm;
}

public struct MassNavigationBlockerProfile
{
    public float RadiusCm;
}

public unsafe struct MassNavigationFlowObstacleProjection
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

public struct MassNavigationHotspotMarker
{
}
