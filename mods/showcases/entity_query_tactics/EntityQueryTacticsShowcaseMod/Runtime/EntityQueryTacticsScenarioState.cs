using System;
using System.Collections.Generic;
using Arch.Core;

namespace EntityQueryTacticsShowcaseMod.Runtime
{
    public sealed class EntityQueryTacticsScenarioState
    {
        private readonly List<string> _log = new(capacity: 16);

        public EntityQueryTacticsScenarioState(EntityQueryTacticsShowcaseConfig config)
        {
            Config = config ?? throw new ArgumentNullException(nameof(config));
        }

        public EntityQueryTacticsShowcaseConfig Config { get; }
        public EntityQueryTacticsFrontendConfig FrontendConfig { get; set; } = new();
        public EntityQueryTacticsScenarioContext? ScenarioContext { get; private set; }
        public IReadOnlyList<string> Log => _log;
        public uint Frame { get; private set; }
        public uint GraphExecutionCount { get; set; }
        public uint CacheProbeCount { get; set; }
        public uint PressurePulseCount { get; set; }
        public uint FormationRevision { get; set; }
        public uint UiBoxRevision { get; set; }
        public uint CommandSourceRevision { get; set; }
        public uint FormationResultRevision { get; set; }
        public uint HostileResultRevision { get; set; }
        public bool LastCacheProbeUnchanged { get; set; }
        public string UiBoxNames { get; set; } = string.Empty;
        public string SelectedNames { get; set; } = string.Empty;
        public string FormationNames { get; set; } = string.Empty;
        public string ThreatNames { get; set; } = string.Empty;
        public Entity SelectedBest { get; set; } = Entity.Null;
        public Entity ThreatBest { get; set; } = Entity.Null;
        public Entity FormationBest { get; set; } = Entity.Null;
        public int UiBoxCount { get; set; }
        public int CommandSourceCount { get; set; }
        public int SelectedCount { get; set; }
        public int ThreatCount { get; set; }
        public int FormationCount { get; set; }
        public float SelectedCommandPowerSum { get; set; }
        public float SelectedSupplySum { get; set; }
        public int ThreatSum { get; set; }
        public int ThreatAverage { get; set; }
        public int ThreatMax { get; set; }
        public float LastFrameMs { get; set; }
        public float FormationMaxCommandPower { get; set; }
        public float FormationMinSupply { get; set; }

        public void SetScenarioContext(EntityQueryTacticsScenarioContext context)
        {
            ScenarioContext = context ?? throw new ArgumentNullException(nameof(context));
        }

        public void ResetScenarioContext()
        {
            ScenarioContext = null;
        }

        public void AdvanceFrame()
        {
            Frame++;
        }

        public void AddLog(string line)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                return;
            }

            if (_log.Count == 16)
            {
                _log.RemoveAt(0);
            }

            _log.Add(line);
        }
    }
}
