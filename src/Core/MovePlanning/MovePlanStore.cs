using System;
using System.Collections.Generic;
using System.Numerics;
using Arch.Core;
using Ludots.Core.Gameplay.GAS.Orders;

namespace Ludots.Core.MovePlanning;

public sealed class MovePlanStore : IMovePlanStore
{
    private sealed class Slot
    {
        public readonly int[] PathXcm = new int[OrderSpatial.MaxPoints];
        public readonly int[] PathYcm = new int[OrderSpatial.MaxPoints];
        public int OrderId;
        public int EntityId;
        public int PointCount;
        public Vector2 FinalGoalWorldCm;
        public short PlanGeneration;
    }

    private readonly IMovePlanFinalTargetResolver? _finalTargetResolver;
    private readonly World _world;
    private readonly Dictionary<int, Slot> _slotsByEntityId = new();

    public MovePlanStore(World world, IMovePlanFinalTargetResolver? finalTargetResolver = null)
    {
        _world = world ?? throw new ArgumentNullException(nameof(world));
        _finalTargetResolver = finalTargetResolver;
    }

    public bool TryBindFromOrder(Entity entity, in Order order, out short planGeneration, out Vector2 finalGoalWorldCm)
    {
        return TryBindFromOrder(entity, in order, bindPositionWorldCm: default, trimToBindPosition: false, out planGeneration, out finalGoalWorldCm);
    }

    public bool TryBindFromOrder(Entity entity, in Order order, Vector2 bindPositionWorldCm, out short planGeneration, out Vector2 finalGoalWorldCm)
    {
        return TryBindFromOrder(entity, in order, bindPositionWorldCm, trimToBindPosition: true, out planGeneration, out finalGoalWorldCm);
    }

    private bool TryBindFromOrder(
        Entity entity,
        in Order order,
        Vector2 bindPositionWorldCm,
        bool trimToBindPosition,
        out short planGeneration,
        out Vector2 finalGoalWorldCm)
    {
        planGeneration = 0;
        finalGoalWorldCm = default;
        int pointCount = OrderWorldSpatialResolver.GetSpatialPointCount(_world, in order);
        if (pointCount <= 0)
        {
            Clear(entity);
            return false;
        }

        Vector2 resolvedFinalTargetWorldCm;
        if (_finalTargetResolver != null)
        {
            if (!_finalTargetResolver.TryResolveFinalTarget(_world, in order, out resolvedFinalTargetWorldCm))
            {
                Clear(entity);
                return false;
            }
        }
        else if (OrderWorldSpatialResolver.TryResolveMoveDestination(_world, in order, out Vector3 destinationWorldCm))
        {
            resolvedFinalTargetWorldCm = new Vector2(destinationWorldCm.X, destinationWorldCm.Z);
        }
        else
        {
            Clear(entity);
            return false;
        }

        Slot slot = GetOrCreateSlot(entity);
        slot.EntityId = entity.Id;
        slot.OrderId = order.OrderId;
        slot.PointCount = 0;
        slot.PlanGeneration++;
        if (slot.PlanGeneration <= 0)
        {
            slot.PlanGeneration = 1;
        }

        for (int pointIndex = 0; pointIndex < pointCount && slot.PointCount < OrderSpatial.MaxPoints; pointIndex++)
        {
            if (!OrderWorldSpatialResolver.TryResolveMoveWaypoint(_world, in order, pointIndex, out Vector3 pointWorldCm))
            {
                continue;
            }

            slot.PathXcm[slot.PointCount] = (int)MathF.Round(pointWorldCm.X, MidpointRounding.AwayFromZero);
            slot.PathYcm[slot.PointCount] = (int)MathF.Round(pointWorldCm.Z, MidpointRounding.AwayFromZero);
            slot.PointCount++;
        }

        if (slot.PointCount <= 0)
        {
            Clear(entity);
            return false;
        }

        if (trimToBindPosition)
        {
            TrimSlotPrefixToBindPosition(slot, bindPositionWorldCm);
        }

        slot.FinalGoalWorldCm = resolvedFinalTargetWorldCm;

        planGeneration = slot.PlanGeneration;
        finalGoalWorldCm = slot.FinalGoalWorldCm;
        return true;
    }

    public bool TryGetPlan(Entity entity, int orderId, out MovePlanView plan)
    {
        plan = default;
        if (!_slotsByEntityId.TryGetValue(entity.Id, out Slot? slot) ||
            slot.OrderId != orderId ||
            slot.PointCount <= 0)
        {
            return false;
        }

        plan = new MovePlanView(
            slot.PathXcm.AsSpan(0, slot.PointCount),
            slot.PathYcm.AsSpan(0, slot.PointCount),
            slot.PointCount,
            slot.FinalGoalWorldCm,
            slot.PlanGeneration);
        return true;
    }

    public void Clear(Entity entity)
    {
        _slotsByEntityId.Remove(entity.Id);
    }

    private static void TrimSlotPrefixToBindPosition(Slot slot, Vector2 bindPositionWorldCm)
    {
        if (slot.PointCount <= 1)
        {
            return;
        }

        float bestDistanceSq = float.MaxValue;
        int bestSegmentIndex = 0;
        int bestProjectedXcm = slot.PathXcm[0];
        int bestProjectedYcm = slot.PathYcm[0];

        for (int segmentIndex = 0; segmentIndex < slot.PointCount - 1; segmentIndex++)
        {
            ProjectPointOnSegment(
                bindPositionWorldCm.X,
                bindPositionWorldCm.Y,
                slot.PathXcm[segmentIndex],
                slot.PathYcm[segmentIndex],
                slot.PathXcm[segmentIndex + 1],
                slot.PathYcm[segmentIndex + 1],
                out int projectedXcm,
                out int projectedYcm);

            float dx = bindPositionWorldCm.X - projectedXcm;
            float dy = bindPositionWorldCm.Y - projectedYcm;
            float distanceSq = (dx * dx) + (dy * dy);
            if (distanceSq > bestDistanceSq)
            {
                continue;
            }

            bestDistanceSq = distanceSq;
            bestSegmentIndex = segmentIndex;
            bestProjectedXcm = projectedXcm;
            bestProjectedYcm = projectedYcm;
        }

        int suffixStart = bestSegmentIndex + 1;
        int writeCount = 0;
        slot.PathXcm[writeCount] = bestProjectedXcm;
        slot.PathYcm[writeCount] = bestProjectedYcm;
        writeCount++;

        for (int readIndex = suffixStart; readIndex < slot.PointCount && writeCount < OrderSpatial.MaxPoints; readIndex++)
        {
            slot.PathXcm[writeCount] = slot.PathXcm[readIndex];
            slot.PathYcm[writeCount] = slot.PathYcm[readIndex];
            writeCount++;
        }

        slot.PointCount = Math.Max(1, writeCount);
    }

    private static void ProjectPointOnSegment(
        float pointXcm,
        float pointYcm,
        int fromXcm,
        int fromYcm,
        int toXcm,
        int toYcm,
        out int projectedXcm,
        out int projectedYcm)
    {
        float deltaXcm = toXcm - fromXcm;
        float deltaYcm = toYcm - fromYcm;
        float lengthSq = (deltaXcm * deltaXcm) + (deltaYcm * deltaYcm);
        if (lengthSq <= 0.0001f)
        {
            projectedXcm = fromXcm;
            projectedYcm = fromYcm;
            return;
        }

        float t = Math.Clamp(((pointXcm - fromXcm) * deltaXcm + (pointYcm - fromYcm) * deltaYcm) / lengthSq, 0f, 1f);
        projectedXcm = (int)MathF.Round(fromXcm + (deltaXcm * t), MidpointRounding.AwayFromZero);
        projectedYcm = (int)MathF.Round(fromYcm + (deltaYcm * t), MidpointRounding.AwayFromZero);
    }

    private Slot GetOrCreateSlot(Entity entity)
    {
        if (_slotsByEntityId.TryGetValue(entity.Id, out Slot? slot))
        {
            return slot;
        }

        slot = new Slot { EntityId = entity.Id };
        _slotsByEntityId.Add(entity.Id, slot);
        return slot;
    }
}
