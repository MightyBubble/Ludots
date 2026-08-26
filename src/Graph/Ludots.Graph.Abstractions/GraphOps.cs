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
        TargetList = 5
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

        // ── Event evaluation context (410-412, RFC-0065 PROV-4b) ──
        LoadViewer           = 410,  // E[Dst] = state.Viewer (fixed register 2)
        LoadEventPayloadInt  = 411,  // I[Dst] = presenter EventPayload int slot (Imm: 0=PayloadA, 1=PayloadB)
        LoadEventPayloadFloat = 412, // F[Dst] = presenter EventPayload float slot (Imm: 0..3 = FloatA..FloatD)

        // ── TriggerGraph entry payload by name (413-415); captured at entry start from the
        // firing ScriptContext per EventSchemaRegistry params ──
        LoadEntryPayloadEntity = 413, // E[Dst] = entry payload (Imm: payload key symbol id)
        LoadEntryPayloadInt    = 414, // I[Dst] = entry payload (Imm: payload key symbol id)
        LoadEntryPayloadFloat  = 415, // F[Dst] = entry payload (Imm: payload key symbol id)

        // ── Placed-entity / region / anchor variable reads (#1108) ──
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

        // ── Generic lookup-table reads (436-438, #881) ──
        /// <summary>I[Dst] = ResolveTableRow(Imm=tableId, I[A]=key).</summary>
        ResolveTableRow = 436,
        /// <summary>I[Dst] = TableReadInt(Imm=fieldId, I[A]=rowHandle). TextToken columns return token id.</summary>
        TableReadInt = 437,
        /// <summary>F[Dst] = TableReadFloat(Imm=fieldId, I[A]=rowHandle).</summary>
        TableReadFloat = 438,

        // ── Panel visibility control (#1014, contract five) ──
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

        // ── TriggerGraph subgraph reuse + structured event dispatch (#1116/#1115) ──
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
        /// #1126 AwaitCallback: register a named callback handle (Imm: callbackType symbol id),
        /// park the slice (Yielded), and on Complete write confirmed into B[Dst] then resume
        /// in the Continuation phase (registration order).
        /// </summary>
        AwaitCallback = 455,
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
