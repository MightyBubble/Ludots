using System;
using Arch.Core;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.Relationships;
using Ludots.Core.Gameplay.Teams;
using Ludots.Core.Map;
using Ludots.Core.Mathematics;
using Ludots.Core.Navigation.GraphQuery;
using Ludots.Core.Spatial;
using Ludots.Platform.Abstractions;

namespace Ludots.Core.NodeLibraries.GASGraph
{
    public readonly struct EffectArgs
    {
        public readonly byte FloatCount;
        public readonly float F0;
        public readonly float F1;

        public EffectArgs(byte floatCount, float f0, float f1)
        {
            FloatCount = floatCount;
            F0 = f0;
            F1 = f1;
        }

        public static EffectArgs None => default;
    }

    public interface IDerivedAttributeGraphRuntimeApi : IGraphRuntimeApi
    {
        const string MissingContractError = "GAS.GRAPH.ERR.MissingDerivedAttributeWriteContract";
        const string SideEffectForbiddenError = "GAS.GRAPH.ERR.DerivedAttributeSideEffectForbidden";

        void BeginDerivedAttributeWrites(Entity entity, in AttributeBuffer attributes);
        void EndDerivedAttributeWrites(Entity entity, ref AttributeBuffer attributes, bool commit);
    }

    /// <summary>
    /// Protocol constants for <see cref="IGraphRuntimeApi.GetRelationship"/>.
    /// Decouples Graph VM from concrete TeamRelationship enum.
    /// </summary>
    public static class GraphRelationship
    {
        public const int Neutral = 0;
        public const int Friendly = 1;
        public const int Hostile = 2;
    }

    public interface IGraphRuntimeApi
    {
        bool TryGetGridPos(Entity entity, out IntVector2 gridPos);
        bool HasTag(Entity entity, int tagId);
        bool TryGetAttributeCurrent(Entity entity, int attributeId, out float value);
        SpatialQueryResult QueryRadius(IntVector2 centerCm, float radiusCm, Span<Entity> buffer);
        SpatialQueryResult QueryCone(IntVector2 originCm, int directionDeg, int halfAngleDeg, float rangeCm, Span<Entity> buffer);
        SpatialQueryResult QueryRectangle(IntVector2 centerCm, int halfWidthCm, int halfHeightCm, int rotationDeg, Span<Entity> buffer);
        SpatialQueryResult QueryLine(IntVector2 originCm, int directionDeg, int lengthCm, int halfWidthCm, Span<Entity> buffer);

        int ResolveTableRow(int tableId, int key)
        {
            throw new InvalidOperationException("GAS.GRAPH.ERR.LookupTableUnavailable");
        }

        int WeightedPick(int distributionKeyId, int modulationPermille)
        {
            throw new InvalidOperationException("GAS.GRAPH.ERR.RngPickUnavailable");
        }

        int TableReadInt(int fieldId, int rowHandle)
        {
            throw new InvalidOperationException("GAS.GRAPH.ERR.LookupTableUnavailable");
        }

        float TableReadFloat(int fieldId, int rowHandle)
        {
            throw new InvalidOperationException("GAS.GRAPH.ERR.LookupTableUnavailable");
        }

        void ShowPanel(int panelTypeId)
        {
            throw new InvalidOperationException("GAS.GRAPH.ERR.PanelActivationUnavailable");
        }

        void HidePanel(int panelTypeId)
        {
            throw new InvalidOperationException("GAS.GRAPH.ERR.PanelActivationUnavailable");
        }

        /// <summary>
        /// Overrides a panel type's audience with one seat (hotseat turn handoff), or
        /// clears the override when seatKeyId is 0 — the template's declared audience
        /// rules again. Fail-closed on key ids that do not resolve to registered names.
        /// </summary>
        void SetPanelAudience(int panelTypeId, int seatKeyId)
        {
            throw new InvalidOperationException("GAS.GRAPH.ERR.PanelActivationUnavailable");
        }

        /// <summary>Sets an entity's world position in centimeters (int, matches LoadTargetPosX/Y).</summary>
        void SetWorldPosition(Entity target, int xCm, int yCm);

        /// <summary>
        /// Sets an entity's interaction mode: writes the sparse InteractionMode component,
        /// or removes it when the mode is the reserved normal default. Fail-closed on dead targets
        /// and mode key ids that do not resolve to an installed interaction mode.
        /// </summary>
        void SetInteractionMode(Entity target, int modeKeyId)
        {
            throw new InvalidOperationException("GAS.GRAPH.ERR.InteractionModeMapUnavailable");
        }

        /// <summary>
        /// Activates an interaction context instance (#1398 S2b) on the subject: context and
        /// parent are ConfigKeyRegistry ids (parent 0 = no parent constraint). Idempotent-failure
        /// on an already-active context; fail-closed on dead subjects, unknown key ids, and
        /// declared parents that are not active. Default rejects — the engine binds a context
        /// instance runtime to serve it.
        /// </summary>
        void ActivateContext(Entity subject, int contextKeyId, int parentContextKeyId)
        {
            throw new InvalidOperationException("GAS.GRAPH.ERR.ContextInstanceRuntimeUnavailable");
        }

        /// <summary>
        /// Deactivates an interaction context instance (and its descendants) on the subject;
        /// the instance's presenter scope is destroyed through the presenter command pipeline.
        /// Fail-closed when the context is not mounted as an instance.
        /// </summary>
        void DeactivateContext(Entity subject, int contextKeyId)
        {
            throw new InvalidOperationException("GAS.GRAPH.ERR.ContextInstanceRuntimeUnavailable");
        }

        /// <summary>
        /// Fires the collection pass-through event (#1398 S2b gap 9): the authored event key
        /// dispatches map-scoped carrying the final entity set plus the set semantics
        /// (opKind 0=replace, 1=add, 2=subtract) and the target collection key id — all under
        /// the reserved MapTrigger.Collection* payload keys. The event key must be a declared
        /// custom event; downstream EventKeyedCollectionWriter instances receive by key.
        /// Default rejects — the engine binds the trigger bridge to serve it.
        /// </summary>
        void DispatchCollectionEvent(int packedKeyIds, int opKind, Entity source, MapId mapId, Span<Entity> entities, int count)
        {
            throw new InvalidOperationException("GAS.GRAPH.ERR.CollectionEventBridgeUnavailable");
        }

        /// <summary>Enqueues a template entity spawn (runtime spawn queue; explicit position optional).</summary>
        void SpawnTemplate(int templateKeyId, Entity source, float xCm, float yCm, bool hasPosition);

        void CreatePanel(int templateKeyId, int anchorKeyId, Entity scope)
        {
            throw new InvalidOperationException("GAS.GRAPH.ERR.PanelHostUnavailable");
        }

        void CreatePanel(int templateKeyId, int anchorKeyId, Entity scope, byte skinId, float zOrder)
        {
            throw new InvalidOperationException("GAS.GRAPH.ERR.PanelHostUnavailable");
        }

        void DestroyPanel(int templateKeyId, Entity scope)
        {
            throw new InvalidOperationException("GAS.GRAPH.ERR.PanelHostUnavailable");
        }

        void PushPresentationText(GraphPresentationTextSurface surface, ReadOnlySpan<char> text)
        {
            throw new InvalidOperationException(GraphPresentationTextSink.UnavailableError);
        }

        /// <summary>Start a DialogueRuntime session by dialogue definition id (config key).</summary>
        void StartDialogue(int dialogueKeyId)
        {
            throw new InvalidOperationException("GAS.GRAPH.ERR.DialogueRuntimeUnavailable");
        }

        /// <summary>
        /// Resolves a patched PresentationTextCatalog token id to default-locale template characters.
        /// Zero-arg tokens only; argCount&gt;0 fails closed until FormatTextKey lands.
        /// </summary>
        ReadOnlySpan<char> ResolvePresentationTextKey(int tokenId)
        {
            throw new InvalidOperationException(
                "GAS.GRAPH.ERR.PresentationTextCatalogUnavailable");
        }

        // ── Map-scoped variables ──

        int ReadMapVarInt(int varKeyId, MapId mapId)
        {
            throw new InvalidOperationException("GAS.GRAPH.ERR.MapVariableStoreUnavailable");
        }

        float ReadMapVarFloat(int varKeyId, MapId mapId)
        {
            throw new InvalidOperationException("GAS.GRAPH.ERR.MapVariableStoreUnavailable");
        }

        void WriteMapVarInt(int varKeyId, MapId mapId, int value)
        {
            throw new InvalidOperationException("GAS.GRAPH.ERR.MapVariableStoreUnavailable");
        }

        void WriteMapVarFloat(int varKeyId, MapId mapId, float value)
        {
            throw new InvalidOperationException("GAS.GRAPH.ERR.MapVariableStoreUnavailable");
        }

        /// <summary>
        /// Resolves a placed entity registered under the instance key id on the mounting
        /// map's MapLoadEntityIndex. False means unregistered (the LoadPlacedEntity op then
        /// writes Entity.Null); liveness stays the caller's contract.
        /// </summary>
        bool TryGetPlacedEntity(int instanceKeyId, MapId mapId, out Entity entity)
        {
            throw new InvalidOperationException("GAS.GRAPH.ERR.PlacedIndexUnavailable");
        }

        /// <summary>
        /// True when Imm region id is present in the mounting map's Regions catalog
        /// (LoadPlacedRegion). Regions never enter MapLoadEntityIndex.
        /// </summary>
        bool TryHasPlacedRegion(int regionKeyId, MapId mapId)
        {
            throw new InvalidOperationException("GAS.GRAPH.ERR.RegionCatalogUnavailable");
        }
        int CollectMapEntities(Span<Entity> buffer)
        {
            throw new InvalidOperationException("Graph entity query runtime is not available.");
        }

        int CopyEntityCollection(Entity owner, int collectionKeyId, Span<Entity> buffer)
        {
            throw new InvalidOperationException("Graph entity collection runtime is not available.");
        }

        /// <summary>
        /// Enumerate alive effect-instance entities from the owner's <c>ActiveEffectContainer</c>
        /// into <paramref name="buffer"/>. Truncates at buffer capacity (same contract as map collect).
        /// </summary>
        int CollectActiveEffects(Entity owner, Span<Entity> buffer)
        {
            throw new InvalidOperationException("Graph active-effect query runtime is not available.");
        }

        int CollectEffectTemplateIds(Span<int> buffer)
        {
            throw new InvalidOperationException("Graph effect-template query runtime is not available.");
        }

        int CollectAbilitySlots(Entity owner, Span<int> buffer)
        {
            throw new InvalidOperationException("Graph ability-slot query runtime is not available.");
        }

        int CollectInventoryItems(Entity owner, Span<Entity> buffer)
        {
            throw new InvalidOperationException("Graph inventory-item query runtime is not available.");
        }

        int CollectItemDefinitionIds(Span<int> buffer)
        {
            throw new InvalidOperationException("Graph item-definition query runtime is not available.");
        }

        int CollectPresentTags(Entity owner, Span<int> buffer)
        {
            throw new InvalidOperationException("Graph present-tag query runtime is not available.");
        }

        int CollectActiveTasks(Entity owner, Span<Entity> buffer)
        {
            throw new InvalidOperationException("Graph active-task query runtime is not available.");
        }

        int CollectActiveActivities(Entity owner, Span<Entity> buffer)
        {
            throw new InvalidOperationException("Graph active-activity query runtime is not available.");
        }

        int CollectProgressionNodes(Entity owner, Span<int> buffer)
        {
            throw new InvalidOperationException("Graph progression-node query runtime is not available.");
        }

        int CollectActiveDialogueChoices(Span<int> buffer)
        {
            throw new InvalidOperationException("Graph dialogue-choice query runtime is not available.");
        }

        int CollectAbilityHolders(int abilityId, ReadOnlySpan<Entity> candidates, Span<Entity> buffer)
        {
            throw new InvalidOperationException("Graph ability-holder query runtime is not available.");
        }

        int FilterTeam(Span<Entity> entities, int count, int teamId)
        {
            throw new InvalidOperationException("Graph entity query runtime is not available.");
        }

        int FilterTeamRelationship(Span<Entity> entities, int count, Entity reference, RelationshipFilter filter)
        {
            throw new InvalidOperationException("Graph entity query runtime is not available.");
        }

        int FilterTemplate(Span<Entity> entities, int count, int templateKeyId)
        {
            throw new InvalidOperationException("Graph entity query runtime is not available.");
        }

        int FilterAttributeRange(Span<Entity> entities, int count, int attributeId, float minInclusive, float maxInclusive)
        {
            throw new InvalidOperationException("Graph entity query runtime is not available.");
        }

        int FilterTagAny(Span<Entity> entities, int count, int tagId)
        {
            throw new InvalidOperationException("Graph entity query runtime is not available.");
        }

        int FilterTagNone(Span<Entity> entities, int count, int tagId)
        {
            throw new InvalidOperationException("Graph entity query runtime is not available.");
        }

        int FilterLayer(Span<Entity> entities, int count, uint requiredMask)
        {
            throw new InvalidOperationException("Graph entity query runtime is not available.");
        }

        int FilterNotEntity(Span<Entity> entities, int count, Entity exclude)
        {
            throw new InvalidOperationException("Graph entity query runtime is not available.");
        }

        int SortStableDedup(Span<Entity> entities, int count)
        {
            throw new InvalidOperationException("Graph entity query runtime is not available.");
        }

        int Limit(Span<Entity> entities, int count, int limit)
        {
            throw new InvalidOperationException("Graph entity query runtime is not available.");
        }

        void SortByAttribute(Span<Entity> entities, int count, int attributeId, bool descending)
        {
            throw new InvalidOperationException("Graph entity query runtime is not available.");
        }

        float SumAttribute(ReadOnlySpan<Entity> entities, int attributeId)
        {
            throw new InvalidOperationException("Graph entity query runtime is not available.");
        }

        float AverageAttribute(ReadOnlySpan<Entity> entities, int attributeId)
        {
            throw new InvalidOperationException("Graph entity query runtime is not available.");
        }

        float MaxAttribute(ReadOnlySpan<Entity> entities, int attributeId)
        {
            throw new InvalidOperationException("Graph entity query runtime is not available.");
        }

        float MinAttribute(ReadOnlySpan<Entity> entities, int attributeId)
        {
            throw new InvalidOperationException("Graph entity query runtime is not available.");
        }

        bool TryMaxEntityByAttribute(ReadOnlySpan<Entity> entities, int attributeId, out Entity entity, out float value)
        {
            throw new InvalidOperationException("Graph entity query runtime is not available.");
        }

        bool TryMinEntityByAttribute(ReadOnlySpan<Entity> entities, int attributeId, out Entity entity, out float value)
        {
            throw new InvalidOperationException("Graph entity query runtime is not available.");
        }

        bool TryMinEntityByWorldDistanceCm(ReadOnlySpan<Entity> entities, WorldCmInt2 centerCm, out Entity entity, out long distanceSquaredCm)
        {
            throw new InvalidOperationException("Graph entity query runtime is not available.");
        }

        // ── Hex spatial queries ──
        SpatialQueryResult QueryHexRange(IntVector2 centerCm, int hexRadius, Span<Entity> buffer);
        SpatialQueryResult QueryHexRing(IntVector2 centerCm, int hexRadius, Span<Entity> buffer);
        SpatialQueryResult QueryHexNeighbors(IntVector2 centerCm, Span<Entity> buffer);

        int GetTeamId(Entity entity);
        /// <summary>Get the EntityLayer.Category bits for an entity. Returns 0 if no EntityLayer.</summary>
        uint GetEntityLayerCategory(Entity entity);
        /// <summary>
        /// Get relationship between two teams.
        /// Returns one of the <see cref="GraphRelationship"/> constants.
        /// </summary>
        int GetRelationship(int teamA, int teamB);
        void EnsureRelationshipLink(Entity source, Entity target, int typeId)
        {
            throw new InvalidOperationException("Graph relationship runtime is not available.");
        }

        void RemoveRelationshipLink(Entity source, Entity target, int typeId)
        {
            throw new InvalidOperationException("Graph relationship runtime is not available.");
        }

        short SetRelationshipMetric(Entity source, Entity target, int metricId, int value, int reasonId, int typeId)
        {
            throw new InvalidOperationException("Graph relationship runtime is not available.");
        }

        short AddRelationshipMetric(Entity source, Entity target, int metricId, int delta, int reasonId, int typeId)
        {
            throw new InvalidOperationException("Graph relationship runtime is not available.");
        }

        short GetRelationshipMetric(Entity source, Entity target, int metricId, int typeId)
        {
            throw new InvalidOperationException("Graph relationship runtime is not available.");
        }

        bool HasRelationshipFlag(Entity source, Entity target, int flagId, int typeId)
        {
            throw new InvalidOperationException("Graph relationship runtime is not available.");
        }

        void SetRelationshipFlag(Entity source, Entity target, int flagId, bool enabled, int reasonId, int typeId)
        {
            throw new InvalidOperationException("Graph relationship runtime is not available.");
        }

        RelationshipQueryResult CollectOutgoing(Entity source, Span<Entity> buffer, int typeId = RelationshipTypeRegistry.AnyTypeId)
        {
            throw new InvalidOperationException("Graph relationship runtime is not available.");
        }

        RelationshipQueryResult CollectIncoming(Entity target, Span<Entity> buffer, int typeId = RelationshipTypeRegistry.AnyTypeId)
        {
            throw new InvalidOperationException("Graph relationship runtime is not available.");
        }

        RelationshipQueryResult CollectMutual(Entity first, Entity second, Span<Entity> buffer, int typeId = RelationshipTypeRegistry.AnyTypeId)
        {
            throw new InvalidOperationException("Graph relationship runtime is not available.");
        }

        RelationshipQueryResult CollectBetweenPair(Entity source, Entity target, Span<Entity> buffer, int typeId = RelationshipTypeRegistry.AnyTypeId)
        {
            throw new InvalidOperationException("Graph relationship runtime is not available.");
        }

        int FilterRelationshipMetricRange(Span<Entity> entities, int count, Entity source, int typeId, int metricId, short minInclusive, short maxInclusive)
        {
            throw new InvalidOperationException("Graph relationship query runtime is not available.");
        }

        int FilterRelationshipFlag(Span<Entity> entities, int count, Entity source, int typeId, int flagId, bool expected)
        {
            throw new InvalidOperationException("Graph relationship query runtime is not available.");
        }

        void SortByRelationshipMetric(Span<Entity> entities, int count, Entity source, int typeId, int metricId, bool descending)
        {
            throw new InvalidOperationException("Graph relationship query runtime is not available.");
        }

        int SumRelationshipMetric(ReadOnlySpan<Entity> entities, Entity source, int typeId, int metricId)
        {
            throw new InvalidOperationException("Graph relationship query runtime is not available.");
        }

        int AverageRelationshipMetric(ReadOnlySpan<Entity> entities, Entity source, int typeId, int metricId)
        {
            throw new InvalidOperationException("Graph relationship query runtime is not available.");
        }

        int MaxRelationshipMetric(ReadOnlySpan<Entity> entities, Entity source, int typeId, int metricId)
        {
            throw new InvalidOperationException("Graph relationship query runtime is not available.");
        }

        int MinRelationshipMetric(ReadOnlySpan<Entity> entities, Entity source, int typeId, int metricId)
        {
            throw new InvalidOperationException("Graph relationship query runtime is not available.");
        }

        bool TryMaxEntityByRelationshipMetric(ReadOnlySpan<Entity> entities, Entity source, int typeId, int metricId, out Entity entity, out int value)
        {
            throw new InvalidOperationException("Graph relationship query runtime is not available.");
        }

        bool TryMinEntityByRelationshipMetric(ReadOnlySpan<Entity> entities, Entity source, int typeId, int metricId, out Entity entity, out int value)
        {
            throw new InvalidOperationException("Graph relationship query runtime is not available.");
        }

        // ── Topology predicates (RFC-0065 PROV-4b / DEC-5) ──

        /// <summary>Edge-existence predicate over RelationshipRuntime.HasLink.</summary>
        bool HasRelationshipLink(Entity source, Entity target, int typeId)
        {
            throw new InvalidOperationException("Graph relationship runtime is not available.");
        }

        /// <summary>Resolves the control domain rep of a target; returns Entity.Null when no domain exists.</summary>
        Entity ResolveControlDomain(Entity target)
        {
            throw new InvalidOperationException("Graph control-domain runtime is not available.");
        }

        /// <summary>Controls-reachability predicate (owns subtree ∪ Controls grants).</summary>
        bool IsControllableBy(Entity controllerRep, Entity target)
        {
            throw new InvalidOperationException("Graph control-domain runtime is not available.");
        }

        /// <summary>True when the viewer holds a live knowledge projection of the target.</summary>
        bool HasKnowledgeProjection(Entity viewer, Entity target)
        {
            throw new InvalidOperationException("Graph knowledge runtime is not available.");
        }
        void ApplyEffectTemplate(Entity caster, Entity target, int templateId);
        void ApplyEffectTemplate(Entity caster, Entity target, int templateId, in EffectArgs args);
        void FanOutDispatchEffect(Entity source, Entity target, Entity targetContext, ReadOnlySpan<Entity> targets, int templateId, int payloadPresetId)
        {
        }
        void RemoveEffectTemplate(Entity target, int templateId);
        void ModifyAttributeAdd(Entity caster, Entity target, int attributeId, float delta);
        void ModifyAttributeSet(Entity caster, Entity target, int attributeId, float value);
        void SendEvent(Entity caster, Entity target, int eventTagId, float magnitude);

        // ── TriggerManager bridge (map-scoped event firing) ──

        /// <summary>
        /// Fires a TriggerManager event resolved from a config-key id. Resolves the
        /// scope entity's map and fires map-scoped when one is present; otherwise falls
        /// back to the global event bus. Optional bridge — requires a bound TriggerManager.
        /// </summary>
        void FireEventKey(Entity scope, int eventKeyId)
        {
            throw new InvalidOperationException("GAS.GRAPH.ERR.TriggerBridgeUnavailable");
        }

        /// <summary>
        /// Fires a schema-checked map-scoped trigger event with a structured payload: values
        /// come from the StoreArg* staging table keyed by the event schema's payload keys;
        /// TriggerManager.ValidateFirePayload backstops missing/mistyped parameters.
        /// selfSource stamps MapTrigger.SourceEntity when the schema declares it (Entity.Null
        /// for map-domain dispatch). Optional bridge — requires a bound TriggerManager.
        /// </summary>
        void FireMapEventPayload(int eventKeyId, MapId mapId, Entity selfSource, GraphEntryPayloadTable? stagedArgs)
        {
            throw new InvalidOperationException("GAS.GRAPH.ERR.TriggerBridgeUnavailable");
        }

        /// <summary>
        /// Fires a schema-checked Global-scope trigger event: delivery goes
        /// through the TriggerManager global subscription table only. The origin map
        /// (empty when unmapped) rides MapTrigger.SourceMapId as transport metadata.
        /// Optional bridge — requires a bound TriggerManager.
        /// </summary>
        void FireGlobalEventPayload(int eventKeyId, MapId originMapId, GraphEntryPayloadTable? stagedArgs)
        {
            throw new InvalidOperationException("GAS.GRAPH.ERR.TriggerBridgeUnavailable");
        }

        /// <summary>
        /// Offers an activity by definition id to the scope host through
        /// ActivityRuntimeService. Repeat/admission policy decides the outcome;
        /// policy rejection lands in the presentation buffer, while an unknown
        /// activity id fails closed with the key in the message. Optional
        /// bridge — requires a bound ActivityRuntimeService.
        /// </summary>
        void OfferActivity(string activityId, Entity scopeHost)
        {
            throw new InvalidOperationException("GAS.GRAPH.ERR.ActivityRuntimeUnavailable");
        }

        /// <summary>
        /// Offers a task by definition id to the scope host through
        /// TaskRuntimeService. Existing live instances are reused; unknown task ids
        /// and invalid scope hosts fail closed. Optional bridge — requires a bound
        /// TaskRuntimeService.
        /// </summary>
        void OfferTask(string taskId, Entity scopeHost)
        {
            throw new InvalidOperationException("GAS.GRAPH.ERR.TaskRuntimeUnavailable");
        }

        /// <summary>
        /// Registers an AwaitCallback waiter (Imm callback type) and parks the slice.
        /// Completions resume through GraphCallbackContinuationSystem in registration order.
        /// </summary>
        void BeginAwaitCallback(string callbackType, MapId mapId, Entity scope, int resultBoolRegister)
        {
            throw new InvalidOperationException("GAS.GRAPH.ERR.GraphCallbackUnavailable");
        }

        // ── Entity lifecycle graph composition ──
        void BeginLifecycleTransaction()
        {
            throw new InvalidOperationException("Graph lifecycle transaction runtime is not available.");
        }

        void InvokeBuiltin(int builtinHandlerId)
        {
            throw new InvalidOperationException("Graph builtin invocation runtime is not available.");
        }

        // ── Blackboard immediate read/write ──

        bool TryReadBlackboardFloat(Entity entity, int keyId, out float value);
        bool TryReadBlackboardInt(Entity entity, int keyId, out int value);
        bool TryReadBlackboardEntity(Entity entity, int keyId, out Entity value);
        void WriteBlackboardFloat(Entity entity, int keyId, float value);
        void WriteBlackboardInt(Entity entity, int keyId, int value);
        void WriteBlackboardEntity(Entity entity, int keyId, Entity value);

        // ── Config parameter reading (from current EffectTemplate context) ──

        bool TryLoadConfigFloat(int keyId, out float value);
        bool TryLoadConfigInt(int keyId, out int value);

        bool TrySnapTargetToNearestInCollection(
            Entity owner,
            int collectionKeyId,
            ref IntVector2 targetPosCm,
            float maxDistanceCm,
            out Entity snappedEntity)
        {
            snappedEntity = Entity.Null;
            return false;
        }

        bool TrySnapTargetToNearestGraphEdge(
            ref IntVector2 targetPosCm,
            float searchRadiusCm,
            out GraphEdgeProjection projection)
        {
            projection = default;
            return false;
        }

        // ── Aimsource pure helpers (input/command chain aim graphs) ──

        /// <summary>
        /// Resolves a screen point against the authoritative ground (camera ray +
        /// heightmap, bounded by the world size). False means the ray left the world.
        /// A null seatId answers under the sole present binding; a named seat answers
        /// under that seat's binding-local screen space.
        /// </summary>
        bool TryScreenPointToGround(float screenX, float screenY, string? seatId, out IntVector2 groundCm)
        {
            groundCm = default;
            throw new InvalidOperationException("GAS.GRAPH.ERR.AimSourceUnavailable");
        }

        /// <summary>
        /// Knowledge-gated pick of the best candidate under the screen point; candidates
        /// are the explicit TargetList working set (no world scan). An empty seatId means
        /// the sole present binding; a named seat answers under that seat's binding-local
        /// screen space.
        /// </summary>
        Entity PickScreenPointEntity(ReadOnlySpan<Entity> candidates, int count, Entity owner, string? seatId, float screenX, float screenY, float radiusPixels)
        {
            throw new InvalidOperationException("GAS.GRAPH.ERR.AimSourceUnavailable");
        }

        /// <summary>
        /// In-place filter of an entity span down to the members whose projected bounds
        /// intersect the screen rect; preserves candidate order (deterministic result).
        /// A null seatId answers under the sole present binding; a named seat answers
        /// under that seat's binding-local screen space.
        /// </summary>
        int FilterScreenRegionEntities(Span<Entity> entities, int count, in ScreenRect rect, string? seatId)
        {
            throw new InvalidOperationException("GAS.GRAPH.ERR.AimSourceUnavailable");
        }

        bool TryReadLivePointerScreen(out float screenX, out float screenY)
        {
            throw new InvalidOperationException("GAS.GRAPH.ERR.AimSourceUnavailable");
        }
    }

    /// <summary>
    /// Resolves symbolic names (tags, attributes, effect templates) to runtime integer ids.
    /// Injected into <see cref="Host.GraphProgramConfigLoader"/> to decouple it from concrete registries.
    /// </summary>
    public interface IGraphSymbolResolver
    {
        int ResolveTag(string name);
        int ResolveAttribute(string name);
        int ResolveEffectTemplate(string name);
        int ResolveAbility(string name)
        {
            throw new InvalidOperationException(
                $"Graph references ability '{name}', but no ability resolver is available.");
        }
        int ResolveRngDistribution(string name)
        {
            throw new InvalidOperationException("GAS.GRAPH.ERR.RngDistributionUnavailable");
        }

        int ResolveGraphLookupTable(string name)
        {
            throw new InvalidOperationException(
                $"Graph references lookup table '{name}', but no GraphLookupTableRegistry resolver is available.");
        }

        int ResolveGraphLookupField(string name)
        {
            throw new InvalidOperationException(
                $"Graph references lookup field '{name}', but no GraphLookupTableRegistry resolver is available.");
        }

        int ResolveRelationshipType(string name);
        int ResolveRelationshipMetric(string name);
        int ResolveRelationshipFlag(string name);
        int ResolveRelationshipReason(string name);
        int ResolveTargetDispatchPreset(string name);
        int ResolveEntityTemplate(string name);

        int ResolveTextToken(string name)
        {
            throw new InvalidOperationException(
                $"Graph references text token '{name}', but no PresentationTextCatalog resolver is available.");
        }
    }
}
