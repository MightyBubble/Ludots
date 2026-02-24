using System.Threading.Tasks;
using Ludots.Core.Engine;
using Ludots.Core.Scripting;

namespace Ludots.Core.Config
{
    public sealed class ReloadConfigTrigger : Trigger
    {
        private readonly GameEngine _engine;

        public ReloadConfigTrigger(GameEngine engine)
        {
            _engine = engine;
            EventKey = ConfigEvents.Reload;
        }

        public override Task ExecuteAsync(ScriptContext context)
        {
            string group = context.Get<string>(ConfigReloadContextKeys.Group);
            string relativePath = context.Get<string>(ConfigReloadContextKeys.RelativePath);
            _engine.ReloadConfigs(group, relativePath);
            return Task.CompletedTask;
        }
    }
}

