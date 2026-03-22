using System;
using System.Collections.Generic;
using System.Numerics;
using Arch.Core;
using Ludots.Core.Gameplay.GAS.Orders;
using Ludots.Core.Mathematics.FixedPoint;

namespace RoadNetworkShowcaseMod.Gameplay
{
    internal readonly ref struct RoadNavPlanView
    {
        public readonly ReadOnlySpan<int> PathXcm;
        public readonly ReadOnlySpan<int> PathYcm;
        public readonly int Count;
        public readonly Vector3 FinalGoalWorldCm;
        public readonly short PlanGeneration;

        public RoadNavPlanView(ReadOnlySpan<int> pathXcm, ReadOnlySpan<int> pathYcm, int count, in Vector3 finalGoalWorldCm, short planGeneration)
        {
            PathXcm = pathXcm;
            PathYcm = pathYcm;
            Count = count;
            FinalGoalWorldCm = finalGoalWorldCm;
            PlanGeneration = planGeneration;
        }

        public bool TryGetWaypoint(int waypointIndex, out Fix64Vec2 waypoint)
        {
            waypoint = default;
            if ((uint)waypointIndex >= (uint)Count)
            {
                return false;
            }

            waypoint = Fix64Vec2.FromInt(PathXcm[waypointIndex], PathYcm[waypointIndex]);
            return true;
        }
    }

    internal sealed class RoadNavPlanStore
    {
        private sealed class Slot
        {
            public readonly int[] PathXcm = new int[OrderSpatial.MaxPoints];
            public readonly int[] PathYcm = new int[OrderSpatial.MaxPoints];
            public int OrderId;
            public int EntityId;
            public int PointCount;
            public Vector3 FinalGoalWorldCm;
            public short PlanGeneration;
        }

        private readonly Dictionary<int, Slot> _slotsByEntityId = new();

        public bool TryBindFromOrder(Entity entity, in Order order, out short planGeneration, out Vector3 finalGoalWorldCm)
        {
            planGeneration = 0;
            finalGoalWorldCm = default;
            int pointCount = OrderWorldSpatialResolver.GetSpatialPointCount(in order.Args.Spatial);
            if (pointCount <= 0)
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
                if (!OrderWorldSpatialResolver.TryResolveMoveWaypoint(in order, pointIndex, out Vector3 pointWorldCm))
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

            slot.FinalGoalWorldCm = RoadRouteFinalTargetResolver.TryResolve(in order, out Vector3 encodedGoal)
                ? encodedGoal
                : OrderWorldSpatialResolver.TryResolveMoveDestination(in order, out Vector3 destinationWorldCm)
                    ? destinationWorldCm
                    : new Vector3(slot.PathXcm[slot.PointCount - 1], 0f, slot.PathYcm[slot.PointCount - 1]);

            planGeneration = slot.PlanGeneration;
            finalGoalWorldCm = slot.FinalGoalWorldCm;
            return true;
        }

        public bool TryGetPlan(Entity entity, int orderId, out RoadNavPlanView plan)
        {
            plan = default;
            if (!_slotsByEntityId.TryGetValue(entity.Id, out Slot? slot) ||
                slot.OrderId != orderId ||
                slot.PointCount <= 0)
            {
                return false;
            }

            plan = new RoadNavPlanView(
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

        private Slot GetOrCreateSlot(Entity entity)
        {
            if (_slotsByEntityId.TryGetValue(entity.Id, out Slot? slot))
            {
                return slot;
            }

            slot = new Slot
            {
                EntityId = entity.Id
            };
            _slotsByEntityId.Add(entity.Id, slot);
            return slot;
        }
    }
}
