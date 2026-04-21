using System;
using Ludots.Core.Gameplay.Spawning;
using Ludots.Core.Map;
using Ludots.Core.Mathematics.FixedPoint;

namespace PerformerBlacksmithShowcaseMod.Runtime
{
    public static class PerformerBlacksmithScatterPlanner
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
            ArgumentNullException.ThrowIfNull(queue);

            if (extraBuildings <= 0)
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

            var random = new Random(seed);
            int ringCount = Math.Max(1, (int)Math.Ceiling(Math.Sqrt(extraBuildings)));
            int ringCapacity = Math.Max(6, extraBuildings / ringCount + 2);
            int queued = 0;
            RuntimeEntitySpawnRequest[] scratch = _scatterScratch ??= new RuntimeEntitySpawnRequest[ScatterScratchCapacity];

            for (int index = 0; index < extraBuildings; index++)
            {
                int batchCount = Math.Min(scratch.Length, extraBuildings - index);
                for (int batchIndex = 0; batchIndex < batchCount; batchIndex++)
                {
                    int spawnIndex = index + batchIndex;
                    int ring = spawnIndex / ringCapacity;
                    int slot = spawnIndex % ringCapacity;
                    float ringAlpha = ringCount <= 1 ? 1f : (ring + 1f) / ringCount;
                    float radius = minRadiusCm + ((maxRadiusCm - minRadiusCm) * ringAlpha);
                    float angle = ((slot / (float)ringCapacity) * MathF.PI * 2f) + ((float)random.NextDouble() * 0.55f);
                    float jitterX = ((float)random.NextDouble() * 2f - 1f) * DefaultJitterCm;
                    float jitterY = ((float)random.NextDouble() * 2f - 1f) * DefaultJitterCm;
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
                        TemplateId = PerformerBlacksmithShowcaseIds.TemplateId,
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
    }
}
