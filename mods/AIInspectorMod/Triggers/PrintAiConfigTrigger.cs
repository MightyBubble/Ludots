using System.Threading.Tasks;
using Ludots.Core.Engine;
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
            var engine = context.Get<GameEngine>(ContextKeys.Engine);
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
            return Task.CompletedTask;
        }
    }
}
