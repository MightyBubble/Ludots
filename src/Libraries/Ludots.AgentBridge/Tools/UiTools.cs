using System.Text.Json.Nodes;
using Ludots.Core.Scripting;
using Ludots.UI;
using Ludots.UI.Runtime;
using Ludots.UI.Runtime.Events;

namespace Ludots.AgentBridge.Tools
{
    internal static class UiNodeJson
    {
        public static JsonObject Serialize(UiNode node, float viewportArea)
        {
            UiRect rect = node.LayoutRect;
            var result = new JsonObject
            {
                ["nodeId"] = node.Id.Value,
                ["kind"] = node.Kind.ToString(),
                ["tag"] = node.TagName,
                ["elementId"] = node.ElementId,
                ["text"] = Truncate(node.TextContent, 160),
                ["rect"] = new JsonObject
                {
                    ["x"] = MathF.Round(rect.X, 1),
                    ["y"] = MathF.Round(rect.Y, 1),
                    ["w"] = MathF.Round(rect.Width, 1),
                    ["h"] = MathF.Round(rect.Height, 1),
                },
                ["screenCoverage"] = MathF.Round(Math.Max(0f, rect.Width) * Math.Max(0f, rect.Height) / Math.Max(1f, viewportArea), 5),
                ["pseudoState"] = node.PseudoState.ToString(),
            };

            if (node.ClassNames.Count > 0)
            {
                var classes = new JsonArray();
                foreach (string c in node.ClassNames) classes.Add(c);
                result["classes"] = classes;
            }

            if (node.CanvasContent != null)
            {
                result["canvasContent"] = node.CanvasContent.GetType().Name;
            }

            if (node.CanScrollHorizontally || node.CanScrollVertically)
            {
                result["scroll"] = new JsonObject
                {
                    ["x"] = node.ScrollOffsetX,
                    ["y"] = node.ScrollOffsetY,
                    ["maxX"] = node.MaxScrollX,
                    ["maxY"] = node.MaxScrollY,
                };
            }

            return result;
        }

        private static string? Truncate(string? text, int max)
        {
            if (text == null) return null;
            return text.Length <= max ? text : text.Substring(0, max) + "…";
        }
    }

    public sealed class UiTreeTool : IAgentTool
    {
        public string Name => "ludots.ui.tree";

        public string Description =>
            "Dump the unified UI tree (Compose/Reactive/Markup all land in the same UiScene). " +
            "Params: {maxDepth?=8, maxNodes?=500, rootElementId?=string}. " +
            "Nodes carry nodeId/tag/elementId/classes/text/rect/screenCoverage/pseudoState/scroll/canvasContent " +
            "(canvasContent marks embedded browser surfaces). Truncation is reported via truncated=true.";

        public JsonObject? InputSchema => new JsonObject
        {
            ["type"] = "object",
            ["properties"] = new JsonObject
            {
                ["maxDepth"] = new JsonObject { ["type"] = "integer", ["minimum"] = 1, ["maximum"] = 64, ["default"] = 8 },
                ["maxNodes"] = new JsonObject { ["type"] = "integer", ["minimum"] = 1, ["maximum"] = 5000, ["default"] = 500 },
                ["rootElementId"] = new JsonObject { ["type"] = "string", ["description"] = "subtree root element id; defaults to scene root" },
            },
        };

        public JsonNode? Execute(JsonObject? args, AgentToolContext context)
        {
            int maxDepth = AgentToolContext.OptionalInt(args, "maxDepth", 8);
            int maxNodes = Math.Clamp(AgentToolContext.OptionalInt(args, "maxNodes", 500), 1, 5000);
            string? rootElementId = AgentToolContext.OptionalString(args, "rootElementId");

            UiScene scene = RequireScene(context, out float viewportArea);

            UiNode? root = scene.Root;
            if (rootElementId != null)
            {
                root = scene.FindByElementId(rootElementId)
                    ?? throw new AgentToolException("ui.node_not_found", $"No UI node with elementId '{rootElementId}'.");
            }

            if (root == null)
            {
                return new JsonObject { ["mounted"] = false, ["nodes"] = new JsonArray() };
            }

            int visited = 0;
            bool truncated = false;
            JsonObject SerializeRecursive(UiNode node, int depth)
            {
                visited++;
                JsonObject json = UiNodeJson.Serialize(node, viewportArea);
                if (depth >= maxDepth || visited >= maxNodes)
                {
                    if (node.Children.Count > 0)
                    {
                        truncated = true;
                        json["childrenTruncated"] = node.Children.Count;
                    }

                    return json;
                }

                if (node.Children.Count > 0)
                {
                    var children = new JsonArray();
                    foreach (UiNode child in node.Children)
                    {
                        if (visited >= maxNodes)
                        {
                            truncated = true;
                            break;
                        }

                        children.Add(SerializeRecursive(child, depth + 1));
                    }

                    json["children"] = children;
                }

                return json;
            }

            JsonObject rootJson = SerializeRecursive(root, 0);
            return new JsonObject
            {
                ["mounted"] = true,
                ["sceneVersion"] = scene.Version,
                ["focusedNodeId"] = scene.FocusedNodeId?.Value,
                ["truncated"] = truncated,
                ["visited"] = visited,
                ["tree"] = rootJson,
            };
        }

        internal static UiScene RequireScene(AgentToolContext context, out float viewportArea)
        {
            if (!context.Engine.GlobalContext.TryGetValue(CoreServiceKeys.UIRoot.Name, out object? obj) ||
                obj is not UIRoot uiRoot)
            {
                throw new AgentToolException(
                    AgentBridgeErrorCodes.ServiceUnavailable,
                    $"Required service '{CoreServiceKeys.UIRoot.Name}' is not available in this runtime.");
            }

            viewportArea = Math.Max(1f, uiRoot.Width * uiRoot.Height);
            return uiRoot.Scene ?? throw new AgentToolException(
                "ui.scene_not_mounted",
                "No UiScene is currently mounted on the UIRoot.");
        }
    }

    public sealed class UiQueryTool : IAgentTool
    {
        public string Name => "ludots.ui.query";

        public string Description =>
            "Query UI nodes with a CSS selector (e.g. '#healthBar', '.button', 'div.panel'). " +
            "Params: {selector: string, limit?=50}. Returns the same node shape as ludots.ui.tree.";

        public JsonObject? InputSchema => new JsonObject
        {
            ["type"] = "object",
            ["properties"] = new JsonObject
            {
                ["selector"] = new JsonObject { ["type"] = "string" },
                ["limit"] = new JsonObject { ["type"] = "integer", ["minimum"] = 1, ["maximum"] = 500, ["default"] = 50 },
            },
            ["required"] = new JsonArray("selector"),
        };

        public JsonNode? Execute(JsonObject? args, AgentToolContext context)
        {
            string selector = AgentToolContext.RequireString(args, "selector");
            int limit = Math.Clamp(AgentToolContext.OptionalInt(args, "limit", 50), 1, 500);

            UiScene scene = UiTreeTool.RequireScene(context, out float viewportArea);
            IReadOnlyList<UiNode> matches = scene.QuerySelectorAll(selector);

            var array = new JsonArray();
            for (int i = 0; i < matches.Count && i < limit; i++)
            {
                array.Add(UiNodeJson.Serialize(matches[i], viewportArea));
            }

            return new JsonObject
            {
                ["selector"] = selector,
                ["totalMatched"] = matches.Count,
                ["returned"] = array.Count,
                ["nodes"] = array,
            };
        }
    }

    public sealed class UiClickTool : IAgentTool
    {
        private static bool TryGetFloat(JsonObject? args, string name, out float value)
        {
            value = 0f;
            if (args?[name] is JsonValue node && node.TryGetValue(out double d))
            {
                value = (float)d;
                return true;
            }

            return false;
        }

        public string Name => "ludots.ui.click";

        public string Description =>
            "Click a UI node by elementId, or hit-test raw screen coordinates. " +
            "Params: {elementId?: string} or {x: number, y: number}. Dispatches Down/Up/Click pointer events into the UiScene.";

        public JsonObject? InputSchema => new JsonObject
        {
            ["type"] = "object",
            ["properties"] = new JsonObject
            {
                ["elementId"] = new JsonObject { ["type"] = "string" },
                ["x"] = new JsonObject { ["type"] = "number" },
                ["y"] = new JsonObject { ["type"] = "number" },
            },
        };

        public JsonNode? Execute(JsonObject? args, AgentToolContext context)
        {
            UiScene scene = UiTreeTool.RequireScene(context, out _);

            UiNode? target;
            float x;
            float y;
            string? elementId = AgentToolContext.OptionalString(args, "elementId");
            if (elementId != null)
            {
                target = scene.FindByElementId(elementId)
                    ?? throw new AgentToolException("ui.node_not_found", $"No UI node with elementId '{elementId}' in the mounted scene. Discover nodes via ludots.ui.tree / ludots.ui.query, or click by raw screen coordinates (x/y).");
                UiRect rect = target.LayoutRect;
                x = rect.X + rect.Width * 0.5f;
                y = rect.Y + rect.Height * 0.5f;
            }
            else if (TryGetFloat(args, "x", out float xRaw) && TryGetFloat(args, "y", out float yRaw))
            {
                x = xRaw;
                y = yRaw;
                target = scene.HitTest(x, y);
            }
            else
            {
                throw new AgentToolException(
                    AgentBridgeErrorCodes.InvalidParams,
                    "Provide either elementId or x/y coordinates.");
            }

            UiNodeId? targetId = target?.Id;
            var down = new UiPointerEvent(UiPointerEventType.Down, PointerId: 9001, X: x, Y: y, TargetNodeId: targetId);
            var up = new UiPointerEvent(UiPointerEventType.Up, PointerId: 9001, X: x, Y: y, TargetNodeId: targetId);
            var click = new UiPointerEvent(UiPointerEventType.Click, PointerId: 9001, X: x, Y: y, TargetNodeId: targetId);
            scene.Dispatch(down);
            scene.Dispatch(up);
            var result = scene.Dispatch(click);

            return new JsonObject
            {
                ["handled"] = result.Handled,
                ["x"] = x,
                ["y"] = y,
                ["target"] = target != null ? UiNodeJson.Serialize(target, 1f) : null,
            };
        }
    }
}
