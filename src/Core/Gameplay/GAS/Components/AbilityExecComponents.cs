using Arch.Core;
using Ludots.Core.Mathematics.FixedPoint;

namespace Ludots.Core.Gameplay.GAS.Components
{
    /// <summary>
    /// Kind of item in an <see cref="AbilityExecSpec"/>.
    /// Grouped by category: Clips (duration), Signals (instant), Gates (pause), Control.
    /// </summary>
    public enum ExecItemKind : byte
    {
        None = 0,

        // ── Clips (duration-based) ──

        /// <summary>Apply a duration effect with optional CallerParams override.</summary>
        EffectClip = 1,
        /// <summary>Add a tag at startTick and auto-remove at startTick + durationTicks.</summary>
        TagClip = 2,
        /// <summary>Like TagClip, but applied to the current Target entity instead of the actor.</summary>
        TagClipTarget = 3,

        // ── Signals (instant) ──

        /// <summary>Apply an instant effect.</summary>
        EffectSignal = 10,
        /// <summary>Publish a GameplayEvent.</summary>
        EventSignal = 11,
        /// <summary>Execute a graph program.</summary>
        GraphSignal = 12,
        /// <summary>Add or remove a tag instantly. PayloadA: 0=add, 1=remove.</summary>
        TagSignal = 13,
        /// <summary>Like TagSignal, but applied to the current Target entity instead of the actor.</summary>
        TagSignalTarget = 14,

        // ── Gates (pause until condition) ──

        /// <summary>Wait for player input confirmation.</summary>
        InputGate = 20,
        /// <summary>Wait for a specific GameplayEvent.</summary>
        EventGate = 21,
        /// <summary>Wait for target selection response.</summary>
        SelectionGate = 22,

        // ── Control ──

        /// <summary>Marks the end of execution.</summary>
        End = 255,
    }

    /// <summary>
    /// Runtime state of an ability execution instance.
    /// </summary>
    public enum AbilityExecRunState : byte
    {
        /// <summary>Actively advancing tick and firing items.</summary>
        Running = 0,
        /// <summary>Blocked on a Gate, waiting for external condition.</summary>
        GateWaiting = 1,
        /// <summary>All effects committed, finishing up.</summary>
        Committed = 2,
        /// <summary>Execution completed normally.</summary>
        Finished = 3,
        /// <summary>Execution was interrupted (e.g., stun).</summary>
        Interrupted = 4,
    }

    /// <summary>
    /// Declarative ability execution specification. 0GC unsafe struct with SoA layout.
    /// Items are sorted by tick. Supports Clips, Signals, and Gates.
    /// Replaces the former AbilityTaskSpec.
    /// </summary>
    public unsafe struct AbilityExecSpec
    {
        public const int MAX_ITEMS = 16;

        /// <summary>Default clock for tick advancement.</summary>
        public GasClockId ClockId;
        /// <summary>Tags that interrupt this ability when present on the caster.</summary>
        public GameplayTagContainer InterruptAny;
        /// <summary>Number of items in this spec.</summary>
        public int ItemCount;

        // ── SoA item arrays ──

        public fixed byte ItemKinds[MAX_ITEMS];
        /// <summary>Tick at which the item fires (relative to execution start).</summary>
        public fixed int ItemTicks[MAX_ITEMS];
        /// <summary>Duration in ticks (Clips only; 0 for Signals/Gates).</summary>
        public fixed int ItemDurationTicks[MAX_ITEMS];
        /// <summary>Per-item clock override (0 = use spec default).</summary>
        public fixed byte ItemClockIds[MAX_ITEMS];
        /// <summary>Tag ID for tag operations, event tags, gate event tags.</summary>
        public fixed int ItemTagIds[MAX_ITEMS];
        /// <summary>Effect template ID for EffectClip/EffectSignal.</summary>
        public fixed int ItemTemplateIds[MAX_ITEMS];
        /// <summary>Index into the CallerParams pool (0xFF = none).</summary>
        public fixed byte ItemCallerParamsIdx[MAX_ITEMS];
        /// <summary>Extra payload (graph program ID for GraphSignal, selection kind for SelectionGate, etc.).</summary>
        public fixed int ItemPayloadA[MAX_ITEMS];

        public ExecItemKind GetKind(int index) { fixed (byte* p = ItemKinds) return (ExecItemKind)p[index]; }
        public int GetTick(int index) { fixed (int* p = ItemTicks) return p[index]; }
        public int GetDurationTicks(int index) { fixed (int* p = ItemDurationTicks) return p[index]; }
        public GasClockId GetClockId(int index) { fixed (byte* p = ItemClockIds) return (GasClockId)p[index]; }
        public int GetTagId(int index) { fixed (int* p = ItemTagIds) return p[index]; }
        public int GetTemplateId(int index) { fixed (int* p = ItemTemplateIds) return p[index]; }
        public byte GetCallerParamsIdx(int index) { fixed (byte* p = ItemCallerParamsIdx) return p[index]; }
        public int GetPayloadA(int index) { fixed (int* p = ItemPayloadA) return p[index]; }

        /// <summary>
        /// Set an item at the given index. Auto-expands ItemCount.
        /// </summary>
        public void SetItem(int index, ExecItemKind kind, int tick, int durationTicks = 0,
            GasClockId clockId = default, int tagId = 0, int templateId = 0,
            byte callerParamsIdx = 0xFF, int payloadA = 0)
        {
            if (index >= MAX_ITEMS) return;
            fixed (byte* kinds = ItemKinds) kinds[index] = (byte)kind;
            fixed (int* ticks = ItemTicks) ticks[index] = tick;
            fixed (int* durs = ItemDurationTicks) durs[index] = durationTicks;
            fixed (byte* clocks = ItemClockIds) clocks[index] = (byte)clockId;
            fixed (int* tags = ItemTagIds) tags[index] = tagId;
            fixed (int* tmpl = ItemTemplateIds) tmpl[index] = templateId;
            fixed (byte* cpIdx = ItemCallerParamsIdx) cpIdx[index] = callerParamsIdx;
            fixed (int* pa = ItemPayloadA) pa[index] = payloadA;
            if (index + 1 > ItemCount) ItemCount = index + 1;
        }
    }

    /// <summary>
    /// Pool of CallerParams sets for an ability definition.
    /// Items in <see cref="AbilityExecSpec"/> reference these by index.
    /// Stored on <see cref="AbilityDefinition"/>, not on the per-instance component.
    /// </summary>
    public unsafe struct AbilityExecCallerParamsPool
    {
        public const int MAX_SETS = 4;

        // Inline storage: 4 EffectConfigParams. Each is ~420 bytes.
        // We use a byte blob and cast on access to keep the struct blittable.
        private EffectConfigParams _p0, _p1, _p2, _p3;
        public int Count;

        public bool TryAdd(in EffectConfigParams p)
        {
            if (Count >= MAX_SETS) return false;
            switch (Count)
            {
                case 0: _p0 = p; break;
                case 1: _p1 = p; break;
                case 2: _p2 = p; break;
                case 3: _p3 = p; break;
            }
            Count++;
            return true;
        }

        public ref readonly EffectConfigParams Get(int index)
        {
            switch (index)
            {
                case 0: return ref _p0;
                case 1: return ref _p1;
                case 2: return ref _p2;
                default: return ref _p3;
            }
        }
    }

    /// <summary>
    /// Runtime state of an ability execution. Attached to caster entities.
    /// </summary>
    public unsafe struct AbilityExecInstance
    {
        public int OrderId;
        public int AbilitySlot;
        public int AbilityId;
        public Entity Target;
        public Entity TargetContext;
        public Fix64Vec2 TargetPosCm;
        public byte HasTargetPos;

        /// <summary>Multi-target storage for SelectionGate results.</summary>
        public int MultiTargetCount;
        public fixed int MultiTargetIds[64];
        public fixed int MultiTargetWorldIds[64];
        public fixed int MultiTargetVersions[64];

        public AbilityExecRunState State;
        /// <summary>Elapsed ticks since execution start (relative to spec ticks).</summary>
        public int CurrentTick;
        /// <summary>The tick value at execution start (absolute clock value).</summary>
        public int StartAbsoluteTick;
        /// <summary>Index of the next item to process.</summary>
        public int NextItemIndex;
        /// <summary>Gate timeout tick (0 = no timeout).</summary>
        public int GateDeadline;
        /// <summary>Tag ID the EventGate is waiting for.</summary>
        public int WaitTagId;
        /// <summary>Request ID for InputGate/SelectionGate.</summary>
        public int WaitRequestId;
        /// <summary>Active clock for this execution.</summary>
        public GasClockId ActiveClockId;
        /// <summary>True when this instance is executing a toggle ability's deactivate timeline.</summary>
        public bool IsToggleDeactivating;

        public void AddMultiTarget(Entity entity)
        {
            if (MultiTargetCount >= 64) return;
            int i = MultiTargetCount;
            fixed (int* ids = MultiTargetIds) ids[i] = entity.Id;
            fixed (int* wids = MultiTargetWorldIds) wids[i] = entity.WorldId;
            fixed (int* vers = MultiTargetVersions) vers[i] = entity.Version;
            MultiTargetCount++;
        }
    }
}
