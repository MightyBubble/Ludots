using System;

namespace Ludots.Core.Gameplay.Level
{
    /// <summary>
    /// Single-director level blueprint runtime (not per-agent). Think-wave driven.
    /// </summary>
    public sealed class LevelDirector
    {
        private readonly LevelTriggerDef[] _triggers;
        private readonly LevelActionDef[] _actions;
        private readonly byte[] _armed;
        private readonly byte[] _fired;
        private int _thinkWaves;
        private int _counter;
        private int _phase;
        private int _lastSignal;

        public LevelDirector(string id, LevelTriggerDef[] triggers, LevelActionDef[] actions)
        {
            if (string.IsNullOrWhiteSpace(id)) throw new ArgumentException("Level director id required.", nameof(id));
            Id = id;
            _triggers = triggers ?? Array.Empty<LevelTriggerDef>();
            _actions = actions ?? Array.Empty<LevelActionDef>();
            if (_triggers.Length > LevelDirectorLimits.MaxTriggers)
            {
                throw new ArgumentException("Too many triggers.");
            }

            if (_actions.Length > LevelDirectorLimits.MaxActions)
            {
                throw new ArgumentException("Too many actions.");
            }

            _armed = new byte[_triggers.Length];
            _fired = new byte[_triggers.Length];
            for (int i = 0; i < _armed.Length; i++)
            {
                _armed[i] = 1;
            }
        }

        public string Id { get; }
        public int ThinkWaves => _thinkWaves;
        public int Counter => _counter;
        public int Phase => _phase;
        public int LastSignal => _lastSignal;
        public int TriggerCount => _triggers.Length;

        public void PulseManual(int triggerIndex, ILevelGraphHost? scriptHost = null)
        {
            ValidateTrigger(triggerIndex);
            if (_triggers[triggerIndex].Kind != LevelTriggerKind.ManualPulse)
            {
                throw new InvalidOperationException("Trigger is not ManualPulse.");
            }

            TryFire(triggerIndex, scriptHost);
        }

        public void AddCounter(int delta)
        {
            _counter += delta;
        }

        public LevelThinkStats TickThinkWave(ILevelGraphHost? scriptHost = null)
        {
            _thinkWaves++;
            int fired = 0;
            int checkedCount = 0;
            for (int i = 0; i < _triggers.Length; i++)
            {
                if (_armed[i] == 0 || _fired[i] != 0)
                {
                    continue;
                }

                checkedCount++;
                LevelTriggerDef tr = _triggers[i];
                bool ready = tr.Kind switch
                {
                    LevelTriggerKind.ManualPulse => false,
                    LevelTriggerKind.ElapsedThinkWaves => _thinkWaves >= tr.Threshold,
                    LevelTriggerKind.CounterReached => _counter >= tr.Threshold,
                    _ => throw new InvalidOperationException($"Unknown trigger kind {tr.Kind}.")
                };
                if (ready && TryFire(i, scriptHost))
                {
                    fired++;
                }
            }

            return new LevelThinkStats(checkedCount, fired, _phase, _counter);
        }

        private bool TryFire(int triggerIndex, ILevelGraphHost? scriptHost)
        {
            if (_fired[triggerIndex] != 0 || _armed[triggerIndex] == 0)
            {
                return false;
            }

            LevelTriggerDef tr = _triggers[triggerIndex];
            if ((uint)tr.ActionIndex >= (uint)_actions.Length)
            {
                throw new InvalidOperationException($"Trigger {triggerIndex} action out of range.");
            }

            RunAction(_actions[tr.ActionIndex], scriptHost);
            _fired[triggerIndex] = 1;
            return true;
        }

        private void RunAction(in LevelActionDef action, ILevelGraphHost? scriptHost)
        {
            switch (action.Kind)
            {
                case LevelActionKind.None:
                    break;
                case LevelActionKind.IncrementCounter:
                    _counter += action.Arg0;
                    break;
                case LevelActionKind.SetPhase:
                    _phase = action.Arg0;
                    break;
                case LevelActionKind.EmitSignal:
                    _lastSignal = action.Arg0;
                    break;
                case LevelActionKind.RunScript:
                    if (scriptHost == null)
                    {
                        throw new InvalidOperationException(
                            $"Level RunScript action graphId={action.Arg0} requires ILevelGraphHost.");
                    }

                    if (action.Arg0 <= 0)
                    {
                        throw new InvalidOperationException("Level RunScript requires positive Arg0 graph id.");
                    }

                    scriptHost.RunScript(action.Arg0);
                    _lastSignal = action.Arg0;
                    break;
                default:
                    throw new InvalidOperationException($"Unknown action {action.Kind}.");
            }
        }

        private void ValidateTrigger(int triggerIndex)
        {
            if ((uint)triggerIndex >= (uint)_triggers.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(triggerIndex));
            }
        }
    }

    public readonly struct LevelThinkStats
    {
        public LevelThinkStats(int triggersChecked, int fired, int phase, int counter)
        {
            TriggersChecked = triggersChecked;
            Fired = fired;
            Phase = phase;
            Counter = counter;
        }

        public int TriggersChecked { get; }
        public int Fired { get; }
        public int Phase { get; }
        public int Counter { get; }
    }

    public static class LevelBlueprintFactory
    {
        /// <summary>Wave0: after 1 think → phase1; counter>=10 → phase2 signal.</summary>
        public static LevelDirector CreateTwoPhaseTrial(string id, int phaseAdvanceGraphId)
        {
            if (phaseAdvanceGraphId <= 0)
            {
                throw new InvalidOperationException("Level phase-advance Script graph id is not registered.");
            }

            int phaseScript = phaseAdvanceGraphId;

            var actions = new[]
            {
                new LevelActionDef(LevelActionKind.SetPhase, arg0: 1, arg1: 0),
                new LevelActionDef(LevelActionKind.SetPhase, arg0: 2, arg1: 0),
                new LevelActionDef(LevelActionKind.RunScript, arg0: phaseScript, arg1: 0),
            };
            var triggers = new[]
            {
                new LevelTriggerDef(LevelTriggerKind.ElapsedThinkWaves, threshold: 1, actionIndex: 0),
                new LevelTriggerDef(LevelTriggerKind.CounterReached, threshold: 10, actionIndex: 1),
                new LevelTriggerDef(LevelTriggerKind.ManualPulse, threshold: 0, actionIndex: 2),
            };
            return new LevelDirector(id, triggers, actions);
        }
    }
}
