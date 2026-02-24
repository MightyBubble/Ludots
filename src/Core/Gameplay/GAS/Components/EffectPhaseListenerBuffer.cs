using System;
using System.Runtime.CompilerServices;

namespace Ludots.Core.Gameplay.GAS.Components
{
    /// <summary>
    /// Shared matching predicate for phase listener collection.
    /// Single source of truth for tag-wildcard and effectId-wildcard semantics.
    /// </summary>
    internal static class PhaseListenerMatcher
    {
        /// <summary>
        /// Returns true if a stored listener entry matches the given query parameters.
        /// A stored value of 0 for tagId or effectId acts as a wildcard (matches everything).
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool Matches(byte storedPhase, int storedTagId, int storedEffectId,
                                   byte queryPhase, int effectTagId, int effectTemplateId)
        {
            if (storedPhase != queryPhase) return false;
            if (storedTagId != 0 && storedTagId != effectTagId) return false;
            if (storedEffectId != 0 && storedEffectId != effectTemplateId) return false;
            return true;
        }
    }

    /// <summary>
    /// Observation perspective of a phase listener.
    /// </summary>
    public enum PhaseListenerScope : byte
    {
        /// <summary>Triggers when the holder entity is the TARGET of an effect.</summary>
        Target = 0,
        /// <summary>Triggers when the holder entity is the SOURCE (caster) of an effect.</summary>
        Source = 1,
    }

    /// <summary>
    /// Action to perform when a phase listener fires.
    /// </summary>
    [Flags]
    public enum PhaseListenerActionFlags : byte
    {
        None = 0,
        /// <summary>Immediately execute a Graph program (same frame).</summary>
        ExecuteGraph = 1,
        /// <summary>Publish a GameplayEvent to the EventBus (deferred to next frame).</summary>
        PublishEvent = 2,
        /// <summary>Both execute graph and publish event.</summary>
        Both = ExecuteGraph | PublishEvent,
    }

    /// <summary>
    /// Collected action produced by listener matching. Used as a scratch buffer during dispatch.
    /// </summary>
    public struct PhaseListenerCollectedAction
    {
        public PhaseListenerActionFlags Flags;
        public int GraphProgramId;
        public int EventTagId;
        public int Priority;
    }

    /// <summary>
    /// Per-entity ECS component that stores effect-bound phase listeners.
    /// Listeners are registered when an Effect with listener configuration is applied
    /// and unregistered when that effect expires or is removed.
    /// Zero-GC, fixed-capacity SOA layout.
    /// </summary>
    public unsafe struct EffectPhaseListenerBuffer
    {
        public const int CAPACITY = GasConstants.EFFECT_PHASE_LISTENER_CAPACITY;

        public int Count;
        /// <summary>Effect tag id to match (0 = wildcard, matches all).</summary>
        public fixed int ListenTagIds[CAPACITY];
        /// <summary>Effect template id to match (0 = wildcard, matches all).</summary>
        public fixed int ListenEffectIds[CAPACITY];
        /// <summary>Which phase triggers this listener.</summary>
        public fixed byte Phases[CAPACITY];
        /// <summary>Observation scope: Target or Source.</summary>
        public fixed byte Scopes[CAPACITY];
        /// <summary>What action to perform when triggered.</summary>
        public fixed byte ActionFlags[CAPACITY];
        /// <summary>Graph program to execute (when ActionFlags includes ExecuteGraph).</summary>
        public fixed int GraphProgramIds[CAPACITY];
        /// <summary>Event tag to publish (when ActionFlags includes PublishEvent).</summary>
        public fixed int EventTagIds[CAPACITY];
        /// <summary>Execution priority (higher = earlier).</summary>
        public fixed int Priorities[CAPACITY];
        /// <summary>Owner effect entity unique id for lifecycle cleanup (Entity.Id).</summary>
        public fixed int OwnerEffectIds[CAPACITY];

        /// <summary>
        /// Try to add a listener entry. Returns false if buffer is full.
        /// </summary>
        public bool TryAdd(int listenTagId, int listenEffectId, EffectPhaseId phase, PhaseListenerScope scope,
                           PhaseListenerActionFlags flags, int graphProgramId, int eventTagId, int priority, int ownerEffectId)
        {
            if (Count >= CAPACITY) return false;
            int idx = Count;
            ListenTagIds[idx] = listenTagId;
            ListenEffectIds[idx] = listenEffectId;
            Phases[idx] = (byte)phase;
            Scopes[idx] = (byte)scope;
            ActionFlags[idx] = (byte)flags;
            GraphProgramIds[idx] = graphProgramId;
            EventTagIds[idx] = eventTagId;
            Priorities[idx] = priority;
            OwnerEffectIds[idx] = ownerEffectId;
            Count++;
            return true;
        }

        /// <summary>
        /// Try to add a listener entry without an owner (for compile-time template setup).
        /// OwnerEffectIds slot is set to 0; real owner id is filled at runtime registration.
        /// </summary>
        public bool TryAddTemplate(int listenTagId, int listenEffectId, EffectPhaseId phase, PhaseListenerScope scope,
                                   PhaseListenerActionFlags flags, int graphProgramId, int eventTagId, int priority)
        {
            return TryAdd(listenTagId, listenEffectId, phase, scope, flags, graphProgramId, eventTagId, priority, 0);
        }

        /// <summary>
        /// Remove all listeners registered by a given owner effect.
        /// </summary>
        public void RemoveByOwner(int ownerEffectId)
        {
            int write = 0;
            for (int read = 0; read < Count; read++)
            {
                if (OwnerEffectIds[read] == ownerEffectId) continue;
                if (write != read)
                {
                    ListenTagIds[write] = ListenTagIds[read];
                    ListenEffectIds[write] = ListenEffectIds[read];
                    Phases[write] = Phases[read];
                    Scopes[write] = Scopes[read];
                    ActionFlags[write] = ActionFlags[read];
                    GraphProgramIds[write] = GraphProgramIds[read];
                    EventTagIds[write] = EventTagIds[read];
                    Priorities[write] = Priorities[read];
                    OwnerEffectIds[write] = OwnerEffectIds[read];
                }
                write++;
            }
            Count = write;
        }

        /// <summary>
        /// Collect all matching entries into <paramref name="output"/> for dispatch.
        /// Returns the number of collected actions.
        /// </summary>
        public int Collect(int effectTagId, int effectTemplateId, EffectPhaseId phase, PhaseListenerScope scope,
                           Span<PhaseListenerCollectedAction> output)
        {
            int collected = 0;
            byte phaseB = (byte)phase;
            byte scopeB = (byte)scope;
            for (int i = 0; i < Count; i++)
            {
                if (Scopes[i] != scopeB) continue;
                if (!PhaseListenerMatcher.Matches(Phases[i], ListenTagIds[i], ListenEffectIds[i],
                                                  phaseB, effectTagId, effectTemplateId)) continue;

                if (collected < output.Length)
                {
                    output[collected] = new PhaseListenerCollectedAction
                    {
                        Flags = (PhaseListenerActionFlags)ActionFlags[i],
                        GraphProgramId = GraphProgramIds[i],
                        EventTagId = EventTagIds[i],
                        Priority = Priorities[i],
                    };
                    collected++;
                }
            }
            return collected;
        }
    }

}
