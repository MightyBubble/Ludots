using System;
using Ludots.Core.Gameplay.Spawning;
using Ludots.Core.Map;
using Ludots.Core.Mathematics;
using Ludots.Core.Mathematics.FixedPoint;

namespace PresenterBlacksmithShowcaseMod.Runtime
{
    public static class PresenterBlacksmithScatterPlanner
    {
        public const float DefaultMinRadiusCm = 750f;
        public const float DefaultMaxRadiusCm = 2400f;
        public const float DefaultJitterCm = 140f;
        private const int ScatterScratchCapacity = 4096;
        [ThreadStatic]
        private static RuntimeEntitySpawnRequest[]? _scatterScratch;

        public static int EnqueueScatter(
            RuntimeEntitySpawnQueue queue,
            MapId mapId,
            int extraBuildings,
            int seed,
            float minRadiusCm = DefaultMinRadiusCm,
            float maxRadiusCm = DefaultMaxRadiusCm)
        {
            return EnqueueTemplateScatter(
                queue,
                mapId,
                PresenterBlacksmithShowcaseIds.TemplateId,
                extraBuildings,
                seed,
                minRadiusCm,
                maxRadiusCm,
                DefaultJitterCm);
        }

        public static int EnqueueTemplateScatter(
            RuntimeEntitySpawnQueue queue,
            MapId mapId,
            string templateId,
            int entityCount,
            int seed,
            float minRadiusCm = DefaultMinRadiusCm,
            float maxRadiusCm = DefaultMaxRadiusCm,
            float jitterCm = DefaultJitterCm)
        {
            ArgumentNullException.ThrowIfNull(queue);
            ArgumentException.ThrowIfNullOrWhiteSpace(templateId);

            if (entityCount <= 0)
            {
                return 0;
            }

            if (minRadiusCm < 0f)
            {
                throw new ArgumentOutOfRangeException(nameof(minRadiusCm));
            }

            if (maxRadiusCm <= minRadiusCm)
            {
                throw new ArgumentOutOfRangeException(nameof(maxRadiusCm));
            }

            if (jitterCm < 0f)
            {
                throw new ArgumentOutOfRangeException(nameof(jitterCm));
            }

            var random = new Random(seed);
            int ringCount = Math.Max(1, (int)Math.Ceiling(Math.Sqrt(entityCount)));
            int ringCapacity = Math.Max(6, entityCount / ringCount + 2);
            int queued = 0;
            RuntimeEntitySpawnRequest[] scratch = _scatterScratch ??= new RuntimeEntitySpawnRequest[ScatterScratchCapacity];

            for (int index = 0; index < entityCount; index++)
            {
                int batchCount = Math.Min(scratch.Length, entityCount - index);
                for (int batchIndex = 0; batchIndex < batchCount; batchIndex++)
                {
                    int spawnIndex = index + batchIndex;
                    int ring = spawnIndex / ringCapacity;
                    int slot = spawnIndex % ringCapacity;
                    float ringAlpha = ringCount <= 1 ? 1f : (ring + 1f) / ringCount;
                    float radius = minRadiusCm + ((maxRadiusCm - minRadiusCm) * ringAlpha);
                    float angle = ((slot / (float)ringCapacity) * MathF.PI * 2f) + ((float)random.NextDouble() * 0.55f);
                    float jitterX = ((float)random.NextDouble() * 2f - 1f) * jitterCm;
                    float jitterY = ((float)random.NextDouble() * 2f - 1f) * jitterCm;
                    float x = MathF.Cos(angle) * radius + jitterX;
                    float y = MathF.Sin(angle) * radius + jitterY;

                    if ((x * x) + (y * y) < (minRadiusCm * minRadiusCm))
                    {
                        float scale = minRadiusCm / MathF.Max(1f, MathF.Sqrt((x * x) + (y * y)));
                        x *= scale;
                        y *= scale;
                    }

                    scratch[batchIndex] = new RuntimeEntitySpawnRequest
                    {
                        Kind = RuntimeEntitySpawnKind.Template,
                        TemplateId = templateId,
                        MapId = mapId,
                        WorldPositionCm = Fix64Vec2.FromFloat(x, y),
                        HasWorldPosition = 1,
                        HasFacing = 1,
                        FacingAngleRad = angle + MathF.PI,
                    };
                }

                int written = queue.EnqueueMany(scratch.AsSpan(0, batchCount));
                queued += written;
                if (written != batchCount)
                {
                    return queued;
                }

                index += batchCount - 1;
            }

            return queued;
        }

        public static int EnqueueTemplateAreaScatter(
            RuntimeEntitySpawnQueue queue,
            MapId mapId,
            string templateId,
            int entityCount,
            int seed,
            float leftCm,
            float rightCm,
            float topCm,
            float bottomCm,
            float jitterCm = DefaultJitterCm)
        {
            ArgumentNullException.ThrowIfNull(queue);
            ArgumentException.ThrowIfNullOrWhiteSpace(templateId);

            if (entityCount <= 0)
            {
                return 0;
            }

            if (!float.IsFinite(leftCm) ||
                !float.IsFinite(rightCm) ||
                !float.IsFinite(topCm) ||
                !float.IsFinite(bottomCm) ||
                leftCm >= rightCm ||
                topCm >= bottomCm)
            {
                throw new ArgumentOutOfRangeException(nameof(leftCm), "Area scatter requires finite min/max bounds with positive width and height.");
            }

            if (jitterCm < 0f || !float.IsFinite(jitterCm))
            {
                throw new ArgumentOutOfRangeException(nameof(jitterCm));
            }

            float widthCm = rightCm - leftCm;
            float heightCm = bottomCm - topCm;
            int columns = Math.Max(1, (int)Math.Ceiling(Math.Sqrt(entityCount * (widthCm / MathF.Max(heightCm, 1f)))));
            int rows = Math.Max(1, (int)Math.Ceiling(entityCount / (float)columns));
            float cellWidthCm = widthCm / columns;
            float cellHeightCm = heightCm / rows;
            float effectiveJitterX = MathF.Min(jitterCm, cellWidthCm * 0.45f);
            float effectiveJitterY = MathF.Min(jitterCm, cellHeightCm * 0.45f);
            var random = new Random(seed);
            int queued = 0;
            RuntimeEntitySpawnRequest[] scratch = _scatterScratch ??= new RuntimeEntitySpawnRequest[ScatterScratchCapacity];

            for (int index = 0; index < entityCount; index++)
            {
                int batchCount = Math.Min(scratch.Length, entityCount - index);
                for (int batchIndex = 0; batchIndex < batchCount; batchIndex++)
                {
                    int spawnIndex = index + batchIndex;
                    int row = spawnIndex / columns;
                    int column = spawnIndex - (row * columns);
                    float jitterX = ((float)random.NextDouble() * 2f - 1f) * effectiveJitterX;
                    float jitterY = ((float)random.NextDouble() * 2f - 1f) * effectiveJitterY;
                    float x = leftCm + ((column + 0.5f) * cellWidthCm) + jitterX;
                    float y = topCm + ((row + 0.5f) * cellHeightCm) + jitterY;
                    x = Math.Clamp(x, leftCm, rightCm);
                    y = Math.Clamp(y, topCm, bottomCm);
                    float angle = (float)random.NextDouble() * MathF.PI * 2f;

                    scratch[batchIndex] = new RuntimeEntitySpawnRequest
                    {
                        Kind = RuntimeEntitySpawnKind.Template,
                        TemplateId = templateId,
                        MapId = mapId,
                        WorldPositionCm = Fix64Vec2.FromFloat(x, y),
                        HasWorldPosition = 1,
                        HasFacing = 1,
                        FacingAngleRad = angle,
                    };
                }

                int written = queue.EnqueueMany(scratch.AsSpan(0, batchCount));
                queued += written;
                if (written != batchCount)
                {
                    return queued;
                }

                index += batchCount - 1;
            }

            return queued;
        }

        public static int EnqueueTemplateClusterScatter(
            RuntimeEntitySpawnQueue queue,
            MapId mapId,
            string templateId,
            int entityCount,
            int seed,
            float centerXCm,
            float centerYCm,
            float radiusCm,
            float jitterCm = DefaultJitterCm)
        {
            ArgumentNullException.ThrowIfNull(queue);
            ArgumentException.ThrowIfNullOrWhiteSpace(templateId);

            if (entityCount <= 0)
            {
                return 0;
            }

            if (!float.IsFinite(centerXCm) ||
                !float.IsFinite(centerYCm) ||
                !float.IsFinite(radiusCm) ||
                radiusCm <= 0f)
            {
                throw new ArgumentOutOfRangeException(nameof(radiusCm), "Cluster scatter requires a finite positive radius and finite center.");
            }

            if (jitterCm < 0f || !float.IsFinite(jitterCm))
            {
                throw new ArgumentOutOfRangeException(nameof(jitterCm));
            }

            int columns = Math.Max(1, (int)Math.Ceiling(Math.Sqrt(entityCount)));
            int rows = Math.Max(1, (int)Math.Ceiling(entityCount / (float)columns));
            float cellWidthCm = (radiusCm * 2f) / columns;
            float cellHeightCm = (radiusCm * 2f) / rows;
            float effectiveJitterX = MathF.Min(jitterCm, cellWidthCm * 0.35f);
            float effectiveJitterY = MathF.Min(jitterCm, cellHeightCm * 0.35f);
            var random = new Random(seed);
            int queued = 0;
            RuntimeEntitySpawnRequest[] scratch = _scatterScratch ??= new RuntimeEntitySpawnRequest[ScatterScratchCapacity];

            for (int index = 0; index < entityCount; index++)
            {
                int batchCount = Math.Min(scratch.Length, entityCount - index);
                for (int batchIndex = 0; batchIndex < batchCount; batchIndex++)
                {
                    int spawnIndex = index + batchIndex;
                    int row = spawnIndex / columns;
                    int column = spawnIndex - (row * columns);
                    float jitterX = ((float)random.NextDouble() * 2f - 1f) * effectiveJitterX;
                    float jitterY = ((float)random.NextDouble() * 2f - 1f) * effectiveJitterY;
                    float x = centerXCm - radiusCm + ((column + 0.5f) * cellWidthCm) + jitterX;
                    float y = centerYCm - radiusCm + ((row + 0.5f) * cellHeightCm) + jitterY;
                    float angle = WorldPlane2D.FacingRadFromDirection(centerXCm - x, centerYCm - y);

                    scratch[batchIndex] = new RuntimeEntitySpawnRequest
                    {
                        Kind = RuntimeEntitySpawnKind.Template,
                        TemplateId = templateId,
                        MapId = mapId,
                        WorldPositionCm = Fix64Vec2.FromFloat(x, y),
                        HasWorldPosition = 1,
                        HasFacing = 1,
                        FacingAngleRad = angle,
                    };
                }

                int written = queue.EnqueueMany(scratch.AsSpan(0, batchCount));
                queued += written;
                if (written != batchCount)
                {
                    return queued;
                }

                index += batchCount - 1;
            }

            return queued;
        }
    }
}
