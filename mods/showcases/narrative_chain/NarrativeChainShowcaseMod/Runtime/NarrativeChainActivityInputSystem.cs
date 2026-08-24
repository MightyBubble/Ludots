using System;
using Arch.System;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.Activities;
using Ludots.Core.Input.Runtime;
using Ludots.Core.Scripting;

namespace NarrativeChainShowcaseMod.Runtime
{
    /// <summary>
    /// Player input bridge for the forced decide activity: F confirms, G declines.
    /// With no live forced chain activity the keys are inert — that is the legal
    /// no-op, not a degraded path.
    /// </summary>
    internal sealed class NarrativeChainActivityInputSystem : ISystem<float>
    {
        private readonly GameEngine _engine;
        private readonly NarrativeChainRuntime _runtime;

        public NarrativeChainActivityInputSystem(GameEngine engine, NarrativeChainRuntime runtime)
        {
            _engine = engine ?? throw new ArgumentNullException(nameof(engine));
            _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
        }

        public void Initialize()
        {
        }

        public void BeforeUpdate(in float dt)
        {
        }

        public void Update(in float dt)
        {
            if (_engine.GetService(CoreServiceKeys.AuthoritativeInput) is not IInputActionReader input ||
                _engine.GetService(CoreServiceKeys.ActivityRuntimeService) is not ActivityRuntimeService activities)
            {
                return;
            }

            bool confirm = input.PressedThisFrame(NarrativeChainIds.ActivityConfirmActionId);
            bool decline = input.PressedThisFrame(NarrativeChainIds.ActivityDeclineActionId);
            if (!confirm && !decline)
            {
                return;
            }

            if (!TryGetForcedDecideActivity(activities, out ActivityView view))
            {
                return;
            }

            string optionId = confirm ? NarrativeChainIds.ActivityOptionConfirm : NarrativeChainIds.ActivityOptionDecline;
            activities.ResolveOption(view.Entity, optionId);
            _runtime.Record("activity", "input_resolved", $"{NarrativeChainIds.DecideActivityId} -> {optionId}");
        }

        public void AfterUpdate(in float dt)
        {
        }

        public void Dispose()
        {
        }

        private static bool TryGetForcedDecideActivity(ActivityRuntimeService activities, out ActivityView view)
        {
            foreach (ActivityView candidate in activities.CaptureViews())
            {
                if (candidate.State == ActivityInstanceState.Active &&
                    candidate.DispatchPolicy == ActivityDispatchPolicy.Forced &&
                    string.Equals(candidate.ActivityId, NarrativeChainIds.DecideActivityId, StringComparison.Ordinal))
                {
                    view = candidate;
                    return true;
                }
            }

            view = default;
            return false;
        }
    }
}
