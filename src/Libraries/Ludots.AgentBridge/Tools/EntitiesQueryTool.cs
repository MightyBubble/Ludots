using System.Numerics;
using System.Text.Json.Nodes;
using Arch.Core;
using Ludots.Core.Components;
using Ludots.Core.Scripting;
using Ludots.Core.Spatial;
using Ludots.Platform.Abstractions;

namespace Ludots.AgentBridge.Tools
{
    /// <summary>
    /// Camera-visible entity feed for non-multimodal agents: world position,
    /// screen rect, and screen coverage fraction, with paging and drop
    /// diagnostics (DataPlane style).
    /// </summary>
    public sealed class EntitiesQueryTool : IAgentTool
    {
        public string Name => "ludots.entities.query";

        public string Description =>
            "Query entities with world positions, projected into the current camera. " +
            "Params: {offset?=0, limit?=100, nameFilter?=string, onScreenOnly?=false}. " +
            "Each row: entityId, name, worldCm, screenRect {x,y,w,h}, screenCoverage (0..1 of the viewport), onScreen. " +
            "Response carries totalMatched/returned/dropped diagnostics.";

        public JsonObject? InputSchema => new JsonObject
        {
            ["type"] = "object",
            ["properties"] = new JsonObject
            {
                ["offset"] = new JsonObject { ["type"] = "integer", ["minimum"] = 0, ["default"] = 0 },
                ["limit"] = new JsonObject { ["type"] = "integer", ["minimum"] = 1, ["maximum"] = 1000, ["default"] = 100 },
                ["nameFilter"] = new JsonObject { ["type"] = "string", ["description"] = "case-insensitive substring match on entity Name" },
                ["onScreenOnly"] = new JsonObject { ["type"] = "boolean", ["default"] = false },
            },
        };

        public JsonNode? Execute(JsonObject? args, AgentToolContext context)
        {
            int offset = AgentToolContext.OptionalInt(args, "offset", 0);
            int limit = Math.Clamp(AgentToolContext.OptionalInt(args, "limit", 100), 1, 1000);
            string? nameFilter = AgentToolContext.OptionalString(args, "nameFilter");
            bool onScreenOnly = AgentToolContext.OptionalBool(args, "onScreenOnly", false);

            var projector = context.RequireService(CoreServiceKeys.ScreenProjector);
            var view = context.RequireService(CoreServiceKeys.ViewController);
            Vector2 resolution = view.Resolution;
            float viewportArea = Math.Max(1f, resolution.X * resolution.Y);

            var world = context.Engine.World;
            var rows = new List<(int Id, string? Name, Vector2 WorldCm, ScreenRect Rect)>();

            var query = new QueryDescription().WithAll<WorldPositionCm>();
            world.Query(in query, (Entity e, ref WorldPositionCm pos) =>
            {
                string? name = world.Has<Name>(e) ? world.Get<Name>(e).Value : null;
                if (nameFilter != null && (name == null || !name.Contains(nameFilter, StringComparison.OrdinalIgnoreCase)))
                {
                    return;
                }

                if (!SpatialBoundsUtility.TryProjectScreenBounds(world, e, projector, out ScreenRect rect))
                {
                    if (onScreenOnly) return;
                    rows.Add((e.Id, name, new Vector2(pos.Value.X.ToFloat(), pos.Value.Y.ToFloat()), default));
                    return;
                }

                var screenBounds = new ScreenRect(0, 0, resolution.X, resolution.Y);
                bool onScreen = rect.Intersects(in screenBounds);
                if (onScreenOnly && !onScreen) return;

                rows.Add((e.Id, name, new Vector2(pos.Value.X.ToFloat(), pos.Value.Y.ToFloat()), rect));
            });

            rows.Sort((a, b) => a.Id.CompareTo(b.Id));

            int totalMatched = rows.Count;
            var entities = new JsonArray();
            int returned = 0;
            for (int i = offset; i < rows.Count && returned < limit; i++)
            {
                var row = rows[i];
                bool hasRect = row.Rect.MaxX != 0f || row.Rect.MaxY != 0f || row.Rect.MinX != 0f || row.Rect.MinY != 0f;
                float width = Math.Max(0f, row.Rect.MaxX - row.Rect.MinX);
                float height = Math.Max(0f, row.Rect.MaxY - row.Rect.MinY);

                var item = new JsonObject
                {
                    ["entityId"] = row.Id,
                    ["name"] = row.Name,
                    ["worldCm"] = new JsonObject { ["x"] = MathF.Round(row.WorldCm.X, 1), ["y"] = MathF.Round(row.WorldCm.Y, 1) },
                };

                if (hasRect)
                {
                    item["screenRect"] = new JsonObject
                    {
                        ["x"] = MathF.Round(row.Rect.MinX, 1),
                        ["y"] = MathF.Round(row.Rect.MinY, 1),
                        ["w"] = MathF.Round(width, 1),
                        ["h"] = MathF.Round(height, 1),
                    };
                    item["screenCoverage"] = MathF.Round(width * height / viewportArea, 5);
                    item["onScreen"] = true;
                }
                else
                {
                    item["onScreen"] = false;
                }

                entities.Add(item);
                returned++;
            }

            return new JsonObject
            {
                ["tick"] = context.Engine.GameSession.CurrentTick,
                ["viewport"] = new JsonObject { ["width"] = resolution.X, ["height"] = resolution.Y },
                ["totalMatched"] = totalMatched,
                ["offset"] = offset,
                ["returned"] = returned,
                ["dropped"] = totalMatched - offset - returned > 0 ? totalMatched - offset - returned : 0,
                ["entities"] = entities,
            };
        }
    }
}
