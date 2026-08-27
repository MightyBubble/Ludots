using System.Text.Json.Nodes;
using Arch.Core;
using Ludots.Core.Components;
using Ludots.Core.EntityCollections;
using Ludots.Core.Fields;
using Ludots.Core.Gameplay.FieldRegions;
using Ludots.Core.Map;
using Ludots.Core.Mathematics.FixedPoint;
using Ludots.Core.Scripting;
using Ludots.Platform.Abstractions;

namespace Ludots.AgentBridge.Tools
{
    /// <summary>List discrete-id (and other) field layers on the focused map session.</summary>
    public sealed class FieldLayersTool : IAgentTool
    {
        public string Name => "ludots.field.layers";

        public string Description =>
            "List field layers hosted by the focused map session (key, kind, chunk/cell sizes, nonDefaultCount, regionCount for discreteId). No parameters.";

        public JsonObject? InputSchema => null;

        public JsonNode? Execute(JsonObject? args, AgentToolContext context)
        {
            MapSession session = RequireSession(context);
            var layers = new JsonArray();
            if (session.Fields == null)
            {
                return new JsonObject
                {
                    ["mapId"] = session.MapId.Value,
                    ["layers"] = layers,
                };
            }

            foreach (FieldLayerData layer in session.Fields.Layers)
            {
                var entry = new JsonObject
                {
                    ["key"] = layer.LayerKey,
                    ["kind"] = layer.Definition.Kind.ToString(),
                    ["cellSizeCm"] = layer.Definition.CellSizeCm,
                    ["chunkSizeCells"] = layer.Definition.ChunkSizeCells,
                    ["persistent"] = layer.Persistent,
                };

                switch (layer)
                {
                    case DiscreteIdFieldLayerData discrete:
                        entry["nonDefaultCount"] = discrete.Field.NonDefaultCount;
                        entry["chunkCount"] = discrete.Field.ChunkCount;
                        entry["regionCount"] = discrete.Regions.Count;
                        var regions = new JsonArray();
                        for (int id = 1; id <= discrete.Regions.Count; id++)
                        {
                            regions.Add(new JsonObject
                            {
                                ["id"] = id,
                                ["key"] = discrete.Regions.GetName(id),
                            });
                        }

                        entry["regions"] = regions;
                        break;
                    case Scalar32FieldLayerData scalar:
                        entry["nonDefaultCount"] = scalar.Field.NonDefaultCount;
                        entry["chunkCount"] = scalar.Field.ChunkCount;
                        break;
                    case Vector2FieldLayerData vector2:
                        entry["nonDefaultCount"] = vector2.Field.NonDefaultCount;
                        entry["chunkCount"] = vector2.Field.ChunkCount;
                        break;
                    case Vector3FieldLayerData vector3:
                        entry["nonDefaultCount"] = vector3.Field.NonDefaultCount;
                        entry["chunkCount"] = vector3.Field.ChunkCount;
                        break;
                }

                layers.Add(entry);
            }

            return new JsonObject
            {
                ["mapId"] = session.MapId.Value,
                ["layers"] = layers,
            };
        }

        internal static MapSession RequireSession(AgentToolContext context)
        {
            MapSession? session = context.Engine.CurrentMapSession;
            if (session == null)
            {
                throw new AgentToolException(
                    AgentBridgeErrorCodes.ServiceUnavailable,
                    "No focused map session; load a map that enables Fields before calling field tools.");
            }

            return session;
        }
    }

    /// <summary>Point-query a discrete-id field cell by world cm or cell coordinates.</summary>
    public sealed class FieldCellTool : IAgentTool
    {
        public string Name => "ludots.field.cell";

        public string Description =>
            "Resolve ownership (or any discreteId layer) at a world or cell coordinate. " +
            "Params: {layer: string, worldXCm?: int, worldYCm?: int, cellX?: int, cellY?: int}. " +
            "Provide either world cm pair or cell pair. Returns regionId/regionKey/cell, plus hierarchy chain when hierarchies are loaded.";

        public JsonObject? InputSchema => new JsonObject
        {
            ["type"] = "object",
            ["properties"] = new JsonObject
            {
                ["layer"] = new JsonObject { ["type"] = "string" },
                ["worldXCm"] = new JsonObject { ["type"] = "integer" },
                ["worldYCm"] = new JsonObject { ["type"] = "integer" },
                ["cellX"] = new JsonObject { ["type"] = "integer" },
                ["cellY"] = new JsonObject { ["type"] = "integer" },
            },
            ["required"] = new JsonArray("layer"),
        };

        public JsonNode? Execute(JsonObject? args, AgentToolContext context)
        {
            MapSession session = FieldLayersTool.RequireSession(context);
            string layerKey = AgentToolContext.RequireString(args, "layer");
            DiscreteIdFieldLayerData layer = RequireDiscrete(session, layerKey);

            FieldCell2D cell;
            if (args?["cellX"] is JsonValue && args["cellY"] is JsonValue)
            {
                cell = new FieldCell2D(AgentToolContext.RequireInt(args, "cellX"), AgentToolContext.RequireInt(args, "cellY"));
            }
            else if (args?["worldXCm"] is JsonValue && args?["worldYCm"] is JsonValue)
            {
                int wx = AgentToolContext.RequireInt(args, "worldXCm");
                int wy = AgentToolContext.RequireInt(args, "worldYCm");
                cell = layer.Field.WorldToCell(new WorldCmInt2(wx, wy));
            }
            else
            {
                throw new AgentToolException(
                    AgentBridgeErrorCodes.InvalidParams,
                    "Provide either {cellX,cellY} or {worldXCm,worldYCm}.");
            }

            int regionId = layer.Field.Get(cell);
            string? regionKey = regionId > 0 ? layer.Regions.GetName(regionId) : null;
            var result = new JsonObject
            {
                ["layer"] = layerKey,
                ["cell"] = new JsonObject { ["x"] = cell.X, ["y"] = cell.Y },
                ["regionId"] = regionId,
                ["regionKey"] = regionKey,
            };

            if (regionId > 0 &&
                session.RegionIndex != null &&
                session.RegionIndex.TryResolve(layer.LayerId, regionId, out Entity regionEntity))
            {
                result["regionEntityId"] = regionEntity.Id;
                var chainLabels = new List<string>();
                if (RegionHierarchyBuilder.TryResolveChain(context.Engine.World, regionEntity, chainLabels))
                {
                    var labels = new JsonArray();
                    foreach (string label in chainLabels)
                    {
                        labels.Add(label);
                    }

                    result["hierarchyChain"] = labels;
                }

                EntityCollectionStore? collections = context.Engine.GetService(CoreServiceKeys.EntityCollectionStore);
                string collectionKey = $"collection.field.{layerKey}.members";
                if (collections != null &&
                    collections.TryGetView(regionEntity, collectionKey, out EntityCollectionView view))
                {
                    result["rosterCount"] = view.Count;
                }
            }

            return result;
        }

        internal static DiscreteIdFieldLayerData RequireDiscrete(MapSession session, string layerKey)
        {
            if (session.Fields == null || !session.Fields.TryGetByKey(layerKey, out FieldLayerData layerData))
            {
                throw new AgentToolException(
                    AgentBridgeErrorCodes.InvalidParams,
                    $"Field layer '{layerKey}' is not enabled on map '{session.MapId.Value}'.");
            }

            if (layerData is not DiscreteIdFieldLayerData discrete)
            {
                throw new AgentToolException(
                    AgentBridgeErrorCodes.InvalidParams,
                    $"Field layer '{layerKey}' is {layerData.Definition.Kind}, not discreteId.");
            }

            return discrete;
        }
    }

    /// <summary>Debug write one cell on a discreteId layer (authoritative Set; marks dirty).</summary>
    public sealed class FieldWriteCellTool : IAgentTool
    {
        public string Name => "ludots.field.writeCell";

        public string Description =>
            "Debug write: set one discreteId cell to a region key (or erase with regionKey null/\"\"). " +
            "Params: {layer, cellX, cellY, regionKey?: string}. Unknown regionKey fails closed. " +
            "Runtime writes do not rewrite Fields/cells assets.";

        public JsonObject? InputSchema => new JsonObject
        {
            ["type"] = "object",
            ["properties"] = new JsonObject
            {
                ["layer"] = new JsonObject { ["type"] = "string" },
                ["cellX"] = new JsonObject { ["type"] = "integer" },
                ["cellY"] = new JsonObject { ["type"] = "integer" },
                ["regionKey"] = new JsonObject { ["type"] = "string" },
            },
            ["required"] = new JsonArray("layer", "cellX", "cellY"),
        };

        public JsonNode? Execute(JsonObject? args, AgentToolContext context)
        {
            MapSession session = FieldLayersTool.RequireSession(context);
            string layerKey = AgentToolContext.RequireString(args, "layer");
            DiscreteIdFieldLayerData layer = FieldCellTool.RequireDiscrete(session, layerKey);
            int cellX = AgentToolContext.RequireInt(args, "cellX");
            int cellY = AgentToolContext.RequireInt(args, "cellY");
            string? regionKey = args?["regionKey"]?.GetValue<string>();
            int regionId = 0;
            if (!string.IsNullOrWhiteSpace(regionKey))
            {
                if (!layer.Regions.Contains(regionKey))
                {
                    throw new AgentToolException(
                        AgentBridgeErrorCodes.InvalidParams,
                        $"Region key '{regionKey}' is not registered on layer '{layerKey}'.");
                }

                regionId = layer.Regions.GetId(regionKey);
            }

            layer.Field.Set(new FieldCell2D(cellX, cellY), regionId);
            return new JsonObject
            {
                ["layer"] = layerKey,
                ["cell"] = new JsonObject { ["x"] = cellX, ["y"] = cellY },
                ["regionId"] = regionId,
                ["regionKey"] = regionId > 0 ? layer.Regions.GetName(regionId) : null,
                ["nonDefaultCount"] = layer.Field.NonDefaultCount,
            };
        }
    }

    /// <summary>Resolve hierarchy chain for a region entity or cell.</summary>
    public sealed class FieldHierarchyTool : IAgentTool
    {
        public string Name => "ludots.field.hierarchy";

        public string Description =>
            "Resolve the hierarchy label chain for a discreteId cell or region key. " +
            "Params: {layer, regionKey?: string, cellX?: int, cellY?: int}. " +
            "Returns keys from finest region up through parent groups (Fields/hierarchies.json).";

        public JsonObject? InputSchema => new JsonObject
        {
            ["type"] = "object",
            ["properties"] = new JsonObject
            {
                ["layer"] = new JsonObject { ["type"] = "string" },
                ["regionKey"] = new JsonObject { ["type"] = "string" },
                ["cellX"] = new JsonObject { ["type"] = "integer" },
                ["cellY"] = new JsonObject { ["type"] = "integer" },
            },
            ["required"] = new JsonArray("layer"),
        };

        public JsonNode? Execute(JsonObject? args, AgentToolContext context)
        {
            MapSession session = FieldLayersTool.RequireSession(context);
            string layerKey = AgentToolContext.RequireString(args, "layer");
            DiscreteIdFieldLayerData layer = FieldCellTool.RequireDiscrete(session, layerKey);
            if (session.RegionIndex == null)
            {
                throw new AgentToolException(
                    AgentBridgeErrorCodes.ServiceUnavailable,
                    "Region index missing; map has no materialized field regions.");
            }

            int regionId;
            string? regionKey = args?["regionKey"]?.GetValue<string>();
            if (!string.IsNullOrWhiteSpace(regionKey))
            {
                regionId = layer.Regions.GetId(regionKey);
            }
            else if (args?["cellX"] is JsonValue && args["cellY"] is JsonValue)
            {
                regionId = layer.Field.Get(new FieldCell2D(
                    AgentToolContext.RequireInt(args, "cellX"),
                    AgentToolContext.RequireInt(args, "cellY")));
                regionKey = regionId > 0 ? layer.Regions.GetName(regionId) : null;
            }
            else
            {
                throw new AgentToolException(
                    AgentBridgeErrorCodes.InvalidParams,
                    "Provide regionKey or {cellX,cellY}.");
            }

            if (regionId <= 0 || !session.RegionIndex.TryResolve(layer.LayerId, regionId, out Entity entity))
            {
                return new JsonObject
                {
                    ["layer"] = layerKey,
                    ["regionId"] = regionId,
                    ["regionKey"] = regionKey,
                    ["chain"] = new JsonArray(),
                    ["reason"] = "No region at this location.",
                };
            }

            var chain = new JsonArray();
            var labels = new List<string>();
            if (RegionHierarchyBuilder.TryResolveChain(context.Engine.World, entity, labels))
            {
                foreach (string label in labels)
                {
                    chain.Add(label);
                }
            }
            else if (!string.IsNullOrEmpty(regionKey))
            {
                chain.Add(regionKey);
            }

            return new JsonObject
            {
                ["layer"] = layerKey,
                ["regionId"] = regionId,
                ["regionKey"] = regionKey,
                ["regionEntityId"] = entity.Id,
                ["chain"] = chain,
            };
        }
    }

    /// <summary>Runtime redraw: apply rect strokes for one region key on a discreteId layer.</summary>
    public sealed class FieldRedrawTool : IAgentTool
    {
        public string Name => "ludots.field.redraw";

        public string Description =>
            "Runtime redraw: set one region's cells from rect strokes on a discreteId layer. " +
            "Params: {layer, regionKey, rects:[[x0,y0,x1,y1],...]}. New region keys are registered " +
            "and materialized; stationary tracked entities re-evaluate on the next tick. " +
            "Runtime writes do not rewrite Fields/cells assets.";

        public JsonObject? InputSchema => new JsonObject
        {
            ["type"] = "object",
            ["properties"] = new JsonObject
            {
                ["layer"] = new JsonObject { ["type"] = "string" },
                ["regionKey"] = new JsonObject { ["type"] = "string" },
                ["rects"] = new JsonObject
                {
                    ["type"] = "array",
                    ["items"] = new JsonObject
                    {
                        ["type"] = "array",
                        ["items"] = new JsonObject { ["type"] = "integer" },
                    },
                },
            },
            ["required"] = new JsonArray("layer", "regionKey", "rects"),
        };

        public JsonNode? Execute(JsonObject? args, AgentToolContext context)
        {
            MapSession session = FieldLayersTool.RequireSession(context);
            string layerKey = AgentToolContext.RequireString(args, "layer");
            string regionKey = AgentToolContext.RequireString(args, "regionKey");
            JsonArray rects = args?["rects"] as JsonArray
                ?? throw new InvalidOperationException("'rects' must be an array of [x0,y0,x1,y1].");

            var strokes = new List<FieldCellRectStroke>();
            foreach (JsonNode? entry in rects)
            {
                if (entry is not JsonArray quad || quad.Count != 4)
                {
                    throw new InvalidOperationException("each rects entry must be [x0,y0,x1,y1].");
                }

                strokes.Add(new FieldCellRectStroke(
                    quad[0]!.GetValue<int>(),
                    quad[1]!.GetValue<int>(),
                    quad[2]!.GetValue<int>(),
                    quad[3]!.GetValue<int>(),
                    regionId: 1));
            }

            FieldRedrawResult result = FieldRegionRedraw.ApplyDiscrete(
                context.Engine.World,
                session,
                layerKey,
                new[]
                {
                    new FieldRegionStrokeEdit(regionKey, strokes),
                });

            return new JsonObject
            {
                ["layer"] = result.LayerKey,
                ["regionKey"] = regionKey,
                ["regionsRegistered"] = result.RegionsRegistered,
                ["cellsChanged"] = result.CellsChanged,
            };
        }
    }
}
