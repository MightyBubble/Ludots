using System;
using System.Threading.Tasks;
using CoreInputMod.Systems;
using Ludots.Core.Engine;
using Ludots.Core.Scripting;

namespace CoreInputMod.Triggers
{
    public sealed class InstallCoreInputOnGameStartTrigger : Trigger
    {
        public InstallCoreInputOnGameStartTrigger()
        {
            EventKey = GameEvents.GameStart;
        }

        public override Task ExecuteAsync(ScriptContext context)
        {
            var engine = context.GetEngine();
            if (engine == null) return Task.CompletedTask;

            if (engine.TryGetService(CoreInputServiceKeys.Installed, out bool installed) && installed)
                return Task.CompletedTask;
            engine.SetService(CoreInputServiceKeys.Installed, true);

            _ = engine.GetService(CoreServiceKeys.InteractionActionBindings)
                ?? throw new InvalidOperationException("InteractionActionBindings must be registered before CoreInputMod installs.");

            _ = engine.GetService(CoreServiceKeys.EntityCollectionStore)
                ?? throw new InvalidOperationException("EntityCollectionStore must be registered before CoreInputMod installs.");

            engine.RegisterSystem(new AbilityExecAimSyncSystem(engine.World, new InputInteractionContextAccessor(engine.World, engine.GlobalContext)), SystemGroup.InputCollection);
            return Task.CompletedTask;
        }
    }
}
