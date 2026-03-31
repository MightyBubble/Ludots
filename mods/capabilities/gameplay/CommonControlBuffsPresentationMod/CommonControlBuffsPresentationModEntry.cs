using System.Threading.Tasks;
using CommonControlBuffsPresentationMod.Runtime;
using Ludots.Core.Engine;
using Ludots.Core.Modding;
using Ludots.Core.Scripting;

namespace CommonControlBuffsPresentationMod
{
    public sealed class CommonControlBuffsPresentationModEntry : IMod
    {
        private const string InstalledKey = "CommonControlBuffsPresentationMod.Installed";

        public void OnLoad(IModContext context)
        {
            context.Log("[CommonControlBuffsPresentationMod] Loaded");
            context.OnEvent(GameEvents.GameStart, scriptContext =>
            {
                GameEngine? engine = scriptContext.GetEngine();
                if (engine == null)
                {
                    return Task.CompletedTask;
                }

                if (engine.GlobalContext.TryGetValue(InstalledKey, out var installedObj) &&
                    installedObj is true)
                {
                    return Task.CompletedTask;
                }

                engine.GlobalContext[InstalledKey] = true;
                var commands = engine.GetService(CoreServiceKeys.PresentationCommandBuffer);
                var performers = engine.GetService(CoreServiceKeys.PerformerDefinitionRegistry);
                var tagOps = engine.GetService(CoreServiceKeys.TagOps);
                if (commands == null || performers == null || tagOps == null)
                {
                    throw new System.InvalidOperationException(
                        "CommonControlBuffsPresentationMod requires presentation and tag services.");
                }

                engine.RegisterPresentationSystem(
                    new CommonControlStatusPresentationSystem(engine.World, commands, performers, tagOps));
                return Task.CompletedTask;
            });
        }

        public void OnUnload()
        {
        }
    }
}
