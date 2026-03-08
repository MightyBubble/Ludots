using System;
using System.Numerics;
using System.Runtime.CompilerServices;
using Ludots.Core.Navigation2D.Config;

namespace Ludots.Core.Navigation2D.Avoidance
{
    public static class SonarSolver2D
    {
        private const float Epsilon = 1e-5f;
        private const int MaxIntervals = 96;

        public readonly struct Obstacle
        {
            public readonly Vector2 Position;
            public readonly Vector2 Velocity;
            public readonly float Radius;

            public Obstacle(Vector2 position, Vector2 velocity, float radius)
            {
                Position = position;
                Velocity = velocity;
                Radius = radius;
            }
        }

        private struct Interval
        {
            public float Min;
            public float Max;

            public Interval(float min, float max)
            {
                Min = min;
                Max = max;
            }
        }

        public static Vector2 ComputeDesiredVelocity(
            Vector2 position,
            Vector2 velocity,
            Vector2 preferredVelocity,
            float maxSpeed,
            float radius,
            float timeHorizon,
            ReadOnlySpan<Obstacle> obstacles,
            Navigation2DSonarConfig config,
            bool fallbackToPreferredVelocity)
        {
            float preferredLen = preferredVelocity.Length();
            if (preferredLen <= Epsilon || maxSpeed <= Epsilon)
            {
                return Vector2.Zero;
            }

            Vector2 desiredDir = preferredVelocity / preferredLen;
            float preferredAngle = MathF.Atan2(desiredDir.Y, desiredDir.X);
            float maxSteerAngle = DegreesToRadians(config.MaxSteerAngleDeg) * 0.5f;
            float backwardPenaltyAngle = DegreesToRadians(config.BackwardPenaltyAngleDeg) * 0.5f;

            Span<Interval> available = stackalloc Interval[MaxIntervals];
            int availableCount = 1;
            available[0] = new Interval(-MathF.PI, MathF.PI);

            if (maxSteerAngle < MathF.PI)
            {
                availableCount = SubtractBlockedInterval(available, availableCount, maxSteerAngle, MathF.PI);
                availableCount = SubtractBlockedInterval(available, availableCount, -MathF.PI, -maxSteerAngle);
            }

            if (backwardPenaltyAngle > Epsilon && velocity.LengthSquared() > Epsilon * Epsilon)
            {
                float backwardAngle = WrapAngle(RelativeAngle(-Vector2.Normalize(velocity), desiredDir));
                availableCount = SubtractBlockedIntervalWrapped(
                    available,
                    availableCount,
                    backwardAngle - backwardPenaltyAngle,
                    backwardAngle + backwardPenaltyAngle);
            }

            float predictionTime = MathF.Max(0f, timeHorizon * config.PredictionTimeScale);
            for (int i = 0; i < obstacles.Length && availableCount > 0; i++)
            {
                ref readonly var obstacle = ref obstacles[i];
                Vector2 relativePosition = obstacle.Position - position;
                Vector2 relativeVelocity = obstacle.Velocity - velocity;
                Vector2 predictedOffset = relativePosition + relativeVelocity * predictionTime;
                float distanceSq = predictedOffset.LengthSquared();
                float combinedRadius = radius + obstacle.Radius;

                if (distanceSq <= Epsilon)
                {
                    return config.BlockedStop ? Vector2.Zero : ClampPreferred(preferredVelocity, maxSpeed, fallbackToPreferredVelocity);
                }

                if (config.IgnoreBehindMovingAgents && obstacle.Velocity.LengthSquared() > Epsilon * Epsilon)
                {
                    Vector2 offsetDir = Vector2.Normalize(predictedOffset);
                    if (Vector2.Dot(desiredDir, offsetDir) < 0f)
                    {
                        continue;
                    }
                }

                float distance = MathF.Sqrt(distanceSq);
                float blockHalfAngle;
                if (distance <= combinedRadius + Epsilon)
                {
                    blockHalfAngle = MathF.PI;
                }
                else
                {
                    float ratio = Math.Clamp(combinedRadius / distance, 0f, 1f);
                    blockHalfAngle = MathF.Asin(ratio);
                }

                float obstacleAngle = RelativeAngle(predictedOffset, desiredDir);
                availableCount = SubtractBlockedIntervalWrapped(
                    available,
                    availableCount,
                    obstacleAngle - blockHalfAngle,
                    obstacleAngle + blockHalfAngle);
            }

            if (ContainsAngleZero(available, availableCount))
            {
                return ScaleToPreferredSpeed(desiredDir, preferredLen, maxSpeed);
            }

            if (TrySelectClosestAngle(available, availableCount, out float relativeAngle))
            {
                float solvedAngle = preferredAngle + relativeAngle;
                Vector2 solvedDirection = new Vector2(MathF.Cos(solvedAngle), MathF.Sin(solvedAngle));
                return ScaleToPreferredSpeed(solvedDirection, preferredLen, maxSpeed);
            }

            if (config.BlockedStop)
            {
                return Vector2.Zero;
            }

            return ClampPreferred(preferredVelocity, maxSpeed, fallbackToPreferredVelocity);
        }

        private static int SubtractBlockedIntervalWrapped(Span<Interval> available, int availableCount, float blockedMin, float blockedMax)
        {
            blockedMin = WrapAngle(blockedMin);
            blockedMax = WrapAngle(blockedMax);
            if (blockedMin <= blockedMax)
            {
                return SubtractBlockedInterval(available, availableCount, blockedMin, blockedMax);
            }

            availableCount = SubtractBlockedInterval(available, availableCount, blockedMin, MathF.PI);
            return SubtractBlockedInterval(available, availableCount, -MathF.PI, blockedMax);
        }

        private static int SubtractBlockedInterval(Span<Interval> available, int availableCount, float blockedMin, float blockedMax)
        {
            int count = availableCount;
            for (int index = 0; index < count; index++)
            {
                var interval = available[index];
                if (blockedMax <= interval.Min || blockedMin >= interval.Max)
                {
                    continue;
                }

                bool cutsLeft = blockedMin > interval.Min;
                bool cutsRight = blockedMax < interval.Max;

                if (!cutsLeft && !cutsRight)
                {
                    RemoveAt(available, ref count, index);
                    index--;
                    continue;
                }

                if (cutsLeft && cutsRight)
                {
                    if (count >= available.Length)
                    {
                        available[index] = new Interval(interval.Min, blockedMin);
                        return count;
                    }

                    ShiftRight(available, count, index + 1);
                    available[index] = new Interval(interval.Min, blockedMin);
                    available[index + 1] = new Interval(blockedMax, interval.Max);
                    count++;
                    return count;
                }

                available[index] = cutsLeft
                    ? new Interval(interval.Min, blockedMin)
                    : new Interval(blockedMax, interval.Max);
            }

            return count;
        }

        private static bool ContainsAngleZero(ReadOnlySpan<Interval> available, int availableCount)
        {
            for (int i = 0; i < availableCount; i++)
            {
                if (available[i].Min <= 0f && 0f <= available[i].Max)
                {
                    return true;
                }
            }

            return false;
        }

        private static bool TrySelectClosestAngle(ReadOnlySpan<Interval> available, int availableCount, out float angle)
        {
            bool found = false;
            float bestAngle = 0f;
            float bestAbs = float.MaxValue;

            for (int i = 0; i < availableCount; i++)
            {
                var interval = available[i];
                float candidateA = interval.Min;
                float candidateB = interval.Max;

                float absA = MathF.Abs(candidateA);
                if (absA < bestAbs)
                {
                    bestAbs = absA;
                    bestAngle = candidateA;
                    found = true;
                }

                float absB = MathF.Abs(candidateB);
                if (absB < bestAbs)
                {
                    bestAbs = absB;
                    bestAngle = candidateB;
                    found = true;
                }
            }

            angle = bestAngle;
            return found;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static Vector2 ScaleToPreferredSpeed(Vector2 direction, float preferredSpeed, float maxSpeed)
        {
            float targetSpeed = preferredSpeed <= maxSpeed ? preferredSpeed : maxSpeed;
            return direction * targetSpeed;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static Vector2 ClampPreferred(Vector2 preferredVelocity, float maxSpeed, bool fallbackToPreferredVelocity)
        {
            if (!fallbackToPreferredVelocity)
            {
                return Vector2.Zero;
            }

            float lenSq = preferredVelocity.LengthSquared();
            if (lenSq <= maxSpeed * maxSpeed)
            {
                return preferredVelocity;
            }

            return Vector2.Normalize(preferredVelocity) * maxSpeed;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static float RelativeAngle(Vector2 vector, Vector2 referenceDirection)
        {
            float angle = MathF.Atan2(vector.Y, vector.X) - MathF.Atan2(referenceDirection.Y, referenceDirection.X);
            return WrapAngle(angle);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static float WrapAngle(float angle)
        {
            while (angle <= -MathF.PI) angle += MathF.Tau;
            while (angle > MathF.PI) angle -= MathF.Tau;
            return angle;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static float DegreesToRadians(int degrees)
        {
            return degrees * (MathF.PI / 180f);
        }

        private static void RemoveAt(Span<Interval> available, ref int count, int index)
        {
            for (int i = index; i < count - 1; i++)
            {
                available[i] = available[i + 1];
            }

            count--;
        }

        private static void ShiftRight(Span<Interval> available, int count, int insertIndex)
        {
            for (int i = count; i > insertIndex; i--)
            {
                available[i] = available[i - 1];
            }
        }
    }
}

