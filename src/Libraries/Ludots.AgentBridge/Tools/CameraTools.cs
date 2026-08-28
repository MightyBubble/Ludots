using System.Numerics;
using System.Text.Json.Nodes;
using Arch.Core;
using Ludots.Core.Components;
using Ludots.Core.Gameplay.Camera;
using Ludots.Core.Presentation.Camera;

namespace Ludots.AgentBridge.Tools
{
    /// <summary>
    /// Entity follow target for the debug bridge: resolves a live entity's
    /// world position (and facing) every camera tick. Unlike the config-driven
    /// follow targets it binds the entity directly — no global-key indirection.
    /// </summary>
    internal sealed class AgentEntityFollowTarget : ICameraFollowTarget
    {
        private readonly World _world;
        private readonly Entity _entity;

        public AgentEntityFollowTarget(World world, Entity entity)
        {
            _world = world;
            _entity = entity;
        }

        public bool TryGetTransform(out CameraTargetTransformSnapshot transform)
        {
            transform = default;
            if (!_world.IsAlive(_entity) || !_world.Has<WorldPositionCm>(_entity))
            {
                return false;
            }

            Vector2 position = _world.Get<WorldPositionCm>(_entity).Value.ToVector2();
            bool hasFacing = _world.TryGet(_entity, out FacingDirection facing);
            transform = new CameraTargetTransformSnapshot(
                position,
                hasFacingYawRad: hasFacing,
                facingYawRad: hasFacing ? facing.AngleRad : 0f);
            return true;
        }
    }

    public sealed class CameraControlTool : IAgentTool
    {
        public string Name => "ludots.camera.control";

        public string Description =>
            "Inspect or drive one seat's present camera. Params: {action: 'get'|'set'|'follow'|'unfollow', " +
            "seatId?: string, entityId?: int (follow), targetXCm/targetYCm?: number, yaw?/pitch?/distanceCm?: number (set)}. " +
            "seatId addresses that seat's PresentBinding LogicView camera (split-screen); omitted seatId keeps the " +
            "single-viewport default (sole binding, or the first binding in seat order under split-screen). " +
            "'set' applies a partial pose through CameraManager.ApplyPose (persists into the active virtual camera); " +
            "'follow' attaches the entity as follow target of the active virtual camera; 'unfollow' clears it.";

        public JsonObject? InputSchema => new JsonObject
        {
            ["type"] = "object",
            ["properties"] = new JsonObject
            {
                ["action"] = new JsonObject { ["type"] = "string", ["enum"] = new JsonArray("get", "set", "follow", "unfollow") },
                ["seatId"] = new JsonObject { ["type"] = "string", ["description"] = "seat whose PresentBinding camera is addressed; default = sole/first binding" },
                ["entityId"] = new JsonObject { ["type"] = "integer", ["description"] = "required for action=follow" },
                ["targetXCm"] = new JsonObject { ["type"] = "number", ["description"] = "set: both targetXCm and targetYCm required" },
                ["targetYCm"] = new JsonObject { ["type"] = "number" },
                ["yaw"] = new JsonObject { ["type"] = "number" },
                ["pitch"] = new JsonObject { ["type"] = "number" },
                ["distanceCm"] = new JsonObject { ["type"] = "number" },
            },
            ["required"] = new JsonArray("action"),
        };

        public JsonNode? Execute(JsonObject? args, AgentToolContext context)
        {
            string action = AgentToolContext.RequireString(args, "action");
            string? seatId = AgentToolContext.OptionalString(args, "seatId");
            CameraManager camera;
            string? resolvedSeatId;
            if (seatId != null)
            {
                (_, _, camera) = SeatRouting.RequireSeatPresentCamera(context, seatId);
                resolvedSeatId = seatId.Trim();
            }
            else
            {
                (camera, resolvedSeatId) = SeatRouting.ResolveDefaultCamera(context);
            }

            switch (action)
            {
                case "get":
                    return Status(camera, resolvedSeatId);

                case "set":
                {
                    var request = new CameraPoseRequest
                    {
                        VirtualCameraId = camera.VirtualCameraBrain?.ActiveCameraId ?? string.Empty,
                        Yaw = OptionalFloat(args, "yaw"),
                        Pitch = OptionalFloat(args, "pitch"),
                        DistanceCm = OptionalFloat(args, "distanceCm"),
                    };

                    float? x = OptionalFloat(args, "targetXCm");
                    float? y = OptionalFloat(args, "targetYCm");
                    if (x.HasValue != y.HasValue)
                    {
                        throw new AgentToolException(
                            AgentBridgeErrorCodes.InvalidParams,
                            "targetXCm and targetYCm must be provided together.");
                    }

                    if (x.HasValue)
                    {
                        request.TargetCm = new Vector2(x.Value, y!.Value);
                    }

                    if (request.TargetCm == null && !request.Yaw.HasValue && !request.Pitch.HasValue && !request.DistanceCm.HasValue)
                    {
                        throw new AgentToolException(
                            AgentBridgeErrorCodes.InvalidParams,
                            "action=set requires at least one of targetXCm/targetYCm, yaw, pitch, distanceCm.");
                    }

                    camera.ApplyPose(request);
                    return Status(camera, resolvedSeatId);
                }

                case "follow":
                {
                    int entityId = AgentToolContext.RequireInt(args, "entityId");
                    Entity entity = context.ResolveEntity(entityId);
                    VirtualCameraBrain brain = RequireBrain(camera);
                    if (!camera.SetFollowTarget(brain.ActiveCameraId, new AgentEntityFollowTarget(context.Engine.World, entity)))
                    {
                        throw new AgentToolException(
                            AgentBridgeErrorCodes.ToolFailed,
                            $"Active virtual camera '{brain.ActiveCameraId}' rejected the follow target.");
                    }

                    var result = Status(camera, resolvedSeatId);
                    result["followingEntityId"] = entityId;
                    return result;
                }

                case "unfollow":
                {
                    VirtualCameraBrain brain = RequireBrain(camera);
                    camera.SetFollowTarget(brain.ActiveCameraId, null);
                    return Status(camera, resolvedSeatId);
                }

                default:
                    throw new AgentToolException(
                        AgentBridgeErrorCodes.InvalidParams,
                        $"Unknown action '{action}'. Expected get | set | follow | unfollow.");
            }
        }

        private static VirtualCameraBrain RequireBrain(CameraManager camera)
        {
            VirtualCameraBrain? brain = camera.VirtualCameraBrain;
            if (brain == null || !brain.HasActiveCamera)
            {
                throw new AgentToolException(
                    AgentBridgeErrorCodes.CapabilityUnavailable,
                    "No active virtual camera. follow/unfollow require a virtual camera; use action=set for a direct pose instead.");
            }

            return brain;
        }

        private static JsonObject Status(CameraManager camera, string? seatId)
        {
            CameraState state = camera.State;
            var result = new JsonObject
            {
                ["seatId"] = seatId,
                ["targetCm"] = new JsonObject { ["x"] = state.TargetCm.X, ["y"] = state.TargetCm.Y },
                ["targetHeightCm"] = state.TargetHeightCm,
                ["yaw"] = state.Yaw,
                ["pitch"] = state.Pitch,
                ["distanceCm"] = state.DistanceCm,
            };

            VirtualCameraBrain? brain = camera.VirtualCameraBrain;
            result["activeCameraId"] = brain != null && brain.HasActiveCamera ? brain.ActiveCameraId : null;
            if (brain != null && brain.HasActiveCamera)
            {
                result["isBlending"] = brain.IsBlending;
                Vector2? follow = brain.ActiveFollowTargetPositionCm;
                result["followTargetCm"] = follow.HasValue
                    ? new JsonObject { ["x"] = follow.Value.X, ["y"] = follow.Value.Y }
                    : null;
            }

            return result;
        }

        private static float? OptionalFloat(JsonObject? args, string name)
        {
            if (args?[name] is JsonValue node && node.TryGetValue(out double d)) return (float)d;
            return null;
        }
    }
}
