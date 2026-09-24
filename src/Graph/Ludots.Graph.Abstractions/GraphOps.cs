using System;

namespace Ludots.Core.NodeLibraries.GASGraph
{
    public enum GraphValueType : byte
    {
        Void = 0,
        Bool = 1,
        Int = 2,
        Float = 3,
        Entity = 4,
        TargetList = 5,
        Text = 6,
        IntIdList = 7,
    }

    public enum GraphNodeOp : ushort
    {
        None = 0,
        ConstBool = 1,
        ConstInt = 2,
        ConstFloat = 3,
        LoadCaster = 4,
        LoadExplicitTarget = 5,
        Jump = 6,
        JumpIfFalse = 7,
        LoadAttribute = 10,
        AddFloat = 20,
        MulFloat = 21,
        SubFloat = 22,
        DivFloat = 23,   // div-by-zero → 0
        MinFloat = 24,
        MaxFloat = 25,
        ClampFloat = 26, // clamp(F[a], F[b], F[c]) → min=F[b], max=F[c]
        AbsFloat = 27,
        NegFloat = 28,
        RandomFloat01 = 34,
        // ── Int Math (29, 31-33) ──
        AddInt            = 29,   // I[Dst] = I[A] + I[B]
        CompareGtFloat = 30,
        CompareLtInt      = 31,   // B[Dst] = I[A] < I[B] ? 1 : 0
        CompareEqInt      = 32,   // B[Dst] = I[A] == I[B] ? 1 : 0
        HasTag            = 33,   // B[Dst] = E[A].HasTag(Imm) ? 1 : 0
        CompareEqEntity   = 35,   // B[Dst] = E[A] == E[B] ? 1 : 0

        SelectEntity = 40,
        QueryRadius = 100,
        // 101 removed (was QueryFilterTagAll; use explicit QueryFilterTagAny/QueryFilterTagNone; multi-tag All needs a real multi-tag data encoding)
        QuerySortStable = 102,
        QueryLimit = 103,
        QueryCone = 104,
        QueryRectangle = 105,
        QueryLine = 106,
        // 110 removed (was QueryFilterTeam — use QueryFilterRelationship instead)
        QueryFilterNotEntity = 111,
        QueryFilterLayer = 112,
        QueryFilterRelationship = 113,

        // ── TargetList iteration / aggregation (120-123) ──
        AggCount = 120,
        AggMinByDistance = 121,
        TargetListGet     = 123,  // E[Dst] = TargetList[I[A]]; B[Flags] = valid (0/1)

        // ── Hex spatial queries (130-132) ──
        QueryHexRange     = 130,  // TargetList = HexRange(WorldCmToHex(TargetPosCm), Imm=radius)
        QueryHexRing      = 131,  // TargetList = HexRing(WorldCmToHex(TargetPosCm), Imm=radius)
        QueryHexNeighbors = 132,  // TargetList = Hex6Neighbors(WorldCmToHex(TargetPosCm))

        // ── Effect / Event Actions ──
        ApplyEffectTemplate = 200,
        FanOutApplyEffect = 201,            // Apply Effect(Imm=templateId) to ALL entities in TargetList
        ApplyEffectDynamic = 202,           // source=Caster, target=E[A], templateId=I[B]
        FanOutApplyEffectDynamic = 203,     // source=Caster, TargetList, templateId=I[A]
        RemoveEffectTemplate = 204,         // Remove all active effects matching templateId from E[A]
        FanOutDispatchEffect = 205,         // source/target/context mapped by payload preset, templateId=Imm, targets=TargetList
        FanOutDispatchEffectDynamic = 206,  // source/target/context mapped by payload preset, templateId=I[A], targets=TargetList
        ModifyAttributeAdd = 210,
        SendEvent = 220,

        // ── Blackboard immediate read/write (300-305) ──
        ReadBlackboardFloat   = 300,  // F[dst] = entity.BB[keyId]
        ReadBlackboardInt     = 301,  // I[dst] = entity.BB[keyId]
        ReadBlackboardEntity  = 302,  // E[dst] = entity.BB[keyId]
        WriteBlackboardFloat  = 303,  // entity.BB[keyId] = F[src] (immediate)
        WriteBlackboardInt    = 304,  // entity.BB[keyId] = I[src]
        WriteBlackboardEntity = 305,  // entity.BB[keyId] = E[src]

        // ── Config parameter reading (310-312) ──
        LoadConfigFloat       = 310,  // F[dst] = EffectTemplate.ConfigParams[keyId]
        LoadConfigInt         = 311,  // I[dst] = EffectTemplate.ConfigParams[keyId]
        LoadConfigEffectId    = 312,  // I[dst] = EffectTemplate.ConfigParams[keyId] (effectTemplateId)

        // ── Context entity loading (320-322) ──
        LoadContextSource        = 320,  // E[dst] = EffectContext.Source
        LoadContextTarget        = 321,  // E[dst] = EffectContext.Target
        LoadContextTargetContext = 322,  // E[dst] = EffectContext.TargetContext

        // ── Self attribute access for derived graphs (330-331) ──
        LoadSelfAttribute        = 330,  // F[dst] = Caster.Attribute[Imm] (no EffectContext needed)
        WriteSelfAttribute       = 331,  // Caster.Attribute[Imm] = F[A] (direct SetCurrent, bypasses modifiers)
        RelationshipEnsureLink   = 360,
        RelationshipRemoveLink   = 361,
        RelationshipSetMetric    = 362,
        RelationshipAddMetric    = 363,
        RelationshipGetMetric    = 364,
        RelationshipHasFlag      = 365,
        RelationshipSetFlag      = 366,
        RelationshipQueryOutgoing = 367,
        RelationshipQueryIncoming = 368,
        RelationshipQueryMutual  = 369,
        RelationshipQueryBetweenPair = 370,
        RelationshipFilterMetricRange = 371,
        RelationshipFilterFlag   = 372,
        RelationshipSortByMetric = 373,
        RelationshipAggSumMetric = 374,
        RelationshipAggMaxMetric = 375,
        RelationshipAggAverageMetric = 376,

        QueryAllMapEntities = 380,
        QueryFromCollection = 381,
        QueryFilterTeam = 382,
        QueryFilterTemplate = 383,
        QueryFilterAttributeRange = 384,
        QueryFilterTagAny = 385,
        QueryFilterTagNone = 386,
        QuerySortByAttribute = 387,
        AggSumAttribute = 388,
        AggAverageAttribute = 389,
        AggMaxAttribute = 390,
        AggMinAttribute = 391,
        AggMaxEntityByAttribute = 392,
        AggMinEntityByAttribute = 393,
        RelationshipAggMinMetric = 394,
        RelationshipAggMaxEntityByMetric = 395,
        RelationshipAggMinEntityByMetric = 396,
        RelationshipHasLink = 397,          // B[Dst] = HasLink(E[A], E[B], type=Flags symbol)
        QueryCollectActiveEffects = 398,    // TargetList = active effect instances on E[A]
        LoadEffectTiming = 399,             // F[Dst] = RemainingTicks|TotalTicks on caster (Flags)
        LoadEffectStack = 429,              // F[Dst] = EffectStack.Count on caster (missing → 1)

        // ── Entity lifecycle composition (400-401) ──
        BeginLifecycleTransaction = 400,
        InvokeBuiltin = 401,

        // ── Placement validation (402-406) ──
        LoadTargetPosX = 402,
        LoadTargetPosY = 403,
        ClampTargetToRange = 404,
        IsPointInCircle = 405,
        SnapToNearestInCollection = 406,
        SnapToNearestGraphEdge = 407,

        // ── Typed collection collectors (408-409, 419, 423-427) ──
        QueryCollectEffectTemplates = 408,  // IntIdList = registered effect template ids
        QueryCollectAbilitySlots = 409,     // IntIdList = resolved ability slot indices on E[A]
        // 410-418 occupied
        QueryCollectInventoryItems = 419,   // TargetList = owned item instance entities for E[A]
        // 420-422 occupied
        QueryCollectItemDefinitions = 423,  // IntIdList = registered item definition ids
        QueryCollectPresentTags = 424,      // IntIdList = present tag ids on E[A]
        QueryCollectActiveTasks = 425,      // TargetList = task instances scoped to E[A]
        QueryCollectProgressionNodes = 426, // IntIdList = progression ids on E[A]
        QueryCollectAbilityHolders = 427,   // TargetList = TargetList candidates holding Imm ability
        QueryCollectActiveActivities = 428, // TargetList = activity instances scoped to E[A]

        // ── Event evaluation context (410-412, RFC-0065 PROV-4b) ──
        LoadViewer           = 410,  // E[Dst] = state.Viewer (fixed register 2)
        LoadEventPayloadInt  = 411,  // I[Dst] = presenter EventPayload int slot (Imm: 0=PayloadA, 1=PayloadB)
        LoadEventPayloadFloat = 412, // F[Dst] = presenter EventPayload float slot (Imm: 0..3 = FloatA..FloatD)

        // ── TriggerGraph entry payload by name (413-415); captured at entry start from the
        // firing ScriptContext per EventSchemaRegistry params ──
        LoadEntryPayloadEntity = 413, // E[Dst] = entry payload (Imm: payload key symbol id)
        LoadEntryPayloadInt    = 414, // I[Dst] = entry payload (Imm: payload key symbol id)
        LoadEntryPayloadFloat  = 415, // F[Dst] = entry payload (Imm: payload key symbol id)

        // ── Placed-entity / region / anchor variable reads ──
        // E[Dst] = entity registered under the placed InstanceId (Imm: instance id key id)
        // on the mounted map. Unregistered or destroyed instances write Entity.Null —
        // unlike LoadEntryPayload*, a miss is a readable value, not a throw. Compile-time
        // validation is mount-time fail-closed (TriggerGraphMounting) because only the
        // mounting map knows its placed-instance catalog.
        LoadPlacedEntity = 416,
        // I[Dst] = 1 when Imm region id is in the mounting map's Regions catalog, else 0.
        // Regions never enter MapLoadEntityIndex.
        LoadPlacedRegion = 417,
        // E[Dst] = same runtime as LoadPlacedEntity; authoring/mount require InstanceId
        // containing "anchor" (SC2/War3-style placed anchors, not panel UI anchors).
        LoadPlacedAnchor = 418,

        // ── Topology predicates (420-422, RFC-0065 DEC-5 viewer-relative semantics) ──
        ControlDomainResolve  = 420, // E[Dst] = control domain rep of E[A], Entity.Null when none
        ControlDomainControls = 421, // B[Dst] = IsControllableBy(controllerRep=E[A], target=E[B])
        KnowledgeHasProjection = 422, // B[Dst] = viewer E[A] has knowledge projection of target E[B]

        // ── Shared control-flow / Script coroutine (430-434) ──
        Call = 430,            // push return PC; pc = Imm (absolute)
        Return = 431,          // pop return PC
        Yield = 432,           // pause; resume at next instruction
        HaltReturnInt = 433,   // halt with ReturnInt = I[A]
        InvokeScript = 434,    // run Script graph Imm to halt (callee must not Yield)
        MoveInt = 435,         // I[Dst] = I[A]

        // ── Generic lookup-table reads (436-438) ──
        /// <summary>I[Dst] = ResolveTableRow(Imm=tableId, I[A]=key).</summary>
        ResolveTableRow = 436,
        /// <summary>I[Dst] = TableReadInt(Imm=fieldId, I[A]=rowHandle). TextToken columns return token id.</summary>
        TableReadInt = 437,
        /// <summary>F[Dst] = TableReadFloat(Imm=fieldId, I[A]=rowHandle).</summary>
        TableReadFloat = 438,

        // ── Panel visibility control (contract five) ──
        /// <summary>Request the named panel type to become visible. Imm = panel type symbol.</summary>
        ShowPanel = 439,
        /// <summary>Request the named panel type to become hidden. Imm = panel type symbol.</summary>
        HidePanel = 440,

        // ── Panel instance lifecycle ──
        /// <summary>Instantiate a panel. Imm = packed template|anchor key ids (symbol pair pre-patch); E[A] = scope entity (A=0xFF → caster).</summary>
        CreatePanel = 441,
        /// <summary>Dispose panel instances of a template. Imm = template key id (symbol pre-patch); E[A] = scope entity (A=0xFF → any scope).</summary>
        DestroyPanel = 442,

        // ── Map-scoped variables (443-446) ──
        /// <summary>I[Dst] = map variable (Imm=varName keyId) read from the map owning E[A] (A=0xFF → caster).</summary>
        ReadMapVarInt = 443,
        /// <summary>F[Dst] = map variable (Imm=varName keyId) read from the map owning E[A] (A=0xFF → caster).</summary>
        ReadMapVarFloat = 444,
        /// <summary>Map variable (Imm=varName keyId) of the map owning E[B] (B=0xFF → caster) := I[A].</summary>
        WriteMapVarInt = 445,
        /// <summary>Map variable (Imm=varName keyId) of the map owning E[B] (B=0xFF → caster) := F[A].</summary>
        WriteMapVarFloat = 446,

        // ── Runtime entity spawning ──
        /// <summary>Enqueue a template entity spawn. Imm = entity template symbol; E[A] = spawn source map anchor (A=0xFF → caster); F[B]/F[C] = optional explicit xCm/yCm (Flags bit 0 = position wired).</summary>
        SpawnTemplate = 447,

        /// <summary>Set an entity's world position. E[A] = target (A=0xFF → caster); I[B] = xCm; I[C] = yCm (int centimeters, matches LoadTargetPosX/Y).</summary>
        SetWorldPosition = 448,

        /// <summary>Pick an integer outcome from a named deterministic distribution. Imm = distribution symbol; I[A] = stream salt.</summary>
        WeightedPick = 449,

        // ── TriggerGraph subgraph reuse + structured event dispatch ──
        // InvokeGraph encoding: Imm = target graph id at run time; Dst = int register
        // receiving the child's HaltReturnInt. Authoring has two modes mirroring InvokeScript:
        // literal graphId (Flags 0) or a graph-key functionName resolved and patched to the id
        // at load time (Flags bit 0 = GraphInstructionFlags.FuncLibName; stable across mod sets,
        // since sequential graph ids are load-order dependent). Flags bit 1 = "entry label
        // authored": compile packs the label's symbol index in the CALLER's symbol table as
        // B | (C << 8); load-time validation (GraphProgramRegistry) resolves the label against
        // the target entry table and rewrites A = entry ordinal + 1 with B/C cleared
        // (A == 0 after validation means never validated and fails closed).
        // No label → target entry table [0].
        /// <summary>Run TriggerGraph Imm to halt from the selected entry; I[Dst] = child HaltReturnInt. Child must not Yield; EntryPayload = the caller's InvokeArgs staging.</summary>
        InvokeGraph = 450,
        /// <summary>I[A] → InvokeArgs staging (Imm: arg key symbol id). Consumed (cleared) by the next InvokeGraph / DispatchMapEvent.</summary>
        StoreArgInt = 451,
        /// <summary>F[A] → InvokeArgs staging (Imm: arg key symbol id).</summary>
        StoreArgFloat = 452,
        /// <summary>E[A] → InvokeArgs staging (Imm: arg key symbol id).</summary>
        StoreArgEntity = 453,
        /// <summary>Assemble a ScriptContext from the InvokeArgs staging per the event schema (Imm: event name symbol id) and fire it map-scoped; Flags 0 = map domain, 1 = self domain.</summary>
        DispatchMapEvent = 454,
        /// <summary>
        /// AwaitCallback: register a named callback handle (Imm: callbackType symbol id),
        /// park the slice (Yielded), and on Complete write confirmed into B[Dst] then resume
        /// in the Continuation phase (registration order).
        /// </summary>
        AwaitCallback = 455,

        // ── Formal text (456-460): fixed-capacity Text registers + presentation sink ──
        /// <summary>T[Dst] = program Symbols[Imm] (Imm is symbol index; never symbol-patched).</summary>
        ConstText = 456,
        /// <summary>T[Dst] = T[A] + T[B]; overflow fails closed.</summary>
        ConcatText = 457,
        /// <summary>T[Dst] = invariant format of I[A].</summary>
        IntToText = 458,
        /// <summary>T[Dst] = invariant format of F[A].</summary>
        FloatToText = 459,
        /// <summary>Push T[A] to presentation sink; Imm = GraphPresentationTextSurface.</summary>
        SinkPresentationText = 460,

        /// <summary>
        /// T[Dst] = PresentationTextCatalog template for Imm token id (patched from textKey symbol).
        /// Zero-arg tokens only in this slice; argCount&gt;0 fails closed.
        /// </summary>
        LoadTextKey = 461,

        StartDialogue = 462,

        /// Set the target entity's interaction mode: add/replace the sparse
        /// InteractionMode component, or remove it when the mode is the reserved mode.normal.
        /// E[A] = target entity (A=0xFF → caster); Imm = mode id symbol, patched to a
        /// ConfigKeyRegistry id and resolved against the installed interaction mode map —
        /// dead targets and unknown mode ids fail closed by name.
        /// </summary>
        SetInteractionMode = 463,

        /// <summary>
        /// Override a panel type's audience with one seat (hotseat turn handoff), or
        /// clear the override when no seat symbol is declared — the template's declared
        /// audience rules again. Imm packs the panelType and seat key ids (seat 0 = clear);
        /// event admission and surface placement both consume the recorded override.
        /// </summary>
        SetPanelAudience = 464,

        /// <summary>
        /// Set the selected target entity's current attribute value through the
        /// AttributeMutationOps authority. E[A] = target entity; F[B] = value;
        /// Imm = attribute symbol patched at load time.
        /// </summary>
        ModifyAttributeSet = 465,
        /// <summary>Offer the activity named by Symbols[Imm] to E[A] as scope host via ActivityRuntimeService.</summary>
        OfferActivity = 466,
        /// <summary>Offer the task named by Symbols[Imm] to E[A] as scope host via TaskRuntimeService.</summary>
        OfferTask = 467,
        /// <summary>IntIdList = currently available DialogueRuntime choice ids.</summary>
        QueryCollectActiveDialogueChoices = 468,

        // ── Aimsource pure helpers (input/command chain; stateless utility kernels the
        // aim graphs compose — screen point to ground, pointer pick, region filter,
        // world/stick to direction). All Query-kind read-only. ──
        /// <summary>
        /// B[Dst] = screen point (F[A]=x px, F[B]=y px) resolved against the authoritative
        /// ground; on success TargetPosCm := the ground point (read via LoadTargetPosX/Y).
        /// </summary>
        ScreenPointToGround = 469,
        /// <summary>
        /// E[Dst] = knowledge-gated pick under the screen point among the current TargetList
        /// candidates (explicit candidate set, no world scan). E[A] = inspecting owner rep;
        /// F[B]/F[C] = pointer x/y px; ImmF = pick radius px; Imm = seat key symbol whose
        /// binding-local screen space the pointer answers under.
        /// </summary>
        ScreenPointToEntity = 470,
        /// <summary>
        /// TargetList := TargetList candidates whose projected bounds intersect the screen
        /// rect (F[A]=minX, F[B]=minY, F[C]=maxX, F[Flags]=maxY px); candidate order is
        /// preserved, so the result order stays deterministic.
        /// </summary>
        ScreenRegionToEntities = 471,
        /// <summary>
        /// F[Dst] = direction angle in degrees (0 = +X) from the rep E[A]'s world position to
        /// the frame's TargetPosCm; B[Flags] = 0 and F[Dst] = 0 when either position is absent.
        /// </summary>
        PointToDirection = 472,
        /// <summary>
        /// F[Dst] = direction angle in degrees (0 = +X) of the stick vector (F[A]=x, F[B]=y,
        /// numeric processors already applied upstream); B[Flags] = 1 when the vector clears
        /// the deadzone, 0 with F[Dst] = 0 otherwise.
        /// </summary>
        StickToDirection = 473,

        // ── Derived interaction context ops (constitution §8.2/§8.3). The
        // entity-mounted context set is world state; these ops are its only derived-context
        // writers. Scope lifecycle (presenter Create/DestroyScope) rides the presenter
        // command pipeline; activation/deactivation publish ContextActivated/Deactivated
        // presentation events keyed by the context profile id. ──
        /// <summary>
        /// Activate a derived interaction context on E[A] (A=0xFF → caster). Imm = context
        /// profile symbol; Dst = optional parent context profile symbol (0xFF → no parent
        /// constraint). Idempotent-failure: an already-active context or an inactive declared
        /// parent fails fast by name.
        /// </summary>
        ActivateContext = 474,
        /// <summary>
        /// Deactivate an interaction context instance (and its descendants transitively) on
        /// E[A] (A=0xFF → caster). Imm = context profile symbol. Fails fast when the context
        /// is not mounted as an instance; the instance's presenter scope is destroyed
        /// wholesale through the presenter command pipeline.
        /// </summary>
        DeactivateContext = 475,
        /// <summary>
        /// Direct owned-collection write: owner = caster (the writing rep), entity list = the
        /// graph's current query result set (s.Targets), I[B] = op (0=replace, 1=add,
        /// 2=subtract, computed in-graph), Imm = collection key symbol patched to its key id.
        /// Set semantics execute in the CollectionWrite primitive; membership change events
        /// fire from the store's presentation diff like any other writer.
        /// </summary>
        WriteCollection = 477,

        /// <summary>
        /// Live pointer screen X (window px) for the authoritative PointerPos action.
        /// Pure float read; fail closed when the input snapshot is unavailable.
        /// </summary>
        LoadPointerScreenX = 479,
        /// <summary>
        /// Live pointer screen Y (window px) for the authoritative PointerPos action.
        /// Pure float read; fail closed when the input snapshot is unavailable.
        /// </summary>
        LoadPointerScreenY = 480,
        BindQueryCollection = 481,
        QueryScreenRegionCollection = 482,

        // ── Order-driven graph brains (issue #1536; 484-499 reserved as the
        //    graph-input-order-chain line's renumbering buffer) ──

        /// <summary>Read an entity's world X in int centimeters. E[A] = source; I[Dst] = xCm; B[Flags] = 0 when the entity is dead or has no WorldPositionCm (routine guard, brains branch on it).</summary>
        LoadEntityPosX = 500,
        /// <summary>Read an entity's world Y in int centimeters. E[A] = source; I[Dst] = yCm; B[Flags] = 0 when the entity is dead or has no WorldPositionCm (routine guard, brains branch on it).</summary>
        LoadEntityPosY = 501,
        IntToFloat = 502,    // F[Dst] = I[A]
        /// <summary>Float→Int with round-half-away-from-zero, matching the world-centimeter rounding convention.</summary>
        FloatToInt = 503,
        SqrtFloat = 504,     // F[Dst] = sqrt(F[A]); negative input fails closed
        /// <summary>
        /// Behavior-side order submission: the acting unit enqueues an assigned order into
        /// the OrderQueue. Imm = order type id (semantic key resolved at patch time);
        /// E[A] = target entity; I[B] = xCm; I[C] = yCm. Script slice hosts only; the
        /// input-side SubmitCommandIntent intent-buffer contract is separate.
        /// </summary>
        /// <summary>E[A] = source; B[Dst] = 1 when the entity is alive and has a WorldPositionCm, 0 otherwise (edge-readable guard companion of LoadEntityPosX/Y).</summary>
        LoadEntityPosValid = 508,
        /// <summary>Load an order type id from its semantic key (Imm resolved at patch time) into I[Dst]. Pure register materialization for order-type dispatch in behavior graphs.</summary>
        LoadOrderTypeId = 507,
        SubmitAssignedOrder = 505,
        /// <summary>
        /// Publish the acting unit's terminal outcome for its active order through the
        /// OrderTerminalResultBuffer. Caster = the acting unit. Script slice hosts only.
        /// </summary>
        CompleteActiveOrder = 506,

        /// <summary>
        /// Submit one command intent into the order pipeline's per-tick submission buffer
        /// (constitution §12). Caster = the acting rep (mount subject); ground point = the
        /// frame's TargetPosCm, which B[B] (condition port, required) asserts was resolved this
        /// run — a false condition fails closed by name. E[A] (target port, optional) carries a
        /// picked entity for entity-target facts; absent or null means ground-only facts. The
        /// order kernel drains the buffer in its own system-group phase: the op never routes,
        /// reads collections, or touches the OrderQueue.
        /// </summary>
        SubmitCommandIntent = 483,

        /// <summary>
        /// Submit one cast intent into the order pipeline's per-tick submission buffer
        /// (constitution §12). Caster = the acting rep; I[A] = ability slot index; E[B]
        /// (optional) = cast target entity; B[C] (optional) asserts the frame's TargetPosCm was
        /// resolved this run and carries the ground point. Imm = the cast order-type key symbol
        /// (e.g. "castAbility"), resolved by the drain through the OrderTypeRegistry. Actors are
        /// the rep's active-context-declared active collection members — same §12 resolution as
        /// command intents.
        /// </summary>
        SubmitCast = 484,

        /// <summary>
        /// TargetList := candidates the viewer E[A] currently has a knowledge projection of
        /// (per the viewer-target knowledge store); candidates order preserved. Read-only
        /// viewer-relative query filter (RFC-0065 DEC-5).
        /// </summary>
        QueryFilterKnowledgeVisible = 485,

        /// <summary>
        /// TargetList := candidates that are command-source selectable now: CommandSourceSelectableTag
        /// present and CommandSourceSelectableState, when present, enabled. Candidates order preserved;
        /// viewer-independent. Restores the selectable gate the retired CommandSourceAcquisitionSystem
        /// enforced for click and box acquisition.
        /// </summary>
        QueryFilterSelectable = 487,

        /// <summary>
        /// Submit one engage intent into the order pipeline's per-tick submission buffer
        /// (constitution §12). Caster = the acting rep; I[A] = ability slot index; E[B] =
        /// the engage target entity (required). Imm = engage profile key symbol resolved to
        /// an EQS query registry id at patch time. The drain resolves actors from the rep's
        /// active-context-declared collection, runs the profile's EQS query around the target
        /// (in-batch exclusion + slot claims), and submits per-actor move-then-cast through the
        /// composite order planner: moveTo the assigned ring point with the cast as an order
        /// continuation — the op never routes inline.
        /// </summary>
        SubmitEngageBatch = 486,

        // ── World calendar reads and writes (509-519). Enabling the calendar stays
        // in Calendar/world.json. These ops read the live projection and write the
        // opening date or move the day forward through CalendarRuntime. ──

        /// <summary>B[Dst] = 1 when the world calendar is enabled, else 0. Does not throw when disabled.</summary>
        ReadCalendarEnabled = 509,
        /// <summary>I[Dst] = world day index. Fails closed when the calendar is disabled.</summary>
        ReadCalendarDayIndex = 510,
        /// <summary>I[Dst] = steps already consumed inside the current day.</summary>
        ReadCalendarTicksIntoDay = 511,
        /// <summary>I[Dst] = progress through the current day, in thousandths.</summary>
        ReadCalendarDayPermille = 512,
        /// <summary>I[Dst] = ConfigKey id of the current day-phase.</summary>
        ReadCalendarDayPhase = 513,
        /// <summary>I[Dst] = projected year. Imm = calendar key id after patch; 0 = active calendar.</summary>
        ReadCalendarYear = 514,
        /// <summary>I[Dst] = ConfigKey id of a cycle's current phase. Imm packs cycle key (low) and calendar key (high, 0 = active).</summary>
        ReadCalendarCyclePhase = 515,
        /// <summary>I[Dst] = 1-based day inside a cycle's current phase. Imm packing matches ReadCalendarCyclePhase.</summary>
        ReadCalendarCycleDay = 516,
        /// <summary>Place the opening day index (I[A]) and ticks into the day (I[B]) once, without replaying events. Same values after the opening is committed are a no-op.</summary>
        ApplyCalendarStart = 517,
        /// <summary>Move the world day index forward to I[A]. Backward fails closed. Each crossed day fires the same events as the clock.</summary>
        SetCalendarDayIndex = 518,
        /// <summary>Set ticks into the current day to I[A], in [0, ticksPerDay). A day-phase change fires Calendar.DayPhaseChanged.</summary>
        SetCalendarTicksIntoDay = 519,
        /// <summary>B[Dst] = domain named by symbols[Imm] is paused. Effective scale includes the parent domain.</summary>
        ReadTimeFlowPaused = 520,
        /// <summary>I[Dst] = effective scale permille of the domain named by symbols[Imm]. 1000 is normal speed. 0 is paused.</summary>
        ReadTimeFlowScalePermille = 521,
        /// <summary>I[Dst] = pause token on the domain named by symbols[Imm]. Owner is the running graph id.</summary>
        AcquireTimeFlowPause = 522,
        /// <summary>I[Dst] = scale token. Domain is symbols[Imm]. Scale permille is I[A], and must be &gt; 0.</summary>
        AcquireTimeFlowScale = 523,
        /// <summary>Release pause or scale token I[A]. A token that is not active fails closed.</summary>
        ReleaseTimeFlowToken = 524,

    }

    public static class GraphNodeOpParser
    {
        public static bool TryParse(string op, out GraphNodeOp parsed)
        {
            parsed = GraphNodeOp.None;
            if (string.IsNullOrWhiteSpace(op)) return false;

            string trimmed = op.Trim();
            if (Enum.TryParse(trimmed, ignoreCase: false, out GraphNodeOp v) &&
                v != GraphNodeOp.None &&
                Enum.IsDefined(typeof(GraphNodeOp), v) &&
                string.Equals(v.ToString(), trimmed, StringComparison.Ordinal))
            {
                parsed = v;
                return true;
            }

            return false;
        }
    }
}
