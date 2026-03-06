using System;
using System.Numerics;
using System.Runtime.CompilerServices;
using Ludots.Core.Navigation2D.Config;

namespace Ludots.Core.Navigation2D.Avoidance
{
    public static class SonarSolver2D
    {
        private const float Epsilon = 1e-5f;
        private const float EpsilonSq = Epsilon * Epsilon;
        public const int MaxIntervals = 96;

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

        public struct Interval
        {
            public float Min;
            public float Max;

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public Interval(float min, float max)
            {
                Min = min;
                Max = max;
            }
        }

        public readonly struct SolveConfig
        {
            public readonly float MaxSteerAngle;
            public readonly float BackwardPenaltyAngle;
            public readonly float PredictionTimeScale;
            public readonly bool IgnoreBehindMovingAgents;
            public readonly bool BlockedStop;
            public readonly bool FallbackToPreferredVelocity;

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public SolveConfig(
                float maxSteerAngle,
                float backwardPenaltyAngle,
                float predictionTimeScale,
                bool ignoreBehindMovingAgents,
                bool blockedStop,
                bool fallbackToPreferredVelocity)
            {
                MaxSteerAngle = maxSteerAngle;
                BackwardPenaltyAngle = backwardPenaltyAngle;
                PredictionTimeScale = predictionTimeScale;
                IgnoreBehindMovingAgents = ignoreBehindMovingAgents;
                BlockedStop = blockedStop;
                FallbackToPreferredVelocity = fallbackToPreferredVelocity;
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public static SolveConfig FromConfig(Navigation2DSonarConfig config, bool fallbackToPreferredVelocity)
            {
                return new SolveConfig(
                    maxSteerAngle: SonarSolver2D.DegreesToRadians(config.MaxSteerAngleDeg) * 0.5f,
                    backwardPenaltyAngle: SonarSolver2D.DegreesToRadians(config.BackwardPenaltyAngleDeg) * 0.5f,
                    predictionTimeScale: config.PredictionTimeScale,
                    ignoreBehindMovingAgents: config.IgnoreBehindMovingAgents,
                    blockedStop: config.BlockedStop,
                    fallbackToPreferredVelocity: fallbackToPreferredVelocity);
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
            Span<Interval> intervalScratch = stackalloc Interval[MaxIntervals];
            var solveConfig = SolveConfig.FromConfig(config, fallbackToPreferredVelocity);
            return ComputeDesiredVelocity(
                position,
                velocity,
                preferredVelocity,
                maxSpeed,
                radius,
                timeHorizon,
                obstacles,
                solveConfig,
                intervalScratch);
        }

        public static Vector2 ComputeDesiredVelocity(
            Vector2 position,
            Vector2 velocity,
            Vector2 preferredVelocity,
            float maxSpeed,
            float radius,
            float timeHorizon,
            ReadOnlySpan<int> obstacleIndices,
            ReadOnlySpan<Vector2> obstaclePositions,
            ReadOnlySpan<Vector2> obstacleVelocities,
            ReadOnlySpan<float> obstacleRadii,
            Navigation2DSonarConfig config,
            bool fallbackToPreferredVelocity)
        {
            Span<Interval> intervalScratch = stackalloc Interval[MaxIntervals];
            var solveConfig = SolveConfig.FromConfig(config, fallbackToPreferredVelocity);
            return ComputeDesiredVelocity(
                position,
                velocity,
                preferredVelocity,
                maxSpeed,
                radius,
                timeHorizon,
                obstacleIndices,
                obstaclePositions,
                obstacleVelocities,
                obstacleRadii,
                solveConfig,
                intervalScratch);
        }

        public static Vector2 ComputeDesiredVelocity(
            Vector2 position,
            Vector2 velocity,
            Vector2 preferredVelocity,
            float maxSpeed,
            float radius,
            float timeHorizon,
            ReadOnlySpan<int> obstacleIndices,
            ReadOnlySpan<Vector2> obstaclePositions,
            ReadOnlySpan<Vector2> obstacleVelocities,
            ReadOnlySpan<float> obstacleRadii,
            in SolveConfig solveConfig,
            Span<Interval> intervalScratch)
        {
            if (!TryBeginSolve(
                velocity,
                preferredVelocity,
                maxSpeed,
                timeHorizon,
                solveConfig,
                intervalScratch,
                out float preferredLen,
                out Vector2 desiredDir,
                out float predictionTime,
                out int availableCount))
            {
                return Vector2.Zero;
            }

            for (int i = 0; i < obstacleIndices.Length && availableCount > 0; i++)
            {
                int obstacleIndex = obstacleIndices[i];
                Vector2 obstaclePosition = obstaclePositions[obstacleIndex];
                Vector2 obstacleVelocity = obstacleVelocities[obstacleIndex];
                float obstacleRadius = obstacleRadii[obstacleIndex];

                Vector2 relativePosition = obstaclePosition - position;
                Vector2 relativeVelocity = obstacleVelocity - velocity;
                Vector2 predictedOffset = relativePosition + relativeVelocity * predictionTime;
                float distanceSq = predictedOffset.LengthSquared();
                float combinedRadius = radius + obstacleRadius;

                if (distanceSq <= EpsilonSq)
                {
                    return solveConfig.BlockedStop
                        ? Vector2.Zero
                        : ClampPreferred(preferredVelocity, maxSpeed, solveConfig.FallbackToPreferredVelocity);
                }

                if (solveConfig.IgnoreBehindMovingAgents && obstacleVelocity.LengthSquared() > EpsilonSq && Vector2.Dot(desiredDir, predictedOffset) < 0f)
                {
                    continue;
                }

                float blockHalfAngle;
                float combinedRadiusWithSlack = combinedRadius + Epsilon;
                if (distanceSq <= combinedRadiusWithSlack * combinedRadiusWithSlack)
                {
                    blockHalfAngle = MathF.PI;
                }
                else
                {
                    float distance = MathF.Sqrt(distanceSq);
                    float ratio = Math.Clamp(combinedRadius / distance, 0f, 1f);
                    blockHalfAngle = MathF.Asin(ratio);
                }

                float obstacleAngle = RelativeAngle(predictedOffset, desiredDir);
                availableCount = SubtractBlockedIntervalWrapped(
                    intervalScratch,
                    availableCount,
                    obstacleAngle - blockHalfAngle,
                    obstacleAngle + blockHalfAngle);
            }

            return FinishSolve(
                desiredDir,
                preferredLen,
                maxSpeed,
                preferredVelocity,
                solveConfig,
                intervalScratch,
                availableCount);
        }

        private static Vector2 ComputeDesiredVelocity(
            Vector2 position,
            Vector2 velocity,
            Vector2 preferredVelocity,
            float maxSpeed,
            float radius,
            float timeHorizon,
            ReadOnlySpan<Obstacle> obstacles,
            in SolveConfig solveConfig,
            Span<Interval> intervalScratch)
        {
            if (!TryBeginSolve(
                velocity,
                preferredVelocity,
                maxSpeed,
                timeHorizon,
                solveConfig,
                intervalScratch,
                out float preferredLen,
                out Vector2 desiredDir,
                out float predictionTime,
                out int availableCount))
            {
                return Vector2.Zero;
            }

            for (int i = 0; i < obstacles.Length && availableCount > 0; i++)
            {
                ref readonly var obstacle = ref obstacles[i];
                Vector2 relativePosition = obstacle.Position - position;
                Vector2 relativeVelocity = obstacle.Velocity - velocity;
                Vector2 predictedOffset = relativePosition + relativeVelocity * predictionTime;
                float distanceSq = predictedOffset.LengthSquared();
                float combinedRadius = radius + obstacle.Radius;

                if (distanceSq <= EpsilonSq)
                {
                    return solveConfig.BlockedStop
                        ? Vector2.Zero
                        : ClampPreferred(preferredVelocity, maxSpeed, solveConfig.FallbackToPreferredVelocity);
                }

                if (solveConfig.IgnoreBehindMovingAgents && obstacle.Velocity.LengthSquared() > EpsilonSq && Vector2.Dot(desiredDir, predictedOffset) < 0f)
                {
                    continue;
                }

                float blockHalfAngle;
                float combinedRadiusWithSlack = combinedRadius + Epsilon;
                if (distanceSq <= combinedRadiusWithSlack * combinedRadiusWithSlack)
                {
                    blockHalfAngle = MathF.PI;
                }
                else
                {
                    float distance = MathF.Sqrt(distanceSq);
                    float ratio = Math.Clamp(combinedRadius / distance, 0f, 1f);
                    blockHalfAngle = MathF.Asin(ratio);
                }

                float obstacleAngle = RelativeAngle(predictedOffset, desiredDir);
                availableCount = SubtractBlockedIntervalWrapped(
                    intervalScratch,
                    availableCount,
                    obstacleAngle - blockHalfAngle,
                    obstacleAngle + blockHalfAngle);
            }

            return FinishSolve(
                desiredDir,
                preferredLen,
                maxSpeed,
                preferredVelocity,
                solveConfig,
                intervalScratch,
                availableCount);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool TryBeginSolve(
            Vector2 velocity,
            Vector2 preferredVelocity,
            float maxSpeed,
            float timeHorizon,
            in SolveConfig solveConfig,
            Span<Interval> intervalScratch,
            out float preferredLen,
            out Vector2 desiredDir,
            out float predictionTime,
            out int availableCount)
        {
            preferredLen = preferredVelocity.Length();
            if (preferredLen <= Epsilon || maxSpeed <= Epsilon)
            {
                desiredDir = Vector2.Zero;
                predictionTime = 0f;
                availableCount = 0;
                return false;
            }

            desiredDir = preferredVelocity / preferredLen;
            availableCount = InitializeAvailableIntervals(intervalScratch, velocity, desiredDir, solveConfig.MaxSteerAngle, solveConfig.BackwardPenaltyAngle);
            predictionTime = MathF.Max(0f, timeHorizon * solveConfig.PredictionTimeScale);
            return true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int InitializeAvailableIntervals(
            Span<Interval> available,
            Vector2 velocity,
            Vector2 desiredDir,
            float maxSteerAngle,
            float backwardPenaltyAngle)
        {
            int availableCount = 1;
            available[0] = new Interval(-MathF.PI, MathF.PI);

            if (maxSteerAngle < MathF.PI)
            {
                availableCount = SubtractBlockedInterval(available, availableCount, maxSteerAngle, MathF.PI);
                availableCount = SubtractBlockedInterval(available, availableCount, -MathF.PI, -maxSteerAngle);
            }

            float velocityLenSq = velocity.LengthSquared();
            if (availableCount > 0 && backwardPenaltyAngle > Epsilon && velocityLenSq > EpsilonSq)
            {
                float invVelocityLen = 1f / MathF.Sqrt(velocityLenSq);
                Vector2 backwardDir = -velocity * invVelocityLen;
                float backwardAngle = RelativeAngle(backwardDir, desiredDir);
                availableCount = SubtractBlockedIntervalWrapped(
                    available,
                    availableCount,
                    backwardAngle - backwardPenaltyAngle,
                    backwardAngle + backwardPenaltyAngle);
            }

            return availableCount;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static Vector2 FinishSolve(
            Vector2 desiredDir,
            float preferredLen,
            float maxSpeed,
            Vector2 preferredVelocity,
            in SolveConfig solveConfig,
            ReadOnlySpan<Interval> available,
            int availableCount)
        {
            if (ContainsAngleZero(available, availableCount))
            {
                return ScaleToPreferredSpeed(desiredDir, preferredLen, maxSpeed);
            }

            if (TrySelectClosestAngle(available, availableCount, out float relativeAngle))
            {
                float preferredAngle = MathF.Atan2(desiredDir.Y, desiredDir.X);
                float solvedAngle = preferredAngle + relativeAngle;
                Vector2 solvedDirection = new Vector2(MathF.Cos(solvedAngle), MathF.Sin(solvedAngle));
                return ScaleToPreferredSpeed(solvedDirection, preferredLen, maxSpeed);
            }

            if (solveConfig.BlockedStop)
            {
                return Vector2.Zero;
            }

            return ClampPreferred(preferredVelocity, maxSpeed, solveConfig.FallbackToPreferredVelocity);
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
            return MathF.Atan2(Det(referenceDirection, vector), Vector2.Dot(referenceDirection, vector));
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

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static float Det(Vector2 a, Vector2 b) => a.X * b.Y - a.Y * b.X;
    }
}
