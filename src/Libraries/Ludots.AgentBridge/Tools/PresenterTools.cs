using System.Numerics;
using System.Text.Json.Nodes;
using Arch.Core;
using Ludots.Core.Client;
using Ludots.Core.Components;
using Ludots.Core.Engine;
using Ludots.Core.Knowledge;
using Ludots.Core.Mathematics;
using Ludots.Core.Mathematics.FixedPoint;
using Ludots.Core.Presentation.Commands;
using Ludots.Core.Presentation.Components;
using Ludots.Core.Presentation.Presenters;
using Ludots.Core.Presentation.Rendering;
using Ludots.Core.Scripting;
using Ludots.Core.Spatial;
using Ludots.Core.UI.PanelHosting;
using Ludots.Platform.Abstractions;
using Ludots.UI;
using Ludots.UI.Runtime;

namespace Ludots.AgentBridge.Tools
{
    internal static class PresenterObservation
    {
        public const int KnowledgeRecordCapacity = 256;

        public static bool TryResolveSeatViewer(
            GameEngine engine,
            string seatId,
            out Entity viewer,
            out string reason)
        {
            viewer = Entity.Null;
            reason = string.Empty;
            if (!engine.TryGetService(CoreServiceKeys.ClientLocalSeatRegistry, out ClientLocalSeatRegistry? registry) ||
                registry == null)
            {
                reason = "ClientLocalSeatRegistry is not registered in this runtime.";
                return false;
            }

            if (!registry.TryGet(seatId, out ClientLocalSeat seat))
            {
                reason = $"Seat '{seatId}' does not exist. Known seats: {DescribeSeatIds(registry)}.";
                return false;
            }

            if (!seat.HasPossession)
            {
                reason = $"Seat '{seatId}' has no possessed player rep.";
                return false;
            }

            if (!engine.World.IsAlive(seat.PossessedRep))
            {
                reason = $"Seat '{seatId}' possesses entity {seat.PossessedRep} which is not alive.";
                return false;
            }

            viewer = seat.PossessedRep;
            return true;
        }

        public static string DescribeSeatIds(ClientLocalSeatRegistry registry)
        {
            return registry.SeatIds.Count == 0 ? "(none)" : string.Join(",", registry.SeatIds);
        }

        public static JsonObject BuildKnowledgeSection(
            AgentToolContext context,
            string seatId,
            Func<Entity, bool> isTargetDrawn,
            Func<Entity, string?> targetName)
        {
            var section = new JsonObject
            {
                ["seatId"] = seatId,
            };

            bool resolved = TryResolveSeatViewer(context.Engine, seatId, out Entity viewer, out string reason);
            section["resolved"] = resolved;
            if (!resolved)
            {
                section["reason"] = reason;
                section["knowledgeRecords"] = 0;
                section["note"] = "Viewer unresolved: knowledge join not evaluated (fail-visible, not an empty match).";
                return section;
            }

            section["viewerEntityId"] = viewer.Id;
            if (!context.TryGetService(CoreServiceKeys.KnowledgeProjectionStore, out KnowledgeProjectionStore? store) ||
                store == null)
            {
                section["reason"] = "KnowledgeProjectionStore is not registered in this runtime.";
                section["knowledgeRecords"] = 0;
                return section;
            }

            int currentTick = KnowledgeProjectionConsumer.ResolveCurrentTick(context.Engine.GlobalContext);
            var targets = new Entity[KnowledgeRecordCapacity];
            var records = new KnowledgeDisclosureRecord[KnowledgeRecordCapacity];
            int count = store.CopyRecords(viewer, currentTick, targets, records);
            section["knowledgeTick"] = currentTick;
            section["knowledgeRecords"] = count;
            section["truncated"] = count >= KnowledgeRecordCapacity;

            if (count == 0)
            {
                section["note"] = "Viewer has no disclosure records (knowledgeRecords=0, fail-visible).";
                return section;
            }

            var diffs = new JsonArray();
            int mismatched = 0;
            for (int i = 0; i < count; i++)
            {
                Entity target = targets[i];
                bool shouldSee = records[i].Presence == KnowledgePresence.LiveVisible;
                bool actualDrawn = context.Engine.World.IsAlive(target) && isTargetDrawn(target);
                if (shouldSee == actualDrawn)
                {
                    continue;
                }

                mismatched++;
                diffs.Add(new JsonObject
                {
                    ["targetEntityId"] = context.Engine.World.IsAlive(target) ? target.Id : 0,
                    ["targetName"] = context.Engine.World.IsAlive(target) ? targetName(target) : null,
                    ["shouldSee"] = PresenceLabel(records[i].Presence),
                    ["positionAccess"] = records[i].Position.ToString(),
                    ["actualDrawn"] = actualDrawn,
                });
            }

            section["diffRows"] = diffs;
            section["mismatched"] = mismatched;
            if (mismatched == 0)
            {
                section["note"] = "All disclosure rows agree with presenter draw state.";
            }

            return section;
        }

        public static string PresenceLabel(KnowledgePresence presence)
        {
            return presence switch
            {
                KnowledgePresence.Unknown => "Unknown",
                KnowledgePresence.Known => "Known",
                KnowledgePresence.LiveVisible => "LiveVisible",
                KnowledgePresence.HiddenWithSource => "HiddenWithSource",
                _ => presence.ToString(),
            };
        }

        public static void CollectExpectedVisualStableIds(
            World world,
            in PresenterState state,
            PresenterDefinitionRegistry definitions,
            PresenterVisualStableIdTable visualStableIds,
            List<int> destination)
        {
            if (!definitions.TryGet(state.DefId, out PresenterDefinition definition))
            {
                return;
            }

            BehaviorSlot[] behaviors = definition.Behaviors;
            for (int i = 0; i < behaviors.Length; i++)
            {
                ref readonly BehaviorSlot slot = ref behaviors[i];
                if (slot.Kind != BehaviorKind.AssetBinding ||
                    slot.SlotIndex is < 0 or >= 32 ||
                    (state.BehaviorActiveMask & (1u << slot.SlotIndex)) == 0)
                {
                    continue;
                }

                if (visualStableIds.TryGet(
                        PresenterBehaviorRuntimeUtility.ComposeVisualStableKey(
                            state.StableId,
                            slot.SlotIndex,
                            slot.AssetBinding.AssetKind,
                            state.DefId),
                        out int stableId))
                {
                    destination.Add(stableId);
                }
            }
        }

        public static bool AdapterBuffersContain(
            StableDrawCache? stableDraws,
            PrimitiveDrawBuffer? primitives,
            PrimitiveDrawBuffer? visualSnapshot,
            IReadOnlyList<int> stableIds)
        {
            for (int i = 0; i < stableIds.Count; i++)
            {
                int stableId = stableIds[i];
                if (stableDraws != null && stableDraws.Contains(stableId))
                {
                    return true;
                }

                if (ContainsStableId(primitives, stableId) || ContainsStableId(visualSnapshot, stableId))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool ContainsStableId(PrimitiveDrawBuffer? buffer, int stableId)
        {
            if (buffer == null)
            {
                return false;
            }

            ReadOnlySpan<PrimitiveDrawItem> items = buffer.GetSpan();
            for (int i = 0; i < items.Length; i++)
            {
                if (items[i].StableId == stableId)
                {
                    return true;
                }
            }

            return false;
        }

        public static string? OwnerName(World world, Entity owner)
        {
            return world.IsAlive(owner) && world.Has<Name>(owner) ? world.Get<Name>(owner).Value : null;
        }

        public static float HorizontalDistanceCm(Vector3 visualMetersA, Vector3 visualMetersB)
        {
            float dx = visualMetersA.X - visualMetersB.X;
            float dz = visualMetersA.Z - visualMetersB.Z;
            return MathF.Sqrt(dx * dx + dz * dz) * WorldUnits.CmPerMeter;
        }

        public static float DistanceToInterpolationSegmentCm(
            Vector3 visualPos,
            Fix64Vec2 previousCm,
            Fix64Vec2 currentCm)
        {
            Vector2 previous = new(previousCm.X.ToFloat(), previousCm.Y.ToFloat());
            Vector2 current = new(currentCm.X.ToFloat(), currentCm.Y.ToFloat());
            Vector2 point = new(visualPos.X * WorldUnits.CmPerMeter, visualPos.Z * WorldUnits.CmPerMeter);
            Vector2 segment = current - previous;
            float lengthSq = segment.LengthSquared();
            float t = lengthSq <= 1e-9f ? 0f : Math.Clamp(Vector2.Dot(point - previous, segment) / lengthSq, 0f, 1f);
            Vector2 closest = previous + segment * t;
            return Vector2.Distance(point, closest);
        }

        internal static float RadiansToDeg(float radians) => radians * (180f / MathF.PI);

        internal static JsonObject SerializeVisualPos(Vector3 value)
        {
            return new JsonObject
            {
                ["x"] = MathF.Round(value.X, 3),
                ["y"] = MathF.Round(value.Y, 3),
                ["z"] = MathF.Round(value.Z, 3),
            };
        }
    }

    public sealed class PresentersQueryTool : IAgentTool
    {
        public string Name => "ludots.presenters.query";

        public string Description =>
            "Query the presenter (visual proxy entity) population across the presentation pipeline. " +
            "Params: {plane?=world|screen, anchorKind?=entity|worldPosition, ownerName?=string, definitionId?=int, seatId?=string, offset?=0, limit?=100}. " +
            "plane=world rows carry the full four-hop chain per presenter: owner logicCm/visualPos, presenterPos, sync marker group " +
            "(transformSyncTick/ownerPayloadSync/staticStable/bootstrapPending/transformSource), emit cache (staticDirty/retainedDirty/cachedVersion), " +
            "cull/LOD, behaviorActiveMask, adapterDrawn (hop4 buffer membership). " +
            "plane=screen rows list mounted PanelHost instances with anchor/rect. " +
            "seatId adds a knowledge join: viewer disclosure records vs presenter draw state (fail-visible when viewer has no records).";

        public JsonObject? InputSchema => new JsonObject
        {
            ["type"] = "object",
            ["properties"] = new JsonObject
            {
                ["plane"] = new JsonObject { ["type"] = "string", ["enum"] = new JsonArray("world", "screen"), ["default"] = "world" },
                ["anchorKind"] = new JsonObject { ["type"] = "string", ["enum"] = new JsonArray("entity", "worldPosition") },
                ["ownerName"] = new JsonObject { ["type"] = "string", ["description"] = "case-insensitive substring match on owner Name" },
                ["definitionId"] = new JsonObject { ["type"] = "integer", ["description"] = "PresenterState.DefId filter" },
                ["seatId"] = new JsonObject { ["type"] = "string", ["description"] = "local seat whose possessed viewer drives the knowledge join" },
                ["offset"] = new JsonObject { ["type"] = "integer", ["minimum"] = 0, ["default"] = 0 },
                ["limit"] = new JsonObject { ["type"] = "integer", ["minimum"] = 1, ["maximum"] = 1000, ["default"] = 100 },
            },
        };

        public JsonNode? Execute(JsonObject? args, AgentToolContext context)
        {
            string plane = AgentToolContext.OptionalString(args, "plane") ?? "world";
            if (plane != "world" && plane != "screen")
            {
                throw new AgentToolException(
                    AgentBridgeErrorCodes.InvalidParams,
                    $"Parameter 'plane' must be 'world' or 'screen' (got '{plane}').");
            }

            string? anchorKind = AgentToolContext.OptionalString(args, "anchorKind");
            PresentationAnchorKind? anchorFilter = null;
            if (anchorKind != null)
            {
                anchorFilter = anchorKind switch
                {
                    "entity" => PresentationAnchorKind.Entity,
                    "worldPosition" => PresentationAnchorKind.WorldPosition,
                    _ => throw new AgentToolException(
                        AgentBridgeErrorCodes.InvalidParams,
                        $"Parameter 'anchorKind' must be 'entity' or 'worldPosition' (got '{anchorKind}')."),
                };
            }

            string? ownerNameFilter = AgentToolContext.OptionalString(args, "ownerName");
            int? definitionIdFilter = null;
            if (args != null && args["definitionId"] is JsonValue definitionNode && definitionNode.TryGetValue(out int definitionId))
            {
                definitionIdFilter = definitionId;
            }

            string? seatId = AgentToolContext.OptionalString(args, "seatId");
            int offset = AgentToolContext.OptionalInt(args, "offset", 0);
            int limit = Math.Clamp(AgentToolContext.OptionalInt(args, "limit", 100), 1, 1000);

            return plane == "screen"
                ? QueryScreenPlane(context, seatId, offset, limit)
                : QueryWorldPlane(context, anchorFilter, ownerNameFilter, definitionIdFilter, seatId, offset, limit);
        }

        private JsonObject QueryWorldPlane(
            AgentToolContext context,
            PresentationAnchorKind? anchorFilter,
            string? ownerNameFilter,
            int? definitionIdFilter,
            string? seatId,
            int offset,
            int limit)
        {
            World world = context.Engine.World;
            var rows = new List<JsonObject>();
            var drawnTargets = new HashSet<int>();

            var query = new QueryDescription().WithAll<PresenterState>();
            world.Query(in query, (Entity entity, ref PresenterState state) =>
            {
                if (anchorFilter.HasValue && state.AnchorKind != anchorFilter.Value)
                {
                    return;
                }

                if (definitionIdFilter.HasValue && state.DefId != definitionIdFilter.Value)
                {
                    return;
                }

                string? ownerName = PresenterObservation.OwnerName(world, state.OwnerEntity);
                if (ownerNameFilter != null && (ownerName == null || !ownerName.Contains(ownerNameFilter, StringComparison.OrdinalIgnoreCase)))
                {
                    return;
                }

                rows.Add(SerializeWorldPresenter(context, entity, in state, ownerName, drawnTargets));
            });

            rows.Sort((a, b) => ((int)a["presenterEntityId"]!).CompareTo((int)b["presenterEntityId"]!));

            var presenters = new JsonArray();
            int returned = 0;
            for (int i = Math.Clamp(offset, 0, rows.Count); i < rows.Count && returned < limit; i++)
            {
                presenters.Add(rows[i]);
                returned++;
            }

            var response = new JsonObject
            {
                ["tick"] = context.Engine.GameSession.CurrentTick,
                ["plane"] = "world",
                ["totalMatched"] = rows.Count,
                ["offset"] = offset,
                ["returned"] = returned,
                ["dropped"] = Math.Max(0, rows.Count - offset - returned),
                ["presenters"] = presenters,
            };

            if (seatId != null)
            {
                response["knowledge"] = PresenterObservation.BuildKnowledgeSection(
                    context,
                    seatId,
                    target => IsTargetDrawn(world, drawnTargets, target),
                    target => PresenterObservation.OwnerName(world, target));
            }

            return response;
        }

        private static bool IsTargetDrawn(World world, HashSet<int> drawnTargets, Entity target)
        {
            if (drawnTargets.Contains(target.Id))
            {
                return true;
            }

            bool drawn = false;
            world.Query(
                new QueryDescription().WithAll<PresenterState>(),
                (Entity presenter, ref PresenterState state) =>
                {
                    if (!drawn && state.OwnerEntity == target && world.IsAlive(presenter))
                    {
                        drawn = world.Has<PresenterCullState>(presenter) && world.Get<PresenterCullState>(presenter).OwnerCullVisible;
                    }
                });
            return drawn;
        }

        private static JsonObject SerializeWorldPresenter(
            AgentToolContext context,
            Entity entity,
            in PresenterState state,
            string? ownerName,
            HashSet<int> drawnTargets)
        {
            World world = context.Engine.World;
            bool ownerAlive = world.IsAlive(state.OwnerEntity);

            var row = new JsonObject
            {
                ["presenterEntityId"] = entity.Id,
                ["defId"] = state.DefId,
                ["stableId"] = state.StableId,
                ["scopeId"] = state.ScopeId,
                ["anchorKind"] = state.AnchorKind.ToString(),
                ["owner"] = new JsonObject
                {
                    ["entityId"] = ownerAlive ? state.OwnerEntity.Id : 0,
                    ["name"] = ownerName,
                    ["alive"] = ownerAlive,
                },
            };

            if (ownerAlive)
            {
                if (world.Has<WorldPositionCm>(state.OwnerEntity))
                {
                    Fix64Vec2 logicCm = world.Get<WorldPositionCm>(state.OwnerEntity).Value;
                    row["logicCm"] = new JsonObject
                    {
                        ["x"] = MathF.Round(logicCm.X.ToFloat(), 1),
                        ["y"] = MathF.Round(logicCm.Y.ToFloat(), 1),
                    };
                }

                if (world.Has<VisualTransform>(state.OwnerEntity))
                {
                    row["visualPos"] = SerializeVector(world.Get<VisualTransform>(state.OwnerEntity).Position);
                }

                if (world.Has<PresentationOwnerHasPresenterPayload>(state.OwnerEntity))
                {
                    PresentationOwnerHasPresenterPayload payload = world.Get<PresentationOwnerHasPresenterPayload>(state.OwnerEntity);
                    row["ownerPayloadRoots"] = payload.RootCount;
                }
            }

            if (world.Has<PresenterWorldPosition>(entity))
            {
                row["presenterPos"] = SerializeVector(world.Get<PresenterWorldPosition>(entity).Value);
            }

            if (world.Has<PresenterWorldPlanePosition>(entity))
            {
                Vector2 planeCm = world.Get<PresenterWorldPlanePosition>(entity).ValueCm;
                row["presenterPlaneCm"] = new JsonObject
                {
                    ["x"] = MathF.Round(planeCm.X, 1),
                    ["y"] = MathF.Round(planeCm.Y, 1),
                };
            }

            if (world.Has<PresenterWorldRotation>(entity))
            {
                row["presenterYawDeg"] = MathF.Round(PresenterObservation.RadiansToDeg(MathF.Atan2(
                    world.Get<PresenterWorldRotation>(entity).Value.Y,
                    world.Get<PresenterWorldRotation>(entity).Value.W) * 2f), 1);
            }

            if (world.Has<PresenterWorldFacing>(entity))
            {
                PresenterWorldFacing facing = world.Get<PresenterWorldFacing>(entity);
                row["presenterFacingDeg"] = MathF.Round(PresenterObservation.RadiansToDeg(facing.AngleRad), 1);
            }

            if (world.Has<PresenterWorldScale>(entity))
            {
                row["presenterScale"] = SerializeVector(world.Get<PresenterWorldScale>(entity).Value);
            }

            row["markers"] = new JsonObject
            {
                ["transformSyncTick"] = world.Has<PerfTransformSyncTick>(entity),
                ["ownerPayloadSync"] = world.Has<PerfOwnerPayloadTransformSync>(entity),
                ["staticStable"] = world.Has<PerfStaticStableVisual>(entity),
                ["bootstrapPending"] = world.Has<PresenterBootstrapPending>(entity),
                ["emitDirty"] = world.Has<PresenterEmitDirty>(entity),
                ["transformSource"] = world.Has<PresenterTransformSource>(entity)
                    ? world.Get<PresenterTransformSource>(entity).Value.ToString()
                    : null,
            };

            if (world.Has<PresenterEmitCache>(entity))
            {
                PresenterEmitCache emitCache = world.Get<PresenterEmitCache>(entity);
                row["emit"] = new JsonObject
                {
                    ["staticDirty"] = emitCache.StaticDirty,
                    ["retainedDirty"] = emitCache.RetainedDirty,
                    ["cachedVersion"] = emitCache.CachedVersion,
                    ["stableVisualPresent"] = emitCache.StableVisualPresent,
                    ["lastEmitPos"] = SerializeVector(emitCache.LastEmitPosition),
                };
            }

            if (world.Has<PresenterCullState>(entity))
            {
                PresenterCullState cull = world.Get<PresenterCullState>(entity);
                row["cull"] = new JsonObject
                {
                    ["ownerCullVisible"] = cull.OwnerCullVisible,
                    ["lod"] = cull.LOD.ToString(),
                };
            }

            row["behaviorActiveMask"] = state.BehaviorActiveMask;
            bool? adapterDrawn = ResolveAdapterDrawn(context, entity, in state);
            row["adapterDrawn"] = adapterDrawn;

            if (adapterDrawn == true && ownerAlive)
            {
                drawnTargets.Add(state.OwnerEntity.Id);
            }

            return row;
        }

        internal static bool? ResolveAdapterDrawn(AgentToolContext context, Entity entity, in PresenterState state)
        {
            if (!context.TryGetService(CoreServiceKeys.PresenterDefinitionRegistry, out PresenterDefinitionRegistry? definitions) ||
                definitions == null ||
                !context.TryGetService(CoreServiceKeys.PresenterVisualStableIdTable, out PresenterVisualStableIdTable? visualStableIds) ||
                visualStableIds == null)
            {
                return null;
            }

            var stableIds = new List<int>();
            PresenterObservation.CollectExpectedVisualStableIds(
                context.Engine.World,
                in state,
                definitions,
                visualStableIds,
                stableIds);
            if (stableIds.Count == 0)
            {
                return null;
            }

            context.TryGetService(CoreServiceKeys.PresentationStableDrawCache, out StableDrawCache? stableDraws);
            context.TryGetService(CoreServiceKeys.PresentationPrimitiveDrawBuffer, out PrimitiveDrawBuffer? primitives);
            context.TryGetService(CoreServiceKeys.PresentationVisualSnapshotBuffer, out PrimitiveDrawBuffer? visualSnapshot);
            return PresenterObservation.AdapterBuffersContain(stableDraws, primitives, visualSnapshot, stableIds);
        }

        private static JsonObject QueryScreenPlane(
            AgentToolContext context,
            string? seatId,
            int offset,
            int limit)
        {
            if (!context.TryGetService(CoreServiceKeys.PanelHost, out Ludots.Core.UI.PanelHosting.PanelHost? panelHost) ||
                panelHost == null)
            {
                throw new AgentToolException(
                    AgentBridgeErrorCodes.ServiceUnavailable,
                    "PanelHost is not available in this runtime.");
            }

            UiScene? scene = null;
            if (context.Engine.GlobalContext.TryGetValue(CoreServiceKeys.UIRoot.Name, out object? rootObj) &&
                rootObj is UIRoot uiRoot)
            {
                scene = uiRoot.Scene;
            }

            var panels = new JsonArray();
            IReadOnlyList<PanelHostInstanceInfo> instances = panelHost.SnapshotInstances();
            int returned = 0;
            for (int i = Math.Clamp(offset, 0, instances.Count); i < instances.Count && returned < limit; i++)
            {
                PanelHostInstanceInfo info = instances[i];
                var row = new JsonObject
                {
                    ["templateId"] = info.TemplateId,
                    ["anchor"] = info.Anchor,
                    ["scopeEntityId"] = info.Scope != Entity.Null ? info.Scope.Id : 0,
                    ["zOrder"] = info.ZOrder,
                    ["revision"] = info.Revision,
                    ["skin"] = info.Skin,
                };

                UiRect? rect = FindPanelRect(scene, info.TemplateId);
                if (rect.HasValue)
                {
                    row["rect"] = new JsonObject
                    {
                        ["x"] = MathF.Round(rect.Value.X, 1),
                        ["y"] = MathF.Round(rect.Value.Y, 1),
                        ["w"] = MathF.Round(rect.Value.Width, 1),
                        ["h"] = MathF.Round(rect.Value.Height, 1),
                    };
                }
                else
                {
                    row["rect"] = null;
                    row["rectNote"] = "Panel not found in mounted UiScene (web-routed or not yet mounted).";
                }

                panels.Add(row);
                returned++;
            }

            var response = new JsonObject
            {
                ["tick"] = context.Engine.GameSession.CurrentTick,
                ["plane"] = "screen",
                ["totalMatched"] = instances.Count,
                ["offset"] = offset,
                ["returned"] = returned,
                ["dropped"] = Math.Max(0, instances.Count - offset - returned),
                ["panels"] = panels,
            };

            if (seatId != null)
            {
                World world = context.Engine.World;
                response["knowledge"] = PresenterObservation.BuildKnowledgeSection(
                    context,
                    seatId,
                    target => IsTargetDrawn(world, new HashSet<int>(), target),
                    target => PresenterObservation.OwnerName(world, target));
            }

            return response;
        }

        internal static UiRect? FindPanelRect(UiScene? scene, string templateId)
        {
            if (scene == null || scene.Root == null)
            {
                return null;
            }

            string classToken = templateId.Replace('.', '-').TrimStart('-');
            return FindPanelRectRecursive(scene.Root, classToken);
        }

        private static UiRect? FindPanelRectRecursive(UiNode node, string classToken)
        {
            if (node.ClassNames.Contains("panel") && node.ClassNames.Contains(classToken))
            {
                return node.LayoutRect;
            }

            foreach (UiNode child in node.Children)
            {
                UiRect? rect = FindPanelRectRecursive(child, classToken);
                if (rect.HasValue)
                {
                    return rect;
                }
            }

            return null;
        }

        private static JsonObject SerializeVector(Vector3 value)
        {
            return new JsonObject
            {
                ["x"] = MathF.Round(value.X, 3),
                ["y"] = MathF.Round(value.Y, 3),
                ["z"] = MathF.Round(value.Z, 3),
            };
        }
    }

    public sealed class PresentersDesyncTool : IAgentTool
    {
        public string Name => "ludots.presenters.desync";

        public string Description =>
            "Four-hop desync diagnosis for entity-anchored presenters: hop1 logic->visual (WorldPositionCm interpolation segment), " +
            "hop2 visual->presenter (horizontal XZ delta; grounding only offsets Y so vertical delta is reported, not judged), " +
            "hop3 presenter->emit (position moved while staticStable && staticDirty==0), hop4 emit->adapter (expected stableId missing from " +
            "StableDrawCache/PrimitiveDrawBuffer). Single-frame semantics: each verdict compares the current frame only; there is no engine-side " +
            "tick history, so staleTicks is accepted and echoed but every threshold is evaluated on the spot. " +
            "Params: {epsilonCm?=5, staleTicks?=3, seatId?=string, offset?=0, limit?=100}. " +
            "Returns broken rows with per-hop details plus summary counts; seatId adds the knowledge shouldSee x actualDrawn diff.";

        public JsonObject? InputSchema => new JsonObject
        {
            ["type"] = "object",
            ["properties"] = new JsonObject
            {
                ["epsilonCm"] = new JsonObject { ["type"] = "number", ["minimum"] = 0, ["default"] = 5 },
                ["staleTicks"] = new JsonObject { ["type"] = "integer", ["minimum"] = 1, ["default"] = 3 },
                ["seatId"] = new JsonObject { ["type"] = "string" },
                ["offset"] = new JsonObject { ["type"] = "integer", ["minimum"] = 0, ["default"] = 0 },
                ["limit"] = new JsonObject { ["type"] = "integer", ["minimum"] = 1, ["maximum"] = 1000, ["default"] = 100 },
            },
        };

        public JsonNode? Execute(JsonObject? args, AgentToolContext context)
        {
            float epsilonCm = OptionalFloat(args, "epsilonCm", 5f);
            if (epsilonCm < 0f)
            {
                throw new AgentToolException(
                    AgentBridgeErrorCodes.InvalidParams,
                    $"Parameter 'epsilonCm' must be >= 0 (got {epsilonCm}).");
            }

            int staleTicks = AgentToolContext.OptionalInt(args, "staleTicks", 3);
            if (staleTicks < 1)
            {
                throw new AgentToolException(
                    AgentBridgeErrorCodes.InvalidParams,
                    $"Parameter 'staleTicks' must be >= 1 (got {staleTicks}).");
            }

            string? seatId = AgentToolContext.OptionalString(args, "seatId");
            int offset = AgentToolContext.OptionalInt(args, "offset", 0);
            int limit = Math.Clamp(AgentToolContext.OptionalInt(args, "limit", 100), 1, 1000);

            World world = context.Engine.World;
            context.TryGetService(CoreServiceKeys.PresenterDefinitionRegistry, out PresenterDefinitionRegistry? definitions);
            context.TryGetService(CoreServiceKeys.PresenterVisualStableIdTable, out PresenterVisualStableIdTable? visualStableIds);
            context.TryGetService(CoreServiceKeys.PresentationStableDrawCache, out StableDrawCache? stableDraws);
            context.TryGetService(CoreServiceKeys.PresentationPrimitiveDrawBuffer, out PrimitiveDrawBuffer? primitives);
            context.TryGetService(CoreServiceKeys.PresentationVisualSnapshotBuffer, out PrimitiveDrawBuffer? visualSnapshot);

            var rows = new List<JsonObject>();
            int checkedPresenters = 0;
            int hop1Broken = 0, hop2Broken = 0, hop3Broken = 0, hop4Broken = 0;
            var drawnTargets = new HashSet<int>();

            var query = new QueryDescription().WithAll<PresenterState>();
            world.Query(in query, (Entity entity, ref PresenterState state) =>
            {
                if (state.AnchorKind != PresentationAnchorKind.Entity)
                {
                    return;
                }

                checkedPresenters++;
                var brokenHops = new JsonArray();
                var deltas = new JsonObject();
                bool ownerTransformSynced =
                    world.Has<PresenterTransformSource>(entity) &&
                    world.Get<PresenterTransformSource>(entity).Value == TransformSource.EntityTransform;
                bool ownerAlive = world.IsAlive(state.OwnerEntity);

                if (ownerTransformSynced && ownerAlive &&
                    world.Has<WorldPositionCm>(state.OwnerEntity) &&
                    world.Has<PreviousWorldPositionCm>(state.OwnerEntity) &&
                    world.Has<VisualTransform>(state.OwnerEntity) &&
                    world.Has<PresenterWorldPosition>(entity))
                {
                    Fix64Vec2 currentCm = world.Get<WorldPositionCm>(state.OwnerEntity).Value;
                    Fix64Vec2 previousCm = world.Get<PreviousWorldPositionCm>(state.OwnerEntity).Value;
                    Vector3 visualPos = world.Get<VisualTransform>(state.OwnerEntity).Position;
                    Vector3 presenterPos = world.Get<PresenterWorldPosition>(entity).Value;

                    float hop1Cm = PresenterObservation.DistanceToInterpolationSegmentCm(visualPos, previousCm, currentCm);
                    deltas["hop1Cm"] = MathF.Round(hop1Cm, 1);
                    if (hop1Cm > epsilonCm)
                    {
                        hop1Broken++;
                        brokenHops.Add(new JsonObject
                        {
                            ["hop"] = 1,
                            ["detail"] = "VisualTransform is outside the [Previous,Current] interpolation segment (logic->visual).",
                            ["logicCm"] = new JsonObject
                            {
                                ["x"] = MathF.Round(currentCm.X.ToFloat(), 1),
                                ["y"] = MathF.Round(currentCm.Y.ToFloat(), 1),
                            },
                            ["visualPos"] = PresenterObservation.SerializeVisualPos(visualPos),
                            ["deltaCm"] = MathF.Round(hop1Cm, 1),
                        });
                    }

                    float hop2Cm = PresenterObservation.HorizontalDistanceCm(presenterPos, visualPos);
                    float deltaYCm = (presenterPos.Y - visualPos.Y) * WorldUnits.CmPerMeter;
                    deltas["hop2Cm"] = MathF.Round(hop2Cm, 1);
                    deltas["hop2DeltaYCm"] = MathF.Round(deltaYCm, 1);
                    if (hop2Cm > epsilonCm)
                    {
                        hop2Broken++;
                        brokenHops.Add(new JsonObject
                        {
                            ["hop"] = 2,
                            ["detail"] = "PresenterWorldPosition horizontal delta vs owner VisualTransform exceeds epsilon (visual->presenter; the 82f03e54fb frozen-presenter signature).",
                            ["visualPos"] = PresenterObservation.SerializeVisualPos(visualPos),
                            ["presenterPos"] = PresenterObservation.SerializeVisualPos(presenterPos),
                            ["deltaCm"] = MathF.Round(hop2Cm, 1),
                            ["deltaYCm"] = MathF.Round(deltaYCm, 1),
                        });
                    }
                }

                bool staticStable = world.Has<PerfStaticStableVisual>(entity);
                bool bootstrapPending = world.Has<PresenterBootstrapPending>(entity);
                if (world.Has<PresenterEmitCache>(entity) && world.Has<PresenterWorldPosition>(entity) && !bootstrapPending)
                {
                    PresenterEmitCache emitCache = world.Get<PresenterEmitCache>(entity);
                    Vector3 presenterPos = world.Get<PresenterWorldPosition>(entity).Value;
                    float emitDeltaCm = PresenterObservation.HorizontalDistanceCm(presenterPos, emitCache.LastEmitPosition);
                    deltas["hop3EmitDeltaCm"] = MathF.Round(emitDeltaCm, 1);
                    if (staticStable && emitCache.StaticDirty == 0 && emitDeltaCm > epsilonCm)
                    {
                        hop3Broken++;
                        brokenHops.Add(new JsonObject
                        {
                            ["hop"] = 3,
                            ["detail"] = "Presenter moved but staticStable && staticDirty==0: emit is starved (presenter->emit).",
                            ["presenterPos"] = PresenterObservation.SerializeVisualPos(presenterPos),
                            ["lastEmitPos"] = PresenterObservation.SerializeVisualPos(emitCache.LastEmitPosition),
                            ["deltaCm"] = MathF.Round(emitDeltaCm, 1),
                        });
                    }

                    if (world.Has<PresenterCullState>(entity) &&
                        world.Get<PresenterCullState>(entity).OwnerCullVisible &&
                        world.Get<PresenterCullState>(entity).LOD != LODLevel.Culled &&
                        emitDeltaCm <= epsilonCm &&
                        definitions != null && visualStableIds != null)
                    {
                        var stableIds = new List<int>();
                        PresenterObservation.CollectExpectedVisualStableIds(world, in state, definitions, visualStableIds, stableIds);
                        deltas["hop4ExpectedVisuals"] = stableIds.Count;
                        if (stableIds.Count > 0 &&
                            !PresenterObservation.AdapterBuffersContain(stableDraws, primitives, visualSnapshot, stableIds))
                        {
                            hop4Broken++;
                            var expectedIds = new JsonArray();
                            foreach (int stableId in stableIds)
                            {
                                expectedIds.Add(stableId);
                            }

                            brokenHops.Add(new JsonObject
                            {
                                ["hop"] = 4,
                                ["detail"] = "Emit claims current visuals but none of the presenter's visual stableIds are present in StableDrawCache/PrimitiveDrawBuffer (emit->adapter).",
                                ["expectedStableIds"] = expectedIds,
                                ["stableDrawCount"] = stableDraws?.Count ?? 0,
                                ["primitiveCount"] = primitives?.Count ?? 0,
                            });
                        }
                        else if (stableIds.Count > 0 && ownerAlive)
                        {
                            drawnTargets.Add(state.OwnerEntity.Id);
                        }
                    }
                }

                if (brokenHops.Count > 0)
                {
                    rows.Add(new JsonObject
                    {
                        ["presenterEntityId"] = entity.Id,
                        ["defId"] = state.DefId,
                        ["stableId"] = state.StableId,
                        ["owner"] = new JsonObject
                        {
                            ["entityId"] = ownerAlive ? state.OwnerEntity.Id : 0,
                            ["name"] = PresenterObservation.OwnerName(world, state.OwnerEntity),
                        },
                        ["markers"] = new JsonObject
                        {
                            ["transformSyncTick"] = world.Has<PerfTransformSyncTick>(entity),
                            ["ownerPayloadSync"] = world.Has<PerfOwnerPayloadTransformSync>(entity),
                            ["staticStable"] = staticStable,
                            ["bootstrapPending"] = bootstrapPending,
                        },
                        ["brokenHops"] = brokenHops,
                        ["deltas"] = deltas,
                    });
                }
            });

            rows.Sort((a, b) => ((int)a["presenterEntityId"]!).CompareTo((int)b["presenterEntityId"]!));

            var brokenRows = new JsonArray();
            int returned = 0;
            for (int i = Math.Clamp(offset, 0, rows.Count); i < rows.Count && returned < limit; i++)
            {
                brokenRows.Add(rows[i]);
                returned++;
            }

            var response = new JsonObject
            {
                ["tick"] = context.Engine.GameSession.CurrentTick,
                ["epsilonCm"] = epsilonCm,
                ["staleTicks"] = staleTicks,
                ["staleTicksSemantics"] = "single-frame: verdicts compare the current frame only (no engine-side tick history)",
                ["checked"] = checkedPresenters,
                ["summary"] = new JsonObject
                {
                    ["brokenPresenters"] = rows.Count,
                    ["hop1LogicToVisual"] = hop1Broken,
                    ["hop2VisualToPresenter"] = hop2Broken,
                    ["hop3PresenterToEmit"] = hop3Broken,
                    ["hop4EmitToAdapter"] = hop4Broken,
                    ["healthyPresenters"] = checkedPresenters - rows.Count,
                },
                ["rows"] = brokenRows,
                ["returned"] = returned,
                ["dropped"] = Math.Max(0, rows.Count - offset - returned),
            };

            if (seatId != null)
            {
                response["knowledge"] = PresenterObservation.BuildKnowledgeSection(
                    context,
                    seatId,
                    target =>
                    {
                        if (drawnTargets.Contains(target.Id))
                        {
                            return true;
                        }

                        bool drawn = false;
                        world.Query(
                            new QueryDescription().WithAll<PresenterState>(),
                            (Entity presenter, ref PresenterState state) =>
                            {
                                if (!drawn && state.OwnerEntity == target && world.IsAlive(presenter))
                                {
                                    drawn = world.Has<PresenterCullState>(presenter) && world.Get<PresenterCullState>(presenter).OwnerCullVisible;
                                }
                            });
                        return drawn;
                    },
                    target => PresenterObservation.OwnerName(world, target));
            }

            return response;
        }

        private static float OptionalFloat(JsonObject? args, string name, float defaultValue)
        {
            if (args == null)
            {
                return defaultValue;
            }

            JsonNode? node = args[name];
            return node is JsonValue value && value.TryGetValue(out double d) ? (float)d : defaultValue;
        }
    }

    public sealed class PresentersScreenTool : IAgentTool
    {
        public string Name => "ludots.presenters.screen";

        public string Description =>
            "Camera-projected presenter manifest for one seat: world-plane presenters projected through the same ScreenProjector as " +
            "ludots.entities.query (screenRect comparable 1:1), plus screen-plane PanelHost panels with rects. " +
            "Params: {seatId?=seat.0, includeOffscreen?=false, offset?=0, limit?=100}. " +
            "seatId resolves the possessed viewer for the knowledge shouldSee x actualDrawn join; projection itself uses the runtime ScreenProjector service.";

        public JsonObject? InputSchema => new JsonObject
        {
            ["type"] = "object",
            ["properties"] = new JsonObject
            {
                ["seatId"] = new JsonObject { ["type"] = "string", ["default"] = "seat.0" },
                ["includeOffscreen"] = new JsonObject { ["type"] = "boolean", ["default"] = false },
                ["offset"] = new JsonObject { ["type"] = "integer", ["minimum"] = 0, ["default"] = 0 },
                ["limit"] = new JsonObject { ["type"] = "integer", ["minimum"] = 1, ["maximum"] = 1000, ["default"] = 100 },
            },
        };

        public JsonNode? Execute(JsonObject? args, AgentToolContext context)
        {
            string seatId = AgentToolContext.OptionalString(args, "seatId") ?? "seat.0";
            bool includeOffscreen = AgentToolContext.OptionalBool(args, "includeOffscreen", false);
            int offset = AgentToolContext.OptionalInt(args, "offset", 0);
            int limit = Math.Clamp(AgentToolContext.OptionalInt(args, "limit", 100), 1, 1000);

            var projector = context.RequireService(CoreServiceKeys.ScreenProjector);
            var view = context.RequireService(CoreServiceKeys.ViewController);
            Vector2 resolution = view.Resolution;
            float viewportArea = Math.Max(1f, resolution.X * resolution.Y);

            World world = context.Engine.World;
            var rows = new List<(int Id, JsonObject Row)>();
            var screenBounds = new ScreenRect(0, 0, resolution.X, resolution.Y);
            var drawnTargets = new HashSet<int>();

            var query = new QueryDescription().WithAll<PresenterState>();
            world.Query(in query, (Entity entity, ref PresenterState state) =>
            {
                if (state.AnchorKind != PresentationAnchorKind.Entity || !world.IsAlive(state.OwnerEntity))
                {
                    return;
                }

                if (!SpatialBoundsUtility.TryProjectScreenBounds(world, state.OwnerEntity, projector, out ScreenRect rect))
                {
                    if (includeOffscreen)
                    {
                        var offRow = BuildPresenterRow(context, world, entity, in state, null, viewportArea);
                        rows.Add((entity.Id, offRow));
                    }

                    return;
                }

                bool onScreen = rect.Intersects(in screenBounds);
                if (!includeOffscreen && !onScreen)
                {
                    return;
                }

                rows.Add((entity.Id, BuildPresenterRow(context, world, entity, in state, rect, viewportArea)));
            });

            rows.Sort((a, b) => a.Id.CompareTo(b.Id));

            var presenters = new JsonArray();
            int returned = 0;
            for (int i = Math.Clamp(offset, 0, rows.Count); i < rows.Count && returned < limit; i++)
            {
                presenters.Add(rows[i].Row);
                returned++;
            }

            var response = new JsonObject
            {
                ["tick"] = context.Engine.GameSession.CurrentTick,
                ["seatId"] = seatId,
                ["viewport"] = new JsonObject { ["width"] = resolution.X, ["height"] = resolution.Y },
                ["totalMatched"] = rows.Count,
                ["offset"] = offset,
                ["returned"] = returned,
                ["dropped"] = Math.Max(0, rows.Count - offset - returned),
                ["presenters"] = presenters,
                ["panels"] = SerializePanels(context),
            };

            response["knowledge"] = PresenterObservation.BuildKnowledgeSection(
                context,
                seatId,
                target =>
                {
                    if (drawnTargets.Contains(target.Id))
                    {
                        return true;
                    }

                    bool drawn = false;
                    world.Query(
                        new QueryDescription().WithAll<PresenterState>(),
                        (Entity presenter, ref PresenterState state) =>
                        {
                            if (!drawn && state.OwnerEntity == target && world.IsAlive(presenter))
                            {
                                drawn = world.Has<PresenterCullState>(presenter) && world.Get<PresenterCullState>(presenter).OwnerCullVisible;
                            }
                        });
                    return drawn;
                },
                target => PresenterObservation.OwnerName(world, target));

            return response;
        }

        private static JsonObject BuildPresenterRow(
            AgentToolContext context,
            World world,
            Entity entity,
            in PresenterState state,
            ScreenRect? rect,
            float viewportArea)
        {
            var row = new JsonObject
            {
                ["presenterEntityId"] = entity.Id,
                ["defId"] = state.DefId,
                ["owner"] = new JsonObject
                {
                    ["entityId"] = state.OwnerEntity.Id,
                    ["name"] = PresenterObservation.OwnerName(world, state.OwnerEntity),
                },
            };

            if (world.Has<PresenterWorldPosition>(entity))
            {
                row["presenterPos"] = PresenterObservation.SerializeVisualPos(world.Get<PresenterWorldPosition>(entity).Value);
            }

            if (world.Has<PresenterCullState>(entity))
            {
                PresenterCullState cull = world.Get<PresenterCullState>(entity);
                row["ownerCullVisible"] = cull.OwnerCullVisible;
                row["lod"] = cull.LOD.ToString();
                if (cull.OwnerCullVisible)
                {
                    drawnTargetsLocal.Add(state.OwnerEntity.Id);
                }
            }

            bool? adapterDrawn = PresentersQueryTool.ResolveAdapterDrawn(context, entity, in state);
            row["adapterDrawn"] = adapterDrawn;

            if (rect.HasValue)
            {
                float width = MathF.Max(0f, rect.Value.MaxX - rect.Value.MinX);
                float height = MathF.Max(0f, rect.Value.MaxY - rect.Value.MinY);
                row["screenRect"] = new JsonObject
                {
                    ["x"] = MathF.Round(rect.Value.MinX, 1),
                    ["y"] = MathF.Round(rect.Value.MinY, 1),
                    ["w"] = MathF.Round(width, 1),
                    ["h"] = MathF.Round(height, 1),
                };
                row["screenCoverage"] = MathF.Round(width * height / viewportArea, 5);
                row["onScreen"] = true;
            }
            else
            {
                row["onScreen"] = false;
            }

            return row;
        }

        [ThreadStatic]
        private static HashSet<int>? drawnTargetsLocal;

        private static JsonArray SerializePanels(AgentToolContext context)
        {
            var panels = new JsonArray();
            if (!context.TryGetService(CoreServiceKeys.PanelHost, out Ludots.Core.UI.PanelHosting.PanelHost? panelHost) ||
                panelHost == null)
            {
                return panels;
            }

            UiScene? scene = null;
            if (context.Engine.GlobalContext.TryGetValue(CoreServiceKeys.UIRoot.Name, out object? rootObj) &&
                rootObj is UIRoot uiRoot)
            {
                scene = uiRoot.Scene;
            }

            foreach (PanelHostInstanceInfo info in panelHost.SnapshotInstances())
            {
                var row = new JsonObject
                {
                    ["templateId"] = info.TemplateId,
                    ["anchor"] = info.Anchor,
                    ["zOrder"] = info.ZOrder,
                };

                UiRect? rect = PresentersQueryTool.FindPanelRect(scene, info.TemplateId);
                row["rect"] = rect.HasValue
                    ? new JsonObject
                    {
                        ["x"] = MathF.Round(rect.Value.X, 1),
                        ["y"] = MathF.Round(rect.Value.Y, 1),
                        ["w"] = MathF.Round(rect.Value.Width, 1),
                        ["h"] = MathF.Round(rect.Value.Height, 1),
                    }
                    : null;

                panels.Add(row);
            }

            return panels;
        }
    }
}
