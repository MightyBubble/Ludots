using System;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace Ludots.Core.Navigation2D.Avoidance
{
    public static class OrcaSolver2D
    {
        public readonly struct Neighbor
        {
            public readonly Vector2 Position;
            public readonly Vector2 Velocity;
            public readonly float Radius;

            public Neighbor(Vector2 position, Vector2 velocity, float radius)
            {
                Position = position;
                Velocity = velocity;
                Radius = radius;
            }
        }

        public struct OrcaLine
        {
            public Vector2 Point;
            public Vector2 Direction;
        }

        public static Vector2 ComputeDesiredVelocity(
            Vector2 position,
            Vector2 velocity,
            Vector2 preferredVelocity,
            float maxSpeed,
            float radius,
            float timeHorizon,
            float deltaTime,
            ReadOnlySpan<Neighbor> neighbors,
            Span<OrcaLine> linesScratch)
        {
            if (maxSpeed <= 0f) return Vector2.Zero;
            int lineCount = 0;

            float invTimeHorizon = timeHorizon > 1e-6f ? (1f / timeHorizon) : 0f;

            for (int i = 0; i < neighbors.Length && lineCount < linesScratch.Length; i++)
            {
                linesScratch[lineCount++] = CreateAgentLine(position, velocity, radius, neighbors[i], invTimeHorizon, deltaTime);
            }

            var lines = linesScratch.Slice(0, lineCount);
            Vector2 result = Vector2.Zero;

            int lineFail = LinearProgram2(lines, maxSpeed, preferredVelocity, ref result);
            if (lineFail < lines.Length)
            {
                LinearProgram3(lines, lineFail, maxSpeed, ref result);
            }

            return result;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static OrcaLine CreateAgentLine(
            Vector2 position,
            Vector2 velocity,
            float radius,
            Neighbor other,
            float invTimeHorizon,
            float deltaTime)
        {
            Vector2 relativePosition = other.Position - position;
            Vector2 relativeVelocity = velocity - other.Velocity;
            float distSq = relativePosition.LengthSquared();
            float combinedRadius = radius + other.Radius;
            float combinedRadiusSq = combinedRadius * combinedRadius;

            OrcaLine line;
            Vector2 u;

            if (distSq > combinedRadiusSq)
            {
                Vector2 w = relativeVelocity - invTimeHorizon * relativePosition;
                float wLengthSq = w.LengthSquared();
                float dotProduct1 = Vector2.Dot(w, relativePosition);

                if (dotProduct1 < 0.0f && dotProduct1 * dotProduct1 > combinedRadiusSq * wLengthSq)
                {
                    float wLength = MathF.Sqrt(wLengthSq);
                    Vector2 unitW = w / wLength;
                    line.Direction = new Vector2(unitW.Y, -unitW.X);
                    u = (combinedRadius * invTimeHorizon - wLength) * unitW;
                }
                else
                {
                    float leg = MathF.Sqrt(distSq - combinedRadiusSq);
                    if (Det(relativePosition, w) > 0.0f)
                    {
                        line.Direction = new Vector2(
                            relativePosition.X * leg - relativePosition.Y * combinedRadius,
                            relativePosition.X * combinedRadius + relativePosition.Y * leg) / distSq;
                    }
                    else
                    {
                        line.Direction = -new Vector2(
                            relativePosition.X * leg + relativePosition.Y * combinedRadius,
                            -relativePosition.X * combinedRadius + relativePosition.Y * leg) / distSq;
                    }

                    float dotProduct2 = Vector2.Dot(relativeVelocity, line.Direction);
                    u = dotProduct2 * line.Direction - relativeVelocity;
                }
            }
            else
            {
                float invTimeStep = deltaTime > 1e-6f ? (1f / deltaTime) : 0f;
                Vector2 w = relativeVelocity - invTimeStep * relativePosition;
                float wLength = w.Length();
                Vector2 unitW = wLength > 1e-6f ? (w / wLength) : Vector2.Zero;
                line.Direction = new Vector2(unitW.Y, -unitW.X);
                u = (combinedRadius * invTimeStep - wLength) * unitW;
            }

            line.Point = velocity + 0.5f * u;
            return line;
        }

        private static int LinearProgram2(ReadOnlySpan<OrcaLine> lines, float radius, Vector2 optVelocity, ref Vector2 result)
        {
            if (optVelocity.LengthSquared() > radius * radius)
            {
                result = Vector2.Normalize(optVelocity) * radius;
            }
            else
            {
                result = optVelocity;
            }

            for (int i = 0; i < lines.Length; i++)
            {
                if (Det(lines[i].Direction, lines[i].Point - result) > 0.0f)
                {
                    Vector2 tempResult = result;
                    if (!LinearProgram1(lines, i, radius, optVelocity, ref result))
                    {
                        result = tempResult;
                        return i;
                    }
                }
            }

            return lines.Length;
        }

        private static bool LinearProgram1(ReadOnlySpan<OrcaLine> lines, int lineNo, float radius, Vector2 optVelocity, ref Vector2 result)
        {
            float dotProduct = Vector2.Dot(lines[lineNo].Point, lines[lineNo].Direction);
            float discriminant = dotProduct * dotProduct + radius * radius - lines[lineNo].Point.LengthSquared();

            if (discriminant < 0.0f)
            {
                return false;
            }

            float sqrtDiscriminant = MathF.Sqrt(discriminant);
            float tLeft = -dotProduct - sqrtDiscriminant;
            float tRight = -dotProduct + sqrtDiscriminant;

            for (int i = 0; i < lineNo; i++)
            {
                float denominator = Det(lines[lineNo].Direction, lines[i].Direction);
                float numerator = Det(lines[i].Direction, lines[lineNo].Point - lines[i].Point);

                if (MathF.Abs(denominator) <= 1e-6f)
                {
                    if (numerator < 0.0f) return false;
                    continue;
                }

                float t = numerator / denominator;
                if (denominator >= 0.0f)
                {
                    tRight = MathF.Min(tRight, t);
                }
                else
                {
                    tLeft = MathF.Max(tLeft, t);
                }

                if (tLeft > tRight) return false;
            }

            float tOpt = Vector2.Dot(lines[lineNo].Direction, (optVelocity - lines[lineNo].Point));
            if (tOpt < tLeft) tOpt = tLeft;
            else if (tOpt > tRight) tOpt = tRight;

            result = lines[lineNo].Point + tOpt * lines[lineNo].Direction;
            return true;
        }

        private static void LinearProgram3(ReadOnlySpan<OrcaLine> lines, int beginLine, float radius, ref Vector2 result)
        {
            float distance = 0.0f;
            const int MaxProjLines = 64;

            for (int i = beginLine; i < lines.Length; i++)
            {
                if (Det(lines[i].Direction, lines[i].Point - result) > distance)
                {
                    if (i > MaxProjLines) return;
                    Span<OrcaLine> projLines = stackalloc OrcaLine[MaxProjLines];
                    int projCount = 0;

                    for (int j = 0; j < i; j++)
                    {
                        OrcaLine line;
                        float determinant = Det(lines[i].Direction, lines[j].Direction);

                        if (MathF.Abs(determinant) <= 1e-6f)
                        {
                            if (Vector2.Dot(lines[i].Direction, lines[j].Direction) > 0.0f)
                            {
                                continue;
                            }

                            line.Point = 0.5f * (lines[i].Point + lines[j].Point);
                        }
                        else
                        {
                            line.Point = lines[i].Point + (Det(lines[j].Direction, lines[i].Point - lines[j].Point) / determinant) * lines[i].Direction;
                        }

                        line.Direction = Vector2.Normalize(lines[j].Direction - lines[i].Direction);
                        projLines[projCount++] = line;
                    }

                    Vector2 tempResult = result;
                    Vector2 optVelocity = new Vector2(-lines[i].Direction.Y, lines[i].Direction.X);
                    if (LinearProgram2(projLines.Slice(0, projCount), radius, optVelocity, ref result) < projCount)
                    {
                        result = tempResult;
                    }

                    distance = Det(lines[i].Direction, lines[i].Point - result);
                }
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static float Det(Vector2 a, Vector2 b) => a.X * b.Y - a.Y * b.X;
    }
}
