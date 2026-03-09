using System;
using System.Collections.Generic;
using System.Numerics;
using Arch.Core;
using Ludots.Core.Components;
using Ludots.Core.Mathematics;
using Ludots.Core.Mathematics.FixedPoint;
using Ludots.Core.Physics2D.Components;

namespace RtsDemoMod.Systems
{
    internal readonly record struct FormationAssignment(Entity Actor, Fix64Vec2 TargetCm);

    internal static class RtsFormationPlanner
    {
        public static List<FormationAssignment> Plan(World world, IReadOnlyList<Entity> actors, in WorldCmInt2 targetCm)
        {
            var assignments = new List<FormationAssignment>(actors.Count);
            if (actors.Count == 0)
            {
                return assignments;
            }

            if (actors.Count == 1)
            {
                assignments.Add(new FormationAssignment(actors[0], Fix64Vec2.FromInt(targetCm.X, targetCm.Y)));
                return assignments;
            }

            float avgRadius = 0f;
            var actorPositions = new List<(Entity Actor, Vector2 PosCm)>(actors.Count);
            for (int i = 0; i < actors.Count; i++)
            {
                var actor = actors[i];
                if (!world.IsAlive(actor))
                {
                    continue;
                }

                avgRadius += RtsUnitRuntimeSetup.GetRadiusCm(world, actor);
                actorPositions.Add((actor, ReadPositionCm(world, actor)));
            }

            if (actorPositions.Count == 0)
            {
                return assignments;
            }

            avgRadius /= actorPositions.Count;
            float spacing = Math.Max(avgRadius * 2.4f, 96f);
            int columns = (int)MathF.Ceiling(MathF.Sqrt(actorPositions.Count));
            int rows = (int)MathF.Ceiling(actorPositions.Count / (float)columns);

            var remainingSlots = new List<Vector2>(actorPositions.Count);
            float halfWidth = (columns - 1) * 0.5f;
            float halfHeight = (rows - 1) * 0.5f;
            for (int row = 0; row < rows; row++)
            {
                for (int col = 0; col < columns; col++)
                {
                    if (remainingSlots.Count >= actorPositions.Count)
                    {
                        break;
                    }

                    float x = targetCm.X + (col - halfWidth) * spacing;
                    float y = targetCm.Y + (row - halfHeight) * spacing;
                    remainingSlots.Add(new Vector2(x, y));
                }
            }

            Vector2 targetVec = new Vector2(targetCm.X, targetCm.Y);
            actorPositions.Sort((a, b) => DistanceSquared(b.PosCm, targetVec).CompareTo(DistanceSquared(a.PosCm, targetVec)));

            for (int i = 0; i < actorPositions.Count; i++)
            {
                var actor = actorPositions[i];
                int bestSlotIndex = 0;
                float bestDistance = float.MaxValue;
                for (int slotIndex = 0; slotIndex < remainingSlots.Count; slotIndex++)
                {
                    float distance = DistanceSquared(actor.PosCm, remainingSlots[slotIndex]);
                    if (distance < bestDistance)
                    {
                        bestDistance = distance;
                        bestSlotIndex = slotIndex;
                    }
                }

                var slot = remainingSlots[bestSlotIndex];
                remainingSlots.RemoveAt(bestSlotIndex);
                assignments.Add(new FormationAssignment(actor.Actor, Fix64Vec2.FromFloat(slot.X, slot.Y)));
            }

            return assignments;
        }

        private static Vector2 ReadPositionCm(World world, Entity entity)
        {
            if (world.TryGet(entity, out Position2D position))
            {
                return position.Value.ToVector2();
            }

            if (world.TryGet(entity, out WorldPositionCm worldPosition))
            {
                return worldPosition.Value.ToVector2();
            }

            return Vector2.Zero;
        }

        private static float DistanceSquared(Vector2 a, Vector2 b)
        {
            var delta = a - b;
            return delta.LengthSquared();
        }
    }
}


