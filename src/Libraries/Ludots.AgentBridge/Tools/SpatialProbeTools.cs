using System.Numerics;
using System.Text.Json.Nodes;
using Arch.Core;
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
    /// gate, projected-bounds tiebreak).
    /// </summary>
    public sealed class EntitiesPickTool : IAgentTool
    {
        public string Name => "ludots.entities.pick";

        public string Description =>
            "Pick the entity under a screen point — the same resolution the game uses for click selection " +
            "(CommandSourceSelectableTag + knowledge-gated inspectability + projected-bounds tiebreak). " +
            "Params: {x: number, y: number, radiusPixels?=24}. Only selectable entities are candidates; " +
            "use ludots.entities.query for the unfiltered feed.";

        public JsonObject? InputSchema => new JsonObject
        {
            ["type"] = "object",
            ["properties"] = new JsonObject
            {
                ["x"] = new JsonObject { ["type"] = "number" },
                ["y"] = new JsonObject { ["type"] = "number" },
                ["radiusPixels"] = new JsonObject { ["type"] = "number", ["default"] = 24 },
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
            var result = new JsonObject
            {
                ["hit"] = true,
                ["entityId"] = hit.Id,
                ["name"] = engine.World.Has<Name>(hit) ? engine.World.Get<Name>(hit).Value : null,
            };

            if (engine.World.TryGet(hit, out WorldPositionCm pos))
            {
                result["worldCm"] = new JsonObject { ["x"] = pos.Value.X.ToFloat(), ["y"] = pos.Value.Y.ToFloat() };
            }

            if (SpatialBoundsUtility.TryProjectScreenBounds(engine.World, hit, projector, out ScreenRect rect))
            {
                result["screenRect"] = new JsonObject
                {
                    ["x"] = MathF.Round(rect.MinX, 1),
                    ["y"] = MathF.Round(rect.MinY, 1),
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
