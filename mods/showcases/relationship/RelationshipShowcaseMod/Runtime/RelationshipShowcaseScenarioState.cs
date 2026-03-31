using System.Collections.Generic;

namespace RelationshipShowcaseMod.Runtime
{
    public sealed class RelationshipShowcaseScenarioState
    {
        private readonly List<string> _log = new();

        public RelationshipShowcaseScenarioState(RelationshipShowcaseConfig config)
        {
            Config = config ?? throw new System.ArgumentNullException(nameof(config));
        }

        public RelationshipShowcaseConfig Config { get; }
        public RelationshipShowcaseFrontendConfig FrontendConfig { get; set; } = new();
        public RelationshipShowcaseScenarioContext? ScenarioContext { get; private set; }

        public int Frame { get; private set; }
        public int SelectedHeroIndex { get; set; }
        public string SelectedName { get; set; } = string.Empty;
        public string EnemyFocusName { get; set; } = string.Empty;
        public bool SynergyActive { get; set; }
        public bool TrustedUnlocked { get; set; }
        public bool OathBondUnlocked { get; set; }
        public IReadOnlyList<string> Log => _log;
        public IReadOnlyList<string> HeroNames => Config.HeroNamesArray();
        public IReadOnlyList<string> EnemyNames => Config.EnemyNames as IReadOnlyList<string> ?? new List<string>(Config.EnemyNames);

        public void AdvanceFrame() => Frame++;

        public void AddLog(string message)
        {
            if (string.IsNullOrWhiteSpace(message))
            {
                return;
            }

            _log.Add($"[T+{Frame:000}] {message}");
            if (_log.Count > 24)
            {
                _log.RemoveAt(0);
            }
        }

        public void ResetForMap()
        {
            Frame = 0;
            SelectedHeroIndex = 0;
            SelectedName = string.Empty;
            EnemyFocusName = string.Empty;
            SynergyActive = false;
            TrustedUnlocked = false;
            OathBondUnlocked = false;
            _log.Clear();
        }

        public void SetScenarioContext(RelationshipShowcaseScenarioContext context)
        {
            ScenarioContext = context;
        }

        public void ResetScenarioContext()
        {
            ScenarioContext = null;
        }
    }
}
