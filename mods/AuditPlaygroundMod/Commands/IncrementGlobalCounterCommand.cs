using System.Threading.Tasks;
using Ludots.Core.Commands;
using Ludots.Core.Diagnostics;
using Ludots.Core.Scripting;

namespace AuditPlaygroundMod.Commands
{
    public sealed class IncrementGlobalCounterCommand : GameCommand
    {
        private readonly string _counterKey;
        private readonly string _label;

        public IncrementGlobalCounterCommand(string counterKey, string label)
        {
            _counterKey = counterKey;
            _label = label;
        }

        public override Task ExecuteAsync(ScriptContext context)
        {
            var engine = context.GetEngine();
            if (engine == null) return Task.CompletedTask;

            int count = 0;
            if (engine.GlobalContext.TryGetValue(_counterKey, out var existing) && existing is int current)
            {
                count = current;
            }

            count++;
            engine.GlobalContext[_counterKey] = count;
            Log.Info(in LogChannels.Engine, $"[AuditPlaygroundMod] {_label} -> {_counterKey}={count}");
            return Task.CompletedTask;
        }
    }
}
