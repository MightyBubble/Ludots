using System.Text.Json.Nodes;
using Ludots.Core.Navigation.NavMesh;
using Ludots.Core.Scripting;

namespace Ludots.AgentBridge.Tools
{
    /// <summary>
    /// NavMesh probes over the production NavQueryService (tile store + Detour
    /// query engine) — the same service movement orders path through.
    /// </summary>
    public sealed class NavProjectTool : IAgentTool
    {
        public string Name => "ludots.nav.project";

        public string Description =>
            "Project a world position onto the navmesh (nearest walkable triangle) via the production NavQueryService. " +
            "Params: {worldXCm: int, worldYCm: int, layer?=0, profile?=0}. " +
            "hit:false means no loaded nav tile covers the point or no walkable triangle nearby.";

        public JsonObject? InputSchema => new JsonObject
        {
            ["type"] = "object",
            ["properties"] = new JsonObject
            {
                ["worldXCm"] = new JsonObject { ["type"] = "integer" },
                ["worldYCm"] = new JsonObject { ["type"] = "integer" },
                ["layer"] = new JsonObject { ["type"] = "integer", ["default"] = 0 },
                ["profile"] = new JsonObject { ["type"] = "integer", ["default"] = 0 },
            },
            ["required"] = new JsonArray("worldXCm", "worldYCm"),
        };

        public JsonNode? Execute(JsonObject? args, AgentToolContext context)
        {
            int x = AgentToolContext.RequireInt(args, "worldXCm");
            int y = AgentToolContext.RequireInt(args, "worldYCm");
            NavQueryService service = ResolveService(args, context);

            if (!service.TryProject(x, y, out NavLocation loc))
            {
                return new JsonObject
                {
                    ["hit"] = false,
                    ["worldCm"] = new JsonObject { ["x"] = x, ["y"] = y },
                    ["reason"] = "No loaded nav tile covers the point or no walkable triangle found.",
                };
            }

            return new JsonObject
            {
                ["hit"] = true,
                ["worldCm"] = new JsonObject { ["x"] = x, ["y"] = y },
                ["tileId"] = loc.TileId.ToString(),
                ["tileVersion"] = loc.TileVersion,
                ["triangleId"] = loc.TriangleId,
                ["localCm"] = new JsonObject { ["x"] = loc.LocalXcm, ["y"] = loc.LocalZcm },
            };
        }

        internal static NavQueryService ResolveService(JsonObject? args, AgentToolContext context)
        {
            int layer = AgentToolContext.OptionalInt(args, "layer", 0);
            int profile = AgentToolContext.OptionalInt(args, "profile", 0);
            var registry = context.RequireService(CoreServiceKeys.NavQueryServices);
            if (!registry.TryCreateQuery(layer, profile, null!, out NavQueryService service))
            {
                throw new AgentToolException(
                    AgentBridgeErrorCodes.ServiceUnavailable,
                    $"No nav tile store for layer={layer} profile={profile}. Check the map's navmesh bake output and NavMeshProfiles.");
            }

            return service;
        }
    }

    public sealed class NavFindPathTool : IAgentTool
    {
        public string Name => "ludots.nav.findPath";

        public string Description =>
            "Find a navmesh path between two world positions via the production NavQueryService (stable-read, portal A*). " +
            "Params: {startXCm, startYCm, goalXCm, goalYCm, layer?=0, profile?=0, maxPortals?=256}. " +
            "status: Ok | NotReady (tiles still baking/loading) | NotReachable | InvalidInput; waypoints in world cm; travelCostCm from the area cost table.";

        public JsonObject? InputSchema => new JsonObject
        {
            ["type"] = "object",
            ["properties"] = new JsonObject
            {
                ["startXCm"] = new JsonObject { ["type"] = "integer" },
                ["startYCm"] = new JsonObject { ["type"] = "integer" },
                ["goalXCm"] = new JsonObject { ["type"] = "integer" },
                ["goalYCm"] = new JsonObject { ["type"] = "integer" },
                ["layer"] = new JsonObject { ["type"] = "integer", ["default"] = 0 },
                ["profile"] = new JsonObject { ["type"] = "integer", ["default"] = 0 },
                ["maxPortals"] = new JsonObject { ["type"] = "integer", ["minimum"] = 1, ["maximum"] = 4096, ["default"] = 256 },
            },
            ["required"] = new JsonArray("startXCm", "startYCm", "goalXCm", "goalYCm"),
        };

        public JsonNode? Execute(JsonObject? args, AgentToolContext context)
        {
            int sx = AgentToolContext.RequireInt(args, "startXCm");
            int sy = AgentToolContext.RequireInt(args, "startYCm");
            int gx = AgentToolContext.RequireInt(args, "goalXCm");
            int gy = AgentToolContext.RequireInt(args, "goalYCm");
            int maxPortals = Math.Clamp(AgentToolContext.OptionalInt(args, "maxPortals", 256), 1, 4096);
            NavQueryService service = NavProjectTool.ResolveService(args, context);

            NavPathResult path = service.TryFindPath(sx, sy, gx, gy, maxPortals);

            var waypoints = new JsonArray();
            int count = Math.Min(path.PathXcm.Length, path.PathZcm.Length);
            for (int i = 0; i < count; i++)
            {
                waypoints.Add(new JsonObject { ["x"] = path.PathXcm[i], ["y"] = path.PathZcm[i] });
            }

            return new JsonObject
            {
                ["status"] = path.Status.ToString(),
                ["ok"] = path.Status == NavPathStatus.Ok,
                ["waypointCount"] = count,
                ["travelCostCm"] = path.TravelCost.ToFloat(),
                ["waypoints"] = waypoints,
            };
        }
    }
}
