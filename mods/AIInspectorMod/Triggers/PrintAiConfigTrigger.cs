using System.Threading.Tasks;
using Arch.Core;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.AI.Components;
using Ludots.Core.Gameplay.AI.Utility;
using Ludots.Core.Modding;
using Ludots.Core.Scripting;

namespace AIInspectorMod.Triggers
{
    public sealed class PrintAiConfigTrigger : Trigger
    {
        private readonly IModContext _modContext;

        public PrintAiConfigTrigger(IModContext modContext)
        {
            _modContext = modContext;
            EventKey = AIInspectorEvents.PrintAiConfig;
        }

        public override Task ExecuteAsync(ScriptContext context)
        {
            var engine = context.Get(CoreServiceKeys.Engine);
            if (engine == null)
            {
                _modContext.Log("[AIInspectorMod] Missing GameEngine in ScriptContext.");
                return Task.CompletedTask;
            }

            var compiled = engine.AiRuntime;

            _modContext.Log($"[AIInspectorMod] Atoms: {compiled.Atoms.Count}");
            _modContext.Log($"[AIInspectorMod] ProjectionRules: {compiled.ProjectionTable.Rules.Length}");
            _modContext.Log($"[AIInspectorMod] UtilityGoals: {compiled.GoalSelector.Count}");
            _modContext.Log($"[AIInspectorMod] GoapActions: {compiled.ActionLibrary.Count}");
            _modContext.Log($"[AIInspectorMod] GoapGoals: {compiled.GoapGoals.Count}");
            _modContext.Log($"[AIInspectorMod] HtnTasks: {compiled.HtnDomain.Tasks.Length}");
            _modContext.Log($"[AIInspectorMod] HtnMethods: {compiled.HtnDomain.Methods.Length}");
            _modContext.Log($"[AIInspectorMod] HtnSubtasks: {compiled.HtnDomain.Subtasks.Length}");
            LogUtilityRuntime(compiled.UtilityRuntime);
            LogUtilityTraces(engine.World);
            return Task.CompletedTask;
        }

        private void LogUtilityRuntime(in UtilityAiCompiledRuntime runtime)
        {
            _modContext.Log($"[AIInspectorMod] UtilityV2.Enabled: {runtime.IsEnabled}");
            _modContext.Log($"[AIInspectorMod] UtilityV2.Profiles: {runtime.Profiles.Length}");
            _modContext.Log($"[AIInspectorMod] UtilityV2.DecisionMakers: {runtime.DecisionMakers.Length}");
            _modContext.Log($"[AIInspectorMod] UtilityV2.Decisions: {runtime.Decisions.Length}");
            _modContext.Log($"[AIInspectorMod] UtilityV2.Considerations: {runtime.Considerations.Length}");
            _modContext.Log($"[AIInspectorMod] UtilityV2.TargetFilters: {runtime.TargetFilters.Length}");
            _modContext.Log($"[AIInspectorMod] UtilityV2.TargetFilterOps: {runtime.TargetFilterOps.Length}");
            _modContext.Log($"[AIInspectorMod] UtilityV2.Inputs: {runtime.Inputs.Length}");
            _modContext.Log($"[AIInspectorMod] UtilityV2.Normalizations: {runtime.Normalizations.Length}");
            _modContext.Log($"[AIInspectorMod] UtilityV2.Curves: {runtime.Curves.Length}");
            _modContext.Log($"[AIInspectorMod] UtilityV2.Tasks: {runtime.Tasks.Length}");
            _modContext.Log($"[AIInspectorMod] UtilityV2.Stances: {runtime.Stances.Length}");
            _modContext.Log($"[AIInspectorMod] UtilityV2.Actuators: {runtime.Actuators.Length}");
        }

        private void LogUtilityTraces(World world)
        {
            if (world == null)
            {
                _modContext.Log("[AIInspectorMod] UtilityV2.TraceEntities: 0");
                return;
            }

            var query = new QueryDescription().WithAll<UtilityAiDecisionTrace>();
            int count = 0;
            foreach (ref var chunk in world.Query(in query))
            {
                var traces = chunk.GetSpan<UtilityAiDecisionTrace>();
                foreach (var index in chunk)
                {
                    ref readonly var trace = ref traces[index];
                    _modContext.Log(
                        $"[AIInspectorMod] UtilityV2.Trace[{count}]: candidates={trace.CandidateCount} bestDecision={trace.BestDecisionId} bestScore={trace.BestScore} bestPriorityBucket={trace.BestPriorityBucket} bestDistanceSq={trace.BestDistanceSq} filterReject={trace.LastFilterRejectReason} readinessBlock={trace.LastReadinessBlockReason} submittedOrderType={trace.LastSubmittedOrderTypeId} submittedAbility={trace.LastSubmittedAbilityId} taskKind={trace.LastTaskKind} taskStatus={trace.LastTaskStatus}");
                    count++;
                }
            }

            _modContext.Log($"[AIInspectorMod] UtilityV2.TraceEntities: {count}");
        }
    }
}
