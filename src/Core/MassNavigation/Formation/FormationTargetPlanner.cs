using System;
using System.Numerics;

namespace Ludots.Core.MassNavigation.Formation;

public static class FormationTargetPlanner
{
    public static readonly float DiscSlotGoldenAngleRadians = MathF.PI * (3f - MathF.Sqrt(5f));
    private const float MaxNormalizableFacingMagnitudeRadians = 1_000_000f;

    public static Vector2 ResolveSlotOffset(in FormationSlotPlan plan, int slotIndex)
    {
        if ((uint)slotIndex >= (uint)plan.SlotCount)
        {
            throw new ArgumentOutOfRangeException(nameof(slotIndex));
        }

        if (plan.Layout == FormationSlotLayout.Grid)
        {
            if (plan.Columns <= 0 ||
                plan.Rows <= 0 ||
                !(plan.SpacingXCm > 0f) ||
                !(plan.SpacingYCm > 0f) ||
                plan.Columns * plan.Rows != plan.SlotCount)
            {
                throw new InvalidOperationException("Formation grid slot plan requires positive columns, rows, spacing and exact slot capacity.");
            }

            int row = slotIndex / plan.Columns;
            int col = slotIndex % plan.Columns;
            float x = (col - ((plan.Columns - 1) * 0.5f)) * plan.SpacingXCm;
            float y = (row - ((plan.Rows - 1) * 0.5f)) * plan.SpacingYCm;
            return new Vector2(x, y);
        }

        if (plan.Layout == FormationSlotLayout.Disc)
        {
            if (!(plan.RingSpacingCm > 0f))
            {
                throw new InvalidOperationException("Formation disc slot plan requires ring spacing > 0.");
            }

            if (slotIndex == 0)
            {
                return Vector2.Zero;
            }

            float radius = MathF.Sqrt(slotIndex) * plan.RingSpacingCm;
            float angle = slotIndex * DiscSlotGoldenAngleRadians;
            return new Vector2(MathF.Cos(angle) * radius, MathF.Sin(angle) * radius);
        }

        throw new InvalidOperationException($"Unsupported Formation slot layout '{plan.Layout}'.");
    }

    public static bool HasTargetChanged(
        in FormationPose currentPose,
        in FormationPose previousPose,
        float targetChangeEpsilonCm,
        float facingChangeEpsilonRadians,
        bool previousInitialized)
    {
        if (!previousInitialized)
        {
            return true;
        }

        if (!(targetChangeEpsilonCm > 0f) ||
            !(facingChangeEpsilonRadians > 0f))
        {
            throw new InvalidOperationException("Formation target change detection requires positive epsilons.");
        }

        Vector2 delta = currentPose.CenterWorldCm - previousPose.CenterWorldCm;
        float facingDelta = MathF.Abs(NormalizeFacingRadians(currentPose.FacingRadians - previousPose.FacingRadians));
        return delta.LengthSquared() >= targetChangeEpsilonCm * targetChangeEpsilonCm ||
               facingDelta >= facingChangeEpsilonRadians;
    }

    public static FormationTargetPlan PlanMemberTarget(in FormationPose pose, in FormationMember member)
    {
        Vector2 offsetWorld = RotateLocalOffset(member.LocalOffsetCm, pose.FacingRadians);
        return new FormationTargetPlan(pose.CenterWorldCm + offsetWorld, offsetWorld);
    }

    public static Vector2 RotateLocalOffset(Vector2 localOffsetCm, float facingRadians)
    {
        float forwardX = MathF.Cos(facingRadians);
        float forwardY = MathF.Sin(facingRadians);
        float lateralX = -forwardY;
        float lateralY = forwardX;
        return new Vector2(
            (lateralX * localOffsetCm.X) + (forwardX * localOffsetCm.Y),
            (lateralY * localOffsetCm.X) + (forwardY * localOffsetCm.Y));
    }

    public static float NormalizeFacingRadians(float angle)
    {
        if (!TryNormalizeFacingRadians(angle, out float normalized))
        {
            throw new InvalidOperationException(
                $"Formation facing radians must be finite and within ±{MaxNormalizableFacingMagnitudeRadians} before normalization.");
        }

        return normalized;
    }

    public static bool TryNormalizeFacingRadians(float angle, out float normalized)
    {
        if (!float.IsFinite(angle) || MathF.Abs(angle) > MaxNormalizableFacingMagnitudeRadians)
        {
            normalized = default;
            return false;
        }

        angle %= MathF.Tau;
        if (angle > MathF.PI)
        {
            angle -= MathF.Tau;
        }
        else if (angle < -MathF.PI)
        {
            angle += MathF.Tau;
        }

        normalized = angle;
        return true;
    }
}
