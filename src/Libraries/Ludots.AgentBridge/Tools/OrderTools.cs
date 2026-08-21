using System.Numerics;
using System.Text.Json.Nodes;
using Arch.Core;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Orders;
using Ludots.Core.Scripting;

namespace Ludots.AgentBridge.Tools
{
    public sealed class OrdersInspectTool : IAgentTool
    {
        public string Name => "ludots.orders.inspect";

        public string Description =>
            "Inspect order pipeline state. Params: {entityId?: int, recent?: int (default 20)}. " +
            "With entityId: that entity's OrderBuffer (active order, queued orders with priority). " +
            "Always includes global admission/terminal result buffers (most recent first) and the orderTypes key catalog (valid keys for ludots.orders.issue).";

        public JsonObject? InputSchema => new JsonObject
        {
            ["type"] = "object",
            ["properties"] = new JsonObject
            {
                ["entityId"] = new JsonObject { ["type"] = "integer" },
                ["recent"] = new JsonObject { ["type"] = "integer", ["minimum"] = 1, ["maximum"] = 200, ["default"] = 20 },
            },
        };

        public JsonNode? Execute(JsonObject? args, AgentToolContext context)
        {
            int recent = Math.Clamp(AgentToolContext.OptionalInt(args, "recent", 20), 1, 200);
            int? entityId = AgentToolContext.OptionalInt(args, "entityId", 0) is int id && id > 0 ? id : null;

            var result = new JsonObject();

            if (entityId.HasValue)
            {
                Entity entity = context.ResolveEntity(entityId.Value);
                result["entity"] = InspectEntity(context.Engine.World, entity, context);
            }

            var admission = context.RequireService(CoreServiceKeys.OrderAdmissionResultBuffer);
            var admissionArray = new JsonArray();
            int admissionStart = Math.Max(0, admission.Count - recent);
            for (int i = admission.Count - 1; i >= admissionStart; i--)
            {
                ref readonly OrderAdmissionOutcome outcome = ref admission[i];
                admissionArray.Add(new JsonObject
                {
                    ["orderId"] = outcome.OrderId,
                    ["orderTypeId"] = outcome.OrderTypeId,
                    ["stage"] = outcome.Stage.ToString(),
                    ["result"] = outcome.Result.ToString(),
                });
            }

            result["admission"] = new JsonObject
            {
                ["count"] = admission.Count,
                ["generation"] = admission.Generation,
                ["overflowCount"] = admission.OverflowCount,
                ["highWatermark"] = admission.HighWatermark,
                ["recent"] = admissionArray,
            };

            if (context.TryGetService(CoreServiceKeys.OrderTerminalResultBuffer, out var terminal))
            {
                var terminalArray = new JsonArray();
                int terminalStart = Math.Max(0, terminal.Count - recent);
                for (int i = terminal.Count - 1; i >= terminalStart; i--)
                {
                    ref readonly OrderTerminalOutcome outcome = ref terminal[i];
                    terminalArray.Add(new JsonObject
                    {
                        ["orderId"] = outcome.OrderId,
                        ["orderTypeId"] = outcome.OrderTypeId,
                        ["state"] = outcome.State.ToString(),
                        ["failureReason"] = outcome.FailureReason.ToString(),
                        ["actorEntityId"] = outcome.Actor.Id,
                    });
                }

                result["terminal"] = new JsonObject
                {
                    ["count"] = terminal.Count,
                    ["recent"] = terminalArray,
                };
            }

            if (context.TryGetService(CoreServiceKeys.OrderTypeRegistry, out var orderTypes))
            {
                var typeArray = new JsonArray();
                foreach (int typeId in orderTypes.GetRegisteredIds())
                {
                    if (orderTypes.TryGet(typeId, out var config))
                    {
                        typeArray.Add(new JsonObject
                        {
                            ["id"] = typeId,
                            ["key"] = config.Key,
                            ["label"] = config.Label,
                        });
                    }
                }

                result["orderTypes"] = typeArray;
            }

            return result;
        }

        private static JsonObject InspectEntity(World world, Entity entity, AgentToolContext context)
        {
            if (!world.Has<OrderBuffer>(entity))
            {
                return new JsonObject
                {
                    ["entityId"] = entity.Id,
                    ["hasOrderBuffer"] = false,
                };
            }

            ref OrderBuffer buffer = ref world.Get<OrderBuffer>(entity);
            context.TryGetService(CoreServiceKeys.OrderTypeRegistry, out var registry);

            JsonObject DescribeOrder(in QueuedOrder queued)
            {
                var json = new JsonObject
                {
                    ["orderId"] = queued.Order.OrderId,
                    ["orderTypeId"] = queued.Order.OrderTypeId,
                    ["priority"] = queued.Priority,
                };

                if (registry != null && registry.TryGet(queued.Order.OrderTypeId, out var config))
                {
                    json["orderTypeKey"] = config.Key;
                    json["orderTypeLabel"] = config.Label;
                }

                if (queued.Order.Args.Spatial.Kind == OrderSpatialKind.WorldCm)
                {
                    Vector3 worldCm = queued.Order.Args.Spatial.WorldCm;
                    json["targetWorldCm"] = new JsonObject { ["x"] = worldCm.X, ["y"] = worldCm.Y, ["z"] = worldCm.Z };
                }

                if (queued.Order.Target != default)
                {
                    json["targetEntityId"] = queued.Order.Target.Id;
                }

                return json;
            }

            var queuedArray = new JsonArray();
            for (int i = 0; i < buffer.QueuedCount; i++)
            {
                queuedArray.Add(DescribeOrder(buffer.GetQueued(i)));
            }

            return new JsonObject
            {
                ["entityId"] = entity.Id,
                ["hasOrderBuffer"] = true,
                ["active"] = buffer.HasActive ? DescribeOrder(buffer.ActiveOrder) : null,
                ["pending"] = buffer.HasPending ? DescribeOrder(buffer.PendingOrder) : null,
                ["queued"] = queuedArray,
            };
        }
    }

    public sealed class OrdersIssueTool : IAgentTool
    {
        public string Name => "ludots.orders.issue";

        public string Description =>
            "Issue an order through the canonical intake queue (OrderQueue.Submit, same path as player input). " +
            "Params: {entityId: int, orderType: string|int, targetEntityId?: int, worldXCm?: number, worldYCm?: number, queued?: bool}. " +
            "Returns the OrderSubmitResult (Activated/Queued/Rejected*).";

        public JsonObject? InputSchema => new JsonObject
        {
            ["type"] = "object",
            ["properties"] = new JsonObject
            {
                ["entityId"] = new JsonObject { ["type"] = "integer" },
                ["orderType"] = new JsonObject { ["description"] = "order type key (string) or id (integer)" },
                ["targetEntityId"] = new JsonObject { ["type"] = "integer" },
                ["worldXCm"] = new JsonObject { ["type"] = "number" },
                ["worldYCm"] = new JsonObject { ["type"] = "number" },
                ["queued"] = new JsonObject { ["type"] = "boolean", ["default"] = false },
            },
            ["required"] = new JsonArray("entityId", "orderType"),
        };

        public JsonNode? Execute(JsonObject? args, AgentToolContext context)
        {
            int entityId = AgentToolContext.RequireInt(args, "entityId");
            Entity actor = context.ResolveEntity(entityId);

            var orderTypeRegistry = context.RequireService(CoreServiceKeys.OrderTypeRegistry);
            var orderQueue = context.RequireService(CoreServiceKeys.OrderQueue);

            int orderTypeId;
            JsonNode? orderTypeNode = args?["orderType"];
            if (orderTypeNode is JsonValue value && value.TryGetValue(out int byId))
            {
                orderTypeId = byId;
            }
            else if (orderTypeNode is JsonValue keyValue && keyValue.TryGetValue(out string? key) && !string.IsNullOrWhiteSpace(key))
            {
                if (!orderTypeRegistry.TryGetId(key, out orderTypeId))
                {
                    throw new AgentToolException(
                        AgentBridgeErrorCodes.InvalidParams,
                        $"Unknown order type key '{key}'. Discover valid keys via ludots.orders.inspect (orderTypes field) or the order_types.json config.");
                }
            }
            else
            {
                throw new AgentToolException(
                    AgentBridgeErrorCodes.InvalidParams,
                    "Parameter 'orderType' must be a string key or integer id.");
            }

            var order = new Order
            {
                OrderId = 0,
                OrderTypeId = orderTypeId,
                PlayerId = AgentToolContext.SolePlayerId(context.Engine),
                Actor = actor,
                CommandSource = actor,
                SubmitMode = AgentToolContext.OptionalBool(args, "queued", false)
                    ? OrderSubmitMode.Queued
                    : OrderSubmitMode.Immediate,
            };

            int targetEntityId = AgentToolContext.OptionalInt(args, "targetEntityId", 0);
            if (targetEntityId > 0)
            {
                order.Target = context.ResolveEntity(targetEntityId);
            }

            float worldX = ReadFloat(args, "worldXCm");
            float worldY = ReadFloat(args, "worldYCm");
            if (!float.IsNaN(worldX) && !float.IsNaN(worldY))
            {
                order.Args = OrderArgs.CreateSingleWorldCm(new Vector3(worldX, worldY, 0f));
            }

            OrderSubmitResult submitResult = orderQueue.Submit(in order);

            return new JsonObject
            {
                ["entityId"] = entityId,
                ["orderTypeId"] = orderTypeId,
                ["result"] = submitResult.ToString(),
                ["accepted"] = OrderSubmitResultSemantics.IsAccepted(submitResult),
            };
        }

        private static float ReadFloat(JsonObject? args, string name)
        {
            if (args?[name] is JsonValue node && node.TryGetValue(out double d))
            {
                return (float)d;
            }

            return float.NaN;
        }
    }
}
