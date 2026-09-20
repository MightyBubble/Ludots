using System.Collections.Generic;
using System.Numerics;
using System.Text.Json.Nodes;
using Arch.Core;
using Ludots.Core.Client;
using Ludots.Core.Components;
using Ludots.Core.Input.CommandSources;
using Ludots.Core.Mathematics;
using Ludots.Core.Scripting;
using Ludots.Core.Spatial;
using Ludots.Platform.Abstractions;

namespace Ludots.AgentBridge.Tools
{
    /// <summary>
    /// Screen-point entity picking via the production command-source hit
    /// resolver (same algorithm as click selection: selectable tag, knowledge
    /// gate, projected-bounds tiebreak). Sole seat answers through the runtime
    /// ScreenProjector; seatId or a split-screen point routes through that
    /// seat's PresentBinding camera with binding-local metrics.
    /// </summary>
    public sealed class EntitiesPickTool : IAgentTool
    {
        public string Name => "ludots.entities.pick";

        public string Description =>
            "Pick the entity under a screen point — the same resolution the game uses for click selection " +
            "(CommandSourceSelectableTag + knowledge-gated inspectability + projected-bounds tiebreak). " +
            "Params: {x: number, y: number, radiusPixels?=24, seatId?=string}. x/y are host-window pixels. " +
            "seatId picks through that seat's PresentBinding viewport (knowledge owner = the seat's possessed rep); " +
            "without seatId a sole seat keeps the single-viewport path and a split-screen point auto-routes to the " +
            "binding whose rect contains it. Only selectable entities are candidates; " +
            "use ludots.entities.query for the unfiltered feed.";

        public JsonObject? InputSchema => new JsonObject
        {
            ["type"] = "object",
            ["properties"] = new JsonObject
            {
                ["x"] = new JsonObject { ["type"] = "number" },
                ["y"] = new JsonObject { ["type"] = "number" },
                ["radiusPixels"] = new JsonObject { ["type"] = "number", ["default"] = 24 },
                ["seatId"] = new JsonObject { ["type"] = "string", ["description"] = "pick through this seat's PresentBinding viewport; default = sole seat or rect-routed binding" },
            },
            ["required"] = new JsonArray("x", "y"),
        };

        public JsonNode? Execute(JsonObject? args, AgentToolContext context)
        {
            float x = RequireFloat(args, "x");
            float y = RequireFloat(args, "y");
            float radius = args?["radiusPixels"] is JsonValue r && r.TryGetValue(out double rd) ? (float)rd : 24f;
            if (radius <= 0f || radius > 500f)
            {
                throw new AgentToolException(AgentBridgeErrorCodes.InvalidParams, "radiusPixels must be in (0, 500].");
            }

            var engine = context.Engine;
            string? seatId = AgentToolContext.OptionalString(args, "seatId");
            if (seatId != null)
            {
                return PickSeatViewport(context, seatId, x, y, radius, routedByPoint: false);
            }

            ClientLocalSeatRegistry registry = Ludots.Core.Client.ClientLocalSeatAccess.RequireRegistry(engine);
            if (registry.TryGetSoleSeat(out _))
            {
                return PickSoleViewport(context, x, y, radius);
            }

            var bindings = new List<(string SeatId, Ludots.Core.Client.PresentBinding Binding)>(registry.Count);
            registry.CopyPresentBindings(bindings);
            if (bindings.Count == 0 ||
                !SeatRouting.TryRouteWindowPoint(context, new Vector2(x, y), bindings, out int routedIndex))
            {
                throw new AgentToolException(
                    AgentBridgeErrorCodes.ServiceUnavailable,
                    "Split-screen pick requires a PresentBinding per seat (and a ViewController for point routing); " +
                    $"this session has {bindings.Count} present bindings. Pass seatId or fix the seat table.");
            }

            return PickSeatViewport(context, bindings[routedIndex].SeatId, x, y, radius, routedByPoint: true);
        }

        private static JsonNode? PickSoleViewport(AgentToolContext context, float x, float y, float radius)
        {
            var engine = context.Engine;
            if (!Ludots.Core.Client.ClientLocalSeatAccess.TryGetSolePossessedRep(engine, out Entity owner) ||
                !engine.World.IsAlive(owner))
            {
                throw new AgentToolException(
                    AgentBridgeErrorCodes.ServiceUnavailable,
                    "No sole possessed local seat entity; picking requires the local command-source owner for knowledge gating.");
            }

            Entity hit = CommandSourcePointerHitResolver.FindNearestInspectableEntity(
                engine.World, engine.GlobalContext, owner, new Vector2(x, y), radius);
            if (hit == Entity.Null || !engine.World.IsAlive(hit))
            {
                return new JsonObject { ["hit"] = false, ["x"] = x, ["y"] = y, ["radiusPixels"] = radius };
            }

            var projector = context.RequireService(CoreServiceKeys.ScreenProjector);
            return BuildHitRow(engine.World, hit, projector, offsetX: 0f, offsetY: 0f);
        }

        private JsonNode? PickSeatViewport(AgentToolContext context, string seatId, float x, float y, float radius, bool routedByPoint)
        {
            var (seat, binding, camera) = SeatRouting.RequireSeatPresentCamera(context, seatId);
            var engine = context.Engine;
            if (!seat.HasPossession || !engine.World.IsAlive(seat.PossessedRep))
            {
                throw new AgentToolException(
                    AgentBridgeErrorCodes.ServiceUnavailable,
                    $"Seat '{seat.SeatId}' has no live possessed rep; per-seat picking uses it as the knowledge-gating owner.");
            }

            var view = context.RequireService(CoreServiceKeys.ViewController);
            Vector4 rect = binding.NormalizedScreenRect;
            float offsetX = rect.X * view.Resolution.X;
            float offsetY = rect.Y * view.Resolution.Y;
            var localPoint = new Vector2(x - offsetX, y - offsetY);

            var projector = new Ludots.Core.Presentation.Camera.CoreScreenProjector(
                camera,
                new Ludots.Core.Client.PresentBindingSurface(binding, view.Fov));
            Entity hit = CommandSourcePointerHitResolver.FindNearestInspectableEntity(
                engine.World, engine.GlobalContext, seat.PossessedRep, localPoint, radius, projector);

            if (hit == Entity.Null || !engine.World.IsAlive(hit))
            {
                return new JsonObject
                {
                    ["hit"] = false,
                    ["x"] = x,
                    ["y"] = y,
                    ["radiusPixels"] = radius,
                    ["seatId"] = seat.SeatId,
                    ["routedByPoint"] = routedByPoint,
                };
            }

            JsonNode row = BuildHitRow(engine.World, hit, projector, offsetX, offsetY);
            var result = (JsonObject)row;
            result["seatId"] = seat.SeatId;
            result["routedByPoint"] = routedByPoint;
            return result;
        }

        private static JsonObject BuildHitRow(
            World world,
            Entity hit,
            IScreenProjector projector,
            float offsetX,
            float offsetY)
        {
            var result = new JsonObject
            {
                ["hit"] = true,
                ["entityId"] = hit.Id,
                ["name"] = world.Has<Name>(hit) ? world.Get<Name>(hit).Value : null,
            };

            if (world.TryGet(hit, out WorldPositionCm pos))
            {
                result["worldCm"] = new JsonObject { ["x"] = pos.Value.X.ToFloat(), ["y"] = pos.Value.Y.ToFloat() };
            }

            if (SpatialBoundsUtility.TryProjectScreenBounds(world, hit, projector, out ScreenRect rect))
            {
                result["screenRect"] = new JsonObject
                {
                    ["x"] = MathF.Round(rect.MinX + offsetX, 1),
                    ["y"] = MathF.Round(rect.MinY + offsetY, 1),
                    ["w"] = MathF.Round(Math.Max(0f, rect.MaxX - rect.MinX), 1),
                    ["h"] = MathF.Round(Math.Max(0f, rect.MaxY - rect.MinY), 1),
                };
            }

            return result;
        }

        private static float RequireFloat(JsonObject? args, string name)
        {
            if (args?[name] is JsonValue node && node.TryGetValue(out double d)) return (float)d;
            throw new AgentToolException(AgentBridgeErrorCodes.InvalidParams, $"Parameter '{name}' (number) is required.");
        }
    }

    /// <summary>
    /// Spatial probes over the production ISpatialQueryService (grid/chunked
    /// partition backends) — the same query layer abilities and auto-targeting
    /// use. Never scans the Arch world directly.
    /// </summary>
    public sealed class SpatialQueryTool : IAgentTool
    {
        private const int BufferCapacity = 512;

        public string Name => "ludots.spatial.query";

        public string Description =>
            "Probe the world with the production spatial query service (same layer as ability/auto-target queries). " +
            "Params: {shape: 'radius'|'aabb'|'cone'|'rect'|'line', centerXCm, centerYCm, " +
            "radiusCm? (radius), halfWidthCm?/halfHeightCm? (aabb/rect), rotationDeg? (rect), " +
            "directionDeg?/halfAngleDeg?/rangeCm? (cone), directionDeg?/lengthCm?/halfWidthCm? (line), limit?=100}. " +
            "Rows: entityId/name/worldCm/distanceCmToCenter; dropped>0 means the backend buffer overflowed.";

        public JsonObject? InputSchema => new JsonObject
        {
            ["type"] = "object",
            ["properties"] = new JsonObject
            {
                ["shape"] = new JsonObject { ["type"] = "string", ["enum"] = new JsonArray("radius", "aabb", "cone", "rect", "line") },
                ["centerXCm"] = new JsonObject { ["type"] = "integer" },
                ["centerYCm"] = new JsonObject { ["type"] = "integer" },
                ["radiusCm"] = new JsonObject { ["type"] = "integer" },
                ["halfWidthCm"] = new JsonObject { ["type"] = "integer" },
                ["halfHeightCm"] = new JsonObject { ["type"] = "integer" },
                ["rotationDeg"] = new JsonObject { ["type"] = "integer" },
                ["directionDeg"] = new JsonObject { ["type"] = "integer", ["description"] = "heading, 0=+X 90=+Y" },
                ["halfAngleDeg"] = new JsonObject { ["type"] = "integer" },
                ["rangeCm"] = new JsonObject { ["type"] = "integer" },
                ["lengthCm"] = new JsonObject { ["type"] = "integer" },
                ["limit"] = new JsonObject { ["type"] = "integer", ["minimum"] = 1, ["maximum"] = 512, ["default"] = 100 },
            },
            ["required"] = new JsonArray("shape", "centerXCm", "centerYCm"),
        };

        public JsonNode? Execute(JsonObject? args, AgentToolContext context)
        {
            string shape = AgentToolContext.RequireString(args, "shape");
            int cx = AgentToolContext.RequireInt(args, "centerXCm");
            int cy = AgentToolContext.RequireInt(args, "centerYCm");
            int limit = Math.Clamp(AgentToolContext.OptionalInt(args, "limit", 100), 1, BufferCapacity);

            var spatial = context.RequireService(CoreServiceKeys.SpatialQueryService);
            var center = new WorldCmInt2(cx, cy);
            var buffer = new Entity[BufferCapacity];

            SpatialQueryResult result = shape switch
            {
                "radius" => spatial.QueryRadius(center, RequirePositiveInt(args, "radiusCm"), buffer),
                "aabb" => spatial.QueryAabb(
                    new WorldAabbCm(
                        cx - RequirePositiveInt(args, "halfWidthCm"),
                        cy - RequirePositiveInt(args, "halfHeightCm"),
                        RequirePositiveInt(args, "halfWidthCm") * 2,
                        RequirePositiveInt(args, "halfHeightCm") * 2),
                    buffer),
                "cone" => spatial.QueryCone(
                    center,
                    AgentToolContext.OptionalInt(args, "directionDeg", 0),
                    RequirePositiveInt(args, "halfAngleDeg"),
                    RequirePositiveInt(args, "rangeCm"),
                    buffer),
                "rect" => spatial.QueryRectangle(
                    center,
                    RequirePositiveInt(args, "halfWidthCm"),
                    RequirePositiveInt(args, "halfHeightCm"),
                    AgentToolContext.OptionalInt(args, "rotationDeg", 0),
                    buffer),
                "line" => spatial.QueryLine(
                    center,
                    AgentToolContext.OptionalInt(args, "directionDeg", 0),
                    RequirePositiveInt(args, "lengthCm"),
                    RequirePositiveInt(args, "halfWidthCm"),
                    buffer),
                _ => throw new AgentToolException(
                    AgentBridgeErrorCodes.InvalidParams,
                    $"Unknown shape '{shape}'. Expected radius | aabb | cone | rect | line."),
            };

            var world = context.Engine.World;
            var entities = new JsonArray();
            int returned = 0;
            for (int i = 0; i < result.Count && returned < limit; i++)
            {
                Entity e = buffer[i];
                if (!world.IsAlive(e)) continue;

                var item = new JsonObject
                {
                    ["entityId"] = e.Id,
                    ["name"] = world.Has<Name>(e) ? world.Get<Name>(e).Value : null,
                };

                if (world.TryGet(e, out WorldPositionCm pos))
                {
                    float px = pos.Value.X.ToFloat();
                    float py = pos.Value.Y.ToFloat();
                    item["worldCm"] = new JsonObject { ["x"] = MathF.Round(px, 1), ["y"] = MathF.Round(py, 1) };
                    item["distanceCmToCenter"] = MathF.Round(Vector2.Distance(new Vector2(px, py), new Vector2(cx, cy)), 1);
                }

                entities.Add(item);
                returned++;
            }

            return new JsonObject
            {
                ["shape"] = shape,
                ["centerCm"] = new JsonObject { ["x"] = cx, ["y"] = cy },
                ["matched"] = result.Count,
                ["returned"] = returned,
                ["dropped"] = result.Dropped + (result.Count - returned),
                ["entities"] = entities,
            };
        }

        private static int RequirePositiveInt(JsonObject? args, string name)
        {
            int value = AgentToolContext.RequireInt(args, name);
            if (value <= 0)
            {
                throw new AgentToolException(AgentBridgeErrorCodes.InvalidParams, $"Parameter '{name}' must be positive.");
            }

            return value;
        }
    }
}
