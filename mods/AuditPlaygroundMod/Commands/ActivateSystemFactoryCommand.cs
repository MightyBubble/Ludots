using System.Threading.Tasks;
using Ludots.Core.Commands;
using Ludots.Core.Diagnostics;
using Ludots.Core.Engine;
using Ludots.Core.Scripting;

namespace AuditPlaygroundMod.Commands
{
    public sealed class ActivateSystemFactoryCommand : GameCommand
    {
        private readonly string _factoryName;

        public ActivateSystemFactoryCommand(string factoryName)
        {
            _factoryName = factoryName;
        }

        public override Task ExecuteAsync(ScriptContext context)
        {
            var engine = context.GetEngine();
            if (engine == null) return Task.CompletedTask;

            var registry = context.Get<SystemFactoryRegistry>(ContextKeys.SystemFactoryRegistry);
            if (registry == null)
            {
                Log.Warn(in LogChannels.Engine, $"[AuditPlaygroundMod] SystemFactoryRegistry missing. name={_factoryName}");
                return Task.CompletedTask;
            }

            bool activated = registry.TryActivate(_factoryName, context, engine);
            int count = 0;
            if (engine.GlobalContext.TryGetValue("Audit.FactoryActivationCount", out var existing) && existing is int current)
            {
                count = current;
            }
            if (activated) count++;
            engine.GlobalContext["Audit.FactoryActivationCount"] = count;
            engine.GlobalContext["Audit.FactoryLastActivated"] = activated;
            Log.Info(in LogChannels.Engine, $"[AuditPlaygroundMod] Factory '{_factoryName}' activated={activated}");
            return Task.CompletedTask;
        }
    }
}
