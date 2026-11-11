using System;
using System.Text.Json.Nodes;
using Ludots.Core.Engine;

namespace PresenterTreeOverrideShowcaseMod.Runtime
{
    internal readonly struct PresenterTreeRiderSchedule
    {
        public PresenterTreeRiderSchedule(
            float cycleSeconds,
            float boardAtSeconds,
            float alightAtSeconds,
            float seatOffsetXCm,
            float seatOffsetYCm,
            int alightRadiusCm)
        {
            CycleSeconds = cycleSeconds;
            BoardAtSeconds = boardAtSeconds;
            AlightAtSeconds = alightAtSeconds;
            SeatOffsetXCm = seatOffsetXCm;
            SeatOffsetYCm = seatOffsetYCm;
            AlightRadiusCm = alightRadiusCm;
        }

        public float CycleSeconds { get; }
        public float BoardAtSeconds { get; }
        public float AlightAtSeconds { get; }
        public float SeatOffsetXCm { get; }
        public float SeatOffsetYCm { get; }
        public int AlightRadiusCm { get; }

        public bool ShouldRide(float elapsedSeconds)
        {
            float t = elapsedSeconds % CycleSeconds;
            return t >= BoardAtSeconds && t < AlightAtSeconds;
        }
    }

    internal static class PresenterTreeShowcaseMetadata
    {
        public const string SectionKey = "presenterTreeShowcase";

        public static bool TryReadRiderSchedule(GameEngine engine, out PresenterTreeRiderSchedule schedule)
        {
            schedule = default;
            if (!TryGetSection(engine, out JsonObject section, out string mapId))
            {
                return false;
            }

            float cycle = ReadRequiredFloat(section, mapId, "riderCycleSeconds");
            float board = ReadRequiredFloat(section, mapId, "riderBoardAtSeconds");
            float alight = ReadRequiredFloat(section, mapId, "riderAlightAtSeconds");
            float seatX = ReadRequiredFloat(section, mapId, "riderSeatOffsetXCm");
            float seatY = ReadRequiredFloat(section, mapId, "riderSeatOffsetYCm");
            float radius = ReadRequiredFloat(section, mapId, "riderAlightRadiusCm");
            if (cycle <= 0f || board < 0f || alight <= board || alight > cycle || radius <= 0f)
            {
                throw new InvalidOperationException(
                    $"Map '{mapId}' metadata.{SectionKey} rider schedule must satisfy 0 <= board < alight <= cycle and alight radius > 0.");
            }

            schedule = new PresenterTreeRiderSchedule(cycle, board, alight, seatX, seatY, (int)radius);
            return true;
        }

        private static bool TryGetSection(GameEngine engine, out JsonObject section, out string mapId)
        {
            section = null!;
            mapId = engine.CurrentMapSession?.MapId.Value ?? string.Empty;
            if (engine.CurrentMapSession?.MapConfig?.Metadata == null ||
                !engine.CurrentMapSession.MapConfig.Metadata.TryGetValue(SectionKey, out JsonNode? node) ||
                node == null)
            {
                return false;
            }

            section = node as JsonObject
                ?? throw new InvalidOperationException($"Map '{mapId}' metadata.{SectionKey} must be an object.");
            return true;
        }

        private static float ReadRequiredFloat(JsonObject section, string mapId, string key)
        {
            if (!section.TryGetPropertyValue(key, out JsonNode? valueNode) || valueNode == null)
            {
                throw new InvalidOperationException($"Map '{mapId}' must declare metadata.{SectionKey}.{key}.");
            }

            float value = valueNode.GetValue<float>();
            if (!float.IsFinite(value))
            {
                throw new InvalidOperationException($"Map '{mapId}' metadata.{SectionKey}.{key} must be finite.");
            }

            return value;
        }
    }
}
