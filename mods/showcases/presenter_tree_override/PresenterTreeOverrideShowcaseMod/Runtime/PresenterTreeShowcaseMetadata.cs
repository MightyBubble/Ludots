using System;
using System.Numerics;
using System.Text.Json.Nodes;
using Ludots.Core.Config;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.Camera;

namespace PresenterTreeOverrideShowcaseMod.Runtime
{
    internal readonly struct PresenterTreeCameraTour
    {
        public PresenterTreeCameraTour(float stopSeconds, CameraPoseRequest[] poses)
        {
            StopSeconds = stopSeconds;
            Poses = poses;
        }

        public float StopSeconds { get; }
        public CameraPoseRequest[] Poses { get; }

        public int StopIndexAt(float elapsedSeconds) => (int)(elapsedSeconds / StopSeconds) % Poses.Length;
    }

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

        /// <summary>
        /// 巡游第 0 站固定是地图的 DefaultCamera 全景，后面依次是 cameraTour.stops；
        /// 站点不写的 yaw / fov 沿用全景，保证整段巡游朝向一致。
        /// </summary>
        public static bool TryReadCameraTour(GameEngine engine, out PresenterTreeCameraTour tour)
        {
            tour = default;
            if (!TryGetSection(engine, out JsonObject section, out string mapId) ||
                !section.TryGetPropertyValue("cameraTour", out JsonNode? tourNode) ||
                tourNode == null)
            {
                return false;
            }

            JsonObject tourObj = tourNode as JsonObject
                ?? throw new InvalidOperationException($"Map '{mapId}' metadata.{SectionKey}.cameraTour must be an object.");
            CameraConfig overview = engine.CurrentMapSession!.MapConfig!.DefaultCamera
                ?? throw new InvalidOperationException($"Map '{mapId}' cameraTour requires DefaultCamera as the overview stop.");
            float stopSeconds = ReadRequiredFloat(tourObj, mapId, "stopSeconds");
            if (stopSeconds <= 0f)
            {
                throw new InvalidOperationException($"Map '{mapId}' metadata.{SectionKey}.cameraTour.stopSeconds must be > 0.");
            }

            JsonArray stops = tourObj["stops"] as JsonArray
                ?? throw new InvalidOperationException($"Map '{mapId}' metadata.{SectionKey}.cameraTour.stops must be an array.");
            if (stops.Count == 0)
            {
                throw new InvalidOperationException($"Map '{mapId}' metadata.{SectionKey}.cameraTour.stops must not be empty.");
            }

            var poses = new CameraPoseRequest[stops.Count + 1];
            poses[0] = new CameraPoseRequest
            {
                TargetCm = new Vector2(
                    overview.TargetXCm ?? throw new InvalidOperationException($"Map '{mapId}' DefaultCamera.TargetXCm is required for cameraTour."),
                    overview.TargetYCm ?? throw new InvalidOperationException($"Map '{mapId}' DefaultCamera.TargetYCm is required for cameraTour.")),
                Yaw = overview.Yaw,
                Pitch = overview.Pitch,
                DistanceCm = overview.DistanceCm,
                FovYDeg = overview.FovYDeg,
            };

            for (int i = 0; i < stops.Count; i++)
            {
                JsonObject stop = stops[i] as JsonObject
                    ?? throw new InvalidOperationException($"Map '{mapId}' metadata.{SectionKey}.cameraTour.stops[{i}] must be an object.");
                string context = $"cameraTour.stops[{i}]";
                poses[i + 1] = new CameraPoseRequest
                {
                    TargetCm = new Vector2(
                        ReadRequiredFloat(stop, mapId, "targetXCm", context),
                        ReadRequiredFloat(stop, mapId, "targetYCm", context)),
                    Yaw = overview.Yaw,
                    Pitch = ReadRequiredFloat(stop, mapId, "pitch", context),
                    DistanceCm = ReadRequiredFloat(stop, mapId, "distanceCm", context),
                    FovYDeg = overview.FovYDeg,
                };
            }

            tour = new PresenterTreeCameraTour(stopSeconds, poses);
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

        private static float ReadRequiredFloat(JsonObject section, string mapId, string key, string? context = null)
        {
            string path = context == null ? $"{SectionKey}.{key}" : $"{SectionKey}.{context}.{key}";
            if (!section.TryGetPropertyValue(key, out JsonNode? valueNode) || valueNode == null)
            {
                throw new InvalidOperationException($"Map '{mapId}' must declare metadata.{path}.");
            }

            float value = valueNode.GetValue<float>();
            if (!float.IsFinite(value))
            {
                throw new InvalidOperationException($"Map '{mapId}' metadata.{path} must be finite.");
            }

            return value;
        }
    }
}
