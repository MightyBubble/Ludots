using System;
using System.Collections.Generic;
using Arch.System;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.Activities;
using Ludots.Core.Gameplay.Tasks;
using Ludots.Core.Scripting;
using UiRegionsMod.Runtime;

namespace NarrativeChainShowcaseMod.Runtime
{
    /// <summary>
    /// Republishes the UiRegions HUD panels when the live task/activity snapshot changes,
    /// so the activity modal and the objective list track the chain without per-frame
    /// retained-scene rebuilds.
    /// </summary>
    internal sealed class NarrativeChainHudRefreshSystem : ISystem<float>
    {
        private readonly GameEngine _engine;
        private readonly UiRegionsHudInstallation _installation;
        private string _lastSignature = string.Empty;

        public NarrativeChainHudRefreshSystem(GameEngine engine, UiRegionsHudInstallation installation)
        {
            _engine = engine ?? throw new ArgumentNullException(nameof(engine));
            _installation = installation ?? throw new ArgumentNullException(nameof(installation));
        }

        public void Initialize()
        {
        }

        public void BeforeUpdate(in float dt)
        {
        }

        public void Update(in float dt)
        {
            if (_engine.GetService(CoreServiceKeys.TaskRuntimeService) is not TaskRuntimeService tasks ||
                _engine.GetService(CoreServiceKeys.ActivityRuntimeService) is not ActivityRuntimeService activities)
            {
                return;
            }

            HudLiveSnapshot snapshot = HudLiveSnapshot.Capture(tasks, activities);
            string signature = BuildSignature(snapshot);
            if (string.Equals(signature, _lastSignature, StringComparison.Ordinal))
            {
                return;
            }

            _lastSignature = signature;
            _installation.RefreshLivePanels();
        }

        public void AfterUpdate(in float dt)
        {
        }

        public void Dispose()
        {
        }

        private static string BuildSignature(HudLiveSnapshot snapshot)
        {
            var parts = new List<string>(snapshot.TaskLines.Count + snapshot.ActivityOptionLines.Count + 3)
            {
                snapshot.HasForcedActivity ? "forced" : "idle",
                snapshot.ForcedActivityTitle ?? string.Empty,
                snapshot.ForcedActivitySummary ?? string.Empty,
            };
            parts.AddRange(snapshot.TaskLines);
            parts.AddRange(snapshot.ActivityOptionLines);
            return string.Join("\n", parts);
        }
    }
}
