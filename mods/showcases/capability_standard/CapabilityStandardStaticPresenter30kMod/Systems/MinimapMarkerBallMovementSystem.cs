using System;
using System.Numerics;
using System.Text.Json.Nodes;
using Arch.Core;
using Arch.System;
using Ludots.Core.Components;
using Ludots.Core.Engine;
using Ludots.Core.Mathematics;
using Ludots.Core.Mathematics.FixedPoint;
using Ludots.Core.Presentation.Terrain;
using Ludots.Core.Scripting;
using CapabilityStandardStaticPresenter30kMod.Runtime;

namespace CapabilityStandardStaticPresenter30kMod.Systems
{
    internal sealed class MinimapMarkerBallMovementSystem : BaseSystem<World, float>
    {
        private const string MetadataSectionKey = "capabilityStandardStaticPresenter30k";
        private const string MovementPaddingMetadataKey = "minimapMarkerMovementPaddingCm";
        private const string MovementSpeedMetadataKey = "minimapMarkerMovementSpeedCmPerSecond";
        private const string MovementTurnPeriodMetadataKey = "minimapMarkerMovementTurnPeriodSeconds";

        private static readonly QueryDescription MarkerQuery = new QueryDescription()
            .WithAll<MinimapMarkerBallMovementTag, WorldPositionCm, PreviousWorldPositionCm, FacingDirection>();

        private readonly GameEngine _engine;
        private string _configuredMapId = string.Empty;
        private float _leftCm;
        private float _rightCm;
        private float _topCm;
        private float _bottomCm;
        private float _speedCmPerSecond;
        private float _turnPeriodSeconds = 11f;
        private float _elapsedSeconds;

        public MinimapMarkerBallMovementSystem(GameEngine engine)
            : base(engine?.World ?? throw new ArgumentNullException(nameof(engine)))
        {
            _engine = engine;
        }

        public override void Update(in float dt)
        {
            if (!ShouldRunForFocusedMap())
            {
                return;
            }

            EnsureConfiguredForFocusedMap();
            _elapsedSeconds += dt;
            float elapsed = _elapsedSeconds;
            float speed = _speedCmPerSecond;
            float turnScale = MathF.PI * 2f / MathF.Max(0.001f, _turnPeriodSeconds);

            foreach (ref var chunk in World.Query(in MarkerQuery))
            {
                Span<WorldPositionCm> positions = chunk.GetSpan<WorldPositionCm>();
                Span<FacingDirection> facings = chunk.GetSpan<FacingDirection>();

                foreach (int index in chunk)
                {
                    ref WorldPositionCm position = ref positions[index];

                    float x = position.Value.X.ToFloat();
                    float y = position.Value.Y.ToFloat();
                    float phase = ((x * 0.00031f) + (y * 0.00047f)) % (MathF.PI * 2f);
                    float wobble = MathF.Sin((elapsed * 0.37f) + phase) * 0.7f;
                    float angle = (elapsed * turnScale) + phase + wobble;
                    Vector2 direction = WorldPlane2D.DirectionFromFacingRad(angle);
                    float vx = direction.X * speed;
                    float vy = direction.Y * speed;
                    float nextX = x + (vx * dt);
                    float nextY = y + (vy * dt);

                    if (nextX < _leftCm)
                    {
                        nextX = _leftCm;
                        vx = MathF.Abs(vx);
                    }
                    else if (nextX > _rightCm)
                    {
                        nextX = _rightCm;
                        vx = -MathF.Abs(vx);
                    }

                    if (nextY < _topCm)
                    {
                        nextY = _topCm;
                        vy = MathF.Abs(vy);
                    }
                    else if (nextY > _bottomCm)
                    {
                        nextY = _bottomCm;
                        vy = -MathF.Abs(vy);
                    }

                    position.Value = Fix64Vec2.FromFloat(nextX, nextY);
                    facings[index].AngleRad = WorldPlane2D.FacingRadFromDirection(vx, vy);
                }
            }
        }

        private bool ShouldRunForFocusedMap()
        {
            return HasRequiredMovementMetadata();
        }

        private void EnsureConfiguredForFocusedMap()
        {
            string mapId = _engine.CurrentMapSession?.MapId.Value
                ?? throw new InvalidOperationException("Minimap marker ball movement requires a focused map session.");
            if (string.Equals(_configuredMapId, mapId, StringComparison.Ordinal))
            {
                return;
            }

            IVisualHeightmap? heightmap = _engine.GetService(CoreServiceKeys.VisualHeightmap);
            if (heightmap is not IVisualHeightmapRenderSource renderSource)
            {
                throw new InvalidOperationException(
                    $"Map '{mapId}' must provide a VisualHeightmap render source before minimap marker ball movement starts.");
            }

            float paddingCm = ReadRequiredMapMetadataFloat(_engine, MovementPaddingMetadataKey);
            float speedCmPerSecond = ReadRequiredMapMetadataFloat(_engine, MovementSpeedMetadataKey);
            float turnPeriodSeconds = ReadRequiredMapMetadataFloat(_engine, MovementTurnPeriodMetadataKey);
            if (paddingCm < 0f || speedCmPerSecond <= 0f || turnPeriodSeconds <= 0f)
            {
                throw new InvalidOperationException(
                    $"Map '{mapId}' has invalid minimap marker movement metadata. Padding must be >= 0, speed must be > 0, and turn period must be > 0.");
            }

            float left = renderSource.Bounds.Left + paddingCm;
            float right = renderSource.Bounds.Right - paddingCm;
            float top = renderSource.Bounds.Top + paddingCm;
            float bottom = renderSource.Bounds.Bottom - paddingCm;
            if (left >= right || top >= bottom)
            {
                throw new InvalidOperationException(
                    $"Map '{mapId}' minimap marker movement padding leaves no valid VisualHeightmap walking area.");
            }

            _leftCm = left;
            _rightCm = right;
            _topCm = top;
            _bottomCm = bottom;
            _speedCmPerSecond = speedCmPerSecond;
            _turnPeriodSeconds = turnPeriodSeconds;
            _configuredMapId = mapId;
        }

        private static float ReadRequiredMapMetadataFloat(GameEngine engine, string key)
        {
            if (engine.CurrentMapSession?.MapConfig?.Metadata == null ||
                !engine.CurrentMapSession.MapConfig.Metadata.TryGetValue(MetadataSectionKey, out JsonNode? sectionNode) ||
                sectionNode is not JsonObject sectionObject ||
                !sectionObject.TryGetPropertyValue(key, out JsonNode? valueNode) ||
                valueNode == null)
            {
                string mapId = engine.CurrentMapSession?.MapId.Value ?? "<none>";
                throw new InvalidOperationException(
                    $"Map '{mapId}' must declare metadata.{MetadataSectionKey}.{key} for minimap marker ball movement.");
            }

            try
            {
                float value = valueNode.GetValue<float>();
                if (!float.IsFinite(value))
                {
                    throw new InvalidOperationException(
                        $"metadata.{MetadataSectionKey}.{key} must be finite for minimap marker ball movement.");
                }

                return value;
            }
            catch (FormatException ex)
            {
                throw new InvalidOperationException(
                    $"metadata.{MetadataSectionKey}.{key} must be a number for minimap marker ball movement.",
                    ex);
            }
            catch (InvalidOperationException ex) when (!ex.Message.Contains("metadata.", StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"metadata.{MetadataSectionKey}.{key} must be a number for minimap marker ball movement.",
                    ex);
            }
        }

        private bool HasRequiredMovementMetadata()
        {
            return _engine.CurrentMapSession?.MapConfig?.Metadata != null &&
                _engine.CurrentMapSession.MapConfig.Metadata.TryGetValue(MetadataSectionKey, out JsonNode? sectionNode) &&
                sectionNode is JsonObject sectionObject &&
                sectionObject.TryGetPropertyValue(MovementPaddingMetadataKey, out JsonNode? paddingNode) &&
                paddingNode != null &&
                sectionObject.TryGetPropertyValue(MovementSpeedMetadataKey, out JsonNode? speedNode) &&
                speedNode != null &&
                sectionObject.TryGetPropertyValue(MovementTurnPeriodMetadataKey, out JsonNode? turnPeriodNode) &&
                turnPeriodNode != null;
        }
    }
}
