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
using Ludots.Platform.Abstractions;

namespace CapabilityStandardStaticPresenter30kMod.Systems
{
    internal sealed class DynamicWorkerCrowdMovementSystem : BaseSystem<World, float>
    {
        private const string MetadataSectionKey = "capabilityStandardStaticPresenter30k";
        private const string MovementPaddingMetadataKey = "dynamicWorkerMovementPaddingCm";
        private const string MovementSpeedMetadataKey = "dynamicWorkerMovementSpeedCmPerSecond";
        private static readonly QueryDescription WorkerQuery = new QueryDescription()
            .WithAll<DynamicWorkerCrowdTag, WorldPositionCm, PreviousWorldPositionCm, FacingDirection>();

        private readonly GameEngine _engine;
        private string _configuredMapId = string.Empty;
        private float _leftCm;
        private float _rightCm;
        private float _topCm;
        private float _bottomCm;
        private float _speedCmPerSecond;
        private float _elapsedSeconds;

        public DynamicWorkerCrowdMovementSystem(GameEngine engine)
            : base(engine?.World ?? throw new ArgumentNullException(nameof(engine)))
        {
            _engine = engine;
        }

        public override void Update(in float dt)
        {
            string? mapId = _engine.CurrentMapSession?.MapId.Value;
            if (!ShouldRunForFocusedMap())
            {
                return;
            }

            EnsureConfiguredForFocusedMap();
            _elapsedSeconds += dt;
            float elapsed = _elapsedSeconds;
            float speed = _speedCmPerSecond;

            foreach (ref var chunk in World.Query(in WorkerQuery))
            {
                Span<WorldPositionCm> positions = chunk.GetSpan<WorldPositionCm>();
                Span<FacingDirection> facings = chunk.GetSpan<FacingDirection>();

                foreach (int index in chunk)
                {
                    ref WorldPositionCm position = ref positions[index];

                    float x = position.Value.X.ToFloat();
                    float y = position.Value.Y.ToFloat();
                    float phase = ((x * 0.0017f) + (y * 0.0023f)) % (MathF.PI * 2f);
                    float angle = elapsed * 0.85f + phase;
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
                ?? throw new InvalidOperationException("Dynamic worker movement requires a focused map session.");
            if (string.Equals(_configuredMapId, mapId, StringComparison.Ordinal))
            {
                return;
            }

            IVisualHeightmap? heightmap = _engine.GetService(CoreServiceKeys.VisualHeightmap);
            if (heightmap is not IVisualHeightmapRenderSource renderSource)
            {
                throw new InvalidOperationException(
                    $"Map '{mapId}' must provide a VisualHeightmap render source before dynamic worker movement starts.");
            }

            float paddingCm = ReadRequiredMapMetadataFloat(_engine, MovementPaddingMetadataKey);
            float speedCmPerSecond = ReadRequiredMapMetadataFloat(_engine, MovementSpeedMetadataKey);
            if (paddingCm < 0f || speedCmPerSecond <= 0f)
            {
                throw new InvalidOperationException(
                    $"Map '{mapId}' has invalid dynamic worker movement metadata. Padding must be >= 0 and speed must be > 0.");
            }

            float left = renderSource.Bounds.Left + paddingCm;
            float right = renderSource.Bounds.Right - paddingCm;
            float top = renderSource.Bounds.Top + paddingCm;
            float bottom = renderSource.Bounds.Bottom - paddingCm;
            if (left >= right || top >= bottom)
            {
                throw new InvalidOperationException(
                    $"Map '{mapId}' dynamic worker movement padding leaves no valid VisualHeightmap walking area.");
            }

            _leftCm = left;
            _rightCm = right;
            _topCm = top;
            _bottomCm = bottom;
            _speedCmPerSecond = speedCmPerSecond;
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
                    $"Map '{mapId}' must declare metadata.{MetadataSectionKey}.{key} for dynamic worker movement.");
            }

            try
            {
                float value = valueNode.GetValue<float>();
                if (!float.IsFinite(value))
                {
                    throw new InvalidOperationException(
                        $"metadata.{MetadataSectionKey}.{key} must be finite for dynamic worker movement.");
                }

                return value;
            }
            catch (FormatException ex)
            {
                throw new InvalidOperationException(
                    $"metadata.{MetadataSectionKey}.{key} must be a number for dynamic worker movement.",
                    ex);
            }
            catch (InvalidOperationException ex) when (!ex.Message.Contains("metadata.", StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"metadata.{MetadataSectionKey}.{key} must be a number for dynamic worker movement.",
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
                speedNode != null;
        }
    }
}
