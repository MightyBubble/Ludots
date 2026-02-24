using System;
using Ludots.Core.Gameplay.GAS.Components;

namespace Ludots.Core.Gameplay.GAS
{
    /// <summary>
    /// Lightweight global registry for map-level phase listeners that are not bound to any entity's effect lifecycle.
    /// Example use cases: ARAM damage modifiers, global statistics broadcasting, map-wide debuff auras.
    /// Thread-safety: intended for single-threaded game loop only.
    /// </summary>
    public sealed class GlobalPhaseListenerRegistry
    {
        public const int MAX_LISTENERS = GasConstants.GLOBAL_PHASE_LISTENER_MAX;

        private int _count;
        private readonly int[] _listenTagIds = new int[MAX_LISTENERS];
        private readonly int[] _listenEffectIds = new int[MAX_LISTENERS];
        private readonly byte[] _phases = new byte[MAX_LISTENERS];
        private readonly byte[] _actionFlags = new byte[MAX_LISTENERS];
        private readonly int[] _graphProgramIds = new int[MAX_LISTENERS];
        private readonly int[] _eventTagIds = new int[MAX_LISTENERS];
        private readonly int[] _priorities = new int[MAX_LISTENERS];

        /// <summary>
        /// Register a global listener. Returns false if capacity is full.
        /// </summary>
        public bool Register(int listenTagId, int listenEffectId, EffectPhaseId phase,
                             PhaseListenerActionFlags flags, int graphProgramId, int eventTagId, int priority)
        {
            if (_count >= MAX_LISTENERS) return false;
            int idx = _count;
            _listenTagIds[idx] = listenTagId;
            _listenEffectIds[idx] = listenEffectId;
            _phases[idx] = (byte)phase;
            _actionFlags[idx] = (byte)flags;
            _graphProgramIds[idx] = graphProgramId;
            _eventTagIds[idx] = eventTagId;
            _priorities[idx] = priority;
            _count++;
            return true;
        }

        /// <summary>
        /// Unregister by matching tag + effect + phase. Removes first match.
        /// </summary>
        public bool Unregister(int listenTagId, int listenEffectId, EffectPhaseId phase)
        {
            byte phaseB = (byte)phase;
            for (int i = 0; i < _count; i++)
            {
                if (_listenTagIds[i] == listenTagId && _listenEffectIds[i] == listenEffectId && _phases[i] == phaseB)
                {
                    RemoveAt(i);
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Collect all global listeners matching the given effect context.
        /// Returns the number collected.
        /// </summary>
        public int Collect(EffectPhaseId phase, int effectTagId, int effectTemplateId,
                           Span<PhaseListenerCollectedAction> output)
        {
            int collected = 0;
            byte phaseB = (byte)phase;
            for (int i = 0; i < _count; i++)
            {
                if (!PhaseListenerMatcher.Matches(_phases[i], _listenTagIds[i], _listenEffectIds[i],
                                                  phaseB, effectTagId, effectTemplateId)) continue;

                if (collected < output.Length)
                {
                    output[collected] = new PhaseListenerCollectedAction
                    {
                        Flags = (PhaseListenerActionFlags)_actionFlags[i],
                        GraphProgramId = _graphProgramIds[i],
                        EventTagId = _eventTagIds[i],
                        Priority = _priorities[i],
                    };
                    collected++;
                }
            }
            return collected;
        }

        public int Count => _count;

        public void Clear() => _count = 0;

        private void RemoveAt(int index)
        {
            int last = _count - 1;
            if (index < last)
            {
                _listenTagIds[index] = _listenTagIds[last];
                _listenEffectIds[index] = _listenEffectIds[last];
                _phases[index] = _phases[last];
                _actionFlags[index] = _actionFlags[last];
                _graphProgramIds[index] = _graphProgramIds[last];
                _eventTagIds[index] = _eventTagIds[last];
                _priorities[index] = _priorities[last];
            }
            _count--;
        }
    }
}
