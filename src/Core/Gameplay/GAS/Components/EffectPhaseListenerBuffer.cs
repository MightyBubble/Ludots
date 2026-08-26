using System;
using System.Runtime.CompilerServices;
using Ludots.Core.GraphRuntime;

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

    public static class EffectPhaseListenerContract
    {
        public const string InvalidRegistrationError = "GAS.PHASE_LISTENER.ERR.InvalidRegistration";
        public const string InvalidBufferCountError = "GAS.PHASE_LISTENER.ERR.InvalidBufferCount";

        public static bool IsPurePhase(EffectPhaseId phase)
            => phase is EffectPhaseId.OnPropose or EffectPhaseId.OnCalculate;

        public static GraphKind GetRequiredGraphKind(EffectPhaseId phase)
            => phase == EffectPhaseId.OnPropose ? GraphKind.Validation : GraphKind.Effect;

        public static bool TryValidateCount(int count, int capacity, out string reason)
        {
            if ((uint)count <= (uint)capacity)
            {
                reason = string.Empty;
                return true;
            }

            reason = $"Listener buffer count must be between 0 and capacity={capacity}; actual={count}.";
            return false;
        }

        public static bool TryValidateRegistration(
            int listenCategoryId,
            int listenEffectId,
            EffectPhaseId phase,
            PhaseListenerScope scope,
            PhaseListenerActionFlags flags,
            int graphProgramId,
            int eventTagId,
            out string reason)
        {
            if (listenCategoryId < 0 || listenEffectId < 0)
            {
                reason = "Listener match ids must be non-negative; zero is the wildcard.";
                return false;
            }
            if ((uint)phase >= EffectPhaseConstants.PhaseCount)
            {
                reason = $"Unknown effect phase value '{(byte)phase}'.";
                return false;
            }
            if (scope is not (PhaseListenerScope.Target or PhaseListenerScope.Source))
            {
                reason = $"Unknown listener scope value '{(byte)scope}'.";
                return false;
            }

            return TryValidateAction(phase, flags, graphProgramId, eventTagId, out reason);
        }

        public static bool TryValidateAction(
            EffectPhaseId phase,
            PhaseListenerActionFlags flags,
            int graphProgramId,
            int eventTagId,
            out string reason)
        {
            if ((uint)phase >= EffectPhaseConstants.PhaseCount)
            {
                reason = $"Unknown effect phase value '{(byte)phase}'.";
                return false;
            }
            if (flags is not (PhaseListenerActionFlags.ExecuteGraph or
                              PhaseListenerActionFlags.PublishEvent or
                              PhaseListenerActionFlags.Both))
            {
                reason = $"Listener action flags '{(byte)flags}' are empty or contain unknown bits.";
                return false;
            }

            bool executesGraph = (flags & PhaseListenerActionFlags.ExecuteGraph) != 0;
            if ((executesGraph && graphProgramId <= 0) || (!executesGraph && graphProgramId != 0))
            {
                reason = executesGraph
                    ? "ExecuteGraph requires a positive graph program id."
                    : "A graph program id is not allowed when ExecuteGraph is absent.";
                return false;
            }

            bool publishesEvent = (flags & PhaseListenerActionFlags.PublishEvent) != 0;
            if ((publishesEvent && eventTagId <= 0) || (!publishesEvent && eventTagId != 0))
            {
                reason = publishesEvent
                    ? "PublishEvent requires a positive event tag id."
                    : "An event tag id is not allowed when PublishEvent is absent.";
                return false;
            }
            if (publishesEvent && IsPurePhase(phase))
            {
                reason = $"Listener event publication is not allowed in pure phase '{phase}'.";
                return false;
            }

            reason = string.Empty;
            return true;
        }

        public static void RequireValidCount(int count, int capacity)
        {
            if (!TryValidateCount(count, capacity, out string reason))
            {
                throw new InvalidOperationException($"{InvalidBufferCountError}: {reason}");
            }
        }

        public static void RequireValidRegistration(
            int listenCategoryId,
            int listenEffectId,
            EffectPhaseId phase,
            PhaseListenerScope scope,
            PhaseListenerActionFlags flags,
            int graphProgramId,
            int eventTagId)
        {
            if (!TryValidateRegistration(
                    listenCategoryId,
                    listenEffectId,
                    phase,
                    scope,
                    flags,
                    graphProgramId,
                    eventTagId,
                    out string reason))
            {
                throw new InvalidOperationException($"{InvalidRegistrationError}: {reason}");
            }
        }

        public static void RequireValidAction(
            EffectPhaseId phase,
            PhaseListenerActionFlags flags,
            int graphProgramId,
            int eventTagId)
        {
            if (!TryValidateAction(phase, flags, graphProgramId, eventTagId, out string reason))
            {
                throw new InvalidOperationException($"{InvalidRegistrationError}: {reason}");
            }
        }
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
        public fixed int ListenCategoryIds[CAPACITY];
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
        public bool TryAdd(int listenCategoryId, int listenEffectId, EffectPhaseId phase, PhaseListenerScope scope,
                           PhaseListenerActionFlags flags, int graphProgramId, int eventTagId, int priority, int ownerEffectId)
        {
            EffectPhaseListenerContract.RequireValidCount(Count, CAPACITY);
            EffectPhaseListenerContract.RequireValidRegistration(
                listenCategoryId,
                listenEffectId,
                phase,
                scope,
                flags,
                graphProgramId,
                eventTagId);
            if (Count >= CAPACITY) return false;
            int idx = Count;
            ListenCategoryIds[idx] = listenCategoryId;
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
        public bool TryAddTemplate(int listenCategoryId, int listenEffectId, EffectPhaseId phase, PhaseListenerScope scope,
                                   PhaseListenerActionFlags flags, int graphProgramId, int eventTagId, int priority)
        {
            return TryAdd(listenCategoryId, listenEffectId, phase, scope, flags, graphProgramId, eventTagId, priority, 0);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool HasMatch(int effectTagId, int effectTemplateId, EffectPhaseId phase, PhaseListenerScope scope)
        {
            EffectPhaseListenerContract.RequireValidCount(Count, CAPACITY);
            byte phaseB = (byte)phase;
            byte scopeB = (byte)scope;
            for (int i = 0; i < Count; i++)
            {
                if (Scopes[i] == scopeB &&
                    PhaseListenerMatcher.Matches(
                        Phases[i], ListenCategoryIds[i], ListenEffectIds[i],
                        phaseB, effectTagId, effectTemplateId))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Remove all listeners registered by a given owner effect.
        /// </summary>
        public void RemoveByOwner(int ownerEffectId)
        {
            EffectPhaseListenerContract.RequireValidCount(Count, CAPACITY);
            int write = 0;
            for (int read = 0; read < Count; read++)
            {
                if (OwnerEffectIds[read] == ownerEffectId) continue;
                if (write != read)
                {
                    ListenCategoryIds[write] = ListenCategoryIds[read];
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
            return Collect(effectTagId, effectTemplateId, phase, scope, output, out _);
        }

        public int Collect(int effectTagId, int effectTemplateId, EffectPhaseId phase, PhaseListenerScope scope,
                           Span<PhaseListenerCollectedAction> output, out int dropped)
        {
            EffectPhaseListenerContract.RequireValidCount(Count, CAPACITY);
            int collected = 0;
            dropped = 0;
            byte phaseB = (byte)phase;
            byte scopeB = (byte)scope;
            for (int i = 0; i < Count; i++)
            {
                if (Scopes[i] != scopeB) continue;
                if (!PhaseListenerMatcher.Matches(Phases[i], ListenCategoryIds[i], ListenEffectIds[i],
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
                else
                {
                    dropped++;
                }
            }
            return collected;
        }
    }

}
